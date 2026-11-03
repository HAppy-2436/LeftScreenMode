# 创建桌面快捷方式：左侧模式 / 全屏模式 / 托盘程序
# 用法: 右键“使用 PowerShell 运行”，或在 PowerShell 中执行：
#   powershell -ExecutionPolicy Bypass -File .\创建桌面快捷方式.ps1

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $root "LeftScreenMode.exe"
if (-not (Test-Path -LiteralPath $exe)) { throw "未找到 LeftScreenMode.exe，请把本脚本放在程序目录" }

$desktop = [Environment]::GetFolderPath("Desktop")
$shell = New-Object -ComObject WScript.Shell

function New-Shortcut([string]$path, [string]$arguments, [string]$description) {
    $sc = $shell.CreateShortcut($path)
    $sc.TargetPath = $exe
    $sc.Arguments = $arguments
    $sc.WorkingDirectory = $root
    $sc.Description = $description
    $sc.IconLocation = "$exe,0"
    $sc.Save()
}

New-Shortcut (Join-Path $desktop "左侧模式.lnk") "--on" "切换到左侧模式（右半屏黑）"
New-Shortcut (Join-Path $desktop "全屏模式.lnk") "--off" "切换回全屏正常显示"
New-Shortcut (Join-Path $desktop "LeftScreenMode 托盘.lnk") "" "启动 LeftScreenMode 托盘程序"

Write-Host "已在桌面创建："
Write-Host "  左侧模式.lnk   -> LeftScreenMode.exe --on"
Write-Host "  全屏模式.lnk   -> LeftScreenMode.exe --off"
Write-Host "  LeftScreenMode 托盘.lnk -> 启动托盘程序"
Write-Host ""
Write-Host "提示：若程序尚未运行，双击任一快捷方式都会自动启动它并应用对应模式。"
