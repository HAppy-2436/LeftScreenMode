// LeftScreenMode - Win32 interop layer
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace LeftScreenMode
{
    internal static class Native
    {
        // ---- SystemParametersInfo ----
        public const int SPI_GETWORKAREA = 0x0030;
        public const int SPI_SETWORKAREA = 0x002F;
        public const int SPIF_UPDATEINIFILE = 0x01;
        public const int SPIF_SENDCHANGE = 0x02;

        // ---- ShowWindow ----
        public const int SW_HIDE = 0;
        public const int SW_MAXIMIZE = 3;
        public const int SW_RESTORE = 9;
        public const int SW_SHOWNOACTIVATE = 4;

        // ---- window styles ----
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        public const int SWP_NOSIZE = 0x0001;
        public const int SWP_NOMOVE = 0x0002;
        public const int SWP_NOZORDER = 0x0004;
        public const int SWP_NOACTIVATE = 0x0010;
        public const int SWP_FRAMECHANGED = 0x0020;
        public const int SWP_SHOWWINDOW = 0x0040;
        public const int SWP_NOOWNERZORDER = 0x0200;

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        public const int LWA_ALPHA = 0x02;

        public const int MONITOR_DEFAULTTONEAREST = 2;
        public const uint MONITORINFOF_PRIMARY = 1;

        public const int MOD_ALT = 0x0001;
        public const int MOD_CONTROL = 0x0002;
        public const int MOD_SHIFT = 0x0004;
        public const int MOD_WIN = 0x0008;
        public const int MOD_NOREPEAT = 0x4000;

        public const int WM_HOTKEY = 0x0312;
        public const int WM_DISPLAYCHANGE = 0x007E;
        public const int WM_MOUSEACTIVATE = 0x0021;
        public const int WM_NCHITTEST = 0x0084;
        public const int MA_NOACTIVATE = 3;
        public const int HTTRANSPARENT = -1;

        public const int ATTACH_PARENT_PROCESS = -1;
        public const int STD_OUTPUT_HANDLE = -11;

        public const int DWMWA_CLOAKED = 14;

        public const uint GW_OWNER = 4;

        // ---- structures ----
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public Rectangle ToRectangle()
            {
                return Rectangle.FromLTRB(Left, Top, Right, Bottom);
            }

            public static RECT FromRectangle(Rectangle r)
            {
                RECT x = new RECT();
                x.Left = r.Left;
                x.Top = r.Top;
                x.Right = r.Right;
                x.Bottom = r.Bottom;
                return x;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

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
        public struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr dwData);
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        // ---- user32 ----
        [DllImport("user32.dll")]
        public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SystemParametersInfo(int uiAction, int uiParam, ref RECT pvParam, int fWinIni);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, int uFlags);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int RegisterWindowMessage(string lpString);
        // ---- 广播设置变更（让任务栏立刻重排，不需要重启 Explorer） ----
        public const int WM_SETTINGCHANGE = 0x001A;
        public const int SMTO_ABORTIFHUNG = 0x0002;
        public static readonly IntPtr HWND_BROADCAST = new IntPtr(0xFFFF);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SendMessageTimeout(IntPtr hWnd, int Msg, IntPtr wParam, string lParam, int fuFlags, int uTimeout, out IntPtr lpdwResult);

        public static void BroadcastSettingChange(string setting)
        {
            try
            {
                IntPtr res;
                SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero, setting, SMTO_ABORTIFHUNG, 2000, out res);
            }
            catch
            {
            }
        }

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);

        // ---- AppBar（Shell 停靠栏）----
        public const uint ABM_NEW = 0x00000000;
        public const uint ABM_REMOVE = 0x00000001;
        public const uint ABM_QUERYPOS = 0x00000002;
        public const uint ABM_SETPOS = 0x00000003;
        public const uint ABE_LEFT = 0;
        public const uint ABE_TOP = 1;
        public const uint ABE_RIGHT = 2;
        public const uint ABE_BOTTOM = 3;
        public const int WM_APPBAR_CALLBACK = 0x8000 + 0x123;
        public const int ABN_POSCHANGED = 1;

        [StructLayout(LayoutKind.Sequential)]
        public struct APPBARDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage;
            public uint uEdge;
            public RECT rc;
            public IntPtr lParam;
        }

        [DllImport("shell32.dll", SetLastError = true)]
        public static extern uint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

        public static bool CreateAppBar(IntPtr hwnd, uint edge, Rectangle rcRect, uint callbackMessage)
        {
            APPBARDATA abd = new APPBARDATA();
            abd.cbSize = Marshal.SizeOf(typeof(APPBARDATA));
            abd.hWnd = hwnd;
            abd.uCallbackMessage = callbackMessage;
            abd.uEdge = edge;
            abd.rc = RECT.FromRectangle(rcRect);
            uint ok = SHAppBarMessage(ABM_NEW, ref abd);
            if (ok == 0) return false;
            return SetAppBarPosition(hwnd, edge, rcRect);
        }

        public static bool SetAppBarPosition(IntPtr hwnd, uint edge, Rectangle rcRect)
        {
            APPBARDATA abd = new APPBARDATA();
            abd.cbSize = Marshal.SizeOf(typeof(APPBARDATA));
            abd.hWnd = hwnd;
            abd.uEdge = edge;
            abd.rc = RECT.FromRectangle(rcRect);
            SHAppBarMessage(ABM_QUERYPOS, ref abd);
            uint s = SHAppBarMessage(ABM_SETPOS, ref abd);
            return s != 0;
        }

        public static void RemoveAppBar(IntPtr hwnd)
        {
            APPBARDATA abd = new APPBARDATA();
            abd.cbSize = Marshal.SizeOf(typeof(APPBARDATA));
            abd.hWnd = hwnd;
            SHAppBarMessage(ABM_REMOVE, ref abd);
        }
        // ---- window placement（精确恢复窗口状态用） ----
        public const int SW_SHOWNORMAL = 1;
        public const int SW_SHOWMINIMIZED = 2;
        public const int SW_SHOWMAXIMIZED = 3;

        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPLACEMENT
        {
            public int length;
            public int flags;
            public int showCmd;
            public POINT ptMinPosition;
            public POINT ptMaxPosition;
            public RECT rcNormalPosition;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);
        // ---- 子窗口枚举 / 窗口区域（任务栏半边显示用） ----
        public delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect);
        // ---- 低级键盘钩子（比例热键 + 数字输入） ----
        public const int WH_KEYBOARD_LL = 13;
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int VK_ESCAPE = 0x1B;

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        // ---- dwmapi ----
        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        // ---- kernel32 ----
        [DllImport("kernel32.dll")]
        public static extern bool AttachConsole(int dwProcessId);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetStdHandle(int nStdHandle);

        // ---- helpers ----
        public static Rectangle GetWorkArea()
        {
            RECT rc = new RECT();
            if (SystemParametersInfo(SPI_GETWORKAREA, 0, ref rc, 0))
                return rc.ToRectangle();
            return Rectangle.Empty;
        }

        public static bool SetWorkArea(Rectangle r)
        {
            RECT rc = RECT.FromRectangle(r);
            return SystemParametersInfo(SPI_SETWORKAREA, 0, ref rc, SPIF_SENDCHANGE);
        }

        public static string GetClassNameSafe(IntPtr hwnd)
        {
            StringBuilder sb = new StringBuilder(256);
            try
            {
                GetClassName(hwnd, sb, sb.Capacity);
            }
            catch
            {
            }
            return sb.ToString();
        }

        public static Rectangle GetWindowRectangle(IntPtr hwnd)
        {
            RECT r;
            if (GetWindowRect(hwnd, out r))
                return r.ToRectangle();
            return Rectangle.Empty;
        }
    }

    internal sealed class MonitorDesc
    {
        public IntPtr Handle;
        public string DeviceName = "";
        public string MonitorId = "";
        public Rectangle Bounds;
        public Rectangle WorkArea;
        public bool Primary;

        public override string ToString()
        {
            return DeviceName + (Primary ? " [主]" : "") + "  " + Bounds.Width + "x" + Bounds.Height +
                   " @ (" + Bounds.X + "," + Bounds.Y + ")";
        }
    }

    internal static class MonitorUtil
    {
        public static List<MonitorDesc> All()
        {
            List<MonitorDesc> list = new List<MonitorDesc>();
            Native.MonitorEnumProc cb = delegate(IntPtr hMon, IntPtr hdc, IntPtr lprc, IntPtr data)
            {
                Native.MONITORINFOEX mi = new Native.MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf(typeof(Native.MONITORINFOEX));
                if (!Native.GetMonitorInfo(hMon, ref mi))
                    return true;

                MonitorDesc d = new MonitorDesc();
                d.Handle = hMon;
                d.DeviceName = mi.szDevice;
                d.Bounds = Rectangle.FromLTRB(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right, mi.rcMonitor.Bottom);
                d.WorkArea = Rectangle.FromLTRB(mi.rcWork.Left, mi.rcWork.Top, mi.rcWork.Right, mi.rcWork.Bottom);
                d.Primary = (mi.dwFlags & Native.MONITORINFOF_PRIMARY) != 0;
                d.MonitorId = GetMonitorId(mi.szDevice);
                list.Add(d);
                return true;
            };
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero);
            return list;
        }

        public static string GetMonitorId(string deviceName)
        {
            try
            {
                Native.DISPLAY_DEVICE dd = new Native.DISPLAY_DEVICE();
                dd.cb = Marshal.SizeOf(typeof(Native.DISPLAY_DEVICE));
                if (Native.EnumDisplayDevices(deviceName, 0, ref dd, 0))
                    return dd.DeviceID;
            }
            catch
            {
            }
            return "";
        }

        public static MonitorDesc Select(string spec)
        {
            List<MonitorDesc> all = All();
            if (all.Count == 0)
                return null;

            if (spec == null)
                spec = "auto";
            spec = spec.Trim();
            string lower = spec.ToLowerInvariant();

            if (lower.Length == 0 || lower == "auto" || lower == "primary" || lower == "主屏" || lower == "主显示器")
            {
                foreach (MonitorDesc m in all)
                {
                    if (m.Primary)
                        return m;
                }
                return all[0];
            }

            foreach (MonitorDesc m in all)
            {
                if (string.Equals(m.DeviceName, spec, StringComparison.OrdinalIgnoreCase))
                    return m;
            }

            int idx;
            if (int.TryParse(spec, out idx) && idx >= 0 && idx < all.Count)
                return all[idx];

            foreach (MonitorDesc m in all)
            {
                if (m.MonitorId.Length > 0 && m.MonitorId.ToLowerInvariant().Contains(lower))
                    return m;
            }

            return null;
        }
    }
}
