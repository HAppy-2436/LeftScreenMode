# 构建 LeftScreenMode.exe
# 用法: powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root "src"
$out = Join-Path $root "LeftScreenMode.exe"

$cscCandidates = @(
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)
$csc = $null
foreach ($c in $cscCandidates) {
    if (Test-Path -LiteralPath $c) { $csc = $c; break }
}
if ($null -eq $csc) {
    throw "未找到 .NET Framework 4.x 编译器 csc.exe"
}

$ico = Join-Path $src "app.ico"
if (-not (Test-Path -LiteralPath $ico)) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $src "MakeIcon.ps1") -Out $ico
}

$manifest = Join-Path $src "app.manifest"
$sources = @(
    (Join-Path $src "Native.cs"),
    (Join-Path $src "LeftScreenMode.cs")
)

$args = @(
    "/nologo",
    "/target:winexe",
    "/platform:anycpu",
    "/optimize+",
    "/codepage:65001",
    "/utf8output",
    "/debug-",
    ("/win32manifest:" + $manifest),
    ("/win32icon:" + $ico),
    "/r:System.dll",
    "/r:System.Core.dll",
    "/r:System.Drawing.dll",
    "/r:System.Windows.Forms.dll",
    ("/out:" + $out)
)
$args += $sources

Write-Host "编译中..."
& $csc $args
if ($LASTEXITCODE -ne 0) {
    throw ("编译失败，退出码 " + $LASTEXITCODE)
}
Write-Host ("编译成功: " + $out)
Get-Item -LiteralPath $out | Select-Object Name, Length, LastWriteTime | Format-List
