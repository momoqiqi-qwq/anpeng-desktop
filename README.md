<div align="center">

<img src="./assets/hero.svg" alt="anpeng-desktop" width="1000" />

**安棚农村草料棚智能火灾预警与初期喷淋系统 Windows 桌面端**

[![stars](https://img.shields.io/github/stars/momoqiqi-qwq/anpeng-desktop?style=for-the-badge&color=f5c518&labelColor=2b2b2b)](https://github.com/momoqiqi-qwq/anpeng-desktop/stargazers) ![C%23](https://img.shields.io/badge/C%23-68217A?style=for-the-badge&logo=dotnet&logoColor=white) ![Windows](https://img.shields.io/badge/Windows-0078D4?style=for-the-badge&logo=windows&logoColor=white)

</div>

<details open>
<summary><b>目录</b></summary>

- [🧭 这是什么](#-这是什么)
- [🔥 三级分级判据（产品内核）](#-三级分级判据产品内核)
- [🖥 界面](#-界面)
- [🧱 技术栈](#-技术栈)
- [🗂 目录结构](#-目录结构)
- [🚀 构建与运行](#-构建与运行)
- [🧪 验证与优化记录](#-验证与优化记录)
- [📄 契约文档](#-契约文档)
- [⚠️ 说明](#️-说明)

</details>

---

## 🧭 这是什么

**安棚**是给中小养殖户的草料棚装的一套**买得起、装得上、不用改建筑**的自动火灾预警与初期喷淋装置。这个仓库是其中的 **Windows 桌面监控台**（WinUI 3）：棚主、合作社、运维在同一块屏幕上看到全部棚舍的实时读数、分级状态、报警记录与喷淋动作。

干草料集中堆放、草捆内部阴燃、夜间无人值守——这三种情况叠在一起，等看见明火时往往已经晚了。所以监控台要做的不是"超阈值就弹窗"，而是**把阈值判断收紧成一条动作链**：先分辨是环境波动还是真火，确认了才开水。

- 产品名：**安棚 / ANPENG**
- 主体：安棚智慧消防科技有限责任公司
- 版本：**1.0.0**（源码与发布包 Portable PDB 中 49 个业务文件 SHA256 一致）
- 事实源：[`source/APP-SPEC.md`](source/APP-SPEC.md) —— 界面、配色、标志几何、判据全部以它为准

## 🔥 三级分级判据（产品内核）

这是整个产品最不能含糊的地方。**只有"确认火警"才开阀；环境预警与疑似火情永不喷淋。**

```
环境预警 ⟺ 烟雾、温度中「仅一项」超预警阈值
疑似火情 ⟺ 烟雾与温度「同时」超限，但不满足确认火警条件
确认火警 ⟺ bothOver
          且 bothSec ≥ SprayGateSeconds          // 4.0 s 防误喷连续性门槛
          且 ( bothSec ≥ 30 s                    // 持续时长
               OR rise ≥ 5.0 °C/min              // 温升速率
               OR temp ≥ 70.0 °C )               // 温度确认火警阈值
```

判据在 [`Services/AlarmEvaluator.cs`](source/desktop/Services/AlarmEvaluator.cs)，与 Android 手持端同一份逻辑，对同一批采样必须得出同一级别；机器可比对的证据由 `Anpeng.Desktop.exe --parity <路径>` 生成。

几个刻意的设计：

| 决定 | 为什么 |
| --- | --- |
| `SprayGateSeconds = 4.0` 写成常量，不写成 `SampleSeconds * 2` | 采样周期可调 1~60 s，派生写法在 1 s 时退化成 2 s，会和硬编码 4.0 的 Android 端分裂 |
| 温升窗口要求**真实跨度 ≥ 9 s**，不足则 `rise = 0` | 否则相隔 1 ms 的两个样本差 0.05 °C 就被放大成几十 °C/min，桌面端会误喷 |
| 开阀保持时长用**秒**常量，再换算周期数 | 早期写成 `SampleSeconds * 8`，改采样周期后"保持多少秒"跟着漂移，界面文案变成假话 |
| 时间戳相等 → 重置计时；倒退 → 清空窗口 | 任何一个异常都不许产出负数或 NaN 时长（置 `TimeAnomaly`），另有 64 条采样硬上限兜底 |

## 🖥 界面

七个页面，导航数组与 `Pages/` 一一对应：

| 页面 | 干什么 |
| --- | --- |
| [总览](source/desktop/Pages/OverviewPage.xaml) | 全部棚舍的状态卡片墙，按告警级别排序 |
| [实时](source/desktop/Pages/RealtimePage.xaml) | 单棚烟雾/温度曲线与当前判据命中情况 |
| [喷淋控制](source/desktop/Pages/SprinklerControlPage.xaml) | 手动开阀、自动联动记录、阀门状态 |
| [报警记录](source/desktop/Pages/AlarmRecordsPage.xaml) | 分级告警流水与处置状态 |
| [报表](source/desktop/Pages/ReportsPage.xaml) | 日/周/月统计（均值、峰值、次数） |
| [设备管理](source/desktop/Pages/DeviceManagementPage.xaml) | 节点、标定、时钟 |
| [设置](source/desktop/Pages/SettingsPage.xaml) | 阈值、采样周期、联动开关 |

品牌标志按 APP-SPEC 的几何数据用 WinUI `Path` 矢量绘制（[`Controls/AnpengLogo.xaml`](source/desktop/Controls/AnpengLogo.xaml)），不位图拉伸、不改色。

## 🧱 技术栈

- **WinUI 3**（Windows App SDK 2.2.0）+ **.NET 10**，`net10.0-windows10.0.26100.0`
- 非打包自包含：`WindowsPackageType=None` + `WindowsAppSDKSelfContained=true`，拷走整个文件夹就能跑
- 本地闭环：检测、判断、报警、喷淋全部不依赖网络；联网只用于上云与远程推送
- 演示数据是草料棚语境的真实中文内容，仓库里没有 `TODO` / `lorem`

## 🗂 目录结构

```
source/
  APP-SPEC.md              应用契约（颜色、标志几何、判据、铁律）
  desktop/
    Controls/              标志、级别徽章、时序图、UI Kit
    Data/                  仓储与演示数据
    Models/                棚舍、读数、告警、喷淋、标定、阀门
    Pages/                 七个页面
    Services/              判据评估、监控循环、报表、蜂鸣、自检、parity
    Styles/                设计令牌与控件样式
verification/
  OptimizationChecks/      无外部依赖的回归工装（1100 项断言）
  optimization.patch       优化前后完整差异
  delivery-manifest.json   交付文件摘要与代码哈希
优化说明.md                 优化项、验证结果、性能测量、复现命令
```

## 🚀 构建与运行

需要 .NET 10 SDK 与 Windows App SDK 2.2.0 的本地 NuGet 缓存：

```powershell
dotnet build .\source\desktop\Anpeng.Desktop.csproj -c Release `
  -p:RestoreSources='C:\Users\<you>\.nuget\packages' -p:NuGetAudit=false

dotnet publish .\source\desktop\Anpeng.Desktop.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=false `
  -p:RestoreSources='C:\Users\<you>\.nuget\packages' -p:NuGetAudit=false `
  -o .\output\安棚桌面端
```

自检与 parity：

```powershell
.\output\安棚桌面端\Anpeng.Desktop.exe --selftest
.\output\安棚桌面端\Anpeng.Desktop.exe --parity .\parity-desktop.json
```

## 🧪 验证与优化记录

上一轮代码优化的结论都在 [`优化说明.md`](优化说明.md) 里，摘要：

- Release 构建 0 警告 0 错误；自包含发布成功
- 回归工装 **1100 项断言通过**，原有报警/喷淋链路自检 61/61
- 报表 **354 组数据**新旧结果一致（含边界前后 1 tick、空数据、随机数据、非有限浮点）
- 保存 1200 条采样的落盘字节与旧序列化方式完全一致
- 采样汇总函数 64.73 μs → **6.01 μs**，线程托管分配 29570 B → **0 B**（本机微基准，约 10.78 倍；只代表该函数）
- **未做逐页视觉验收**：桌面自动化工具报窗口身份识别错误，无法抓界面。这一项仍是空的，写在这里而不是装作跑过了

复跑工装：

```powershell
dotnet restore .\verification\OptimizationChecks\OptimizationChecks.csproj --configfile .\verification\OptimizationChecks\NuGet.Config
dotnet run --project .\verification\OptimizationChecks\OptimizationChecks.csproj -c Release --no-restore
```

## 📄 契约文档

| 文档 | 内容 |
| --- | --- |
| [APP-SPEC.md](source/APP-SPEC.md) | 铁律 R1–R7、标志精确几何（512×512，`#14538C` / `#EE5A2E` / `#6FCBEE`）、设计令牌、判据定义 |
| [desktop/README.md](source/desktop/README.md) | 判据口径详解、常量表、两个评委可能追问的口径说明 |
| [verification/OptimizationChecks/README.md](verification/OptimizationChecks/README.md) | 工装如何隔离运行、覆盖范围 |

## ⚠️ 说明

参赛项目（第 9 届精武杯 · 创客比赛），桌面端为 Windows 专属；Android 手持端在另一处代码库，不在本仓库内。界面截图尚未入库——上面"未做逐页视觉验收"就是这个原因，不放渲染图充数。

