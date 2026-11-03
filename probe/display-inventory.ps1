#Requires -Version 5.1
<#
  display-inventory.ps1 —— 本机显示硬件只读盘点
  ------------------------------------------------------------------
  只读：仅使用 Get-CimInstance / Get-PnpDevice / Get-ItemProperty / Get-ChildItem /
        Test-Path / Get-Process / Get-Service 等查询接口。
  不写注册表、不启停服务/进程、不改显示设置、不安装任何东西。
  输出：stdout（不落盘）。
  用法： pwsh -NoProfile -File .\probe\display-inventory.ps1
#>

$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'

function Section([string]$t) { Write-Output ""; Write-Output ("=" * 100); Write-Output ("## " + $t); Write-Output ("=" * 100) }
function Sub([string]$t) { Write-Output ""; Write-Output ("-- " + $t) }

function U16Str($a) {
    if ($null -eq $a) { return '' }
    $chars = @()
    foreach ($v in $a) { if ($v -ne 0) { $chars += [char]$v } }
    return (-join $chars)
}

function Decode-Edid([byte[]]$e) {
    $r = New-Object System.Collections.Generic.List[string]
    if ($null -eq $e -or $e.Count -lt 128) { $r.Add("  EDID 长度不足 128 字节: " + $(if ($e) { $e.Count } else { 0 })); return $r }
    $r.Add("  原始字节数: " + $e.Count)
    $hdr = (($e[0..7]) | ForEach-Object { $_.ToString('X2') }) -join ' '
    $r.Add("  头部: $hdr  (期望 00 FF FF FF FF FF FF 00)")
    $mfg = ([int]$e[8] -shl 8) -bor [int]$e[9]
    $l1 = [char](64 + (($mfg -shr 10) -band 0x1F))
    $l2 = [char](64 + (($mfg -shr 5) -band 0x1F))
    $l3 = [char](64 + ($mfg -band 0x1F))
    $r.Add("  EDID 厂商ID: 0x" + $mfg.ToString('X4') + " => '" + $l1 + $l2 + $l3 + "'  (Windows PnP ID 前缀)")
    $prod = [int]$e[10] -bor ([int]$e[11] -shl 8)
    $r.Add("  产品代码: 0x" + $prod.ToString('X4') + " (" + $prod + ")")
    $ser = [BitConverter]::ToUInt32($e, 12)
    $r.Add("  序列号: " + $ser + "  制造: " + (1990 + $e[17]) + " 年第 " + $e[16] + " 周")
    $r.Add("  EDID 版本: " + $e[18] + "." + $e[19])
    $r.Add("  视频输入定义: 0x" + $e[20].ToString('X2') + "  (bit7=1 表示数字输入)")
    $r.Add("  屏幕物理尺寸: " + $e[21] + " x " + $e[22] + " cm  (对角约 " + ([math]::Round([math]::Sqrt(($e[21] * $e[21]) + ($e[22] * $e[22])) / 2.54, 1)) + " 英寸)")
    $r.Add("  gamma: " + ([math]::Round((($e[23] + 100) / 100), 2)) + "   feature support: 0x" + $e[24].ToString('X2'))
    # established timings (bytes 35-37)
    $est = @()
    $b35 = $e[35]; $b36 = $e[36]; $b37 = $e[37]
    if ($b35 -band 0x20) { $est += '720x400@70' }; if ($b35 -band 0x10) { $est += '720x400@88' }
    if ($b35 -band 0x08) { $est += '640x480@60' }; if ($b35 -band 0x04) { $est += '640x480@67' }
    if ($b35 -band 0x02) { $est += '640x480@72' }; if ($b35 -band 0x01) { $est += '640x480@75' }
    if ($b36 -band 0x80) { $est += '800x600@56' }; if ($b36 -band 0x40) { $est += '800x600@60' }
    if ($b36 -band 0x20) { $est += '800x600@72' }; if ($b36 -band 0x10) { $est += '800x600@75' }
    if ($b36 -band 0x08) { $est += '832x624@75' }; if ($b36 -band 0x04) { $est += '1024x768@87i' }
    if ($b36 -band 0x02) { $est += '1024x768@60' }; if ($b36 -band 0x01) { $est += '1024x768@70' }
    if ($b37 -band 0x80) { $est += '1024x768@75' }; if ($b37 -band 0x40) { $est += '1280x1024@75' }
    if ($b37 -band 0x20) { $est += '1152x870@75' }
    $r.Add("  既定时序(established timings): " + $(if ($est.Count) { $est -join ', ' } else { '(none)' }))
    # standard timings bytes 38..53
    $std = @()
    for ($i = 0; $i -lt 8; $i++) {
        $o = 38 + 2 * $i
        if ($e[$o] -eq 0x01 -and $e[$o + 1] -eq 0x01) { continue }
        $w = ([int]$e[$o] + 31) * 8
        $ratio = (($e[$o + 1] -shr 6) -band 0x03)
        $hz = ([int]$e[$o + 1] -band 0x3F) + 60
        switch ($ratio) { 0 { $hh = [int]($w / 16 * 10) } 1 { $hh = [int]($w / 4 * 3) } 2 { $hh = [int]($w / 5 * 4) } 3 { $hh = [int]($w / 16 * 9) } }
        $std += ("${w}x${hh}@" + $hz)
    }
    $r.Add("  标准时序(standard timings): " + $(if ($std.Count) { $std -join ', ' } else { '(none)' }))
    # 4 x 18-byte descriptors at 54, 72, 90, 108
    for ($i = 0; $i -lt 4; $i++) {
        $o = 54 + 18 * $i
        if ($e[$o] -eq 0 -and $e[$o + 1] -eq 0) {
            $tag = $e[$o + 3]
            $txt = ''
            if ($tag -in 0xFC, 0xFF, 0xFE) {
                $raw = [System.Text.Encoding]::ASCII.GetString($e[($o + 5)..($o + 17)])
                $txt = $raw.TrimEnd([char]0).TrimEnd([char]10).TrimEnd([char]32)
            }
            switch ($tag) {
                0xFC { $r.Add("  descriptor[$i]: 显示器名称 = '" + $txt + "'") }
                0xFF { $r.Add("  descriptor[$i]: 序列号文本 = '" + $txt + "'") }
                0xFE { $r.Add("  descriptor[$i]: ASCII 文本 = '" + $txt + "'") }
                0xFD {
                    $minV = $e[$o + 5]; $maxV = $e[$o + 6]; $minH = $e[$o + 7]; $maxH = $e[$o + 8]
                    $maxClk = $e[$o + 9] * 10
                    $r.Add("  descriptor[$i]: 范围限制 V=" + $minV + "-" + $maxV + "Hz, H=" + $minH + "-" + $maxH + "kHz, maxPixelClock=" + $maxClk + "MHz")
                }
                default { $r.Add("  descriptor[$i]: tag=0x" + $tag.ToString('X2') + " (未解析)") }
            }
        }
        else {
            $pixClk = ((([int]$e[$o + 1] -shl 8) -bor [int]$e[$o]) * 10) # kHz
            $ha = ([int]$e[$o + 2]) -bor ((([int]$e[$o + 4]) -band 0xF0) -shl 4)
            $hb = ([int]$e[$o + 3]) -bor ((([int]$e[$o + 4]) -band 0x0F) -shl 8)
            $va = ([int]$e[$o + 5]) -bor ((([int]$e[$o + 7]) -band 0xF0) -shl 4)
            $vb = ([int]$e[$o + 6]) -bor ((([int]$e[$o + 7]) -band 0x0F) -shl 8)
            $ht = $ha + $hb; $vt = $va + $vb
            $ref = 0
            if ($ht -gt 0 -and $vt -gt 0) { $ref = [math]::Round(($pixClk * 1000.0) / ($ht * $vt), 1) }
            $r.Add("  descriptor[$i]: 详细时序(DTD) " + $ha + "x" + $va + "@" + $ref + "Hz  htotal=" + $ht + " vtotal=" + $vt + " pixClk=" + [math]::Round($pixClk / 1000.0, 2) + "MHz")
        }
    }
    $extCount = $e[126]
    $r.Add("  extension block 数量: " + $extCount)
    for ($b = 0; $b -lt $extCount; $b++) {
        $base = 128 * ($b + 1)
        if ($e.Count -lt ($base + 128)) { break }
        $tag = $e[$base]
        switch ($tag) {
            0x02 {
                $rev = $e[$base + 1]
                $dtdOffset = $e[$base + 2]
                $r.Add("  ext[$b]: CEA-861 (rev " + $rev + ", dtd offset " + $dtdOffset + ")")
                # data blocks start at base+4，止于 DTD 偏移处
                $p = $base + 4
                $dtdStart = $base + $dtdOffset
                while ($p -lt $dtdStart -and $p -lt ($base + 127)) {
                    $hdr = $e[$p]
                    if ($hdr -eq 0) { break }
                    $blen = $hdr -band 0x1F
                    $btag = ($hdr -shr 5) -band 0x07
                    $btxt = switch ($btag) { 1 { 'Audio Data Block' } 2 { 'Video Data Block(SVD)' } 3 { 'Vendor-Specific Data Block(VSDB)' } 4 { 'Speaker Allocation Data Block' } 5 { 'VESA DTC Data Block' } 6 { 'reserved' } 7 { 'Use Extended Tag' } default { '?' } }
                    $r.Add("    data block tag=" + $btag + " (" + $btxt + ") len=" + $blen)
                    if ($btag -eq 2) {
                        $vics = @()
                        for ($k = 0; $k -lt $blen; $k++) { $vics += $e[$p + 1 + $k] }
                        $r.Add("      SVD/VIC 列表(原始字节): " + (($vics | ForEach-Object { '0x' + $_.ToString('X2') }) -join ' '))
                    }
                    if ($btag -eq 3 -and $blen -ge 3) {
                        # OUI 在 VSDB 中为小端 3 字节
                        $oui = ([int]$e[$p + 1]) -bor ([int]$e[$p + 2] -shl 8) -bor ([int]$e[$p + 3] -shl 16)
                        $ouiName = switch ($oui) { 0x000C03 { 'HDMI Licensing (HDMI VSDB)' } 0xC45DD8 { 'HDMI Forum (HF-VSDB)' } 0x00001A { 'AMD (FreeSync VSDB)' } default { '未识别 OUI' } }
                        $r.Add("      VSDB OUI=0x" + $oui.ToString('X6') + " => " + $ouiName)
                        if ($oui -eq 0x000C03 -and $blen -ge 7) {
                            $pa = $e[$p + 4]; $pb = $e[$p + 5]
                            $r.Add("        HDMI 物理地址: " + (($pa -shr 4) -band 0x0F) + "." + ($pa -band 0x0F) + "." + (($pb -shr 4) -band 0x0F) + "." + ($pb -band 0x0F) + "   maxTMDSclock=" + (5 * $e[$p + 7]) + "MHz")
                        }
                        if ($oui -eq 0xC45DD8 -and $blen -ge 7) {
                            $r.Add("        HF-VSDB 版本=" + $e[$p + 4] + "  maxTMDScharRate=" + (5 * $e[$p + 5]) + "MHz  (600MHz = HDMI 2.0 18Gbps 档)")
                        }
                        if ($oui -eq 0x00001A -and $blen -ge 7) {
                            $r.Add("        AMD FreeSync: 版本=" + $e[$p + 4] + " flags=0x" + $e[$p + 5].ToString('X2') + "  minRefresh=" + $e[$p + 6] + "Hz  maxRefresh=" + $e[$p + 7] + "Hz")
                        }
                    }
                    if ($btag -eq 7 -and $blen -ge 1) {
                        $etag = $e[$p + 1]
                        $ename = switch ($etag) { 0x00 { 'Video Capability Data Block' } 0x05 { 'Colorimetry Data Block' } 0x06 { 'HDR Static Metadata Data Block' } 0x0E { 'YCbCr 4:2:0 Video Data Block' } 0x0F { 'YCbCr 4:2:0 Capability Map' } default { '其他扩展标签' } }
                        $r.Add("      扩展标签=0x" + $etag.ToString('X2') + " (" + $ename + ")")
                    }
                    $p += 1 + $blen
                }
                # CEA 扩展块内的 DTD（本机 260Hz/240Hz/144Hz 等时序就在此）
                $dq = $base + $dtdOffset
                while (($dq + 18) -le ($base + 127)) {
                    $pc2 = (([int]$e[$dq + 1]) -shl 8) -bor [int]$e[$dq]
                    if ($pc2 -eq 0) { break }
                    $ha2 = ([int]$e[$dq + 2]) -bor ((([int]$e[$dq + 4]) -band 0xF0) -shl 4)
                    $hb2 = ([int]$e[$dq + 3]) -bor ((([int]$e[$dq + 4]) -band 0x0F) -shl 8)
                    $va2 = ([int]$e[$dq + 5]) -bor ((([int]$e[$dq + 7]) -band 0xF0) -shl 4)
                    $vb2 = ([int]$e[$dq + 6]) -bor ((([int]$e[$dq + 7]) -band 0x0F) -shl 8)
                    $ht2 = $ha2 + $hb2; $vt2 = $va2 + $vb2
                    $ref2 = 0
                    if ($ht2 -gt 0 -and $vt2 -gt 0) { $ref2 = [math]::Round((($pc2 * 10000.0) / ($ht2 * $vt2)), 1) }
                    $r.Add("    CEA DTD: " + $ha2 + "x" + $va2 + "@" + $ref2 + "Hz  htotal=" + $ht2 + " vtotal=" + $vt2 + " pixClk=" + [math]::Round(($pc2 / 100.0), 2) + "MHz")
                    $dq += 18
                }
            }
            0x70 { $r.Add("  ext[$b]: DisplayID (tag 0x70) —— 高刷新率/自定义时序常在此块") }
            0x40 { $r.Add("  ext[$b]: VESA DTD block (tag 0x40)") }
            default { $r.Add("  ext[$b]: tag=0x" + $tag.ToString('X2') + " (未解析)") }
        }
    }
    return $r
}

# =====================================================================================
Section "0. 系统信息"
$os = Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue
if ($os) {
    Write-Output ("  OS        : " + $os.Caption + " " + $os.Version + " (build " + $os.BuildNumber + ") " + $os.OSArchitecture)
    Write-Output ("  LastBoot  : " + $os.LastBootUpTime)
}
Write-Output ("  本地时间  : " + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz'))
Write-Output ("  会话/用户 : " + $env:SESSIONNAME + " / " + $env:USERNAME)
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
Write-Output ("  管理员权限: " + $isAdmin)

# =====================================================================================
Section "1. 显示适配器 (Win32_VideoController)"
Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Output ""
    Write-Output ("  Name              : " + $_.Name)
    Write-Output ("  PNPDeviceID       : " + $_.PNPDeviceID)
    Write-Output ("  DriverVersion     : " + $_.DriverVersion + "   DriverDate: " + $_.DriverDate)
    Write-Output ("  VideoProcessor    : " + $_.VideoProcessor)
    Write-Output ("  Status            : " + $_.Status + "   ConfigManagerErrorCode: " + $_.ConfigManagerErrorCode)
    Write-Output ("  CurrentMode       : " + $_.VideoModeDescription + "   (" + $_.CurrentHorizontalResolution + "x" + $_.CurrentVerticalResolution + " @ " + $_.CurrentRefreshRate + "Hz, " + $_.CurrentBitsPerPixel + "bpp)")
    Write-Output ("  AdapterRAM        : " + $_.AdapterRAM + "   Compatibility: " + $_.AdapterCompatibility)
}

# =====================================================================================
Section "2. PnP 设备：显示适配器 (Class=Display) 与 显示器 (Class=Monitor)"
Sub "Class=Display (显示适配器)"
Get-PnpDevice -Class Display -ErrorAction SilentlyContinue | Sort-Object FriendlyName, InstanceId |
    Select-Object Status, FriendlyName, InstanceId | Format-Table -AutoSize -Wrap | Out-String -Width 200 | Write-Output
Sub "Class=Monitor (显示器设备节点)"
Get-PnpDevice -Class Monitor -ErrorAction SilentlyContinue | Sort-Object InstanceId |
    Select-Object Status, FriendlyName, InstanceId | Format-Table -AutoSize -Wrap | Out-String -Width 200 | Write-Output
Sub "Monitor 设备节点的 problem code / 关键属性"
Get-PnpDevice -Class Monitor -ErrorAction SilentlyContinue | ForEach-Object {
    $id = $_.InstanceId
    $prob = $null
    try { $prob = (Get-PnpDeviceProperty -InstanceId $id -KeyName 'DEVPKEY_Device_ProblemCode' -ErrorAction Stop).Data } catch { }
    Write-Output ("  " + $id)
    Write-Output ("    Status=" + $_.Status + "  ProblemCode=" + $prob)
}

# =====================================================================================
Section "3. WMI MSMonitorClass：显示器 ID / 物理尺寸 / 连接方式 / 亮度"
Sub "WmiMonitorID"
Get-CimInstance -Namespace root\wmi -ClassName WmiMonitorID -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Output ("  InstanceName        : " + $_.InstanceName)
    Write-Output ("    Active            : " + $_.Active)
    Write-Output ("    ManufacturerName  : " + (U16Str $_.ManufacturerName) + "  (raw: " + (($_.ManufacturerName) -join ',') + ")")
    Write-Output ("    ProductCodeID     : " + (U16Str $_.ProductCodeID))
    Write-Output ("    UserFriendlyName  : " + (U16Str $_.UserFriendlyName))
    Write-Output ("    SerialNumberID    : " + (U16Str $_.SerialNumberID))
    Write-Output ("    Year/Week         : " + $_.YearOfManufacture + " / " + $_.WeekOfManufacture)
}
Sub "WmiMonitorBasicDisplayParams"
Get-CimInstance -Namespace root\wmi -ClassName WmiMonitorBasicDisplayParams -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Output ("  " + $_.InstanceName + "  Active=" + $_.Active + "  MaxImage=" + $_.MaxHorizontalImageSize + "x" + $_.MaxVerticalImageSize + "cm  VideoInputType=" + $_.VideoInputType + "  TransferChar=" + $_.DisplayTransferCharacteristic)
}
Sub "WmiMonitorConnectionParams (VideoOutputTechnology: 5=DVI, 6=HDMI, 10=DP外部, 11=DP内嵌, 0x80000000=内部)"
Get-CimInstance -Namespace root\wmi -ClassName WmiMonitorConnectionParams -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Output ("  " + $_.InstanceName + "  Active=" + $_.Active + "  VideoOutputTechnology=" + $_.VideoOutputTechnology)
}
Sub "WmiMonitorBrightness / WmiMonitorBrightnessMethods (若存在)"
$br = Get-CimInstance -Namespace root\wmi -ClassName WmiMonitorBrightness -ErrorAction SilentlyContinue
if ($br) { $br | ForEach-Object { Write-Output ("  " + $_.InstanceName + "  CurrentBrightness=" + $_.CurrentBrightness + "  Levels=" + ($_.Levels -join ',')) } } else { Write-Output "  (无 WmiMonitorBrightness 实例)" }
$bm = Get-CimInstance -Namespace root\wmi -ClassName WmiMonitorBrightnessMethods -ErrorAction SilentlyContinue
if ($bm) { $bm | ForEach-Object { Write-Output ("  " + $_.InstanceName + "  (仅枚举实例，未调用任何写方法)") } } else { Write-Output "  (无 WmiMonitorBrightnessMethods 实例)" }

# =====================================================================================
Section "4. EDID 原始数据（注册表 Enum\DISPLAY\*，只读）与解码"
$displayRoot = 'HKLM:\SYSTEM\CurrentControlSet\Enum\DISPLAY'
if (Test-Path $displayRoot) {
    Get-ChildItem $displayRoot -ErrorAction SilentlyContinue | ForEach-Object {
        $mfgKey = $_
        Get-ChildItem $mfgKey.PSPath -ErrorAction SilentlyContinue | ForEach-Object {
            $instKey = $_
            $dpPath = "$($instKey.PSPath)\Device Parameters"
            $edid = $null
            try { $edid = (Get-ItemProperty -Path $dpPath -Name EDID -ErrorAction Stop).EDID } catch { }
            Write-Output ""
            Write-Output ("  PnP ID: DISPLAY\" + $mfgKey.PSChildName + "\" + $instKey.PSChildName)
            if ($null -eq $edid) { Write-Output "    (无 EDID 值或不可读)"; return }
            Decode-Edid ([byte[]]$edid) | ForEach-Object { Write-Output $_ }
        }
    }
}
else { Write-Output "  无法读取 $displayRoot" }

# =====================================================================================
Section "5. 当前活动显示配置（只读）"
Sub "HKCU PerMonitorSettings（每显示器 DPI 缩放）"
$pms = 'HKCU:\Control Panel\Desktop\PerMonitorSettings'
if (Test-Path $pms) {
    Get-ChildItem $pms -ErrorAction SilentlyContinue | ForEach-Object {
        $k = $_
        $vals = @()
        foreach ($n in $k.Property) { $vals += ($n + '=' + (Get-ItemPropertyValue -Path $k.PSPath -Name $n -ErrorAction SilentlyContinue)) }
        Write-Output ("  " + $k.PSChildName + "  ->  " + ($vals -join '; '))
    }
}
else { Write-Output "  (无 PerMonitorSettings)" }
Sub "HKLM GraphicsDrivers\Configuration 子键（当前拓扑缓存，只列名字与主要尺寸值）"
$cfg = 'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\Configuration'
if (Test-Path $cfg) {
    Get-ChildItem $cfg -ErrorAction SilentlyContinue | ForEach-Object {
        $c = $_
        Get-ChildItem $c.PSPath -ErrorAction SilentlyContinue | ForEach-Object {
            $p = Get-ItemProperty -Path $_.PSPath -ErrorAction SilentlyContinue
            Write-Output ("  " + $c.PSChildName + " / " + $_.PSChildName + "  PrimSurf=" + $p.'PrimSurfSize.cx' + "x" + $p.'PrimSurfSize.cy' + "  ActiveSize=" + $p.'ActiveSize.cx' + "x" + $p.'ActiveSize.cy')
        }
    }
}
else { Write-Output "  (无 GraphicsDrivers\Configuration)" }

# =====================================================================================
Section "6. AMD Adrenalin / Radeon 软件安装与驱动（只读注册表/目录）"
Sub "HKLM:\SOFTWARE\AMD 下的键与值（截取）"
foreach ($p in @('HKLM:\SOFTWARE\AMD', 'HKLM:\SOFTWARE\WOW6432Node\AMD')) {
    if (Test-Path $p) {
        Write-Output ("  [" + $p + "]")
        Get-ChildItem $p -ErrorAction SilentlyContinue | Select-Object -First 30 | ForEach-Object {
            Write-Output ("    subkey: " + $_.PSChildName)
            $props = @()
            try { $props = (Get-Item -Path $_.PSPath -ErrorAction Stop).Property } catch { }
            if ($props) {
                foreach ($n in $props) {
                    $v = $null
                    try { $v = Get-ItemPropertyValue -Path $_.PSPath -Name $n -ErrorAction Stop } catch { }
                    Write-Output ("       " + $n + " = " + $v)
                }
            }
        }
        $rootProps = @()
        try { $rootProps = (Get-Item -Path $p -ErrorAction Stop).Property } catch { }
        foreach ($n in $rootProps) {
            $v = $null
            try { $v = Get-ItemPropertyValue -Path $p -Name $n -ErrorAction Stop } catch { }
            Write-Output ("    " + $n + " = " + $v)
        }
    }
    else { Write-Output ("  [" + $p + "] 不存在") }
}
Sub "AMD 显示驱动类键 (Class\{4d36e968-...}) 下各实例"
$cls = 'HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}'
if (Test-Path $cls) {
    Get-ChildItem $cls -ErrorAction SilentlyContinue | Where-Object { $_.PSChildName -match '^\d{4}$' } | ForEach-Object {
        $p = Get-ItemProperty -Path $_.PSPath -ErrorAction SilentlyContinue
        if ($p.DriverDesc) {
            Write-Output ("  [" + $_.PSChildName + "] " + $p.DriverDesc)
            Write-Output ("     DriverVersion=" + $p.DriverVersion + "  DriverDate=" + $p.DriverDate + "  ProviderName=" + $p.ProviderName + "  InfPath=" + $p.InfPath)
            Write-Output ("     MatchingDeviceId=" + $p.MatchingDeviceId)
        }
    }
}
Sub "已安装程序（卸载表）中匹配 AMD / Radeon / GameViewer / UU / 网易"
$uninst = @()
foreach ($u in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*')) {
    $uninst += Get-ItemProperty -Path $u -ErrorAction SilentlyContinue
}
$uninst | Where-Object { $_.DisplayName -match 'AMD|Radeon|Adrenalin|GameViewer|UU|网易|NetEase' } |
    Select-Object DisplayName, DisplayVersion, Publisher, InstallLocation, InstallDate |
    Format-Table -AutoSize -Wrap | Out-String -Width 220 | Write-Output
Sub "AMD / Radeon 相关目录与可执行文件"
foreach ($f in @(
        'C:\Program Files\AMD\CNext\CNext\RadeonSoftware.exe',
        'C:\Program Files\AMD\CNext\CNext64\RadeonSoftware.exe',
        'C:\Program Files\AMD\CNext\CNext\AMDSoftwareInstaller.exe',
        'C:\Program Files\AMD\CNext\CNext\RadeonSettings.exe',
        'C:\Program Files\AMD\CNext\CNext\cncmd.exe',
        'C:\Program Files\AMD\CNext\CNext\Dashboard\RadeonSoftware.exe',
        'C:\Program Files\AMD\CNext',
        'C:\Program Files\AMD')) {
    if (Test-Path $f) {
        $it = Get-Item $f -ErrorAction SilentlyContinue
        if ($it -and -not $it.PSIsContainer) { Write-Output ("  [EXISTS] " + $f + "   ver=" + $it.VersionInfo.FileVersion + "  size=" + $it.Length) }
        else { Write-Output ("  [DIR]    " + $f) }
    }
    else { Write-Output ("  [MISSING] " + $f) }
}
Sub "AMD 相关进程（只列名，不操作）"
Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match 'Radeon|AMD|atiesrxx|amdfendr|AUEP' } |
    Select-Object ProcessName, Id | Format-Table -AutoSize | Out-String -Width 120 | Write-Output

# =====================================================================================
Section "7. GameViewer（网易UU远程）虚拟显示适配器 —— 只读状态"
Sub "PnP 设备中匹配 GameViewer / Virtual Display / UU"
Get-PnpDevice -ErrorAction SilentlyContinue | Where-Object { $_.FriendlyName -match 'GameViewer|Virtual Display|UU|NetEase|网易' -or $_.InstanceId -match 'GAMEVIEWER|UU|NETEASE' } |
    Select-Object Status, Class, FriendlyName, InstanceId | Format-Table -AutoSize -Wrap | Out-String -Width 220 | Write-Output
Sub "服务中匹配 GameViewer / UU / NetEase"
Get-Service -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'GameViewer|UU|NetEase|NvStream|Sunlogin' -or $_.DisplayName -match 'GameViewer|UU|网易|NetEase' } |
    Select-Object Status, StartType, Name, DisplayName | Format-Table -AutoSize -Wrap | Out-String -Width 200 | Write-Output
Sub "注册表 Services 中匹配 GameViewer / UU / NetEase"
$svcRoot = 'HKLM:\SYSTEM\CurrentControlSet\Services'
if (Test-Path $svcRoot) {
    Get-ChildItem $svcRoot -ErrorAction SilentlyContinue | Where-Object { $_.PSChildName -match 'GameViewer|^UU|NetEase|netease' } | ForEach-Object {
        $p = Get-ItemProperty -Path $_.PSPath -ErrorAction SilentlyContinue
        Write-Output ("  " + $_.PSChildName + "  DisplayName=" + $p.DisplayName + "  Start=" + $p.Start + "  Type=" + $p.Type + "  ImagePath=" + $p.ImagePath)
    }
}
Sub "GameViewer 相关进程（只列名，不操作）"
Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match 'GameViewer|uu|UU|NetEase' } |
    Select-Object ProcessName, Id | Format-Table -AutoSize | Out-String -Width 120 | Write-Output
Sub "安装目录探测"
foreach ($d in @('C:\Program Files\GameViewer', 'C:\Program Files (x86)\GameViewer', 'C:\Program Files\NetEase', 'C:\Program Files (x86)\NetEase', 'C:\Program Files\Netease\GameViewer')) {
    if (Test-Path $d) { Write-Output ("  [EXISTS] " + $d); Get-ChildItem $d -ErrorAction SilentlyContinue | Select-Object -First 15 Name, Length | Format-Table -AutoSize | Out-String -Width 120 | Write-Output }
    else { Write-Output ("  [MISSING] " + $d) }
}
Write-Output ""
Write-Output ("  Program Files 下名字含 GameViewer/UU/NetEase 的目录:")
Get-ChildItem 'C:\Program Files', 'C:\Program Files (x86)' -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'GameViewer|UU|NetEase|网易' } | ForEach-Object { Write-Output ("    " + $_.FullName) }

# =====================================================================================
Section "8. 与 DDC/CI 可能相关的注册表痕迹（只读）"
Sub "显示器设备实例的 Device Parameters 值名"
Get-ChildItem 'HKLM:\SYSTEM\CurrentControlSet\Enum\DISPLAY' -ErrorAction SilentlyContinue | ForEach-Object {
    $mfg = $_.PSChildName
    Get-ChildItem $_.PSPath -ErrorAction SilentlyContinue | ForEach-Object {
        $inst = $_.PSChildName
        $dp = "$($_.PSPath)\Device Parameters"
        if (Test-Path $dp) {
            $props = @()
            try { $props = (Get-Item -Path $dp -ErrorAction Stop).Property } catch { }
            Write-Output ("  DISPLAY\" + $mfg + "\" + $inst + "  ->  " + ($props -join ', '))
        }
    }
}
Sub "dxva2.dll 文件信息"
$dx = Get-Item "$env:WINDIR\System32\dxva2.dll" -ErrorAction SilentlyContinue
if ($dx) { Write-Output ("  " + $dx.FullName + "  version=" + $dx.VersionInfo.FileVersion + "  size=" + $dx.Length) }

Write-Output ""
Write-Output "### END OF INVENTORY"
