# 用 UI Automation 读取任务栏各按钮的位置（需要 Windows PowerShell 5.1 运行）
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ClassNameProperty, "Shell_TrayWnd")
$tray = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if ($null -eq $tray) { Write-Output "未找到任务栏"; exit 1 }

$all = $tray.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.Condition]::TrueCondition)
Write-Output ("任务栏元素数: " + $all.Count)
foreach ($e in $all) {
    try {
        $r = $e.Current.BoundingRectangle
        if ($r.Width -le 1 -or $r.Height -le 1) { continue }
        $ct = $e.Current.ControlType.ProgrammaticName.Replace("ControlType.", "")
        $name = $e.Current.Name
        $aid = $e.Current.AutomationId
        $cls = $e.Current.ClassName
        Write-Output ("{0,-12} {1,-22} {2,-30} rect={3},{4},{5},{6}" -f $ct, $aid, $name,
            [int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height)
    } catch { }
}
