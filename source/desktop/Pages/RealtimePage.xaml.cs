using Anpeng.Desktop.Controls;
using Anpeng.Desktop.Models;
using Anpeng.Desktop.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Anpeng.Desktop.Pages;

/// <summary>
/// 实时监测（APP-SPEC 5.2）：选中棚的实时双曲线（温度 + 烟雾，含阈值参考带）、
/// 当前读数大字 + 温升速率 + 超限计时、最近 30 条采样表。
/// </summary>
public sealed partial class RealtimePage : Page
{
    private bool _built;

    public RealtimePage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (!_built)
            {
                _built = true;
                RuntimeState.Monitor.Changed += Rebuild;
            }
            Rebuild();
        };
        Unloaded += (_, _) =>
        {
            if (_built)
            {
                RuntimeState.Monitor.Changed -= Rebuild;
                _built = false;
            }
        };
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string barnId && !string.IsNullOrWhiteSpace(barnId))
        {
            RuntimeState.Monitor.SetActiveBarn(barnId);
        }
    }

    private void Rebuild()
    {
        var monitor = RuntimeState.Monitor;
        var settings = monitor.Settings;
        var barns = monitor.Data.Barns;
        if (barns.Count == 0) return;

        var activeId = monitor.ActiveBarnId ?? barns[0].Id;
        var barn = barns.FirstOrDefault(b => b.Id == activeId) ?? barns[0];
        var readings = monitor.ReadingsFor(barn.Id).OrderBy(r => r.Time).ToList();
        var evaluator = monitor.EvaluatorFor(barn.Id);
        var current = readings.LastOrDefault();
        var level = barn.CurrentLevel;

        // 只读最近一次判定结果：绝不在这里再调 Evaluate —— 那会把同一条采样二次计入
        // 历史与连续超限计时，污染判据状态。
        var eval = evaluator.LastEvaluation;

        Host.Children.Clear();

        // ── 页头 + 棚舍选择 ──
        var head = UiKit.VStack(4);
        head.Children.Add(UiKit.PageTitle("实时监测"));
        head.Children.Add(UiKit.Caption("本地采样闭环：MQ-2 烟雾 + PT100 温度逐周期进入 AlarmEvaluator，界面与判据同源"));

        var pickerRow = UiKit.HStack(12);
        pickerRow.VerticalAlignment = VerticalAlignment.Center;
        pickerRow.Children.Add(UiKit.Label("选择草料棚", 14, FontWeights.Normal, UiKit.Muted));

        var combo = new ComboBox { MinWidth = 240, ItemsSource = barns.Select(b => b.Name).ToList() };
        combo.SelectedIndex = Math.Max(0, barns.FindIndex(b => b.Id == barn.Id));
        combo.SelectionChanged += (_, _) =>
        {
            var idx = combo.SelectedIndex;
            if (idx < 0 || idx >= barns.Count) return;
            RuntimeState.Monitor.SetActiveBarn(barns[idx].Id);
        };
        pickerRow.Children.Add(combo);
        pickerRow.Children.Add(UiKit.Pill(AlarmLevelInfo.Name(level), level, solid: level == AlarmLevel.Alarm));

        var runChip = UiKit.Pill(RuntimeState.IsTicking ? "实时采集中" : "已暂停",
            RuntimeState.IsTicking ? AlarmLevel.Normal : AlarmLevel.Offline);
        pickerRow.Children.Add(runChip);
        pickerRow.Children.Add(UiKit.Pill(monitor.Clock.Now.ToString("HH:mm:ss"), AlarmLevel.Normal));

        var toggle = UiKit.PlainButton(RuntimeState.IsTicking ? "暂停采样" : "继续采样", (_, _) =>
        {
            if (RuntimeState.IsTicking) RuntimeState.StopTicking();
            else RuntimeState.StartTicking(this.DispatcherQueue);
            Rebuild();
        });
        pickerRow.Children.Add(toggle);

        var headCard = UiKit.Card(pickerRow);
        head.Children.Add(headCard);
        Host.Children.Add(head);

        // ── 大字读数 + 温升速率 + 超限计时 ──
        var readout = UiKit.EqualColumns(4, 12);

        // 读数一律取引擎选定的展示小数位（APP-SPEC §4.2.1 第 1 条）：
        // 这里是「显示 799.6 还是 800」的唯一决定点，不能让界面把未超限的读数写成超限的样子。
        var smokeText = eval is not null
            ? ValueDisplay.Text(eval.SmokePpm, eval.SmokeDecimals)
            : current is null ? "—" : ValueDisplay.Text(current.SmokePpm, 0);
        var tempText = eval is not null
            ? ValueDisplay.Text(eval.TempC, eval.TempDecimals)
            : current is null ? "—" : ValueDisplay.Text(current.TempC, 1);
        var riseText = eval is not null
            ? ValueDisplay.Text(eval.RisePerMinute, eval.RiseDecimals)
            : "0.00";
        var overText = eval is not null
            ? ValueDisplay.Text(eval.OverLimitSeconds, eval.OverLimitDecimals)
            : "0.0";

        var r0 = BuildReadout("当前烟雾浓度", smokeText, "ppm",
            $"预警阈值 {settings.SmokeNoticePpm:0} ppm", AlarmLevel.Notice);
        var r1 = BuildReadout("当前棚内温度", tempText, "°C",
            $"预警 {settings.TempNoticeC:0.0} °C / 确认 {settings.TempAlarmC:0.0} °C", AlarmLevel.Alarm);
        var r2 = BuildReadout("温升速率", riseText, "°C/min",
            $"判据阈值 {settings.TempRiseCPerMinute:0.0} °C/min（{AlarmEvaluator.RiseWindowSeconds} s 滑动窗口，最小真实跨度 {AlarmEvaluator.RiseWindowMinSeconds:0.0} s）", AlarmLevel.Suspect);
        var r3 = BuildReadout("两项连续超限", overText, "s",
            $"烟温同时超限的真实计时；满 {settings.ConfirmSeconds:0} s 即确认，或更早命中温升/温度确认值（须先过防误喷门槛 {AlarmEvaluator.SprayGateSeconds:0.0} s）", AlarmLevel.Alarm);

        Grid.SetColumn(r0, 0);
        Grid.SetColumn(r1, 1);
        Grid.SetColumn(r2, 2);
        Grid.SetColumn(r3, 3);
        readout.Children.Add(r0);
        readout.Children.Add(r1);
        readout.Children.Add(r2);
        readout.Children.Add(r3);
        Host.Children.Add(readout);

        // ── 是否触发喷淋：本产品最关键的差异点，必须大字明示（契约 4.2 前两级绝不喷淋）──
        Host.Children.Add(BuildSprinklerBanner(barn, eval, current));

        // ── 双曲线 ──
        var chart = new TimeSeriesGraph
        {
            Height = 220,
            BarnName = $"{barn.Name} · 最近 {Math.Min(60, readings.Count)} 个采样点",
            SmokeNoticePpm = settings.SmokeNoticePpm,
            TempNoticeC = settings.TempNoticeC,
            TempAlarmC = settings.TempAlarmC,
            Level = level,
            Readings = readings.TakeLast(60).ToList(),
        };
        var legend = UiKit.HStack(16);
        legend.Children.Add(BuildLegend("烟雾浓度（左轴归一化）", UiKit.Res("BrandDropBrush")));
        legend.Children.Add(BuildLegend("温度（右轴归一化）", UiKit.Fire));
        legend.Children.Add(BuildLegend($"烟雾预警线 {settings.SmokeNoticePpm:0} ppm", UiKit.Level(AlarmLevel.Notice)));
        legend.Children.Add(BuildLegend($"温度确认线 {settings.TempAlarmC:0.0} °C", UiKit.Level(AlarmLevel.Alarm)));

        var chartBody = UiKit.VStack(10);
        chartBody.Children.Add(UiKit.Scroll(legend, 40));
        chartBody.Children.Add(chart);
        Host.Children.Add(UiKit.TitledCard("实时双曲线（含阈值参考带）", chartBody,
            "采样周期 " + $"{settings.SampleSeconds:0.0} s；曲线的归一化只影响显示，判据使用原始值"));

        // ── 判据链路 ──
        Host.Children.Add(BuildChainCard(barn, eval, current));

        // ── 最近 30 条采样 ──
        // 采样表里的读数同样走自适应展示位：宁可多显示一位小数，也不让表格把未超限的读数写成阈值本身。
        var rows = readings.TakeLast(30).Reverse()
            .Select(r =>
            {
                var s = ValueDisplay.VsMulti(r.SmokePpm, settings.SmokeNoticePpm);
                var t = ValueDisplay.VsMulti(r.TempC, settings.TempNoticeC, settings.TempAlarmC);
                return new[]
                {
                    r.Time.ToString("HH:mm:ss"),
                    ValueDisplay.Text(s.Value, s.Decimals),
                    ValueDisplay.Text(t.Value, t.Decimals),
                    $"{r.Voltage:0.00}",
                    r.Online ? "在线" : "离线",
                    ValveStateInfo.Name(r.Valve),
                };
            })
            .ToList();
        Host.Children.Add(UiKit.TitledCard("最近 30 条采样",
            UiKit.Table(
                new[] { "时间", "烟雾(ppm)", "温度(°C)", "电压(V)", "在线状态", "阀门状态" }, rows),
            $"共 {readings.Count} 条本棚采样，落盘于 %LOCALAPPDATA%\\安棚\\data.json"));
    }

    /// <summary>
    /// 「是否触发喷淋」横幅：把本级判定的执行动作大字摆出来。
    /// 这是安棚与普通烟感报警器最关键的差异（契约 4.2：环境预警与疑似火情只报警不喷淋，
    /// 确认火警才开阀），必须让评审在一屏内看到，不能只藏在判据文字里。
    /// </summary>
    private static Border BuildSprinklerBanner(Barn barn, AlarmEvaluation? eval, SensorReading? current)
    {
        var level = eval?.Level ?? AlarmLevel.Offline;
        var requests = eval?.RequestSprinkler == true;
        var valve = current?.Valve ?? ValveState.Closed;

        var left = UiKit.VStack(6);
        left.Children.Add(UiKit.Label("是否触发喷淋（本级判定结果）", 13, FontWeights.Normal, UiKit.Muted));
        left.Children.Add(UiKit.Label(
            requests ? "触发喷淋：开阀" : "不触发喷淋：保持关阀",
            28, FontWeights.SemiBold, UiKit.Level(requests ? AlarmLevel.Alarm : level)));
        left.Children.Add(UiKit.Label(
            requests
                ? $"{AlarmLevelInfo.Name(level)}：本机长鸣 + 继电器动作开阀 + 写入处置记录（棚主远程推送由现场 ESP-01 → OneNet 链路承担，本监控台离线运行）"
                : $"{AlarmLevelInfo.Name(level)}：{AlarmLevelInfo.Action(level)}",
            13, FontWeights.Normal, UiKit.Ink));

        var right = UiKit.VStack(6);
        right.VerticalAlignment = VerticalAlignment.Center;
        right.Children.Add(UiKit.Pill(AlarmLevelInfo.Name(level), level, solid: level == AlarmLevel.Alarm));
        right.Children.Add(UiKit.Pill($"阀门回读：{ValveStateInfo.Name(valve)}",
            valve == ValveState.Open ? AlarmLevel.Alarm : AlarmLevel.Normal));

        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        g.Children.Add(left);
        g.Children.Add(right);

        var card = UiKit.Card(g, 16);
        card.BorderBrush = UiKit.Level(level);
        card.BorderThickness = new Thickness(1.5);
        return card;
    }

    private static Border BuildReadout(string label, string value, string unit, string note, AlarmLevel accent)
    {
        var stack = UiKit.VStack(4);
        stack.Children.Add(UiKit.Caption(label));
        var row = UiKit.HStack(6);
        row.VerticalAlignment = VerticalAlignment.Bottom;
        row.Children.Add(UiKit.Readout(value));
        var u = UiKit.Label(unit, 16, FontWeights.Normal, UiKit.Muted);
        u.Margin = new Thickness(0, 0, 0, 8);
        row.Children.Add(u);
        stack.Children.Add(row);
        stack.Children.Add(UiKit.Label(note, 12, FontWeights.Normal, UiKit.Level(accent)));
        return UiKit.Card(stack);
    }

    private static StackPanel BuildLegend(string text, Microsoft.UI.Xaml.Media.Brush brush)
    {
        var p = UiKit.HStack(6);
        p.VerticalAlignment = VerticalAlignment.Center;
        p.Children.Add(new Border
        {
            Width = 18,
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Background = brush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        p.Children.Add(UiKit.Label(text, 12, FontWeights.Normal, UiKit.Muted));
        return p;
    }

    private static Border BuildChainCard(Barn barn, AlarmEvaluation? eval, SensorReading? current)
    {
        var level = eval?.Level ?? AlarmLevel.Offline;
        var stack = UiKit.VStack(10);
        stack.Children.Add(BuildChainStep("① 触发", "1",
            current is null
                ? "暂无采样"
                : eval is not null
                    ? $"烟雾 {eval.SmokeText} ppm、温度 {eval.TempText} °C（{current.Time:HH:mm:ss}）"
                    : $"烟雾与温度（{current.Time:HH:mm:ss}）尚未评估"));
        stack.Children.Add(BuildChainStep("② 判断", "2", eval?.ReasonText ?? "尚未评估"));
        stack.Children.Add(BuildChainStep("③ 动作", "3",
            $"{AlarmLevelInfo.Action(level)}（本级请求喷淋：{(eval?.RequestSprinkler == true ? "是" : "否")}）"));

        var head = UiKit.HStack(12);
        head.Children.Add(UiKit.Pill(AlarmLevelInfo.Name(level), level, solid: level == AlarmLevel.Alarm));
        head.Children.Add(UiKit.Caption($"升级事件：{(eval?.Escalated == true ? "已产生" : "未产生")} · 阀门回读：{ValveStateInfo.Name(current?.Valve ?? ValveState.Closed)}"));

        var body = UiKit.VStack(10);
        body.Children.Add(head);
        body.Children.Add(UiKit.Divider());
        body.Children.Add(UiKit.Label($"【触发 → 判断 → 动作】{barn.Name}", 14, FontWeights.SemiBold, UiKit.BrandText));
        body.Children.Add(stack);
        return UiKit.Card(body);
    }

    private static Grid BuildChainStep(string title, string index, string detail)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var t = UiKit.Label($"{index}. {title}", 14, FontWeights.SemiBold, UiKit.BrandText);
        Grid.SetColumn(t, 0);
        var d = UiKit.Label(detail, 14, FontWeights.Normal, UiKit.Ink);
        Grid.SetColumn(d, 1);
        g.Children.Add(t);
        g.Children.Add(d);
        return g;
    }
}
