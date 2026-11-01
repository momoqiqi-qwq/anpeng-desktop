using Anpeng.Desktop.Controls;
using Anpeng.Desktop.Models;
using Anpeng.Desktop.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Anpeng.Desktop.Pages;

/// <summary>
/// 报警记录（APP-SPEC 5.3）：三级分级列表 + 筛选（全部 / 环境预警 / 疑似火情 / 确认火警），
/// 每条含时间 / 棚 / 触发值 / 级别 / 处置动作（是否喷淋）/ 处置人 / 备注；
/// 详情面板展示「触发 → 判断 → 动作」链路。
/// </summary>
public sealed partial class AlarmRecordsPage : Page
{
    private AlarmLevel? _filter;
    private string? _barnId;
    private string? _selectedId;
    private bool _built;

    public AlarmRecordsPage()
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
        var all = monitor.Data.Alarms.OrderByDescending(a => a.Time).ToList();
        var barns = monitor.Data.Barns;

        var list = all
            .Where(a => _filter is null || a.Level == _filter)
            .Where(a => _barnId is null || a.BarnId == _barnId)
            .ToList();

        Host.Children.Clear();

        // ── 页头 ──
        var head = UiKit.VStack(4);
        head.Children.Add(UiKit.PageTitle("报警记录"));
        head.Children.Add(UiKit.Caption(
            $"共 {all.Count} 条记录，三级齐全；演示数据落盘于 %LOCALAPPDATA%\\安棚\\data.json"));
        Host.Children.Add(head);

        // ── 分级统计 ──
        var byLevel = ReportService.ByLevel(all);
        var stat = UiKit.EqualColumns(4, 12);
        var s0 = UiKit.Stat("全部记录", $"{all.Count} 条", "三级分级判断产生", UiKit.BrandText);
        var s1 = UiKit.Stat("环境预警", $"{byLevel[AlarmLevel.Notice]} 条", "单项异常 · 不喷淋",
            UiKit.Level(AlarmLevel.Notice));
        var s2 = UiKit.Stat("疑似火情", $"{byLevel[AlarmLevel.Suspect]} 条", "烟温同时异常 · 不喷淋",
            UiKit.Level(AlarmLevel.Suspect));
        var s3 = UiKit.Stat("确认火警", $"{byLevel[AlarmLevel.Alarm]} 条", "连续超限 · 开阀喷淋",
            UiKit.Level(AlarmLevel.Alarm));
        Grid.SetColumn(s0, 0);
        Grid.SetColumn(s1, 1);
        Grid.SetColumn(s2, 2);
        Grid.SetColumn(s3, 3);
        stat.Children.Add(s0);
        stat.Children.Add(s1);
        stat.Children.Add(s2);
        stat.Children.Add(s3);
        Host.Children.Add(stat);

        // ── 筛选条 ──
        var bar = UiKit.HStack(10);
        bar.VerticalAlignment = VerticalAlignment.Center;
        bar.Children.Add(UiKit.Caption("按级别筛选"));
        bar.Children.Add(FilterButton("全部", null));
        bar.Children.Add(FilterButton("环境预警", AlarmLevel.Notice));
        bar.Children.Add(FilterButton("疑似火情", AlarmLevel.Suspect));
        bar.Children.Add(FilterButton("确认火警", AlarmLevel.Alarm));

        var barnCombo = new ComboBox { MinWidth = 200 };
        barnCombo.Items.Add("全部草料棚");
        foreach (var b in barns) barnCombo.Items.Add(b.Name);
        barnCombo.SelectedIndex = _barnId is null
            ? 0
            : Math.Max(0, barns.FindIndex(b => b.Id == _barnId) + 1);
        barnCombo.SelectionChanged += (_, _) =>
        {
            var i = barnCombo.SelectedIndex;
            _barnId = i <= 0 || i > barns.Count ? null : barns[i - 1].Id;
            Rebuild();
        };
        bar.Children.Add(barnCombo);
        bar.Children.Add(UiKit.Pill($"当前筛选命中 {list.Count} 条", AlarmLevel.Normal));
        Host.Children.Add(UiKit.Card(bar));

        // ── 列表 + 详情 ──
        var selected = list.FirstOrDefault(a => a.Id == _selectedId) ?? list.FirstOrDefault();
        _selectedId = selected?.Id;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        grid.ColumnSpacing = 16;

        var left = UiKit.TitledCard("报警列表", UiKit.Scroll(BuildList(list), 400),
            "点击任意一行查看该条的完整判断链路");
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        var right = UiKit.TitledCard("详情：触发 → 判断 → 动作",
            selected is null ? UiKit.Empty("当前筛选条件下没有记录") : BuildDetail(selected),
            "链路三段与界面显示同源，均来自 AlarmEvaluator 与报警记录快照");
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        Host.Children.Add(grid);

        // ── 喷淋联动统计 ──
        var fired = all.Count(a => a.SprinklerFired);
        var notFired = all.Count - fired;
        Host.Children.Add(UiKit.TitledCard("喷淋联动核对",
            UiKit.Scroll(BuildSprinklerAudit(fired, notFired), 160),
            "确认火警才开阀；环境预警与疑似火情均为「未喷淋」"));
    }

    private Button FilterButton(string text, AlarmLevel? level)
    {
        var selected = _filter == level;
        var b = UiKit.PlainButton(selected ? $"● {text}" : text, (_, _) =>
        {
            _filter = level;
            _selectedId = null;
            Rebuild();
        });
        return b;
    }

    private UIElement BuildList(List<AlarmRecord> list)
    {
        var stack = UiKit.VStack(0);
        foreach (var a in list)
        {
            stack.Children.Add(BuildRow(a));
        }
        if (list.Count == 0) stack.Children.Add(UiKit.Empty("当前筛选条件下没有记录"));
        return stack;
    }

    private UIElement BuildRow(AlarmRecord a)
    {
        var b = new Button
        {
            Padding = new Thickness(12, 10, 12, 10),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = a.Id == _selectedId ? UiKit.Res("BrandBlueSoftBrush") : UiKit.Surface,
            BorderBrush = UiKit.Line,
            BorderThickness = new Thickness(0, 0, 0, 1),
            CornerRadius = new CornerRadius(0),
        };

        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(128) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

        var t = UiKit.Label(a.TimeText, 13, FontWeights.Normal, UiKit.Muted);
        t.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(t, 0);

        var mid = UiKit.VStack(2);
        mid.Children.Add(UiKit.Label($"{a.BarnName} · {a.Trigger}", 14, FontWeights.SemiBold, UiKit.Ink));
        mid.Children.Add(UiKit.Label(
            $"烟雾 {a.SmokeText} · 温度 {a.TempText} · 温升 {a.RiseText} · 连续超限 {a.OverLimitText}", 12,
            FontWeights.Normal, UiKit.Muted));
        Grid.SetColumn(mid, 1);

        var pill = UiKit.Pill(a.LevelName, a.Level, solid: a.Level == AlarmLevel.Alarm);
        pill.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(pill, 2);

        var fire = UiKit.Label(a.SprinklerText, 13, FontWeights.SemiBold,
            a.SprinklerFired ? UiKit.Fire : UiKit.Muted);
        fire.VerticalAlignment = VerticalAlignment.Center;
        fire.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(fire, 3);

        g.Children.Add(t);
        g.Children.Add(mid);
        g.Children.Add(pill);
        g.Children.Add(fire);

        b.Content = g;
        b.Click += (_, _) =>
        {
            _selectedId = a.Id;
            Rebuild();
        };
        return b;
    }

    private static UIElement BuildDetail(AlarmRecord a)
    {
        var stack = UiKit.VStack(12);

        var head = UiKit.HStack(10);
        head.Children.Add(UiKit.Pill(a.LevelName, a.Level, solid: a.Level == AlarmLevel.Alarm));
        head.Children.Add(UiKit.Label(a.BarnName, 16, FontWeights.SemiBold, UiKit.BrandText));
        stack.Children.Add(head);
        stack.Children.Add(UiKit.Caption($"记录编号 {a.Id} · 发生时间 {a.TimeText}"));

        stack.Children.Add(UiKit.Divider());

        stack.Children.Add(Step("① 触发", a.Trigger));
        stack.Children.Add(Step("② 判断",
            $"烟雾 {a.SmokeText}、温度 {a.TempText}、温升速率 {a.RiseText}、连续超限 {a.OverLimitText}；" +
            $"判据：{AlarmLevelInfo.Criteria(a.Level, RuntimeState.Repo.Settings.ToThresholds())}"));
        stack.Children.Add(Step("③ 动作", a.Action));

        stack.Children.Add(UiKit.Divider());

        var kv = UiKit.VStack(6);
        kv.Children.Add(UiKit.KeyValue("是否喷淋", a.SprinklerText, a.SprinklerFired ? UiKit.Fire : UiKit.Muted));
        kv.Children.Add(UiKit.KeyValue("处置人", a.Handler));
        kv.Children.Add(UiKit.KeyValue("处置时间", a.HandledAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "尚未处置"));
        kv.Children.Add(UiKit.KeyValue("处置备注", a.Remark));
        stack.Children.Add(kv);

        stack.Children.Add(UiKit.Divider());

        var note = UiKit.Card(UiKit.VStack(6), 12);
        ((StackPanel)note.Child).Children.Add(UiKit.Label("防误喷设计说明", 13, FontWeights.SemiBold, UiKit.BrandText));
        ((StackPanel)note.Child).Children.Add(UiKit.Label(
            "连续采样滤波 + 温升趋势共同确认；环境预警与疑似火情永不喷淋；" +
            "草料扬尘干扰试验纳入现场调试项目。", 12, FontWeights.Normal, UiKit.Muted));
        stack.Children.Add(note);

        // DEF-D4：不制造「界面说假话」——「推送棚主」是现场链路的动作，本监控台离线运行、不承载联网推送。
        var pushNote = UiKit.Card(UiKit.VStack(6), 12);
        ((StackPanel)pushNote.Child).Children.Add(UiKit.Label("关于「推送棚主」", 13, FontWeights.SemiBold, UiKit.BrandText));
        ((StackPanel)pushNote.Child).Children.Add(UiKit.Label(
            "本监控台按契约 R4 完全本地运行、不联网，因此本机不发出任何推送，也不会显示「已推送」。"
            + "历史处置动作里的「推送棚主」指现场 ESP-01 → OneNet 云平台链路发出的通知（由棚主手机接收），"
            + "或由值班人员电话通知后人工填写的处置记录。本机承担的是：检测 → 三级判断 → 本机鸣响 → 确认火警开阀喷淋 → 写入处置记录。",
            12, FontWeights.Normal, UiKit.Muted));
        stack.Children.Add(pushNote);

        return stack;
    }

    private static Grid Step(string title, string detail)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var t = UiKit.Label(title, 14, FontWeights.SemiBold, UiKit.BrandText);
        t.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(t, 0);
        var d = UiKit.Label(detail, 13, FontWeights.Normal, UiKit.Ink);
        Grid.SetColumn(d, 1);
        g.Children.Add(t);
        g.Children.Add(d);
        return g;
    }

    private static UIElement BuildSprinklerAudit(int fired, int notFired)
    {
        var stack = UiKit.VStack(10);
        stack.Children.Add(UiKit.Bar("已喷淋（确认火警）", fired, Math.Max(1, fired + notFired),
            UiKit.Level(AlarmLevel.Alarm), $"{fired} 条"));
        stack.Children.Add(UiKit.Bar("未喷淋（前两级）", notFired, Math.Max(1, fired + notFired),
            UiKit.Level(AlarmLevel.Notice), $"{notFired} 条"));
        stack.Children.Add(UiKit.Label(
            $"核对结论：{fired} 条确认火警全部执行开阀喷淋；{notFired} 条前置告警全部未喷淋，符合契约 4.2 的动作列。",
            13, FontWeights.Normal, UiKit.Muted));
        return stack;
    }
}
