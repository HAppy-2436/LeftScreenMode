# CRU / EDID 可行性调研：能否「只用左半屏、右半屏纯黑、内容左对齐」

> **任务**：task-1（团队调研员 cru-researcher）
> **用户场景**：Windows 11 24H2 笔记本（机械革命 WUJIE，AMD Radeon 880M 核显，无独显，Adrenalin 32.0.23017.1001）；唯一活动显示器为外接 24.5" 1920x1080@260Hz（PnP ID `SKG2526`，名称 "25S1"，HDMI，主屏，100% 缩放）；屏幕右侧被物理遮挡。
> **目标**：快速切换 A. 全屏 1920x1080；B. 桌面内容全部限制在左半边（约 960x1080），右半边纯黑。
> **调研日期**：2026-11-03（以下所有 URL 的访问日期均为 2026-11-03）
> **约束**：只读调研。未修改本机任何显示设置/分辨率/EDID，未安装或下载软件，未使用管理员命令。本文所有"本机"结论均为**基于公开资料的推断**，未经真机实测。

---

## 0. 结论摘要（TL;DR）

1. **CRU 能做什么**：让 Windows/显卡驱动"以为"显示器支持一条自定义时序（例如 960x1080@60），并在你选择该分辨率时把这条时序作为**活动信号**发给显示器。**CRU 不能做什么**：决定这条信号在面板上画在哪、占多大。缩放与定位由 **GPU 缩放器**或**显示器 scaler** 决定。CRU 作者 ToastyX 原话：**"CRU doesn't deal with scaling. Scaling is handled by the monitor with display scaling or by the GPU with GPU scaling."**（[来源](https://www.monitortests.com/forum/Thread-Integer-scaling-with-CRU-created-resolutions)，等级 B）
2. **B 目标（内容左对齐 + 右半纯黑）在 CRU/EDID 路径上不可实现**，除非显示器 OSD 在 HDMI 输入下提供可用的水平位置（H-Position）调节——这在消费级显示器上极少见，且需要每次进 OSD 操作，与"快速切换"矛盾。半宽时序会被显示器 scaler 处理成**拉伸全屏**或**居中 + 左右两条黑边**，不会左对齐。
3. **非对称消隐（大 front/back porch）在数字 HDMI 上不改变画面位置**：HDMI/DP 用 **DE（Data Enable）** 界定有效像素窗口，scaler 把 DE 窗口当作整幅源图像；porch 只影响同步/扫描速率，不携带"图像在面板上的位置"信息。模拟 VGA 时代的"位置调节"机制在数字接口上不存在（多数显示器 OSD 的 `Auto` 图像位置/时钟调节也只在模拟输入可用，见 §2.3）。
4. **把 EDID 原生分辨率声明成 960x1080 只改变"PC 发什么信号"，不改变"显示器怎么显示"**：显示器仍会把收到的 960x1080 拉伸或居中（§2.4）。
5. **驱动层只能居中/拉伸，没有 offset**：Windows 的 `DISPLAYCONFIG_SCALING` 只有 identity / centered / stretched / aspect-centered / custom(厂商私有)；NVIDIA 控制面板的 Position（位置）页**仅对模拟 TV/VGA 开放**（官方文档表格）；AMD/Intel 完全没有位置控制。所以用户"显卡驱动做不到"的直觉是对的，但 **CRU 同样做不到**——这是"合成层"问题，不是"时序/EDID"问题（§3）。
6. **建议**：**不要**为这个目标使用 CRU（风险/收益极差）。正确层是**软件合成**（DWM 叠加一个覆盖右半屏的黑色置顶窗口 + 把工作区限制为 960 宽），可瞬时切换、可逆、保留 1920x1080@260、无需管理员/重启。CRU 仅在"显示器 OSD 确实有 HDMI 可用的 1:1 + H-Position"这一小概率前提下才值得一试（§4）。

### 0.1 路径判定表

| 路径 | 做法 | 显示器最终呈现 | 能否左对齐 | 判定 |
|---|---|---|---|---|
| (a) 半宽 Detail Timing | CRU 加 960x1080 时序，Windows 切该分辨率 | 全屏拉伸 或 居中（左右黑边） | 否（除非 OSD 有 H-Position） | **不可行** |
| (b) 非对称消隐 | HActive=960、HBlank=960（HTotal 保持 1920）或大 front/back porch | 与 (a) 相同：显示器按 DE 识别为 960x1080 源 | 否 | **不可行/不可控** |
| (c) EDID 原生=半宽 | 第一条 Detailed Timing 改成 960x1080 | 显示器仍按自身 OSD 缩放/居中 | 否 | **不可行**（且有副作用） |
| (d) OSD 组合 | 960x1080 + 1:1/点对点 + H-Position 左移 | 若 H-Position 在 HDMI 下可用 → 可左对齐 | **理论上可以** | **唯一有理论可能的路径，但依赖 OSD、非快速切换** |
| (e) 软件合成（非 CRU） | DWM 黑色置顶窗口 + 工作区限制 | 原生 1920x1080 上，右侧被纯黑窗口覆盖 | **可以** | **推荐**（另一任务范围） |

### 0.2 证据等级说明

| 等级 | 含义 |
|---|---|
| **A** | 官方文档：VESA/Microsoft/NVIDIA/Intel/AMD 官方文档，或工具作者在官方发布页的说明 |
| **B** | 工具作者（ToastyX）在论坛的明确答复、可复现的开发者级实测 |
| **C** | 社区个案/第三方教程/论坛经验，未交叉验证 |
| **D** | 纯技术推断（本文基于协议语义的推理，无公开实证） |

> **本环境限制**：`amd.com` 的支持文章在本调研环境被网络策略阻断（多次 `fetch failed`），AMD 官方页面只能列出 URL 供人工核对；AMD 侧具体 UI 以工具作者说明 + 第三方指南为准（等级 B/C）。

---

## 1. CRU 到底是什么、改什么、在 Win11 24H2 + AMD 880M 上如何工作

### 1.1 定位与原理

CRU（Custom Resolution Utility，作者 ToastyX，官网 monitortests.com）的官方定义：

> "Custom Resolution Utility (CRU) is an **EDID editor** that focuses on custom resolutions. CRU shows you how the monitor defines resolutions and other capabilities and gives you the power to change it. Add custom resolutions, remove unwanted resolutions, edit FreeSync ranges, and more. **CRU creates software EDID overrides in the registry and does not modify the hardware.**"
> —— [CRU 官方发布帖](https://www.monitortests.com/forum/Thread-Custom-Resolution-Utility-CRU)（当前版本 1.5.3，最后编辑 2026-01-26），等级 A

关键点：
- CRU 是**纯软件层的 EDID 覆盖**：改的是 Windows 注册表里"这台显示器支持什么"的数据，**不写入显示器 EEPROM、不改硬件**（写 EEPROM 是另一个工具 EDID/DisplayID Writer 的事）。
- 因此 CRU **不涉及驱动签名/Secure Boot**（它不修改任何 `.sys` 文件）；只需要管理员权限写 `HKLM`。
- 官方 Requirements：Windows Vista 或更高；**AMD/ATI 或 NVIDIA GPU 且安装了正常驱动**；Microsoft Basic Display Adapter（基本显示适配器）不支持 EDID override；Intel GPU/双显卡笔记本需要特定驱动版本，且 **Intel 下 CRU 读不到扩展块**。（同上，等级 A）

### 1.2 CRU 能改哪些东西

| 类别 | 可改内容 | 限制/注意 |
|---|---|---|
| **Detailed Resolutions（详细分辨率）** | 新增/删除/编辑：H/V **Active**（有效像素）、H/V **Front Porch**、**Sync Width**、**Back Porch**、**Sync Polarity**、**Pixel Clock**、刷新率；支持 interlaced；提供 Manual / Automatic PC / Automatic HDTV / Automatic CRT / Native PC/HDTV / Exact / Exact reduced / Exact CRT / CVT / CVT-RB / CVT-RB2 / GTF / 垂直总行数计算器（QFT）等时序预设 | EDID 详细时序上限 **4095x4095、655.35 MHz**；超出需用 DisplayID 扩展块。**第一条详细时序 = 原生/首选分辨率**（见 §1.3） |
| **Standard Resolutions（标准分辨率）** | 常见 VESA 分辨率列表 | 仅支持 4:3/5:4/16:9/16:10；H 256–2288 且为 8 的倍数；刷新率 60–123Hz；**AMD 只认下拉列表里的值，其他值驱动会忽略（灰色显示）** |
| **Extension Blocks（扩展块）** | CTA-861（可加详细时序、TV 分辨率、音频格式、HDMI/HDMI 2.0 数据块、FreeSync 数据块、HDMI 2.1 VRR）；VTB-EXT（更多标准分辨率）；DisplayID 1.3/2.0（突破 4095/655.35MHz 上限）；Default（占位原厂扩展块） | 最多 7 个扩展块（1.5.2 起）；**AMD 只支持 1 个 VTB-EXT 且必须是最后一个**；NVIDIA 要求至少留 2 字节给数据块、不支持 Default 扩展块；**1.5.3 起支持 HF-EEODB，AMD/NVIDIA 可读全部扩展块** |
| **Range Limits（行/场频范围）** | 显示器支持的 H/V 扫描范围 | 若自定义时序超出范围，驱动可能拒绝 |
| **FreeSync / VRR** | DisplayPort Range Limits 的 V rate；HDMI FreeSync 数据块；HDMI 2.1 VRR 数据块 | 改错会丢失 VRR |
| **Display Properties** | 显示器名称、序列号、产品 ID、ID 序列号 | 可用来绕过 NVIDIA 对某些型号的硬编码 VRR 范围 |
| **导入/导出** | `.bin` / `.dat` / `.inf`（未签名显示器驱动）/ `.txt` / `.csv` / `.exe`（自解压安装器） | `.inf` 走微软官方 EDID override 机制（见 §1.3） |

来源：[CRU 官方发布帖](https://www.monitortests.com/forum/Thread-Custom-Resolution-Utility-CRU)（A）；[Timing parameters explained](https://www.monitortests.com/blog/timing-parameters-explained/)（A，ToastyX 官方博客）

**时序参数的定义**（ToastyX 官方博客，等级 A）：
- Active = 可见分辨率
- Blanking = front porch + sync width + back porch
- Total = Active + Blanking
- 刷新率 = 每秒帧数；Horizontal scan rate = Vertical total × Refresh rate；Pixel clock = HTotal × VTotal × Refresh rate

### 1.3 EDID override 的落地机制（微软官方文档）

微软官方文档《Using an INF file to override EDIDs》说明了 Windows 的 EDID 覆盖机制（等级 A）：

- EDID 按 128 字节一块（block 0 为主块，1.3+ 可带扩展块）。
- INF 中用 `HKR, EDID_OVERRIDE, BlockNumber, 0x1, Byte1, Byte2, ...` 写入 128 字节覆盖数据。
- 设备安装时，Windows 把覆盖数据存到**该显示器设备的 hardware key 下**（每个被覆盖的块一个值）。
- "**The monitor driver checks the registry during initialization and uses any EDID information stored there instead of the corresponding information on EEPROM. EDID information that is added to the registry always takes precedence over EEPROM EDID info.**"
- 即：**注册表 EDID_OVERRIDE 优先于显示器 EEPROM 的 EDID**，且只覆盖你指定的块，其余块仍来自 EEPROM。

CRU 走的就是同一条注册表机制（官方帖明确说 "software EDID overrides in the registry"），只是省去了手工做 INF 的步骤；导出的 `.inf` 是官方文档那条路。**重要推论**：EDID override 只影响"PC 认为显示器支持什么"，**物理显示器本身的 EDID/行为完全不变**。

### 1.4 在 Windows 11 24H2 + AMD 核显（Radeon 880M）上的支持方式

**AMD 侧的支持方式**：
- CRU 官方 Requirements 明确列出 AMD/ATI 支持，走注册表 EDID override；AMD 驱动在初始化时读取覆盖值。（A）
- AMD 已知限制（B）：
  - **AMD 驱动在 MST Hub 下会忽略 EDID override**（ToastyX：*"AMD's driver ignores EDID overrides with MST hubs."* —— [SRE 帖](https://www.monitortests.com/forum/Thread-Scaled-Resolution-Editor-SRE)）。本场景是 HDMI 直连，不受影响。
  - AMD 的 Standard Resolutions 只支持下拉列表内的值（A）。
  - 老 AMD 卡有个怪癖：最高刷新率有时必须放在第一条详细时序才会被驱动识别（2012 年帖，B，仅供参考）。
  - `restart64.exe` 重启驱动时会顺带重启 Radeon Settings（cnext.exe），否则设置界面可能无响应（A，1.2.5 更新日志）。
- AMD 自己还有一套**驱动级自定义分辨率**（Adrenalin 的 Custom Resolutions），存在注册表 `HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-...}\####\DAL*_DATA\common\EDID_<vid>_<pid>_<name>` 下的二进制值 `mode_timing_override`（B，2025-07 实测帖 [How to create a custom resolution for AMD?](https://www.monitortests.com/forum/Thread-How-to-create-a-custom-resolution-for-AMD)）。这是**驱动级**、不是 EDID 级：Windows 分辨率列表里会出现，但显示器 EDID 不变；重装驱动可能丢。
- Radeon 880M 是 RDNA3.5 核显，属"较新 AMD GPU"，上述 EDID override 机制适用；但**AMD/ATI Pixel Clock Patcher 明确不支持 RX 5000 系列及更新（RDNA）**（见 §1.6），所以 880M 上不能用"打补丁解锁像素时钟"这条老路。不过本场景的半宽模式像素时钟**远低于**原生 260Hz 模式，本来就不需要解锁（§4.2）。

**Windows 11 24H2 侧的相关变化**：
- 24H2 没有废弃 EDID override 机制；CRU 1.5.3 在 2026-01 仍在更新，说明机制仍有效。（A/B）
- 24H2 有一个**容易误解**的现象：有用户报告"CRU 删不掉多余的刷新率"，ToastyX 定位为 **G-SYNC/VRR 的 "quick media switching" 虚拟刷新率**，这些刷新率不在 EDID 里，关掉 G-SYNC 就消失；**与"添加分辨率"无关**。（B，[Windows 11 24H2 doesn't respect my CRU settings](https://www.monitortests.com/forum/Thread-Windows-11-24H2-doesn-t-respect-my-CRU-settings)，2024-11）
- Windows 11 **会忽略没有有效 EDID 的显示器的 override**（显示为 `PNP09FF - Generic Non-PnP Monitor`），这是另一类坑，与本机无关（本机能报出 SKG2526/25S1，说明 EDID 有效）。（A，CRU 官方帖）

### 1.5 restart64.exe / reset-all.exe / 安全模式：失败后怎么恢复

CRU 官方给出的操作与恢复流程（等级 A，[官方帖](https://www.monitortests.com/forum/Thread-Custom-Resolution-Utility-CRU)）：

1. 运行 `CRU.exe`（会弹 UAC，因为要写注册表）→ 选择显示器（`(active)` 表示已连接并被驱动识别；`*` 表示已保存 override）→ 编辑 → OK。
2. 运行 **`restart64.exe`**（64 位系统）重启显卡驱动，使改动生效。
   - **若 15 秒内画面没有回来：按 F8 进入 recovery mode**，会**临时卸载所有 EDID override 但不删除它们**；再次重启驱动即可重新加载。
   - 部分系统重启驱动时驱动会崩溃，重启后驱动可能被禁用，**再运行一次 `restart64.exe` 即可重新启用**。
3. 到 Windows 显示设置里选择分辨率/刷新率。
4. **恢复默认**：
   - 单个显示器：CRU 里用 `Delete` 删除该显示器的 override，重启。
   - 全部显示器：运行 **`reset-all.exe`** 然后重启；**必要时可在安全模式执行**。
5. 官方还建议：**动手前先熟悉如何用恢复驱动器进入安全模式**；没有恢复驱动器时，在 Windows 启动中**长按电源键强制关机两次**，会进入恢复选项：疑难解答 → 高级选项 → 启动设置 → 重启 → 安全模式。
6. 命令行：`reset-all.exe /q`（无提示重置）、`restart64.exe /q`（无提示重启驱动，或把文件改名为 `restart-only.exe`）、`restart64.exe /r`（无提示进入 recovery mode）；导出的 `.exe` 支持 `/i`（无提示安装 EDID）与 `/r`（无提示重置 EDID）。

> 注意：`reset-all.exe` 在 1.2 版本后**还会重置 Windows 分辨率设置**（A，更新日志）。这对"画面异常但还能看见"的恢复很有效；对"完全黑屏"则需要安全模式或 F8 recovery。

### 1.6 驱动签名 / Secure Boot / 反作弊：哪些有影响、哪些没有

| 项目 | 是否影响 CRU | 说明 |
|---|---|---|
| **驱动签名 / Secure Boot** | **不影响 CRU 本身** | CRU 只写注册表 EDID override，不修改任何驱动文件；不需要禁用驱动签名、不需要测试模式。（A，官方帖"does not modify the hardware"） |
| **AMD/ATI Pixel Clock Patcher** | **本场景不适用** | 该工具修改 `atikmdag.sys` 以解除 165/330/400MHz 像素时钟限制；Requirements 写明"HD 5000 系列至 Polaris/Vega；**RX 5000 系列及更新不支持**"——RDNA3.5 的 880M 不在支持范围。且它会修改并自签名驱动（可选自签名需 Windows 测试模式），**Riot Vanguard / FACEIT 反作弊会拒绝加载打过补丁的驱动**。（A，[AMD/ATI Pixel Clock Patcher](https://www.monitortests.com/forum/Thread-AMD-ATI-Pixel-Clock-Patcher)） |
| **反作弊** | 对 CRU 无影响 | 同上；只有"改驱动"才会触发反作弊。 |
| **Windows 更新/驱动更新** | 会重置或冲突 | 升级显卡驱动可能重置驱动级自定义分辨率/缩放设置；CRU 的 EDID override 通常保留，但建议升级后核对。（A/B） |

### 1.7 风险清单

| # | 风险 | 触发条件 | 后果 | 缓解/恢复 |
|---|---|---|---|---|
| R1 | **黑屏/无信号** | 显示器不接受自定义时序；时序超出范围；EDID override 写坏 | 主屏黑屏，笔记本内屏若被禁用则暂时"无显示可用" | `restart64.exe` 15 秒内按 **F8** 进 recovery；`restart64 /r`；安全模式跑 `reset-all.exe`；拔 HDMI 让 Windows 回退到内屏；RDP/另一台设备远程 |
| R2 | **EDID override 持久化** | override 写进注册表，重启后仍生效 | 每次开机都黑屏，直到删除 override | 安全模式 + `reset-all.exe`；恢复驱动器 |
| R3 | **丢失 HDR / FreeSync(VRR) / 音频 / 色深** | 覆盖 EDID 时没有保留原扩展块（CTA-861 的 HDMI/FreeSync/音频数据块） | 260Hz VRR、HDR、HDMI 音频可能失效 | 编辑前先 `Export` 备份原始 EDID（.bin）；改完逐项核对；必要时恢复备份 |
| R4 | **显示器拒收 260Hz 非原生模式** | scaler 对非原生分辨率有刷新率上限 | 选 260Hz 半宽模式时黑屏/掉回 60Hz | 先用 60/120Hz 半宽模式测试；不要一上来 260Hz |
| R5 | **Windows 把模式"吃掉"** | 驱动不认、或 Windows 列表不显示 | 白折腾 | 用 `restart64.exe` 重启驱动；检查"高级显示 → 显示适配器属性 → 列出所有模式"；必要时用 DisplayID 扩展块 |
| R6 | **每次切换都要重启驱动** | EDID override 生效需要驱动重启 | 切换 A/B 不是"瞬时"，要黑屏几秒甚至重启 | 这是 CRU 方案与"快速切换"需求的根本冲突 |
| R7 | **改坏原生分辨率定义** | 把 960x1080 放到第一条 Detailed Timing | Windows 推荐分辨率、全屏游戏默认分辨率变化；1920x1080 不再是"原生" | 保留 1920x1080 为第一条；半宽放第二条 |
| R8 | **管理员/安全软件拦截** | CRU 需要写 HKLM；部分安全软件拦截 | 改不进去 | 临时允许；改完恢复 |
| R9 | **MST Hub/转接器** | 经 MST Hub 连接 | AMD 忽略 EDID override | 直连 HDMI（本场景是直连） |
| R10 | **误以为"改 EDID = 改显示器"** | 概念混淆 | 期望落空：显示器行为不变 | 本文 §1.3 |

---

## 2. 核心问题：能否「只有左半屏显示内容、右半屏纯黑、且内容左对齐」

### 2.0 先厘清：画面位置由谁决定（信号链）

```
Windows 桌面（source mode，例如 1920x1080 或 960x1080）
        │
        ├─ 若 source ≠ target 且开启 GPU 缩放 → GPU 缩放器按缩放模式把桌面画进 target 帧
        │
        ▼
target mode = 实际发给显示器的活动信号（active signal，例如 1920x1080@260 或 960x1080@60）
        │  HDMI（TMDS/FRL，含 DE 有效像素窗口）
        ▼
显示器 scaler（按 OSD 的 画面比例/1:1/尺寸 设置）→ 映射到 1920x1080 面板
```

- **若 GPU 缩放开启**（AMD Software → 设置 → 显示 → GPU Scaling = On）：GPU 把低分辨率桌面缩放到**原生 1920x1080** 再发给显示器；显示器始终收到原生信号，**显示器的 OSD 缩放模式不起作用**。
- **若 GPU 缩放关闭**：低分辨率作为**活动信号**直接发给显示器（例如 960x1080），由**显示器 scaler** 决定怎么显示。
- 无论哪条路径，**最后一级缩放器决定位置**，而 Windows/GPU/显示器三者的缩放模式都是"铺满 / 保持比例 / 居中"这几类，**没有"偏移到左侧"这个选项**（§3）。

ToastyX 对这条链的权威描述（B，[2024-11 帖](https://www.monitortests.com/forum/Thread-Help-with-stretched-res-on-23-8-inches-of-display-using-a-27-inch-monitor)）：
> "The scaling mode in AMD's control panel is for **GPU-scaled resolutions only**. For resolutions sent to the monitor, you need to change the scaling mode **in the monitor's OSD**, if it has the option. The active signal mode is the resolution sent to the monitor. ... **The native resolution is always sent to the monitor even if GPU scaling is enabled** because GPU scaling scales lower resolutions to the native resolution."

> "The monitor can't change the active signal mode. That happens on the PC's end. **The scaling options in the monitor will scale any resolution it receives using its own scaler.**"

### 2.1 路径 (a)：添加一条半宽（960x1080）Detail Timing，然后 Windows 切到该模式

**做法**：CRU 里 Add Detailed Resolution：`Active 960 x 1080`，刷新率先用 60Hz（CVT-RB），确认像素时钟/行场频在显示器范围内；`restart64.exe`；Windows 显示设置里选 960x1080。

**结果取决于"谁来缩放"和"缩放模式"**：

| 场景 | 显示器收到 | 缩放执行者/模式 | 实际画面 | 满足 B？ |
|---|---|---|---|---|
| GPU Scaling = On，Scaling Mode = **Full Panel** | 1920x1080（原生） | GPU 拉伸 | 960x1080 内容被横向拉伸铺满 1920x1080，**无黑边、变形** | ❌ |
| GPU Scaling = On，Scaling Mode = **Preserve Aspect Ratio** | 1920x1080（原生） | GPU 保持比例 | 960x1080 居中显示，**左右各约 480px 黑边** | ❌（居中，不是左对齐） |
| GPU Scaling = On，Scaling Mode = **Center** | 1920x1080（原生） | GPU 1:1 居中 | 同上，**居中 + 左右黑边** | ❌ |
| GPU Scaling = On，**Integer Scaling**（1x） | 1920x1080（原生） | GPU 整数倍（1x）居中 | 同上，**居中 + 左右黑边** | ❌ |
| GPU Scaling = Off，OSD **全屏/Full/拉伸** | 960x1080 | 显示器 scaler 拉伸 | 铺满 1920x1080，**变形** | ❌ |
| GPU Scaling = Off，OSD **保持比例/Aspect/16:9/自动** | 960x1080 | 显示器 scaler 保持比例 | 960x1080（8:9）按高度铺满 → **960x1080 居中，左右各 480px 黑边** | ❌ |
| GPU Scaling = Off，OSD **1:1 / 点对点 / Just Scan / Original** | 960x1080 | 显示器 scaler 不缩放、居中 | **960x1080 居中，左右黑边** | ❌ |
| GPU Scaling = Off，OSD 有 **H-Position（水平位置）且 HDMI 下可用** | 960x1080 | 显示器 scaler 不缩放 + 左移 | **可左对齐**（右半纯黑） | ✅（唯一） |

**关键结论**：
- 960x1080 的宽高比是 **8:9**，不是 16:9。所以"保持比例"模式下它按**高度**受限，显示为 960x1080 的竖长条——**左右都有黑边**，不是"右半黑"。
- "1:1/点对点"模式下，主流显示器是**居中显示**（ToastyX 把这类选项统称为 "center scaling option"，并指出 LG 上对应 "Just Scan"，"Original" 则是保持比例）。→ 仍是居中。
- **只有显示器 OSD 在 HDMI 输入下提供 H-Position 时**，才能把居中的 960x1080 平移到最左，实现"右半纯黑 + 内容左对齐"。这是路径 (a) 唯一的成功条件，且每次 A/B 切换都要进 OSD 改位置 → **不满足"快速切换"**。
- 额外风险：显示器 scaler 可能**不接受 960x1080@260**（非原生分辨率常被限制刷新率），需要先降刷新率测试（R4）。
- 若把 960x1080 作为"GPU 缩放分辨率"（不改 EDID，用 SRE 或 AMD 自定义分辨率添加为 scaled resolution），结果同样是拉伸或居中，**不会左对齐**。

**判定**：**不可行**（除 OSD H-Position 特例）。

### 2.2 路径 (b)：非对称消隐（大 front/back porch）让 DE 有效区偏左

**想法**：保持总行宽/像素时钟接近原生，但把 HActive 改成 960、HBlank 拉大到 960（HTotal 仍是 1920），希望显示器把"整行 1920 像素"按 1:1 映射到面板，于是内容只出现在左边 960 列、右边 960 列是消隐（黑）。

**为什么在 HDMI/DP 上行不通**：
1. 数字接口用 **DE（Data Enable）** 信号界定"哪些像素是有效像素"。显示器 scaler 把 **DE 窗口**当作整幅源图像（source raster），再按自己的缩放模式映射到面板。**DE 窗口从行首开始，宽度就是 HActive**；porch 在 DE 之外，不携带任何"图像在面板上的位置"信息。
2. 显示器 scaler **没有"按 HTotal 映射"的常规实现**：它不知道、也不关心 HBlank 是 100 还是 960；它只看到"一条行里 960 个有效像素"。所以 (b) 在显示器眼里和 (a) 完全一样：**一个 960x1080 的源**。
3. porch 不对称在**模拟 VGA** 时代确实会影响图像位置（显示器用同步信号做 auto-position/auto-adjust），但 HDMI/DP 是数字链路，没有"相位/位置"可调；多数显示器说明书里的 `Auto`（自动调节图像位置/时钟/稳定性）明确标注**仅模拟输入**可用（见 §2.3 的旁证）。→ 等级 D（基于协议语义的推断，无公开实证）。
4. **实证层面**：AMD Adrenalin 的自定义分辨率**拒绝**"总宽 ≠ 有效宽"这类参数——2025-07 有用户想建 "3840 total / 1920 active"（Frame Packing 3D），AMD 软件直接报错，ToastyX 回复 *"AMD doesn't provide a way to do what you want."*；用户最后只能**十六进制改注册表 `mode_timing_override`** 才造出来，而且**一开 GPU Scaling 该模式就从列表消失**（B，[How to create a custom resolution for AMD?](https://www.monitortests.com/forum/Thread-How-to-create-a-custom-resolution-for-AMD)）。这说明：即使能"造"出非对称消隐时序，也是**不被支持、不稳定**的路径。
5. **没有任何公开案例**表明在 HDMI 显示器上通过 porch 把画面整体左移。→ 等级 D。

**补充**：CRU 的 Manual timing 确实允许任意 porch（甚至 1.3.1 起允许 0 back porch），所以"造得出来"没问题；问题在"显示器不会按你希望的方式解释它"。

**判定**：**不可行/不可控**；且属于"无收益但高风险"的操作（可能直接黑屏或触发显示器保护）。

### 2.3 旁证：显示器 OSD 的位置调节通常是模拟输入专属

- 多份显示器说明书把 `Auto`（自动调节图像位置、像素时钟、稳定性）标注为**仅模拟输入**可用，例如 Sharp/NEC 说明书："**Auto (nur analoger Eingang)** — Stellt Bildposition, Bildpunkte und Stabilität automatisch ein"（[Sharp 说明书 PDF](https://sharpone.sharp.it/api/pim/download/e4541cad-2818-4f11-9ec4-0ddb42527667)，检索摘要，等级 C）。
- NVIDIA 官方帮助文档的"Adjust Desktop Size and Position"表格明确：**Position（位置）只对模拟 TV（S-Video/复合/分量）和 VGA 开放；Digital (DVI/DisplayPort/HDMI) 只有 Scaling 和 Resize，没有 Position**（[NVIDIA 官方文档](https://www.nvidia.com/content/Control-Panel-Help/vLatest/en-gb/mergedProjects/nvdspENG/Adjust_Desktop_Size_and_Position_-_Windows_Vista_and_Later.htm)，等级 A）。
- 即：**"图像位置偏移"在数字显示链路上不是标准能力**——GPU 侧没有（NVIDIA 官方表格），显示器侧多数也没有（说明书把位置调节归到模拟输入）。

### 2.4 路径 (c)：EDID 里把原生分辨率声明为半宽（960x1080）

**做法**：CRU 把 960x1080 设为**第一条 Detailed Resolution**（= 原生/首选分辨率），甚至删掉 1920x1080。

**会发生什么**（B，ToastyX）：
- Windows/AMD 驱动会认为显示器"原生就是 960x1080"，**永远把 960x1080 作为活动信号发给显示器**（"The native resolution is always sent to the monitor even if GPU scaling is enabled"）。
- 更低的分辨率会被 **GPU 放大到 960x1080**（而不是 1920x1080）。
- Windows 的"推荐分辨率"变成 960x1080；全屏游戏默认分辨率/分辨率列表可能变化。

**但显示器还是那块 1920x1080 面板**：它收到 960x1080 信号后，仍按自己的 OSD 缩放模式处理——**拉伸铺满**（变形）或**居中**（左右黑边）。EDID 声明**不能命令显示器"把图像放左边"**。→ 与 (a) 的结局完全相同。

**副作用**：
- 1920x1080 不再是"原生"，全屏游戏/部分应用会优先 960x1080；
- 若删除 1920x1080 的 detailed timing，某些场景可能无法再回到原生（需保留它）；
- 若覆盖 EDID 时丢了 CTA-861/HDMI 数据块，可能丢 HDR/VRR/音频（R3）；
- Windows DPI 仍是 100%，但桌面可用面积只剩 960x1080（若真能左对齐才有意义）。

**判定**：**不可行**（不改变位置问题），且副作用最大，**不建议**。

### 2.5 路径 (d)：配合显示器 OSD 的 Aspect / 1:1 / Position 设置

先列出典型显示器 OSD 的"画面比例/显示模式"选项及语义（不同厂商叫法不同）：

| 厂商叫法 | 语义 | 对 960x1080 信号的效果 |
|---|---|---|
| Full / 全屏 / 16:9 / 拉伸 | 铺满面板 | 横向拉伸到 1920x1080（变形，无黑边） |
| Aspect / 保持比例 / 自动 / Original | 保持宽高比 | 960x1080 按高度铺满 → 居中，左右各 480px 黑边 |
| 1:1 / 点对点 / Just Scan / Pixel-to-Pixel / 原始大小 | 不缩放、原尺寸 | 960x1080 **居中**（多数实现），左右黑边 |
| 4:3 / 5:4 / 16:10 | 按指定比例缩放 | 960x1080（8:9）→ 按比例缩到更小并居中 |
| 屏幕尺寸 / 24.5" / 尺寸调节（部分机型） | 缩小画面到指定尺寸 | 缩小 + **居中**，不能偏左 |
| **H-Position / 水平位置**（部分机型，多见于 VGA） | 水平移动画面 | 若 HDMI 下可用 → **可左移**，实现左对齐 |
| PBP / 双画面（部分机型） | 把面板分成左右两半，各显示一个输入 | 若只接一个输入且该输入放在左半，可能物理上把画面限制在左半（右半黑/无信号）；但通常限制刷新率、需 OSD 切换、且该型号是否支持未知 |

**结论**：
- 仅靠 Aspect/1:1/尺寸 这些选项，**都是居中或拉伸，不能左对齐**。
- **H-Position 是唯一可能实现左对齐的 OSD 功能**，但：(1) 多数显示器只在模拟输入下开放；(2) 需要每次进 OSD 操作，无法"快速切换"；(3) 该型号（SKG2526 / 25S1）是否具备、且 HDMI 下是否可用，**需查说明书或实测 OSD**（本任务只读，未操作）。
- PBP 是另一个"物理限制到一半"的旁路，但不是 CRU，且大概率不满足 260Hz/快速切换；仅作备选记录，需 monitor-probe 确认该机型是否支持。

**判定**：**理论上唯一可行，但依赖 OSD 且与"快速切换"矛盾**；只有确认该显示器 HDMI 下有可用 H-Position 时才值得尝试。

### 2.6 组合矩阵汇总（GPU 缩放 × 显示器缩放）

| GPU 缩放 | 显示器 OSD | 960x1080 桌面的呈现 | 右半纯黑？ | 左对齐？ |
|---|---|---|---|---|
| On + Full Panel | 无关（收到原生） | 拉伸铺满 1920x1080 | 否 | 否 |
| On + Preserve Aspect Ratio | 无关 | 居中 960x1080，左右黑边 | 否（两侧黑） | 否 |
| On + Center | 无关 | 居中 960x1080 | 否（两侧黑） | 否 |
| On + Integer (1x) | 无关 | 居中 960x1080 | 否（两侧黑） | 否 |
| Off | 全屏/拉伸 | 拉伸铺满 | 否 | 否 |
| Off | 保持比例/Aspect | 居中 960x1080 | 否（两侧黑） | 否 |
| Off | 1:1/点对点 | 居中 960x1080 | 否（两侧黑） | 否 |
| Off | 1:1 + **H-Position 左移** | 960x1080 贴左，右半黑 | **是** | **是** |
| （任意） | 收到原生 1920x1080 且软件在右半画黑 | 正常 1920x1080 桌面 | **是** | **是（软件合成）** |

> 最后一行就是"软件合成"方案：**保持原生 1920x1080**，用一个覆盖右半屏的黑色置顶窗口（或限制工作区为 960 宽）实现，**不需要动 EDID/时序**。

### 2.7 为什么"左对齐"在数字显示链路上没有原语

1. **HDMI/DP 的 active video 语义**：DE 窗口 = 源图像；sink 只知道"这一帧有多少有效像素"，不知道"这块内容对应面板哪一半"。缩放器把源图像整体映射到面板，映射规则只有"铺满/保持比例/居中"。
2. **Windows/驱动的缩放模型**只有 identity / centered / stretched / aspect-centered / custom（厂商私有）五种（§3.2），**没有 offset/position 字段**。
3. **显示器 scaler 的输出定位**由 OSD 模式决定，通常只有 Full/Aspect/Center/尺寸，数字输入没有 offset（§2.3）。
4. **结论**：左对齐只能在**合成层**（DWM/应用层）做，或在**模拟输入/带 H-Position 的 OSD** 上做。CRU 工作在"时序/EDID"层，**天生够不到这个自由度**。

---

## 3. AMD / NVIDIA / Intel 的差异，以及"为什么驱动层只能居中不能左对齐"

### 3.1 三家对照

| 维度 | AMD（Adrenalin） | NVIDIA | Intel（Graphics Command Center） |
|---|---|---|---|
| GPU 缩放开关 | **GPU Scaling** On/Off（设置 → 显示）[C] | **Perform scaling on**: GPU / Display / Automatic [A] | 缩放选项随驱动版本变化 [A] |
| 缩放模式 | **Full Panel（铺满）/ Preserve Aspect Ratio（保持比例）/ Center（居中）** [C] | **Aspect ratio / Full-screen / No scaling（居中）/ Integer scaling** [A] | **Maintain Display Scaling / Custom**；选非推荐分辨率时出现 **Centered / Stretched / Maintain Aspect Ratio** [A] |
| 自定义分辨率入口 | Adrenalin → 设置 → 显示 → **Custom Resolutions**；驱动级，存 DAL 注册表 `mode_timing_override` [B/C] | NVIDIA 控制面板 → 更改分辨率 → 自定义 [A] | Intel GCC → 显示 → 自定义分辨率（部分版本被移除/灰化）[A] |
| 位置/偏移（offset）控制 | **无** | **Position 页仅对模拟 TV/VGA 开放**；数字 DVI/DP/HDMI 只有 Scaling/Resize，**没有 Position** [A] | **无**（Custom 仅用于 overscan 校正/缩放，不是位置）[A] |
| 整数缩放 | RDNA 起支持（Radeon HD 7000+）[B] | Turing 起支持 [A] | 10 代酷睿起（"Retro Scaling"），部分机型不可用 [B] |
| CRU EDID override | 支持；**MST Hub 下被忽略** [A/B] | 支持；但有 DSC/多屏/像素时钟等坑 [A] | 支持（需较新驱动）；**CRU 读不到 Intel 的扩展块** [A] |

来源：
- AMD 缩放模式：AMD 官方文章《GPU Scaling with AMD Software: Adrenalin Edition》（[DH3-019](https://www.amd.com/en/resources/support-articles/faqs/DH3-019.html)）与《Create Custom Resolutions》（[DH3-032](https://www.amd.com/en/resources/support-articles/faqs/DH3-032.html)）——**本环境无法抓取 amd.com**，故具体 UI 文案以第三方指南为准（[Tier1Settings, 2026-07-25](https://tier1settings.com/amd-stretched-resolution-not-working-fix/)，等级 C）与 ToastyX 的答复（等级 B）。官方入口 URL 保留供人工核对。
- NVIDIA：[Adjust Desktop Size and Position（官方帮助）](https://www.nvidia.com/content/Control-Panel-Help/vLatest/en-gb/mergedProjects/nvdspENG/Adjust_Desktop_Size_and_Position_-_Windows_Vista_and_Later.htm)（A）。
- Intel：[Scaling Options in the Intel Graphics Command Center](https://www.intel.com/content/www/us/en/support/articles/000058312/graphics.html)（A）；[Scale Options Are No Longer Available After Updating the Intel GCC](https://www.intel.com/content/www/us/en/support/articles/000057700/graphics.html)（A）。
- 整数缩放：ToastyX 答复（[SRE 帖](https://www.monitortests.com/forum/Thread-Scaled-Resolution-Editor-SRE)，B）；NVIDIA 官方文档（A）。

### 3.2 Windows 层的证据：缩放枚举里没有"偏移"

微软官方 `DISPLAYCONFIG_SCALING`（[MS Learn](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ne-wingdi-displayconfig_scaling)，等级 A）只有这些值：

| 值 | 含义 |
|---|---|
| `DISPLAYCONFIG_SCALING_IDENTITY` (1) | 不缩放（源=目标分辨率时） |
| `DISPLAYCONFIG_SCALING_CENTERED` (2) | **居中**，不缩放 |
| `DISPLAYCONFIG_SCALING_STRETCHED` (3) | 拉伸铺满 |
| `DISPLAYCONFIG_SCALING_ASPECTRATIOCENTEREDMAX` (4) | 保持比例并**居中** |
| `DISPLAYCONFIG_SCALING_CUSTOM` (5) | "调用方无法用其他值描述的**自定义缩放**"；**只有硬件厂商的增值应用应使用**，且可能需要私有接口 |
| `DISPLAYCONFIG_SCALING_PREFERRED` (128) | 无偏好，用数据库/默认值 |

微软《Scaling the Desktop Image》（[MS Learn](https://learn.microsoft.com/en-us/windows-hardware/drivers/display/scaling-the-desktop-image)，等级 A）也只描述三种：**Centered（居中）**、**Stretched（拉伸）**、**Aspect-Ratio-Preserving Stretched（保持比例，黑边在上/下或左/右，但不可能同时四边）**。

**结论**：Windows 的显示模型里**根本没有"把图像偏移到左侧"的参数**。`CUSTOM` 是留给 OEM 私有实现的（例如 overscan 校正），普通应用/用户无法通过它获得任意 offset；而且即使驱动支持 `CUSTOM`，它也是"缩放"语义，不是"位置"语义。

### 3.3 为什么驱动只能居中

1. **WDDM 路径模型**：一条 path = source mode（桌面分辨率）+ target mode（发给显示器的完整时序）+ scaling intent（缩放意图）。target mode 是**整幅**时序，没有"图像在 target 中的偏移"字段。
2. **GPU 缩放器硬件**：通常实现为把源图像写进 target 的一个**居中视口**（viewport），"居中"是硬件/固件约定；"偏移"需要额外的视口原点寄存器，消费级驱动不暴露、也不保证支持。
3. **OS 层没有 API**：`SetDisplayConfig` 只接受 `DISPLAYCONFIG_SCALING` 枚举；`DISPLAYCONFIG_VIDEO_SIGNAL_INFO` 里只有 activeSize/totalSize/pixelRate/同步频率等，**没有位置字段**。
4. **历史遗留**：真正需要"位置"的场景是**模拟 CRT/TV 的 overscan/underscan**（NVIDIA 的 Position 页、显示器的 H/V Position），所以这些控制只存在于模拟路径；数字路径不需要也不提供。
5. **AMD/Intel 完全没有位置控制**；NVIDIA 有 Position 页但**官方表格写明数字输出不可用**（§2.3）。→ 驱动层"只能居中"不是实现偷懒，而是**模型里没有这个自由度**。

---

## 4. 结论与建议

### 4.1 对用户场景：CRU 是否值得尝试？

**结论：不值得。** 理由按优先级：

1. **技术目标不可达**：B 目标要求"内容左对齐 + 右半纯黑"，而 CRU 能改变的只是"PC 发什么时序/EDID 声明什么"，**改变不了缩放器的定位规则**。所有常规组合的结果只有"拉伸铺满"或"居中 + 两侧黑边"，**都不是"右半黑、内容贴左"**。
2. **唯一例外依赖 OSD**：只有显示器在 HDMI 下提供可用的 H-Position 才能左对齐；这需要每次进 OSD 调整，**直接违背"快速切换"需求**；而且该型号是否支持未知。
3. **风险/成本高**：CRU 需要管理员、要重启显卡驱动、失败可能黑屏；恢复要 F8/安全模式/reset-all；还可能丢 HDR/VRR/音频；改完还要处理 Windows/驱动更新重置。
4. **有零风险的正确方案**：保持原生 1920x1080@260，用**软件合成**在右半屏画一个纯黑置顶窗口（并把工作区限制为 960 宽，让最大化窗口/桌面图标只在左半）。**瞬时切换、可逆、不黑屏、不改 EDID、不需要管理员**——这才是"合成层"该做的事（本团队另一任务的范围，本文只给结论）。

### 4.2 若仍想尝试：前提条件

只有以下条件**全部满足**时才值得动手：

1. **显示器在 HDMI 下确实有可用的 H-Position/水平位置**（查说明书或 OSD 实测；若只有 VGA 可用 → 直接放弃）。
2. **显示器接受 960x1080 非原生模式**（先用 **AMD Adrenalin 自带 Custom Resolution** 建 960x1080@60 测试，**不要先改 EDID**；能显示、不黑屏、不掉信号再谈 CRU）。
3. **接受"每次 A/B 切换要进 OSD 改位置 + 可能重启驱动"**（否则不满足快速切换）。
4. **有恢复手段**：第二块屏/笔记本内屏可在拔 HDMI 后自动恢复；或有 RDP/另一台设备；会进安全模式；已备份原始 EDID（CRU → Export → `.bin`）。
5. **接受副作用**：可能丢 HDR/FreeSync/音频；Windows/驱动更新后需重新核对；1920x1080 的原生地位可能受影响（若把半宽设为第一条 detailed timing）。

### 4.3 若仍想尝试：具体步骤（CRU 路径，仅供参考）

> 以下步骤**未在本机执行**（只读调研约束），仅作为"如果一定要试"的操作清单。**建议先用 AMD 自带自定义分辨率做第 1-2 步的等价测试，确认显示器能吃 960x1080 后再考虑 EDID override。**

1. **备份**：CRU → 选中 `(active)` 的 SKG2526 → `Export...` 保存原始 `.bin`；截图记录当前 EDID 列表/扩展块。
2. **确认显示器能接受 960x1080**：Adrenalin → 设置 → 显示 → Custom Resolutions → 新建 `960x1080`，刷新率 **60Hz**，Timing 用 CVT-RB/自动；应用后看画面是否正常（这一步不改 EDID，失败可直接删）。
3. **CRU 加半宽时序**：`Add` → Detailed Resolution → `960 x 1080`；Timing 选 **CVT-RB**（60Hz）；确认像素时钟/行频/场频在显示器范围内（CRU 会把越界值标红）。**不要**把它放到第一条（保留 1920x1080 为第一条）。
4. **应用**：`OK` → 运行 `restart64.exe`；**画面 15 秒内不回来就按 F8**（recovery mode，临时卸载 override）。
5. **选分辨率**：Windows 设置 → 系统 → 显示 → 分辨率选 960x1080（若列表没有：高级显示 → 显示适配器属性 → 列出所有模式；或重启再试）。
6. **调 OSD**：显示器 OSD → 画面比例设为 **1:1/点对点**；若有 **H-Position** 则左移到底。
7. **验证**：右半是否纯黑；内容是否贴左；是否仍能 260Hz（半宽模式很可能只能 60/120Hz）；HDR/VRR/音频是否正常。
8. **切换 A**：分辨率切回 1920x1080 + OSD 恢复（或 1:1 保持——原生信号下 1:1 与全屏通常等价）。

**粗略时序估算**（等级 D，按 ToastyX 博客公式；实际值以 CRU 计算为准）：
- 960x1080@60 CVT-RB：HTotal ≈ 1120，VTotal ≈ 1111 → 像素时钟 ≈ **74.7 MHz**，行频 ≈ 66.7 kHz。
- 960x1080@260 CVT-RB：HTotal ≈ 1120，VTotal ≈ 1111 → 像素时钟 ≈ **323 MHz**，行频 ≈ 288.9 kHz（与原生 260Hz 同场频；显示器 scaler 是否接受非原生 260Hz 未知）。
- 原生 1920x1080@260（假设 CVT-RB）：HTotal ≈ 2080 → 像素时钟 ≈ **601 MHz**（说明 260Hz 原生模式本身已接近 HDMI 2.0 的 600MHz TMDS 上限，实际可能用 HDMI 2.1 FRL 或 DSC；半宽模式时钟更低，**不需要**像素时钟补丁）。

### 4.4 回滚方法（从轻到重）

| 级别 | 操作 | 适用场景 |
|---|---|---|
| L1 | `restart64.exe /r`（无提示进入 recovery mode） | 画面异常但还能操作/还能看到部分画面 |
| L2 | 运行 `restart64.exe` 后 **15 秒内按 F8** | 改完黑屏，键盘仍可用 |
| L3 | `reset-all.exe`（可加 `/q`）→ 重启 | 能进桌面但显示异常；会同时重置 Windows 分辨率设置 |
| L4 | **安全模式**（恢复驱动器，或启动中长按电源键强制关机两次 → 疑难解答 → 高级选项 → 启动设置 → 安全模式）→ 运行 `reset-all.exe` | 完全黑屏/进不了桌面 |
| L5 | **拔掉 HDMI**，让 Windows 回退到笔记本内屏（若内屏只是被禁用，通常断开外接后会重新启用）；或从另一台设备 **RDP** 进本机 | 主屏全黑、内屏也不亮 |
| L6 | 系统还原 / 重装显卡驱动 / 删除注册表 `EDID_OVERRIDE` 值 | 以上都失败（最后手段） |

### 4.5 不可行的技术原因（给最终报告用的论据清单）

1. **CRU 只改 EDID/时序，不改缩放与定位**：官方定义是 "EDID editor ... software EDID overrides in the registry"，作者明确说 "CRU doesn't deal with scaling"（[A](https://www.monitortests.com/forum/Thread-Custom-Resolution-Utility-CRU) / [B](https://www.monitortests.com/forum/Thread-Integer-scaling-with-CRU-created-resolutions)）。
2. **Windows 缩放模型没有 offset**：`DISPLAYCONFIG_SCALING` 只有 identity/centered/stretched/aspect-centered/custom(厂商私有)；官方"Scaling the Desktop Image"只描述居中/拉伸/保持比例（[A](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ne-wingdi-displayconfig_scaling)）。
3. **GPU 驱动没有位置控制**：NVIDIA 官方表格写明 Position 仅模拟 TV/VGA，数字 DVI/DP/HDMI 不可用；AMD/Intel 完全没有位置选项（[A](https://www.nvidia.com/content/Control-Panel-Help/vLatest/en-gb/mergedProjects/nvdspENG/Adjust_Desktop_Size_and_Position_-_Windows_Vista_and_Later.htm)）。
4. **显示器 scaler 对非原生信号只有"铺满/保持比例/居中/尺寸"几类**：ToastyX 指出"显示器会用自身 scaler 缩放收到的任何分辨率"，且居中选项（LG 的 "Just Scan"）就是居中（[B](https://www.monitortests.com/forum/Thread-Help-with-stretched-res-on-23-8-inches-of-display-using-a-27-inch-monitor)）。
5. **半宽时序 = 让显示器识别为 960x1080 源**：结果是拉伸（变形）或居中（左右黑边），**不可能只黑右半**；非对称消隐在数字链路不携带位置信息（DE 语义），且 AMD 软件拒绝 total≠active 的参数、只能非官方 hex 改注册表且开 GPU Scaling 后失效（[B](https://www.monitortests.com/forum/Thread-How-to-create-a-custom-resolution-for-AMD)）。
6. **EDID 声明原生=半宽只改变"PC 发什么"**，不改变显示器行为（[B](https://www.monitortests.com/forum/Thread-Help-with-stretched-res-on-23-8-inches-of-display-using-a-27-inch-monitor)）。
7. **唯一例外是显示器 OSD 的 H-Position（HDMI 下可用）**，但这需要每次 OSD 操作，**与"快速切换"需求直接冲突**；且该型号是否支持未知。
8. **CRU 的风险/成本与收益不成比例**：黑屏、需 F8/安全模式恢复、可能丢 HDR/VRR/音频、驱动更新重置、需要管理员（§1.7）。

---

## 5. 本机待确认项（交给 monitor-probe / 后续实测）

1. **SKG2526 / "25S1" 的 OSD 是否有**：画面比例 1:1/点对点、H-Position/V-Position（HDMI 下是否可用）、屏幕尺寸/24.5" 缩放、PBP/双画面。
2. **该显示器是否接受 960x1080（60Hz / 120Hz / 260Hz）非原生模式**（可用 AMD Adrenalin Custom Resolution 只读测试，不改 EDID）。
3. **笔记本内屏是否只是"禁用"而非硬件断开**：拔 HDMI 后能否自动恢复（作为 L5 恢复路径的前提）。
4. **当前是否启用 HDR/FreeSync/VRR**：若启用，任何 EDID override 都需保留对应扩展块。
5. **驱动版本 32.0.23017.1001 对应的 Adrenalin 版本号**（本环境未查到公开映射，建议在 AMD 官网/驱动属性核对）。

---

## 6. 参考来源

> 访问日期均为 **2026-11-03**。等级见 §0.2。

| # | 来源 | URL | 等级 | 用途 |
|---|---|---|---|---|
| 1 | ToastyX《Custom Resolution Utility (CRU)》官方发布帖（v1.5.3，2026-01-26 更新） | https://www.monitortests.com/forum/Thread-Custom-Resolution-Utility-CRU | A | CRU 定义、能力、限制、恢复流程、AMD 限制、像素时钟限制 |
| 2 | ToastyX《Timing parameters explained》 | https://www.monitortests.com/blog/timing-parameters-explained/ | A | active/blanking/porch/sync/pixel clock 定义与公式 |
| 3 | Microsoft Learn《Using an INF file to override EDIDs》 | https://learn.microsoft.com/en-us/windows-hardware/drivers/display/overriding-monitor-edids | A | EDID override 的官方机制、注册表优先级 |
| 4 | Microsoft Learn《DISPLAYCONFIG_SCALING enumeration》 | https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ne-wingdi-displayconfig_scaling | A | 缩放枚举（无 offset） |
| 5 | Microsoft Learn《Scaling the desktop image》 | https://learn.microsoft.com/en-us/windows-hardware/drivers/display/scaling-the-desktop-image | A | 居中/拉伸/保持比例三种缩放的官方描述 |
| 6 | NVIDIA《Adjust Desktop Size and Position》(官方帮助) | https://www.nvidia.com/content/Control-Panel-Help/vLatest/en-gb/mergedProjects/nvdspENG/Adjust_Desktop_Size_and_Position_-_Windows_Vista_and_Later.htm | A | Position 仅模拟输出；数字只有 Scaling/Resize；缩放模式列表 |
| 7 | Intel《Scaling Options in the Intel Graphics Command Center》 | https://www.intel.com/content/www/us/en/support/articles/000058312/graphics.html | A | Intel 缩放模式（Centered/Stretched/Maintain Aspect Ratio/Maintain Display Scaling/Custom） |
| 8 | Intel《Scale Options Are No Longer Available After Updating the Intel GCC》 | https://www.intel.com/content/www/us/en/support/articles/000057700/graphics.html | A | Intel 缩放选项随版本变化 |
| 9 | ToastyX《AMD/ATI Pixel Clock Patcher》 | https://www.monitortests.com/forum/Thread-AMD-ATI-Pixel-Clock-Patcher | A | 像素时钟补丁的支持范围（RX5000+ 不支持）、自签名/测试模式、反作弊 |
| 10 | ToastyX《Scaled Resolution Editor (SRE)》 | https://www.monitortests.com/forum/Thread-Scaled-Resolution-Editor-SRE | A/B | GPU 缩放分辨率列表；AMD 在 MST Hub 下忽略 EDID override；安全模式恢复 |
| 11 | ToastyX《Integer scaling with CRU-created resolutions》 | https://www.monitortests.com/forum/Thread-Integer-scaling-with-CRU-created-resolutions | B | "CRU doesn't deal with scaling"；显示器/GPU 缩放分工 |
| 12 | ToastyX《Help with stretched res ... 27 inch monitor》 | https://www.monitortests.com/forum/Thread-Help-with-stretched-res-on-23-8-inches-of-display-using-a-27-inch-monitor | B | AMD 缩放模式只对 GPU 缩放分辨率生效；原生分辨率永远发给显示器；显示器 scaler 行为；居中/Just Scan/Original |
| 13 | ToastyX《How to create a custom resolution for AMD?》 | https://www.monitortests.com/forum/Thread-How-to-create-a-custom-resolution-for-AMD | B | AMD 拒绝 total≠active 自定义时序；`mode_timing_override` 注册表；开 GPU Scaling 后模式消失 |
| 14 | ToastyX《Windows 11 24H2 doesn't respect my CRU settings》 | https://www.monitortests.com/forum/Thread-Windows-11-24H2-doesn-t-respect-my-CRU-settings | B | 24H2 的 VRR 虚拟刷新率现象（与添加分辨率无关） |
| 15 | Microsoft Q&A《Align custom resolution to left》 | https://learn.microsoft.com/en-us/answers/questions/4187867/align-custom-resolution-to-left | C | 同场景用户实测：自定义分辨率只能居中；官方答复"做不到" |
| 16 | Tier1Settings《AMD Stretched Resolution Not Working? Full-Screen Fix》(2026-07-25) | https://tier1settings.com/amd-stretched-resolution-not-working-fix/ | C | AMD Adrenalin：GPU Scaling 开关 + Full Panel/Preserve Aspect Ratio/Center 文案 |
| 17 | AMD 官方《GPU Scaling with AMD Software: Adrenalin Edition》 | https://www.amd.com/en/resources/support-articles/faqs/DH3-019.html | A（待人工核对，本环境被阻断） | AMD 缩放模式官方说明入口 |
| 18 | AMD 官方《Create Custom Resolutions with AMD Software: Adrenalin Edition》 | https://www.amd.com/en/resources/support-articles/faqs/DH3-032.html | A（待人工核对，本环境被阻断） | AMD 自定义分辨率官方说明入口 |
| 19 | Sharp/NEC 显示器说明书（Auto 仅模拟输入，调节图像位置/像素时钟/稳定性） | https://sharpone.sharp.it/api/pim/download/e4541cad-2818-4f11-9ec4-0ddb42527667 | C | 位置调节属于模拟输入的旁证 |
| 20 | Microsoft Q&A《black screen after adjusting screen resolution》 | https://learn.microsoft.com/en-us/answers/questions/3955268/black-screen-after-adjusting-screen-resolution | C | 分辨率设置导致黑屏的恢复经验（Win+Ctrl+Shift+B 等） |

---

### 附：一句话回答用户

> 你听说的 CRU 能"改分辨率/改 EDID"是真的，但它改的是"电脑发给显示器的信号长什么样"，**改不了"显示器把信号画在面板哪一块"**；而显卡驱动的缩放模式只有"铺满/保持比例/居中"，**没有"贴左"这个选项**。所以"右半纯黑 + 内容贴左"用 CRU/EDID 做不到（除非你的显示器 OSD 在 HDMI 下能调水平位置，而且你愿意每次手动调）。正确做法是在 Windows 合成层解决：保持 1920x1080@260 不动，用一个覆盖右半屏的纯黑置顶窗口 + 把工作区限制在左侧 960 宽，一键切换、零风险。
