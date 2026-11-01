# 安棚桌面监控台（WinUI 3）

农村草料棚智能火灾预警与初期喷淋系统 —— Windows 桌面监控台。

- 产品名：安棚 / ANPENG
- 主体：安棚智慧消防科技有限责任公司
- 版本：1.0.0
- 技术栈：WinUI 3（Windows App SDK 2.2.0）+ .NET 10（`net10.0-windows10.0.26100.0`），非打包自包含
- 事实源契约：`../APP-SPEC.md`

---

## 一、三级分级判断判据（与 Android 端已对齐）

**判据见 APP-SPEC 4.2。桌面端 `Services/AlarmEvaluator.cs` 与 Android 端
`android/app/src/main/java/com/anpeng/monitor/core/AlarmEvaluator.java` 使用同一条判据，
对同一批采样必须得出同一级别。**

```
环境预警 ⟺ 烟雾 或 温度 中「仅一项」超预警阈值
疑似火情 ⟺ 烟雾与温度「同时」超限，但不满足确认火警条件
确认火警 ⟺ bothOver（两项同时超限）
          且 bothSec ≥ SprayGateSeconds      // 4.0 s，防误喷连续性门槛（独立常量）
          且 ( bothSec ≥ 30 s                // 持续时长（APP-SPEC 4.4 连续超限确认时长）
               OR rise ≥ 5.0 °C/min           // 温升速率（APP-SPEC 4.4 温升速率阈值）
               OR temp ≥ 70.0 °C )            // 温度确认火警阈值（APP-SPEC 4.4）
```

**只有「确认火警」才 `RequestSprinkler = true`；环境预警与疑似火情永不喷淋（APP-SPEC R3）。**

### 两个常量（跨端 parity 的关键，改成常量是为了两端不漂移）

```csharp
// Services/AlarmEvaluator.cs
public const double SprayGateSeconds      = 4.0;   // 防误喷连续性门槛
public const double RiseWindowMinSeconds  = 9.0;   // 温升窗口最小真实跨度（= Android 的 riseWindowMinMs 9000）
public const int    RiseWindowSeconds     = 60;    // 温升窗口长度
private const int   HistoryMaxSize        = 64;    // 采样窗口硬容量上限

// Services/MonitoringService.cs
public const double AutoSprinklerHoldSeconds   = 16.0;   // 确认火警联动开阀后的保持时长（秒）
public const double ManualSprinklerHoldSeconds = 24.0;   // 手动开阀后的保持时长（秒）
```

**开阀保持时长也是「秒」而不是「几个采样周期」**：早期写成 `SampleSeconds * 8`，
但采样周期在设置页可调 1~60 s，周期一改「保持多少秒」就跟着漂移，界面文案随之变成假话
（和防误喷门槛 `SampleSeconds * 2` 是同一个错误模式）。现在由秒数常量换算周期数
（`CyclesFor(seconds) = max(1, round(seconds / SampleSeconds))`），秒数是唯一事实源。

这两条**刻意不随采样周期派生**：

- `SprayGateSeconds` 写成常量而非 `Math.Max(SampleSeconds * 2, 2.0)`：后者在采样周期配成
  1 s 时会退化成 2 s，而 Android 端是硬编码 4.0，两端会在「改过阈值之后」分裂。
- `RiseWindowMinSeconds = 9.0`：温升速率只取**一个口径** —— 最近 ≤60 s 窗口的首末斜率，
  且要求该窗口**真实跨度 ≥ 9 s**，不足则 `rise = 0`。没有这条下限时，两个相隔 1 ms 的样本
  差 0.05 °C 就能被「最近两点斜率」放大成几十 °C/min，**桌面端会误喷、Android 端不会** ——
  分裂方向正是危险的那一侧。`RecentRisePerMinute` 仍然对外暴露，但**只用于界面展示，不进入判定**。

### 两个口径说明（评委可能追问，这里先讲清）

1. **「两项连续超限」的计时口径**：`OverLimitSeconds` 只在烟雾与温度**同时**超限时累计，
   任一项回落即清零，按**真实时间戳跨度**计算（不是「采样点数 × 采样周期」）。
   这是 APP-SPEC 4.2「两项**连续**超限」的字面口径，也是与 Android 端一致的口径。
   另有 `CurrentAnyOverLimitSeconds`（任一项超限的计时）**只用于环境预警的文案展示，不参与确认火警判据**。

2. **时间戳异常**：相等 → 重置计时；倒退 → 清空窗口 + 重置计时，并且**绝不产生负数或 NaN 时长**
   （`AlarmEvaluation.TimeAnomaly` 会置位）。采样窗口另有 `HistoryMaxSize = 64` 硬上限兜底，
   防止时间戳卡死后无界增长。

3. **机器可比对的 parity 证据**：`../_verify/parity-desktop.json`
   由 `Anpeng.Desktop.exe --parity <路径>` 生成，含全部生效阈值与 C1–C10
   十个固定命名用例的 `level` / `spray` / `confirmBy`，可与 Android 端同结构 JSON 逐字段 diff。
   `thresholds` 的键名与 Android 端**逐字对齐**（`bothSustainSeconds` / `riseWindowMinMs` /
   `riseWindowMinSeconds` / `windowSpanMs` / `windowSpanSeconds` / `windowMaxSize`），
   两端阈值语义一一对应，机械 diff 不应出现任何单侧多出的键。

---

## 二、目录结构

```
desktop/
├─ Anpeng.Desktop.csproj     工程文件（UseWinUI / WindowsPackageType=None / 自包含）
├─ app.manifest              非打包应用清单（supportedOS + PerMonitorV2 dpiAwareness）
├─ App.xaml(.cs)             应用入口、导航映射 NavMap、全局异常兜底、命令行开关
├─ MainWindow.xaml(.cs)      外壳：TitleBar（安棚矢量标志）+ NavigationView + Frame
├─ RuntimeState.cs           进程级单例门面（数据仓库 / 监控服务 / 采样计时器）
├─ Assets/                   窗口图标与打包图标（AppIcon.ico、LogoSquare-*.png、LogoMark-512.png）
├─ Data/
│  ├─ DemoData.cs            草料棚语境演示数据集（≥5 棚 / 36 报警 / 22 喷淋 / 240 采样点）
│  └─ DataRepository.cs      %LOCALAPPDATA%\安棚 持久化（settings.json / data.json）
├─ Models/                   Barn / SensorReading / AlarmRecord / SprinklerRecord / DeviceNode /
│                            AlarmLevel / ValveState / CalibrationRecord
├─ Services/
│  ├─ AlarmEvaluator.cs      ★ 分级判断内核（纯逻辑，无 UI / 无 IO）
│  ├─ MonitoringService.cs   实时轮询驱动：采样 → 判断 → 报警 → 联动开阀
│  ├─ DeviceClock.cs         演示用设备时钟（N × 采样周期）
│  ├─ ReportService.cs       日/周/月报筛选与 CSV 导出
│  ├─ ParityCases.cs         跨端 parity 固定用例 C1–C10（--parity 用）
│  ├─ SelfTest.cs            自检：三条分级链路 + 统一判据正反例 + 运行期真实链路 + B05 递进演练
│  └─ AppLog.cs              运行日志（含未捕获异常）
├─ Controls/
│  ├─ AnpengLogo.xaml(.cs)   ★ 安棚主标志 A 的矢量复刻（512×512 viewBox，参数化尺寸）
│  ├─ TimeSeriesGraph.xaml   双曲线趋势图（温度 + 烟雾，带阈值参考带）
│  ├─ LevelBadge.xaml        级别药丸徽章
│  └─ UiKit.cs               页面装配工具集（令牌取色、卡片/表格/柱状图/按钮）
├─ Pages/                    7 个功能页（总览/实时监测/报警记录/喷淋控制/设备管理/报表统计/设置）
│                            + 判据自检页 DebugPage
└─ Styles/
   ├─ AnpengTokens.xaml      ★ 契约第 3 节色值 → SolidColorBrush，Light/Dark/HighContrast 三套
   └─ AnpengStyles.xaml      通用样式（页面/卡片/标题/表格/按钮）
```

---

## 三、构建与运行

```powershell
# 调试构建
dotnet build Anpeng.Desktop.csproj -c Release

# 发布自包含便携版（免装 .NET 运行时与 Windows App SDK 运行时）
dotnet publish Anpeng.Desktop.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=false -o ..\..\_scratch\pub-desktop
```

> `.pri` 提示（**本工程真的踩过，不是理论风险**）：非打包 WinUI 3 应用运行期靠
> `<AssemblyName>.pri` 解析 `ms-appx:///` 资源（`Styles/AnpengTokens.xaml`、`Assets/AppIcon.ico` 等）。
> 因为本工程设了 `EnableDefaultPRIResourceItems=false`，`dotnet publish` **不会**自动把它带进输出目录，
> 结果发布版启动即 `0xC000027B`（STATUS_STOWED_EXCEPTION）秒退。
> csproj 里因此有一个 `CopyPriToPublish` Target（`AfterTargets="Publish"`）自动补入，
> 构建日志会打印「安棚：已把 Anpeng.Desktop.pri 补入发布目录 …」。
> **不要删掉这个 Target。**

### 命令行开关

| 开关 | 作用 |
| --- | --- |
| `--page <tag>` | 启动直接落到指定页（`overview` / `realtime` / `alarms` / `sprinkler` / `devices` / `reports` / `settings` / `debug`） |
| `--barn <id>` | 启动时把实时监测页切到指定棚（`B01`…`B05`），用于分别截取三级告警的对照图 |
| `--theme light\|dark\|system` | 强制主题 |
| `--size <W>x<H>` | 覆盖窗口初始尺寸（截图取证用，默认按显示器工作区自适应） |
| `--scroll bottom` | 启动后把当前页滚到底（用于截取设置页首屏之下的「关于」块） |
| `--exit-after <秒>` | 到点自动正常退出（截图取证用） |
| `--selftest` | 跑三条分级链路 + 统一判据正反例，打印断言结果，退出码 = 是否全通过 |
| `--selftest-real` | 真跑运行期链路（B04 真实信号 + B05 三级递进演练 90 个周期） |
| `--parity <路径>` | 输出跨端判据 parity JSON（阈值 + C1–C10 十个固定用例） |
| `--reset-demo` | 按契约重建演示数据集后退出 |

自检结果会同时写到 `%TEMP%\anpeng-selftest.txt`（**UTF-8 无 BOM**），便于在无控制台的
GUI 进程下取证。注意：GUI 进程直接经 `pwsh` 捕获 stdout 会得到 GBK 乱码，请读该文件：
`[IO.File]::ReadAllText($p, [Text.Encoding]::UTF8)`。

---

## 四、自包含便携版的体积：219.47 MB / 521 文件是诚实的代价，不要瘦身

`anpeng/dist/` 与发布目录是一份**非打包自包含** WinUI 3 应用（Windows App SDK 2.2.0 全套原生依赖）。
实测把看似「用不到」的 70 个文件（`onnxruntime.dll` / `DirectML.dll` / `Microsoft.ML.OnnxRuntime.dll` /
`WinUIEdit.dll` / `PerceptiveStreaming.dll` / `NPUDetect.dll` / `Microsoft.Windows.AI.*` / `Microsoft.Windows.Vision*` /
`WebView2*` / `msquic.dll` / `createdump.exe` / `mscordaccore*` …）删掉可省 **59.54 MB**（当时构建 219.36 → 159.82 MB），
**但那是不可用的**：

> ⚠️ **「进程存活」不等于「渲染正常」** —— 瘦身版启动后进程**仍然存活**、
> `MainWindowTitle` **仍然正确**，只看这两项会误判为成功；实际主内容区**完全不渲染**
> （总览页只剩页面标题与副标题，四张统计卡与五张棚卡全空），
> 而且 5 个不同页面截出的 PNG **字节数完全相同**（都是 40123），说明窗口内容根本没重绘过。

所以验证必须是**「起窗口 + 抓窗口内容 + 像素探针命中品牌色」**三件套，
不能用「进程还活着 + 标题对」代替（`../_verify/capture.ps1` 就是按这个口径做的）。

**结论：保留原样，不做任何文件裁剪。**

### 交付形态

`anpeng/dist/` 里同时保留两套命名，且必须**成套同步**（改一个就得改齐）：

| 文件 | 说明 |
| --- | --- |
| `安棚 1.0.0.exe` | 交付入口（双击即用）。它是 apphost，**内嵌的 DLL 名固定是 `Anpeng.Desktop.dll`**，所以不能把 dll 跟着改名 |
| `Anpeng.Desktop.exe` / `.dll` / `.pri` / `.deps.json` / `.runtimeconfig.json` | 原始自包含负载，**名字不能动** |
| `安棚 1.0.0.pri` | 与 `Anpeng.Desktop.pri` 同一份内容的副本 |
| `安棚-v1.0.0.apk` | Android 手持端产物（另一个交付方负责，不要动） |

**为什么改名后必须补一份 `.pri`（本轮实测踩到的根因）**：非打包 WinUI 3 的 MRT Core
按**可执行文件名**去查找同名资源包。主程序一改名成 `安棚 1.0.0.exe`，它就会去找
`安棚 1.0.0.pri`；找不到时进程会静默秒退（`0xC0000135` 一类），既没有窗口也没有日志，
看起来像"双击没反应"。同理 apphost 内嵌的托管集名固定是 `Anpeng.Desktop.dll`，
**把 dll 跟着改名同样起不来**。正确做法只有一条：整份自包含负载原样复制，只改主 exe 名，
再补一份与它同名、内容相同的 `.pri`。

---

## 五、离线可用（APP-SPEC R4）

检测（MQ-2 / PT100）、判断（`AlarmEvaluator`）、报警、喷淋动作全部在本地闭环，
界面常驻「本地运行中」标识，断网不改变任何行为。OneNet 云端推送为可选增强，
未联网时仅记入本地处置记录。

---

## 六、自证入口（本仓库的验收口径）

```powershell
# 1) 构建必须 0 警告 0 错误
dotnet build Anpeng.Desktop.csproj -c Release

# 2) 判据自检（三条链路 + 统一判据正反例 + 运行期真实链路）
.\bin\Release\net10.0-windows10.0.26100.0\win-x64\Anpeng.Desktop.exe --selftest --selftest-real
#    → 结果写 %TEMP%\anpeng-selftest.txt（UTF-8），退出码 0 = 全通过

# 3) 跨端判据 parity 证据
.\...\Anpeng.Desktop.exe --parity ..\..\_verify\parity-desktop.json

# 4) 逐页截图 + 像素探针（窗口内容级证据，落 ..\_verify\）
pwsh -NoProfile -File ..\_verify\capture.ps1
```

界面内还有一个「判据自检」页（导航 Tag `debug`），把上面第 2 步的断言清单与三条链路
的逐周期数据直接渲染出来，方便评委当场复核。
