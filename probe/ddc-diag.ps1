#Requires -Version 5.1
<#
  ddc-diag.ps1 —— 仅用于定位 GetPhysicalMonitorsFromHMONITOR 返回空句柄的编组问题（只读）。
  多路对比：buffer 版 / 结构体数组版 / MonitorFromPoint 版，并输出原始字节。
#>
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class DdcDiag
{
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int left; public int top; public int right; public int bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szPhysicalMonitorDescription;
    }

    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("dxva2.dll", SetLastError = true)] public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint pdwNumberOfPhysicalMonitors);
    [DllImport("dxva2.dll", SetLastError = true)] public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint dwPhysicalMonitorArraySize, IntPtr pArray);
    [DllImport("dxva2.dll", SetLastError = true, EntryPoint = "GetPhysicalMonitorsFromHMONITOR")]
    public static extern bool GetPhysicalMonitorsFromHMONITOR_Arr(IntPtr hMonitor, uint dwPhysicalMonitorArraySize, [In, Out, MarshalAs(UnmanagedType.LPArray)] PHYSICAL_MONITOR[] pArray);

    [DllImport("dxva2.dll", SetLastError = true)] public static extern bool GetCapabilitiesStringLength(IntPtr hMonitor, out uint len);
    [DllImport("dxva2.dll", SetLastError = true, CharSet = CharSet.Ansi)] public static extern bool CapabilitiesRequestAndCapabilitiesReply(IntPtr hMonitor, [Out] StringBuilder sb, uint len);
    [DllImport("dxva2.dll", SetLastError = true)] public static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr hMonitor, byte code, out int vct, out uint cur, out uint max);
    [DllImport("dxva2.dll", SetLastError = true)] public static extern bool GetMonitorCapabilities(IntPtr hMonitor, out uint caps, out uint temps);

    static string HexDump(IntPtr p, int n)
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < n; i++)
        {
            if (i % 16 == 0) sb.Append("\n      " + i.ToString("X3") + ": ");
            sb.Append(Marshal.ReadByte(p, i).ToString("X2") + " ");
        }
        return sb.ToString();
    }

    public static string Run()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("## dxva2 physical-monitor handle diagnostics (READ-ONLY)");
        sb.AppendLine("sizeof(PHYSICAL_MONITOR) = " + Marshal.SizeOf(typeof(PHYSICAL_MONITOR)));

        // --- HMONITOR via MonitorFromPoint ---
        POINT pt = new POINT(); pt.x = 100; pt.y = 100;
        IntPtr hMonP = MonitorFromPoint(pt, 2 /*MONITOR_DEFAULTTONEAREST*/);
        sb.AppendLine("MonitorFromPoint(100,100) = 0x" + hMonP.ToString("X"));

        // --- HMONITOR via EnumDisplayMonitors ---
        List<IntPtr> hmons = new List<IntPtr>();
        MonitorEnumProc cb = delegate(IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr data) { hmons.Add(hMon); return true; };
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero);
        GC.KeepAlive(cb);
        sb.AppendLine("EnumDisplayMonitors count = " + hmons.Count);
        if (hmons.Count > 0) sb.AppendLine("  first HMONITOR = 0x" + hmons[0].ToString("X") + "  sameAsPoint=" + (hmons[0] == hMonP));

        List<IntPtr> test = new List<IntPtr>();
        test.Add(hMonP);
        foreach (IntPtr h in hmons) if (!test.Contains(h)) test.Add(h);

        foreach (IntPtr hMon in test)
        {
            sb.AppendLine();
            sb.AppendLine("=== HMONITOR 0x" + hMon.ToString("X"));
            MONITORINFOEX mi = new MONITORINFOEX();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
            bool gi = GetMonitorInfo(hMon, ref mi);
            sb.AppendLine("  GetMonitorInfo=" + gi + " device=" + (mi.szDevice == null ? "?" : mi.szDevice));

            uint cnt = 0;
            bool nOk = GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out cnt);
            int nErr = Marshal.GetLastWin32Error();
            sb.AppendLine("  GetNumberOfPhysicalMonitorsFromHMONITOR -> " + nOk + " count=" + cnt + " err=" + nErr);

            // attempt 1: IntPtr buffer
            int one = Marshal.SizeOf(typeof(PHYSICAL_MONITOR));
            IntPtr buf = Marshal.AllocHGlobal(one * (int)Math.Max(cnt, 1u) + 64);
            try
            {
                for (int i = 0; i < one * (int)Math.Max(cnt, 1u) + 64; i++) Marshal.WriteByte(buf, i, 0xCC);
                bool ok = GetPhysicalMonitorsFromHMONITOR(hMon, cnt, buf);
                int err = Marshal.GetLastWin32Error();
                sb.AppendLine("  [buffer ] GetPhysicalMonitorsFromHMONITOR -> " + ok + " err=" + err);
                sb.AppendLine("    raw bytes (0xCC = untouched):" + HexDump(buf, Math.Min(48, one + 16)));
                IntPtr h = Marshal.ReadIntPtr(buf);
                string d = Marshal.PtrToStringUni(new IntPtr(buf.ToInt64() + IntPtr.Size));
                sb.AppendLine("    handle=0x" + h.ToString("X") + " desc=\"" + (d == null ? "" : d) + "\"");
            }
            finally { Marshal.FreeHGlobal(buf); }

            // attempt 2: struct array
            if (cnt > 0)
            {
                PHYSICAL_MONITOR[] arr = new PHYSICAL_MONITOR[cnt];
                bool ok2 = GetPhysicalMonitorsFromHMONITOR_Arr(hMon, cnt, arr);
                int err2 = Marshal.GetLastWin32Error();
                sb.AppendLine("  [array  ] GetPhysicalMonitorsFromHMONITOR -> " + ok2 + " err=" + err2);
                sb.AppendLine("    arr[0].handle=0x" + arr[0].hPhysicalMonitor.ToString("X") + " desc=\"" + (arr[0].szPhysicalMonitorDescription == null ? "" : arr[0].szPhysicalMonitorDescription) + "\"");

                // try DDC on whatever handle we got
                IntPtr hh = arr[0].hPhysicalMonitor;
                if (hh == IntPtr.Zero) hh = hmons.Count > 0 ? arr[0].hPhysicalMonitor : IntPtr.Zero;
                sb.AppendLine("    probe DDC with arr handle 0x" + hh.ToString("X") + ":");
                ProbeDdc(hh, sb, "      ");
            }
        }
        return sb.ToString();
    }

    static void ProbeDdc(IntPtr h, StringBuilder sb, string ind)
    {
        if (h == IntPtr.Zero) { sb.AppendLine(ind + "handle is NULL -> cannot call DDC APIs"); return; }
        uint caps = 0, temps = 0;
        bool ok = GetMonitorCapabilities(h, out caps, out temps);
        sb.AppendLine(ind + "GetMonitorCapabilities -> " + ok + " err=" + Marshal.GetLastWin32Error() + " caps=0x" + caps.ToString("X"));
        uint len = 0;
        ok = GetCapabilitiesStringLength(h, out len);
        sb.AppendLine(ind + "GetCapabilitiesStringLength -> " + ok + " err=" + Marshal.GetLastWin32Error() + " len=" + len);
        if (ok && len > 0)
        {
            StringBuilder cs = new StringBuilder((int)len + 2);
            ok = CapabilitiesRequestAndCapabilitiesReply(h, cs, len);
            sb.AppendLine(ind + "CapabilitiesRequestAndCapabilitiesReply -> " + ok + " err=" + Marshal.GetLastWin32Error());
            if (ok) sb.AppendLine(ind + "caps=" + cs.ToString());
        }
        int vct; uint cur, max;
        ok = GetVCPFeatureAndVCPFeatureReply(h, 0x10, out vct, out cur, out max);
        sb.AppendLine(ind + "GetVCPFeature(0x10 brightness) -> " + ok + " err=" + Marshal.GetLastWin32Error() + " cur=" + cur + " max=" + max);
    }
}
'@

[DdcDiag]::Run()
Write-Host ""
Write-Host "dxva2.dll:"
$d = Get-Item "$env:WINDIR\System32\dxva2.dll" -ErrorAction SilentlyContinue
if ($d) { $d | Select-Object FullName, Length, @{n='FileVersion';e={$_.VersionInfo.FileVersion}} | Format-List }
