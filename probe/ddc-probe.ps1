#Requires -Version 5.1
<#
  ddc-probe.ps1 —— Windows 只读 DDC/CI 能力探测（dxva2.dll / lowlevelmonitorconfigurationapi）
  ------------------------------------------------------------------------------------------
  严格只读：本脚本仅调用以下"读"接口
    user32:   EnumDisplayMonitors / GetMonitorInfo / EnumDisplayDevices
    dxva2:    GetNumberOfPhysicalMonitorsFromHMONITOR / GetPhysicalMonitorsFromHMONITOR
              GetMonitorCapabilities / GetCapabilitiesStringLength
              CapabilitiesRequestAndCapabilitiesReply / GetVCPFeatureAndVCPFeatureReply
              DestroyPhysicalMonitors
  绝不调用 SetVCPFeature / SetMonitorBrightness / ChangeDisplaySettings / SetDisplayConfig /
  SetDisplayConfig 等任何写接口；不修改分辨率、拓扑、OSD、输入源。
  输出：stdout（不落盘）。
  用法： pwsh -NoProfile -File .\probe\ddc-probe.ps1
#>

$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;

public static class DdcProbe
{
    // ---------------- Win32 / DXVA2 interop (read-only subset) ----------------

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int left; public int top; public int right; public int bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szPhysicalMonitorDescription;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint pdwNumberOfPhysicalMonitors);

    // 用 IntPtr 手工编组数组，避免 [Out] 结构体数组在部分 .NET 版本上回填失败（实测 handle 被填 0）
    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint dwPhysicalMonitorArraySize, IntPtr pPhysicalMonitorArray);

    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool DestroyPhysicalMonitors(uint dwPhysicalMonitorArraySize, IntPtr pPhysicalMonitorArray);

    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool GetMonitorCapabilities(IntPtr hMonitor, out uint pdwMonitorCapabilities, out uint pdwSupportedColorTemperatures);

    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool GetCapabilitiesStringLength(IntPtr hMonitor, out uint pdwCapabilitiesStringLengthInCharacters);

    [DllImport("dxva2.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    public static extern bool CapabilitiesRequestAndCapabilitiesReply(IntPtr hMonitor, [Out] StringBuilder pszASCIICapabilitiesString, uint dwCapabilitiesStringLengthInCharacters);

    // 只读：仅查询 VCP 当前值 / 最大值，不设置
    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr hMonitor, byte bVCPCode, out int pvct, out uint pdwCurrentValue, out uint pdwMaximumValue);

    // ---------------- helpers ----------------

    static string LastErr()
    {
        int e = Marshal.GetLastWin32Error();
        string hint = "";
        if (e == 0) hint = "ERROR_SUCCESS";
        else if (e == 1) hint = "ERROR_INVALID_FUNCTION";
        else if (e == 5) hint = "ERROR_ACCESS_DENIED";
        else if (e == 6) hint = "ERROR_INVALID_HANDLE";
        else if (e == 31) hint = "ERROR_GEN_FAILURE (monitor did not answer the DDC/CI request)";
        else if (e == 87) hint = "ERROR_INVALID_PARAMETER";
        else if (e == 1168) hint = "ERROR_NOT_FOUND";
        else if (e == 1169) hint = "ERROR_NO_MORE_ITEMS";
        return "Win32 err " + e + (hint.Length > 0 ? " (" + hint + ")" : "");
    }

    static string StateText(uint f)
    {
        List<string> s = new List<string>();
        if ((f & 0x00000001) != 0) s.Add("ATTACHED_TO_DESKTOP");
        if ((f & 0x00000002) != 0) s.Add("MULTI_DRIVER");
        if ((f & 0x00000004) != 0) s.Add("PRIMARY_DEVICE");
        if ((f & 0x00000008) != 0) s.Add("MIRRORING_DRIVER");
        if ((f & 0x00000010) != 0) s.Add("VGA_COMPATIBLE");
        if ((f & 0x00000020) != 0) s.Add("REMOVABLE");
        if ((f & 0x00000040) != 0) s.Add("ACC_DRIVER");
        if ((f & 0x01000000) != 0) s.Add("RDPUDD");
        if ((f & 0x02000000) != 0) s.Add("DISCONNECT");
        if ((f & 0x04000000) != 0) s.Add("REMOTE");
        if ((f & 0x08000000) != 0) s.Add("MODESPRUNED");
        return s.Count == 0 ? "" : " [" + string.Join(",", s.ToArray()) + "]";
    }

    // 依据 lowlevelmonitorconfigurationapi.h 的 MC_CAPS_* 位定义（原始 hex 才是权威值）
    static string CapsFlagText(uint f)
    {
        if (f == 0) return "none";
        List<string> s = new List<string>();
        if ((f & 0x00000001) != 0) s.Add("MONITOR_TECHNOLOGY_TYPE");
        if ((f & 0x00000002) != 0) s.Add("BRIGHTNESS");
        if ((f & 0x00000004) != 0) s.Add("CONTRAST");
        if ((f & 0x00000008) != 0) s.Add("COLOR_TEMPERATURE");
        if ((f & 0x00000010) != 0) s.Add("RED_GREEN_BLUE_GAIN");
        if ((f & 0x00000020) != 0) s.Add("RED_GREEN_BLUE_DRIVE");
        if ((f & 0x00000040) != 0) s.Add("DEGAUSS");
        if ((f & 0x00000080) != 0) s.Add("DISPLAY_AREA_POSITION");
        if ((f & 0x00000100) != 0) s.Add("DISPLAY_AREA_SIZE");
        if ((f & 0x00000400) != 0) s.Add("RESTORE_FACTORY_DEFAULTS");
        if ((f & 0x00000800) != 0) s.Add("RESTORE_FACTORY_COLOR_DEFAULTS");
        if ((f & 0x00001000) != 0) s.Add("RESTORE_FACTORY_DEFAULTS_ENABLES_MONITOR_SETTINGS");
        if ((f & 0x00004000) != 0) s.Add("POWER_MODE");
        if ((f & 0x00008000) != 0) s.Add("SYSTEM_ID");
        if ((f & 0x00010000) != 0) s.Add("INPUT_SOURCE");
        return string.Join(",", s.ToArray());
    }

    // VCP 名称参考 VESA MCCS（0xE0-0xFF 多为厂商自定义，名称仅供索引，不代表已实现语义）
    static string VcpName(byte c)
    {
        switch (c)
        {
            case 0x00: return "Code Page";
            case 0x01: return "Momentary";
            case 0x02: return "New Control Value";
            case 0x03: return "Soft Controls";
            case 0x04: return "Restore Factory Defaults";
            case 0x05: return "Restore Factory Defaults (brightness/contrast)";
            case 0x0C: return "Horizontal Position (Phase)";
            case 0x0E: return "Vertical Position (Line)";
            case 0x10: return "Brightness";
            case 0x11: return "Fine Focus";
            case 0x12: return "Contrast";
            case 0x14: return "Color Preset";
            case 0x16: return "Red Gain";
            case 0x18: return "Green Gain";
            case 0x1A: return "Blue Gain";
            case 0x1E: return "Auto Size Center";
            case 0x20: return "Horizontal Position";
            case 0x22: return "Vertical Position";
            case 0x24: return "Horizontal Size";
            case 0x26: return "Vertical Size";
            case 0x28: return "Pincushion";
            case 0x2A: return "Pincushion Balance";
            case 0x2C: return "Convergence (H)";
            case 0x2E: return "Convergence (V)";
            case 0x30: return "Raster Moire (H)";
            case 0x32: return "Raster Moire (V)";
            case 0x34: return "Raster Geometry";
            case 0x36: return "Raster Geometry (H)";
            case 0x38: return "Raster Geometry (V)";
            case 0x3A: return "Raster Geometry (H/V)";
            case 0x3C: return "Raster Rotation";
            case 0x40: return "?";
            case 0x52: return "Active Control";
            case 0x60: return "Input Source";
            case 0x62: return "Audio Speaker Volume";
            case 0x64: return "Audio Microphone Volume";
            case 0x66: return "Audio Jack Connection Status";
            case 0x6C: return "Display Technology Type";
            case 0x6E: return "Power Mode";
            case 0x70: return "Display Controller ID";
            case 0x72: return "Display Firmware Version";
            case 0x86: return "?";
            case 0x87: return "Sharpness";
            case 0x8D: return "Audio Mute";
            case 0xAC: return "Horizontal Frequency";
            case 0xAE: return "Vertical Frequency";
            case 0xB6: return "Display Usage Time";
            case 0xC0: return "Display Descriptor";
            case 0xC6: return "?";
            case 0xC8: return "?";
            case 0xC9: return "?";
            case 0xCA: return "OSD/Button Control";
            case 0xCC: return "OSD Language";
            case 0xD6: return "?";
            case 0xDF: return "VCP Version";
            case 0xE0: return "Manufacturer Specific (E0)";
            case 0xE1: return "Manufacturer Specific (E1)";
            case 0xE2: return "Manufacturer Specific (E2)";
            case 0xE3: return "Manufacturer Specific (E3)";
            case 0xE4: return "Manufacturer Specific (E4)";
            case 0xE5: return "Manufacturer Specific (E5)";
            case 0xE6: return "Manufacturer Specific (E6)";
            case 0xE7: return "Manufacturer Specific (E7)";
            case 0xE8: return "Manufacturer Specific (E8)";
            case 0xE9: return "Manufacturer Specific (E9)";
            case 0xEA: return "Manufacturer Specific (EA)";
            case 0xEB: return "Manufacturer Specific (EB)";
            case 0xEC: return "Manufacturer Specific (EC)";
            case 0xED: return "Manufacturer Specific (ED)";
            case 0xEE: return "Manufacturer Specific (EE)";
            case 0xEF: return "Manufacturer Specific (EF)";
            case 0xF0: return "Manufacturer Specific (F0)";
            case 0xF1: return "Manufacturer Specific (F1)";
            case 0xF2: return "Manufacturer Specific (F2)";
            case 0xF3: return "Manufacturer Specific (F3)";
            case 0xF4: return "Manufacturer Specific (F4)";
            case 0xF5: return "Manufacturer Specific (F5)";
            case 0xF6: return "Manufacturer Specific (F6)";
            case 0xF7: return "Manufacturer Specific (F7)";
            case 0xF8: return "Manufacturer Specific (F8)";
            case 0xF9: return "Manufacturer Specific (F9)";
            case 0xFA: return "Manufacturer Specific (FA)";
            case 0xFB: return "Manufacturer Specific (FB)";
            case 0xFC: return "Manufacturer Specific (FC)";
            case 0xFD: return "Manufacturer Specific (FD)";
            case 0xFE: return "Manufacturer Specific (FE)";
            case 0xFF: return "Manufacturer Specific (FF)";
            default: return "Unknown / vendor specific";
        }
    }

    static string Hex(byte c) { return "0x" + c.ToString("X2"); }

    static string JoinCodes(List<byte> codes)
    {
        if (codes.Count == 0) return "(none)";
        string[] a = new string[codes.Count];
        for (int i = 0; i < codes.Count; i++) a[i] = Hex(codes[i]);
        return string.Join(" ", a);
    }

    // 取 caps 字符串中形如 name(...) 的字段内容（支持嵌套括号）
    static string ExtractField(string caps, string name)
    {
        if (caps == null) return null;
        int i = caps.IndexOf(name + "(", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;
        int start = i + name.Length + 1;
        int depth = 1;
        int j = start;
        for (; j < caps.Length; j++)
        {
            if (caps[j] == '(') depth++;
            else if (caps[j] == ')') { depth--; if (depth == 0) break; }
        }
        if (j >= caps.Length) return null;
        return caps.Substring(start, j - start).Trim();
    }

    // 解析 vcp(...) 顶层 token（忽略 0x10(00 01) 这种值能力列表里的子 token）
    static List<byte> ParseVcpCodes(string caps)
    {
        List<byte> list = new List<byte>();
        string body = ExtractField(caps, "vcp");
        if (body == null) return list;
        List<string> toks = new List<string>();
        int depth = 0;
        StringBuilder cur = new StringBuilder();
        foreach (char ch in body)
        {
            if (ch == '(')
            {
                if (cur.Length > 0) { toks.Add(cur.ToString()); cur.Length = 0; }
                depth++;
            }
            else if (ch == ')')
            {
                if (cur.Length > 0) { toks.Add(cur.ToString()); cur.Length = 0; }
                if (depth > 0) depth--;
            }
            else if (char.IsWhiteSpace(ch))
            {
                if (cur.Length > 0) { toks.Add(cur.ToString()); cur.Length = 0; }
            }
            else
            {
                if (depth == 0) cur.Append(ch);
            }
        }
        if (cur.Length > 0) toks.Add(cur.ToString());
        foreach (string t in toks)
        {
            if (t.Length != 2) continue;
            byte b;
            if (byte.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b))
                if (!list.Contains(b)) list.Add(b);
        }
        return list;
    }

    // ---------------- per-monitor probe ----------------

    static void ProbeVcp(IntPtr h, StringBuilder sb, string ind)
    {
        uint mcCaps = 0, colorTemps = 0;
        if (GetMonitorCapabilities(h, out mcCaps, out colorTemps))
            sb.AppendLine(ind + "GetMonitorCapabilities: OK  flags=0x" + mcCaps.ToString("X8") + " [" + CapsFlagText(mcCaps) + "]  colorTemps=0x" + colorTemps.ToString("X8"));
        else
            sb.AppendLine(ind + "GetMonitorCapabilities: FAILED (" + LastErr() + ")");

        string caps = null;
        uint len = 0;
        if (!GetCapabilitiesStringLength(h, out len))
        {
            sb.AppendLine(ind + "GetCapabilitiesStringLength: FAILED (" + LastErr() + ")");
        }
        else
        {
            sb.AppendLine(ind + "capabilities string length = " + len);
            if (len > 0)
            {
                StringBuilder cs = new StringBuilder((int)len + 2);
                if (!CapabilitiesRequestAndCapabilitiesReply(h, cs, len))
                {
                    sb.AppendLine(ind + "CapabilitiesRequestAndCapabilitiesReply: FAILED (" + LastErr() + ")");
                }
                else
                {
                    caps = cs.ToString();
                    sb.AppendLine(ind + "CAPS_RAW_BEGIN");
                    sb.AppendLine(caps);
                    sb.AppendLine(ind + "CAPS_RAW_END");
                    string cmds = ExtractField(caps, "cmds");
                    if (cmds != null) sb.AppendLine(ind + "field cmds(...)      = " + cmds);
                    string mccs = ExtractField(caps, "mccs_ver");
                    if (mccs != null) sb.AppendLine(ind + "field mccs_ver(...)  = " + mccs);
                    string typ = ExtractField(caps, "type");
                    if (typ != null) sb.AppendLine(ind + "field type(...)      = " + typ);
                    string model = ExtractField(caps, "model");
                    if (model != null) sb.AppendLine(ind + "field model(...)     = " + model);
                    string prot = ExtractField(caps, "prot");
                    if (prot != null) sb.AppendLine(ind + "field prot(...)      = " + prot);
                    string mswhql = ExtractField(caps, "mswhql");
                    if (mswhql != null) sb.AppendLine(ind + "field mswhql(...)    = " + mswhql);
                }
            }
        }

        List<byte> codes = ParseVcpCodes(caps);
        sb.AppendLine(ind + "advertised VCP codes (" + codes.Count + "): " + JoinCodes(codes));

        // 只读：按 caps 声明逐个读取；跳过会改变/确认状态的"控制类"码 0x02/0x03/0x04/0x05
        HashSet<byte> skip = new HashSet<byte>();
        skip.Add(0x02); skip.Add(0x03); skip.Add(0x04); skip.Add(0x05);
        List<byte> toRead = new List<byte>();
        foreach (byte c in codes) if (!skip.Contains(c) && !toRead.Contains(c)) toRead.Add(c);

        sb.AppendLine(ind + "VCP read via GetVCPFeatureAndVCPFeatureReply (READ-ONLY):");
        sb.AppendLine(ind + "  code | name                                    | type | cur    | max    | curHex | maxHex");
        foreach (byte c in toRead)
        {
            int vct; uint cur, max;
            bool ok = GetVCPFeatureAndVCPFeatureReply(h, c, out vct, out cur, out max);
            if (ok)
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0}  {1}  {2,-40} {3,-4} {4,-6} {5,-6} 0x{6:X4}  0x{7:X4}",
                    ind, Hex(c), VcpName(c), (vct == 1 ? "SP" : "mom"), cur, max, cur, max));
            else
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0}  {1}  {2,-40} READ-FAILED ({3})", ind, Hex(c), VcpName(c), LastErr()));
        }

        // 关键码即使未在 caps 中声明也做一次只读尝试，用于记录"不支持"的失败证据
        byte[] key = new byte[] { 0x0C, 0x0E, 0x10, 0x12, 0x14, 0x20, 0x22, 0x24, 0x26, 0x60, 0x6E, 0xDF, 0xE1, 0xE2, 0xF0 };
        sb.AppendLine(ind + "key VCP codes not advertised in caps (single read-only attempt, evidence of absence):");
        bool anyKeyMissing = false;
        foreach (byte c in key)
        {
            if (toRead.Contains(c)) continue;
            anyKeyMissing = true;
            int vct; uint cur, max;
            bool ok = GetVCPFeatureAndVCPFeatureReply(h, c, out vct, out cur, out max);
            if (ok)
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0}  [NOT-ADVERTISED] {1}  {2,-40} read=OK cur={3} max={4}", ind, Hex(c), VcpName(c), cur, max));
            else
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0}  [NOT-ADVERTISED] {1}  {2,-40} read=FAILED ({3})", ind, Hex(c), VcpName(c), LastErr()));
        }
        if (!anyKeyMissing) sb.AppendLine(ind + "  (all key codes were advertised and already read above)");
    }

    static void ProbeMonitor(IntPtr hMon, StringBuilder sb, string ind)
    {
        uint cnt = 0;
        if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out cnt))
        {
            sb.AppendLine(ind + "GetNumberOfPhysicalMonitorsFromHMONITOR: FAILED (" + LastErr() + ")");
            return;
        }
        sb.AppendLine(ind + "physical monitor count = " + cnt);
        if (cnt == 0) return;

        int oneSize = Marshal.SizeOf(typeof(PHYSICAL_MONITOR));
        sb.AppendLine(ind + "sizeof(PHYSICAL_MONITOR) = " + oneSize + " bytes (expected 8 + 128*2 = 264)");
        IntPtr buf = Marshal.AllocHGlobal((int)cnt * oneSize);
        try
        {
            for (int z = 0; z < oneSize * (int)cnt; z++) Marshal.WriteByte(buf, z, 0);
            if (!GetPhysicalMonitorsFromHMONITOR(hMon, cnt, buf))
            {
                sb.AppendLine(ind + "GetPhysicalMonitorsFromHMONITOR: FAILED (" + LastErr() + ")");
                return;
            }
            for (int k = 0; k < (int)cnt; k++)
            {
                IntPtr p = new IntPtr(buf.ToInt64() + (long)k * oneSize);
                IntPtr h = Marshal.ReadIntPtr(p);
                string desc = Marshal.PtrToStringUni(new IntPtr(p.ToInt64() + IntPtr.Size));
                if (desc == null) desc = "";
                sb.AppendLine();
                sb.AppendLine(ind + "--- physical[" + k + "] desc=\"" + desc + "\" handle=0x" + h.ToString("X"));
                if (h == IntPtr.Zero) { sb.AppendLine(ind + "    null handle, skip"); continue; }
                ProbeVcp(h, sb, ind + "    ");
            }
        }
        finally
        {
            DestroyPhysicalMonitors(cnt, buf);
            Marshal.FreeHGlobal(buf);
        }
    }

    // ---------------- entry ----------------

    public static string Run()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("### DDC/CI READ-ONLY PROBE — no SetVCPFeature, no mode/topology change");
        sb.AppendLine("### host: " + Environment.MachineName + "   user: " + Environment.UserName);
        sb.AppendLine("### time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        sb.AppendLine("### OS: " + Environment.OSVersion.VersionString);
        sb.AppendLine();

        sb.AppendLine("## [A] EnumDisplayDevices — display adapters and their monitors");
        uint i = 0;
        while (true)
        {
            DISPLAY_DEVICE dd = new DISPLAY_DEVICE();
            dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
            if (!EnumDisplayDevices(null, i, ref dd, 0)) break;
            sb.AppendLine("  ADAPTER[" + i + "] name=" + dd.DeviceName + "  desc=\"" + dd.DeviceString + "\"  state=0x" + dd.StateFlags.ToString("X8") + StateText(dd.StateFlags));
            uint j = 0;
            while (true)
            {
                DISPLAY_DEVICE md = new DISPLAY_DEVICE();
                md.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                if (!EnumDisplayDevices(dd.DeviceName, j, ref md, 0)) break;
                sb.AppendLine("      MONITOR[" + j + "] name=" + md.DeviceName + "  desc=\"" + md.DeviceString + "\"  state=0x" + md.StateFlags.ToString("X8") + StateText(md.StateFlags) + "  id=" + md.DeviceID);
                j++;
            }
            i++;
        }
        sb.AppendLine();

        sb.AppendLine("## [B] EnumDisplayMonitors -> physical monitors -> DDC/CI capabilities & VCP");
        List<IntPtr> hmons = new List<IntPtr>();
        MonitorEnumProc cb = delegate(IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr data)
        {
            hmons.Add(hMon);
            return true;
        };
        bool enumOk = EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero);
        GC.KeepAlive(cb);
        sb.AppendLine("  EnumDisplayMonitors ok=" + enumOk + "  HMONITOR count = " + hmons.Count);
        int n = 0;
        foreach (IntPtr hMon in hmons)
        {
            n++;
            MONITORINFOEX mi = new MONITORINFOEX();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
            bool got = GetMonitorInfo(hMon, ref mi);
            string dev = mi.szDevice == null ? "?" : mi.szDevice;
            sb.AppendLine();
            sb.AppendLine("  === HMONITOR #" + n + " device=" + dev
                + "  rect=(" + mi.rcMonitor.left + "," + mi.rcMonitor.top + ")-(" + mi.rcMonitor.right + "," + mi.rcMonitor.bottom + ")"
                + "  primary=" + ((mi.dwFlags & 1) != 0)
                + "  GetMonitorInfo=" + got);
            ProbeMonitor(hMon, sb, "  ");
        }

        sb.AppendLine();
        sb.AppendLine("### END OF PROBE");
        return sb.ToString();
    }
}
'@

[DdcProbe]::Run()
