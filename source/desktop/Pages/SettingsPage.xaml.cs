using Anpeng.Desktop.Controls;
using Anpeng.Desktop.Models;
using Anpeng.Desktop.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Anpeng.Desktop.Pages;

/// <summary>
/// 设置（APP-SPEC 5.7）：阈值设置（4.4 全部项）、报警声音开关、判据联动说明、数据与账号，
/// 以及关于页（安棚矢量标志 + 版本 + 公司全称 + 一句话产品说明）。
/// </summary>
public sealed partial class SettingsPage : Page
{
    /// <summary>「连续超限确认时长」至少要覆盖的采样周期数（输入下限钳制用，非判据门槛）。见 <see cref="SettingsBounds"/>。</summary>
    private const double MinConfirmCycles = SettingsBounds.MinConfirmCycles;

    private bool _built;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (!_built) _built = true;
            Rebuild();
        };
    }

    private void Rebuild()
    {
        var repo = RuntimeState.Repo;
        var s = repo.Settings;

        Host.Children.Clear();

        var head = UiKit.VStack(4);
        head.Children.Add(UiKit.PageTitle("设置"));
        head.Children.Add(UiKit.Caption(
            "阈值改动即时生效：监控服务每个采样周期都会把设置同步到各棚的 AlarmEvaluator"));
        Host.Children.Add(head);

        // ── 阈值设置（APP-SPEC 4.4 全部项） ──
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnSpacing = 16;

        var left = UiKit.VStack(12);
        left.Children.Add(ThresholdRow("烟雾预警阈值（ppm）", s.SmokeNoticePpm, 50, 5000, 20, v => s.SmokeNoticePpm = v,
            "MQ-2 等效浓度，超过即触发环境预警（单项异常，不喷淋）"));
        left.Children.Add(ThresholdRow("温度预警阈值（°C）", s.TempNoticeC, 30, 120, 0.5, v => s.TempNoticeC = v,
            "PT100 + MAX31865 三线制读数，超过即触发环境预警"));
        left.Children.Add(ThresholdRow("温度确认火警阈值（°C）", s.TempAlarmC, 40, 200, 0.5, v => s.TempAlarmC = v,
            "确认火警三条 disjunct 之一：温度按原始读数 ≥ 该值即满足该条，不经过展示取整"));
        Grid.SetColumn(left, 0);

        var right = UiKit.VStack(12);
        right.Children.Add(ThresholdRow("温升速率阈值（°C/min）", s.TempRiseCPerMinute, 0.5, 40, 0.5,
            v => s.TempRiseCPerMinute = v, $"按 {AlarmEvaluator.RiseWindowSeconds} s 滑动窗口计算，用于识别草捆内部阴燃"));
        right.Children.Add(ThresholdRow("连续超限确认时长（s）", s.ConfirmSeconds, 4, 600, 2, v => s.ConfirmSeconds = v,
            $"两项同时连续超限达到该时长才可能升级为确认火警。"
            + $"引擎的防误喷连续性门槛是独立常量 {AlarmEvaluator.SprayGateSeconds:0.0} s，与本项无关。"
            + SettingsBounds.ConfirmSecondsHint));
        right.Children.Add(ThresholdRow("采样周期（s）", s.SampleSeconds, 1, 60, 1, v => s.SampleSeconds = v,
            "STM 单片机逐周期上报；改变周期后实时轮询节拍同步调整"));
        Grid.SetColumn(right, 1);

        grid.Children.Add(left);
        grid.Children.Add(right);

        var thresholdBody = UiKit.VStack(12);
        thresholdBody.Children.Add(grid);
        thresholdBody.Children.Add(UiKit.Divider());

        // DEF-15：契约 APP-SPEC 4.4 只列 5 项阈值，确认火警只有三条 disjunct
        //（持续时长 / 温升速率 / 温度确认阈值）。此处曾多出一个「烟雾确认阈值」控件，
        // 判据从不读它，属功能性虚假承诺，已移除。

        var buttons = UiKit.HStack(12);
        buttons.Children.Add(UiKit.PrimaryButton("保存阈值", (_, _) =>
        {
            if (s.SampleSeconds < 1) s.SampleSeconds = 1;
            // 确认时长必须至少覆盖两个完整采样周期，否则用户把采样周期调大后这一项就失去意义。
            // 这是对「确认时长」这一项输入的下限钳制，与防误喷连续性门槛（AlarmEvaluator.SprayGateSeconds
            // = 4.0 s 的独立常量）无关，不要混淆。
            var minConfirmSeconds = s.SampleSeconds * MinConfirmCycles;
            if (s.ConfirmSeconds < minConfirmSeconds) s.ConfirmSeconds = minConfirmSeconds;
            repo.SaveSettings();
            RuntimeState.StartTicking(DispatcherQueue);
            AppLog.Info("阈值设置已保存并同步到判定引擎");
            Rebuild();
        }));
        buttons.Children.Add(UiKit.PlainButton("恢复契约 4.4 默认值", (_, _) =>
        {
            s.SmokeNoticePpm = 800;
            s.TempNoticeC = 55.0;
            s.TempAlarmC = 70.0;
            s.TempRiseCPerMinute = 5.0;
            s.ConfirmSeconds = 30;
            s.SampleSeconds = 2;
            repo.SaveSettings();
            RuntimeState.StartTicking(DispatcherQueue);
            Rebuild();
        }));
        buttons.Children.Add(UiKit.PlainButton("重新载入演示数据", async (_, _) => await ResetDemoAsync()));
        thresholdBody.Children.Add(buttons);

        Host.Children.Add(UiKit.TitledCard("阈值设置（APP-SPEC 4.4）", thresholdBody,
            $"落盘位置 {Path.Combine(repo.Directory, "settings.json")}"));

        // ── 开关 ──
        var switches = UiKit.VStack(10);
        // DEF-D4：本项是**真开关**。读取点：Services\MonitoringService.cs 的 RaiseAlarm 里
        // `if (Settings.AlarmSoundEnabled) AlarmBeeper.Play(eval.Level)`。关闭后新报警不再出声。
        // 声音由本监控台的系统提示音发；棚内真实蜂鸣器在 STC89C52 节点上，由单片机驱动，与本开关是两条链路。
        switches.Children.Add(ToggleRow("报警声音提示", s.AlarmSoundEnabled, v => s.AlarmSoundEnabled = v,
            "开启后，监控台在本机用系统提示音按三级鸣响节奏发声（环境预警短促单响 / 疑似火情连续双响 / 确认火警长鸣）；关闭即静音。棚内蜂鸣器由 STC89C52 节点驱动，不随本开关变化"));
        switches.Children.Add(ToggleRow("确认火警自动开阀喷淋", s.AutoSprinklerEnabled, v => s.AutoSprinklerEnabled = v,
            "关闭后只报警不喷淋（用于调试 / 无水压场景），环境预警与疑似火情无论开关如何都不喷淋"));
        // DEF-D4：本应用按契约 R4 完全本地运行、不联网，因此这一项**当前不生效**，文案必须如实。
        switches.Children.Add(ToggleRow("远程推送棚主（预留项，当前不生效）", s.PushEnabled, v => s.PushEnabled = v,
            "如实说明：推送由现场 ESP-01 → OneNet 云平台链路承担，本监控台按契约 R4 离线运行、不承载联网推送，因此本开关目前只记录意图、不会真的发出任何推送；界面与报警详情不得声称「已推送」"));
        var soundCard = UiKit.TitledCard("报警与联动开关", switches, "开关改动即时落盘 settings.json");
        Host.Children.Add(soundCard);

        // ── 判据联动说明 ──
        var rules = UiKit.VStack(10);
        rules.Children.Add(RuleRow(AlarmLevel.Notice,
            $"任一单项超限（烟雾 ≥ {s.SmokeNoticePpm:0} ppm 或 温度 ≥ {s.TempNoticeC:0.0} °C）",
            "只提示，不喷淋"));
        rules.Children.Add(RuleRow(AlarmLevel.Suspect, "烟雾与温度两项同时超限，但未满足确认火警条件", "只报警（棚主通知由现场 ESP-01 → OneNet 链路承担），不喷淋"));
        rules.Children.Add(RuleRow(AlarmLevel.Alarm,
            $"两项同时连续超限 ≥ {AlarmEvaluator.SprayGateSeconds:0.0} s，且满足三项之一：持续 ≥ {s.ConfirmSeconds:0} s、温升 ≥ {s.TempRiseCPerMinute:0.0} °C/min、温度 ≥ {s.TempAlarmC:0.0} °C",
            "本机长鸣 + 开阀喷淋 + 写入处置记录；棚主远程推送由现场 ESP-01 → OneNet 链路承担，本监控台离线运行、不声称已推送"));
        rules.Children.Add(UiKit.Label(
            "防误喷设计：连续采样滤波 + 温升趋势双重条件；前两级永不驱动阀门；草料扬尘干扰纳入现场干扰试验。",
            13, FontWeights.Normal, UiKit.Muted));
        Host.Children.Add(UiKit.TitledCard("判据联动说明", rules, "判据实现为纯逻辑类 Services\\AlarmEvaluator.cs，可在「判据自检」页复现"));

        // ── 数据与账号 ──
        var data = RuntimeState.Monitor.Data;
        var kv = UiKit.VStack(8);
        kv.Children.Add(UiKit.KeyValue("场地名称", s.SiteName));
        kv.Children.Add(UiKit.KeyValue("值班运维", s.Operator));
        kv.Children.Add(UiKit.KeyValue("数据目录", repo.Directory));
        kv.Children.Add(UiKit.KeyValue("运行日志", repo.LogPath));
        kv.Children.Add(UiKit.KeyValue("数据规模",
            $"{data.Barns.Count} 棚 · {data.Nodes.Count} 节点 · {data.Readings.Count} 条采样 · {data.Alarms.Count} 条报警 · {data.Sprinklers.Count} 条喷淋"));
        kv.Children.Add(UiKit.KeyValue("账号体系", "本机单机运行，不需要登录，也不上传任何数据"));
        var dataBody = UiKit.VStack(12);
        dataBody.Children.Add(kv);
        dataBody.Children.Add(UiKit.Divider());
        dataBody.Children.Add(UiKit.Label(s.CloudNote, 14, FontWeights.SemiBold, UiKit.Res("BrandDropBrush")));
        Host.Children.Add(UiKit.TitledCard("数据与账号", dataBody, "本地闭环：检测、判断、报警、喷淋全部在本机完成，断网不影响"));

        // ── 关于 ──
        Host.Children.Add(BuildAboutCard());
    }

    private static Border BuildAboutCard()
    {
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnSpacing = 20;

        var logo = new AnpengLogo { DisplaySize = 128 };
        Grid.SetColumn(logo, 0);

        var text = UiKit.VStack(10);
        text.Children.Add(UiKit.Label("ANPENG", 14, FontWeights.SemiBold, UiKit.Muted));
        text.Children.Add(UiKit.Label("安棚智慧消防科技有限责任公司", 24, FontWeights.SemiBold, UiKit.BrandText));
        text.Children.Add(UiKit.Label("安棚 · 农村草料棚智能火灾预警与初期喷淋系统", 16, FontWeights.Normal, UiKit.Ink));
        text.Children.Add(UiKit.Label(
            "给中小养殖户的草料棚装一套买得起、装得上、不用改建筑的自动火灾预警与初期喷淋装置。",
            15, FontWeights.Normal, UiKit.Ink));
        text.Children.Add(UiKit.Divider());
        var meta = UiKit.VStack(6);
        meta.Children.Add(UiKit.KeyValue("版本", "1.0.0"));
        meta.Children.Add(UiKit.KeyValue("平台", "Windows 桌面 / WinUI 3（Windows App SDK 2.2.0 + .NET 10）"));
        meta.Children.Add(UiKit.KeyValue("运行方式", "非打包便携版，免安装运行时，双击即用"));
        meta.Children.Add(UiKit.KeyValue("适用对象", "中小养殖户、养殖合作社、家庭农场"));
        meta.Children.Add(UiKit.KeyValue("单点装置造价", "≤ 1000 元（小型单点自动喷淋装置）"));
        text.Children.Add(meta);
        Grid.SetColumn(text, 1);

        body.Children.Add(logo);
        body.Children.Add(text);

        return UiKit.TitledCard("关于安棚", body,
            "安棚标志为矢量绘制（512×512 画布，圆角方底 116，棚 #FFFFFF 描边 34，火苗 #EE5A2E，水滴 #6FCBEE）");
    }

    private UIElement ThresholdRow(string label, double value, double min, double max, double step,
        Action<double> setter, string hint)
    {
        var box = new NumberBox
        {
            Header = label,
            Value = value,
            Minimum = min,
            Maximum = max,
            SmallChange = step,
            LargeChange = step * 5,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        box.ValueChanged += (_, e) =>
        {
            if (double.IsNaN(e.NewValue)) return;
            setter(e.NewValue);
        };
        var stack = UiKit.VStack(2);
        stack.Children.Add(box);
        stack.Children.Add(UiKit.Caption(hint));
        return stack;
    }

    private UIElement ToggleRow(string label, bool value, Action<bool> setter, string hint)
    {
        var toggle = new ToggleSwitch { Header = label, IsOn = value, OnContent = "已开启", OffContent = "已关闭" };
        toggle.Toggled += (_, _) =>
        {
            setter(toggle.IsOn);
            RuntimeState.Repo.SaveSettings();
        };
        var stack = UiKit.VStack(2);
        stack.Children.Add(toggle);
        stack.Children.Add(UiKit.Caption(hint));
        return stack;
    }

    private static Grid RuleRow(AlarmLevel level, string criteria, string action)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        var pill = UiKit.Pill(AlarmLevelInfo.Name(level), level, solid: level == AlarmLevel.Alarm);
        pill.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetColumn(pill, 0);
        var c = UiKit.Label(criteria, 13, FontWeights.Normal, UiKit.Ink);
        c.Margin = new Thickness(0, 4, 12, 4);
        Grid.SetColumn(c, 1);
        var a = UiKit.Label(action, 13, FontWeights.Normal, UiKit.Muted);
        a.Margin = new Thickness(0, 4, 0, 4);
        Grid.SetColumn(a, 2);
        g.Children.Add(pill);
        g.Children.Add(c);
        g.Children.Add(a);
        return g;
    }

    private async Task ResetDemoAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "重新载入演示数据？",
            Content = UiKit.Label(
                "将丢弃当前 %LOCALAPPDATA%\\安棚 下的全部运行数据，并重建契约要求的演示数据集" +
                "（≥4 座草料棚、≥30 条三级齐全的报警、≥20 条喷淋、≥200 个采样点）。",
                14, FontWeights.Normal, UiKit.Ink),
            PrimaryButtonText = "重建",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        RuntimeState.Repo.ResetToDemo();
        AppLog.Warn("用户选择重新载入演示数据");
        Rebuild();
    }
}
