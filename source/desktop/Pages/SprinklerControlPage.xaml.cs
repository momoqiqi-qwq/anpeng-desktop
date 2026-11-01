using Anpeng.Desktop.Controls;
using Anpeng.Desktop.Models;
using Anpeng.Desktop.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Anpeng.Desktop.Pages;

/// <summary>
/// 喷淋控制（APP-SPEC 5.4）：阀门当前状态大卡、手动开阀（二次确认，危险色）、手动关阀、
/// 喷淋历史（时长 / 原因 / 触发级别）、管路自检按钮。
/// </summary>
public sealed partial class SprinklerControlPage : Page
{
    private string? _barnId;
    private List<(string Item, string Result)> _selfTest = new();
    private bool _built;

    public SprinklerControlPage()
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

    private void Rebuild()
    {
        var monitor = RuntimeState.Monitor;
        var settings = monitor.Settings;
        var barns = monitor.Data.Barns;
        if (barns.Count == 0) return;

        var barn = barns.FirstOrDefault(b => b.Id == _barnId) ?? barns[0];
        _barnId = barn.Id;
        var reading = monitor.Current(barn.Id);
        var valve = reading?.Valve ?? ValveState.Closed;
        var isOpen = valve == ValveState.Open;

        Host.Children.Clear();

        // ── 页头 ──
        var head = UiKit.VStack(4);
        head.Children.Add(UiKit.PageTitle("喷淋控制"));
        head.Children.Add(UiKit.Caption(
            "DC12V 常闭式电磁阀：断电即关，是失电安全侧；只有确认火警会自动开阀，手动开阀需二次确认"));
        Host.Children.Add(head);

        // ── 选择棚 ──
        var picker = UiKit.HStack(12);
        picker.VerticalAlignment = VerticalAlignment.Center;
        picker.Children.Add(UiKit.Label("受控棚舍", 14, FontWeights.Normal, UiKit.Muted));
        var combo = new ComboBox { MinWidth = 240, ItemsSource = barns.Select(b => b.Name).ToList() };
        combo.SelectedIndex = Math.Max(0, barns.FindIndex(b => b.Id == barn.Id));
        combo.SelectionChanged += (_, _) =>
        {
            var i = combo.SelectedIndex;
            if (i < 0 || i >= barns.Count) return;
            _barnId = barns[i].Id;
            _selfTest = new List<(string, string)>();
            Rebuild();
        };
        picker.Children.Add(combo);
        picker.Children.Add(UiKit.Pill($"自动联动：{(settings.AutoSprinklerEnabled ? "已启用" : "已停用")}",
            settings.AutoSprinklerEnabled ? AlarmLevel.Normal : AlarmLevel.Offline));
        picker.Children.Add(UiKit.Pill($"喷淋覆盖：{barn.SprinklerCoverage}", AlarmLevel.Normal));
        Host.Children.Add(UiKit.Card(picker));

        // ── 阀门状态大卡 ──
        var big = UiKit.VStack(14);
        var top = UiKit.HStack(16);
        top.VerticalAlignment = VerticalAlignment.Center;
        var valveLevel = isOpen ? AlarmLevel.Alarm : valve == ValveState.Fault ? AlarmLevel.Suspect : AlarmLevel.Normal;
        top.Children.Add(UiKit.StatusDot(valveLevel, 22));
        top.Children.Add(UiKit.Label(ValveStateInfo.Name(valve), 40, FontWeights.SemiBold, UiKit.Level(valveLevel)));
        top.Children.Add(UiKit.Pill(isOpen ? "阀门已开 · 正在喷淋" : "阀门已关 · 常闭待命", valveLevel, solid: isOpen));
        big.Children.Add(top);
        var eval = RuntimeState.Monitor.EvaluatorFor(barn.Id).LastEvaluation;
        var tempText = eval is not null
            ? ValueDisplay.Text(eval.TempC, eval.TempDecimals) + " °C"
            : reading is null ? "—" : ValueDisplay.Text(reading.TempC, 1) + " °C";
        var smokeText = eval is not null
            ? ValueDisplay.Text(eval.SmokePpm, eval.SmokeDecimals) + " ppm"
            : reading is null ? "—" : ValueDisplay.Text(reading.SmokePpm, 0) + " ppm";
        big.Children.Add(UiKit.Label(
            $"{barn.Name} · {barn.Location} · 管网水压 {barn.WaterPressure:0.00} MPa · " +
            $"当前温度 {tempText} · 当前烟雾 {smokeText}",
            14, FontWeights.Normal, UiKit.Muted));

        var ops = UiKit.HStack(12);
        ops.Children.Add(UiKit.DangerButton("手动开阀喷淋（需二次确认）", async (_, _) => await ManualOpenAsync(barn)));
        ops.Children.Add(UiKit.PlainButton("手动关阀", (_, _) =>
        {
            RuntimeState.Monitor.ManualCloseValve(barn, RuntimeState.Repo.Settings.Operator);
            Rebuild();
        }));
        ops.Children.Add(UiKit.PlainButton("管路自检", (_, _) =>
        {
            _selfTest = RuntimeState.Monitor.RunPipeSelfTest(barn);
            Rebuild();
        }));
        big.Children.Add(ops);

        var warn = UiKit.Card(UiKit.Label(
            "安全提示：喷淋方向须避开配电箱、插座与牲畜活动区；涉及带电设施的火情应先安全断电再处置；" +
            "现场水压、流量与喷头覆盖需在安装前核查，草料扬尘干扰需做干扰试验。",
            13, FontWeights.Normal, UiKit.Muted), 12);
        big.Children.Add(warn);

        Host.Children.Add(UiKit.Card(big));

        // ── 管路自检结果 ──
        if (_selfTest.Count > 0)
        {
            var rows = _selfTest.Select(t => new[] { t.Item, t.Result }).ToList();
            Host.Children.Add(UiKit.TitledCard("管路自检结果",
                UiKit.Scroll(UiKit.Table(new[] { "检查项", "结论" }, rows, firstWide: true), 320),
                $"共 {_selfTest.Count} 项：喷头逐只出水检查 + 主管路保压 + 电磁阀动作 + 喷淋方向复核"));
        }

        // ── 喷淋历史 ──
        var all = monitor.Data.Sprinklers.OrderByDescending(s => s.StartTime).ToList();
        var mine = all.Where(s => s.BarnId == barn.Id).ToList();
        var rows2 = mine.Select(s => new[]
        {
            s.StartText,
            s.DurationText,
            s.Reason,
            s.LevelName,
            s.Mode,
            s.Operator,
            s.TempDropText,
        }).ToList();
        Host.Children.Add(UiKit.TitledCard($"喷淋历史（{barn.Name} · {mine.Count} 条）",
            UiKit.Scroll(UiKit.Table(
                new[] { "开始时间", "时长", "原因", "触发级别", "方式", "操作人", "温度回落" }, rows2, firstWide: true),
                380),
            $"全部草料棚共 {all.Count} 条喷淋记录（自动联动 + 人工试喷 / 自检）"));

        // ── 自动联动规则 ──
        var rule = UiKit.VStack(10);
        rule.Children.Add(UiKit.Label(
            "确认火警 → 继电器吸合 → 电磁阀开 → 开式喷头喷放，并按 " +
            $"{MonitoringService.AutoSprinklerHoldSeconds:0} s 保持后回读关闭" +
            $"（手动开阀保持 {MonitoringService.ManualSprinklerHoldSeconds:0} s）；" +
            "环境预警与疑似火情只报警，绝不驱动阀门。",
            14, FontWeights.Normal, UiKit.Ink));
        rule.Children.Add(UiKit.Bar("自动联动记录", all.Count(s => s.Mode == "自动"), Math.Max(1, all.Count),
            UiKit.Level(AlarmLevel.Alarm), $"{all.Count(s => s.Mode == "自动")} 条"));
        rule.Children.Add(UiKit.Bar("人工试喷 / 自检记录", all.Count(s => s.Mode != "自动"), Math.Max(1, all.Count),
            UiKit.Level(AlarmLevel.Normal), $"{all.Count(s => s.Mode != "自动")} 条"));
        Host.Children.Add(UiKit.TitledCard("联动规则与操作留痕", rule,
            "手动开阀 / 关阀都会写入运行日志（%LOCALAPPDATA%\\安棚\\运行日志.txt）"));
    }

    private async Task ManualOpenAsync(Barn barn)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "确认手动开阀喷淋？",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    UiKit.Label($"{barn.Name}（{barn.Location}）", 16, FontWeights.SemiBold, UiKit.BrandText),
                    UiKit.Label(
                        "手动开阀会立即向草料垛喷水，属于危险操作：请先确认棚内无人员作业、配电箱已断电、" +
                        "喷淋方向无电气设备。本次操作将记入喷淋历史与运行日志。",
                        14, FontWeights.Normal, UiKit.Ink),
                },
            },
            PrimaryButtonText = "确认开阀",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        RuntimeState.Monitor.ManualOpenValve(barn, RuntimeState.Repo.Settings.Operator, "值班人员手动开阀现场处置");
        Rebuild();
    }
}
