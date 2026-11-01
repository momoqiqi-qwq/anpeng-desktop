using Anpeng.Desktop.Controls;
using Anpeng.Desktop.Models;
using Anpeng.Desktop.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Anpeng.Desktop.Pages;

/// <summary>
/// 报表统计（APP-SPEC 5.6）：日报 / 周报 / 月报切换、报警次数按级别堆叠柱状、按棚分布、
/// 喷淋次数趋势、导出 CSV。
/// </summary>
public sealed partial class ReportsPage : Page
{
    private ReportService.Range _range = ReportService.Range.Week;
    private string _exportNote = "尚未导出。点击上方按钮可把当前统计范围的数据导出为 CSV（UTF-8，Excel 直接打开不乱码）。";
    private bool _built;

    public ReportsPage()
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
        var data = monitor.Data;
        var now = monitor.Clock.Now;

        var alarms = ReportService.Filter(data.Alarms, _range, now);
        var sprinklers = ReportService.Filter(data.Sprinklers, _range, now);
        var byLevel = ReportService.ByLevel(alarms);
        var byBarn = ReportService.ByBarn(alarms);
        var trend = ReportService.SprinklerTrend(data.Sprinklers, _range, now);
        var readings = ReportService.SummarizeReadings(data.Readings, _range, now);

        Host.Children.Clear();

        var head = UiKit.VStack(4);
        head.Children.Add(UiKit.PageTitle("报表统计"));
        head.Children.Add(UiKit.Caption(
            $"{ReportService.RangeName(_range)}统计范围：{ReportService.RangeStart(_range, now):yyyy-MM-dd} 至 {now:yyyy-MM-dd}；全部统计基于本地数据，不依赖网络"));
        Host.Children.Add(head);

        // ── 范围切换 ──
        var bar = UiKit.HStack(10);
        bar.VerticalAlignment = VerticalAlignment.Center;
        bar.Children.Add(UiKit.Caption("统计周期"));
        bar.Children.Add(RangeButton("日报", ReportService.Range.Day));
        bar.Children.Add(RangeButton("周报", ReportService.Range.Week));
        bar.Children.Add(RangeButton("月报", ReportService.Range.Month));
        bar.Children.Add(UiKit.PrimaryButton("导出报警记录 CSV", (_, _) => Export(ExportKind.Alarms)));
        bar.Children.Add(UiKit.PlainButton("导出喷淋记录 CSV", (_, _) => Export(ExportKind.Sprinklers)));
        bar.Children.Add(UiKit.PlainButton("导出采样数据 CSV", (_, _) => Export(ExportKind.Readings)));
        Host.Children.Add(UiKit.Card(bar));

        // ── 汇总指标 ──
        var stat = UiKit.EqualColumns(4, 12);
        var s0 = UiKit.Stat("报警总次数", $"{alarms.Count} 次",
            $"环境预警 {byLevel[AlarmLevel.Notice]} · 疑似 {byLevel[AlarmLevel.Suspect]} · 确认 {byLevel[AlarmLevel.Alarm]}",
            UiKit.Level(AlarmLevel.Alarm));
        var s1 = UiKit.Stat("喷淋次数", $"{sprinklers.Count} 次",
            $"自动联动 {sprinklers.Count(s => s.Mode == "自动")} 次", UiKit.Fire);
        var s2 = UiKit.Stat("平均温度", $"{readings.AverageTemp:0.0} °C",
            $"峰值 {readings.PeakTemp:0.0} °C");
        var s3 = UiKit.Stat("峰值烟雾", $"{readings.PeakSmoke:0} ppm",
            $"预警阈值 {monitor.Settings.SmokeNoticePpm:0} ppm", UiKit.Level(AlarmLevel.Suspect));
        Grid.SetColumn(s0, 0);
        Grid.SetColumn(s1, 1);
        Grid.SetColumn(s2, 2);
        Grid.SetColumn(s3, 3);
        stat.Children.Add(s0);
        stat.Children.Add(s1);
        stat.Children.Add(s2);
        stat.Children.Add(s3);
        Host.Children.Add(stat);

        // ── 报警次数按级别（堆叠柱状） ──
        var levelChart = UiKit.ColumnChart(new List<(string, double, Microsoft.UI.Xaml.Media.Brush)>
        {
            ("环境预警", byLevel[AlarmLevel.Notice], UiKit.Level(AlarmLevel.Notice)),
            ("疑似火情", byLevel[AlarmLevel.Suspect], UiKit.Level(AlarmLevel.Suspect)),
            ("确认火警", byLevel[AlarmLevel.Alarm], UiKit.Level(AlarmLevel.Alarm)),
        });
        var levelBody = UiKit.VStack(12);
        levelBody.Children.Add(levelChart);
        levelBody.Children.Add(UiKit.Label(
            $"本{ReportService.RangeName(_range)}共 {alarms.Count} 次报警；确认火警 {byLevel[AlarmLevel.Alarm]} 次全部执行开阀喷淋，前两级 {byLevel[AlarmLevel.Notice] + byLevel[AlarmLevel.Suspect]} 次均未喷淋。",
            13, FontWeights.Normal, UiKit.Muted));
        Host.Children.Add(UiKit.TitledCard("报警次数按级别", levelBody, "柱高为次数，颜色取契约 3.2 的三级告警色"));

        // ── 按棚分布 + 喷淋趋势 ──
        var twoCol = UiKit.EqualColumns(2, 16);

        var barnStack = UiKit.VStack(10);
        var maxBarn = byBarn.Count == 0 ? 1 : byBarn.Max(x => x.Count);
        foreach (var (name, count) in byBarn)
        {
            barnStack.Children.Add(UiKit.Bar(name, count, maxBarn, UiKit.Brand, $"{count} 次"));
        }
        if (byBarn.Count == 0) barnStack.Children.Add(UiKit.Empty("该统计周期内没有报警记录"));
        var barnCard = UiKit.TitledCard("按棚分布", barnStack, "识别报警高发草料棚，安排重点巡检");
        Grid.SetColumn(barnCard, 0);

        var trendStack = UiKit.VStack(10);
        var trendBars = trend
            .Select(t => (t.Day.ToString("MM-dd"), (double)t.Count, UiKit.Brand as Microsoft.UI.Xaml.Media.Brush))
            .ToList();
        trendStack.Children.Add(trendBars.Count == 0
            ? UiKit.Empty("该统计周期内没有喷淋记录")
            : UiKit.ColumnChart(trendBars, 130));
        trendStack.Children.Add(UiKit.Label(
            $"本{ReportService.RangeName(_range)}喷淋 {sprinklers.Count} 次，其中自动联动 {sprinklers.Count(s => s.Mode == "自动")} 次、人工试喷 {sprinklers.Count(s => s.Mode != "自动")} 次。",
            13, FontWeights.Normal, UiKit.Muted));
        var trendCard = UiKit.TitledCard("喷淋次数趋势", trendStack, "逐日统计，最后 30 天为月报上限");
        Grid.SetColumn(trendCard, 1);

        twoCol.Children.Add(barnCard);
        twoCol.Children.Add(trendCard);
        Host.Children.Add(twoCol);

        // ── 明细 ──
        var rows = alarms.Take(40).Select(a => new[]
        {
            a.TimeText,
            a.BarnName,
            a.LevelName,
            a.SmokeText,
            a.TempText,
            a.RiseText,
            a.OverLimitText,
            a.SprinklerText,
        }).ToList();
        Host.Children.Add(UiKit.TitledCard($"报警明细（最多显示 40 条，共 {alarms.Count} 条）",
            UiKit.Scroll(UiKit.Table(
                new[] { "时间", "棚舍", "级别", "烟雾", "温度", "温升", "连续超限", "是否喷淋" }, rows, firstWide: true),
                380),
            "完整数据可用上方按钮导出 CSV"));

        // ── 导出状态 ──
        Host.Children.Add(UiKit.Card(UiKit.Label(_exportNote, 13, FontWeights.Normal, UiKit.Muted)));
    }

    private Button RangeButton(string text, ReportService.Range range)
    {
        return UiKit.PlainButton(_range == range ? $"● {text}" : text, (_, _) =>
        {
            _range = range;
            Rebuild();
        });
    }

    private enum ExportKind { Alarms, Sprinklers, Readings }

    private void Export(ExportKind kind)
    {
        var monitor = RuntimeState.Monitor;
        var data = monitor.Data;
        var now = monitor.Clock.Now;
        var dir = Path.Combine(RuntimeState.Repo.Directory, "导出");
        System.IO.Directory.CreateDirectory(dir);
        var stamp = now.ToString("yyyyMMdd-HHmmss");

        try
        {
            string file;
            string content;
            switch (kind)
            {
                case ExportKind.Sprinklers:
                    file = Path.Combine(dir, $"喷淋记录-{ReportService.RangeName(_range)}-{stamp}.csv");
                    content = ReportService.ExportSprinklersCsv(ReportService.Filter(data.Sprinklers, _range, now));
                    break;
                case ExportKind.Readings:
                    file = Path.Combine(dir, $"采样数据-{ReportService.RangeName(_range)}-{stamp}.csv");
                    content = ReportService.ExportReadingsCsv(
                        data.Readings.Where(r => r.Time >= ReportService.RangeStart(_range, now)), data.Barns);
                    break;
                default:
                    file = Path.Combine(dir, $"报警记录-{ReportService.RangeName(_range)}-{stamp}.csv");
                    content = ReportService.ExportAlarmsCsv(ReportService.Filter(data.Alarms, _range, now));
                    break;
            }

            File.WriteAllText(file, content, new System.Text.UTF8Encoding(true));
            AppLog.Info($"导出 CSV：{file}");
            _exportNote = $"已导出 {ReportService.RangeName(_range)}：{file}（UTF-8 带 BOM，Excel 可直接打开）";
        }
        catch (Exception ex)
        {
            AppLog.Error("导出 CSV 失败", ex);
            _exportNote = $"导出失败：{ex.Message}";
        }

        Rebuild();
    }
}
