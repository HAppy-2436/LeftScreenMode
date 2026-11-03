# 验证 LeftScreenMode（AppBar 工作区约束 + 遮罩 + 切换）
# 用法（在项目目录）: powershell -ExecutionPolicy Bypass -File .\tools\Verify.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$exe = Join-Path $root "LeftScreenMode.exe"
$runtime = Join-Path $root "LeftScreenMode.runtime"
$probe = Join-Path $root "tools\MaxProbe.exe"
$verifyDir = Join-Path $root "tools\verify"
New-Item -ItemType Directory -Force -Path $verifyDir | Out-Null

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class LsmDiag2
{
    [DllImport("user32.dll")] public static extern bool SystemParametersInfo(int a, int b, ref RECT r, int c);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr h, IntPtr c, MonProc cb, IntPtr d);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFOEX mi);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc2 cb, IntPtr l);
    public delegate bool EnumProc2(IntPtr h, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);

    public delegate bool MonProc(IntPtr m, IntPtr dc, IntPtr rc, IntPtr d);

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX { public int cbSize; public RECT mon; public RECT work; public uint flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dev; }

    public static string WorkArea()
    {
        RECT r = new RECT();
        SystemParametersInfo(0x30, 0, ref r, 0);
        return r.L + "," + r.T + "," + r.R + "," + r.B;
    }

    public static string MonWork()
    {
        string s = "?";
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr m, IntPtr dc, IntPtr rc, IntPtr d)
        {
            MONITORINFOEX mi = new MONITORINFOEX();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
            if (GetMonitorInfo(m, ref mi))
                s = mi.dev + " work=" + mi.work.L + "," + mi.work.T + "," + mi.work.R + "," + mi.work.B;
            return true;
        }, IntPtr.Zero);
        return s;
    }

    public static string TaskbarBridge()
    {
        IntPtr tray = FindWindow("Shell_TrayWnd", null);
        if (tray == IntPtr.Zero) return "无任务栏";
        IntPtr res = IntPtr.Zero;
        EnumChildWindows(tray, delegate(IntPtr h, IntPtr l)
        {
            StringBuilder c = new StringBuilder(256);
            GetClassName(h, c, 256);
            if (c.ToString() == "Windows.UI.Composition.DesktopWindowContentBridge") { res = h; return false; }
            return true;
        }, IntPtr.Zero);
        if (res == IntPtr.Zero) return "未找到任务栏内容窗口";
        RECT r;
        GetWindowRect(res, out r);
        return r.L + "," + r.T + "," + r.R + "," + r.B;
    }

    public static string OurWindows(int pid)
    {
        StringBuilder sb = new StringBuilder();
        EnumWindows(delegate(IntPtr h, IntPtr l)
        {
            uint p;
            GetWindowThreadProcessId(h, out p);
            if (p == (uint)pid)
            {
                RECT r;
                GetWindowRect(h, out r);
                int ex = GetWindowLong(h, -20);
                sb.AppendLine("    hwnd=" + h + " rect=" + r.L + "," + r.T + "," + r.R + "," + r.B +
                              " visible=" + IsWindowVisible(h) + " exstyle=0x" + ex.ToString("X"));
            }
            return true;
        }, IntPtr.Zero);
        return sb.ToString();
    }
}
'@

function Capture([string]$path) {
    $bmp = New-Object System.Drawing.Bitmap(1920, 1080)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.CopyFromScreen(0, 0, 0, 0, $bmp.Size)
        $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $g.Dispose(); $bmp.Dispose() }
}

$orig = [LsmDiag2]::WorkArea()
$origMon = [LsmDiag2]::MonWork()
Write-Host ("原始工作区: " + $orig + " | " + $origMon)

$existing = Get-Process -Name LeftScreenMode -ErrorAction SilentlyContinue
if ($existing) { throw "LeftScreenMode 已在运行" }
$ok = $false
try {
    Write-Host "== 开启左侧模式 =="
    Start-Process -FilePath $exe -ArgumentList "--on" | Out-Null
    Start-Sleep -Milliseconds 2500
    $p = Get-Process -Name LeftScreenMode -ErrorAction SilentlyContinue
    if (-not $p) { throw "程序没有保持运行" }
    Write-Host ("进程 PID: " + $p.Id)

    Write-Host "运行信息:"
    if (Test-Path $runtime) { Get-Content -LiteralPath $runtime -Encoding UTF8 | ForEach-Object { "    " + $_ } }
    Write-Host ("SPI 工作区: " + [LsmDiag2]::WorkArea())
    Write-Host ("显示器工作区: " + [LsmDiag2]::MonWork())
    Write-Host ("任务栏内容窗口: " + [LsmDiag2]::TaskbarBridge())
    Write-Host ("TaskbarAl: " + (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' -Name TaskbarAl -ErrorAction SilentlyContinue).TaskbarAl)
    Write-Host "程序窗口:"
    Write-Host ([LsmDiag2]::OurWindows($p.Id))
    Capture (Join-Path $verifyDir "leftmode.png")
    Write-Host "截图: tools\verify\leftmode.png"

    Write-Host "== 新建最大化窗口验证（应限制在左半） =="
    & $probe 2>&1 | Out-String | Write-Host

    Write-Host "== 关闭左侧模式 =="
    & $exe --off | Out-Null
    Start-Sleep -Milliseconds 1500
    Write-Host ("关闭后 SPI 工作区: " + [LsmDiag2]::WorkArea())
    Write-Host ("关闭后显示器工作区: " + [LsmDiag2]::MonWork())
    Write-Host ("关闭后任务栏内容窗口: " + [LsmDiag2]::TaskbarBridge())
    Write-Host ("关闭后 TaskbarAl: " + (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' -Name TaskbarAl -ErrorAction SilentlyContinue).TaskbarAl)
    $p2 = Get-Process -Name LeftScreenMode -ErrorAction SilentlyContinue
    if ($p2) { Write-Host "关闭后程序窗口:"; Write-Host ([LsmDiag2]::OurWindows($p2.Id)) }
    Capture (Join-Path $verifyDir "off.png")

    Write-Host "== 退出程序 =="
    & $exe --quit | Out-Null
    Start-Sleep -Milliseconds 1200
    $p3 = Get-Process -Name LeftScreenMode -ErrorAction SilentlyContinue
    Write-Host ("退出后仍在运行: " + [bool]$p3)
    Write-Host ("退出后工作区: " + [LsmDiag2]::WorkArea() + " | " + [LsmDiag2]::MonWork())

    if ([LsmDiag2]::WorkArea() -ne $orig) { throw "工作区没有恢复!" }
    if ($p3) { throw "程序没有退出!" }
    $ok = $true
    Write-Host "== 验证通过 =="
}
finally {
    $p = Get-Process -Name LeftScreenMode -ErrorAction SilentlyContinue
    if ($p) {
        & $exe --quit | Out-Null
        Start-Sleep -Milliseconds 800
        Stop-Process -Name LeftScreenMode -Force -ErrorAction SilentlyContinue
    }
    if ([LsmDiag2]::WorkArea() -ne $orig) {
        Write-Host ("!! 工作区未恢复，当前: " + [LsmDiag2]::WorkArea())
    }
    if (-not $ok) { Write-Host "!! 验证失败" }
}
