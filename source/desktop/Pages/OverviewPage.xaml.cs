using Anpeng.Desktop.Controls;
using Anpeng.Desktop.Models;
using Anpeng.Desktop.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Anpeng.Desktop.Pages;

/// <summary>
/// 总览（APP-SPEC 5.1）：顶部汇总带（在线节点 / 今日报警 / 喷淋次数 / 平均温度）
/// + 棚舍卡片墙（每棚状态灯、当前烟雾与温度、阀门状态、最近报警时间）。
/// </summary>
public sealed partial class OverviewPage : Page
{
    public OverviewPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            RuntimeState.Monitor.Changed += Rebuild;
            Rebuild();
        };
        Unloaded += (_, _) => RuntimeState.Monitor.Changed -= Rebuild;
    }

    private void Rebuild()
    {
        var monitor = RuntimeState.Monitor;
        var settings = monitor.Settings;
        var data = monitor.Data;

        Host.Children.Clear();

        // ── 页头 ──
        var head = UiKit.VStack(4);
        head.Children.Add(UiKit.PageTitle("总览"));
        head.Children.Add(UiKit.Caption(
            $"{settings.SiteName} · 共 {data.Barns.Count} 座草料棚 · 数据目录 {RuntimeState.Repo.Directory}"));
        Host.Children.Add(head);

        // ── 顶部汇总带 ──
        var (online, todayAlarms, todaySprinklers, avgTemp) = monitor.Summary();
        var totalNodes = data.Nodes.Count;
        var band = UiKit.EqualColumns(4);
        var b0 = UiKit.Stat("在线节点", $"{online} / {totalNodes}", $"心跳正常，采样周期 {settings.SampleSeconds:0.0} s");
        var b1 = UiKit.Stat("今日报警", $"{todayAlarms} 次", "含环境预警 / 疑似火情 / 确认火警", UiKit.Level(AlarmLevel.Suspect));
        var b2 = UiKit.Stat("今日喷淋", $"{todaySprinklers} 次", "自动联动 + 人工试喷");
        var b3 = UiKit.Stat("平均温度", $"{avgTemp:0.0} °C", $"全场 {data.Barns.Count} 棚最近采样均值");
        Grid.SetColumn(b0, 0);
        Grid.SetColumn(b1, 1);
        Grid.SetColumn(b2, 2);
        Grid.SetColumn(b3, 3);
        band.Children.Add(b0);
        band.Children.Add(b1);
        band.Children.Add(b2);
        band.Children.Add(b3);
        Host.Children.Add(band);

        // ── 棚舍卡片墙 ──
        Host.Children.Add(UiKit.SectionTitle("棚舍卡片墙"));

        var wall = UiKit.EqualColumns(2);
        for (var i = 0; i < data.Barns.Count; i++)
        {
            var row = i / 2;
            var col = i % 2;
            while (wall.RowDefinitions.Count <= row)
            {
                wall.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }
            var card = BuildBarnCard(data.Barns[i]);
            Grid.SetRow(card, row);
            Grid.SetColumn(card, col);
            card.Margin = new Thickness(0, 0, 0, 12);
            wall.Children.Add(card);
        }
        Host.Children.Add(wall);

        // ── 判据速览 ──
        Host.Children.Add(BuildCriteriaCard());

        // ── 离线说明 ──
        Host.Children.Add(UiKit.Card(UiKit.Label(settings.CloudNote, 14, FontWeights.Normal, UiKit.Muted)));
    }

    private Border BuildBarnCard(Barn barn)
    {
        var monitor = RuntimeState.Monitor;
        var reading = monitor.Current(barn.Id);
        var level = barn.CurrentLevel;
        var lastAlarm = monitor.Data.Alarms.Where(a => a.BarnId == barn.Id)
            .OrderByDescending(a => a.Time).FirstOrDefault();

        var stack = UiKit.VStack(10);

        // 标题行：状态灯 + 棚名 + 级别药丸
        var titleRow = new Grid();
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var dot = UiKit.StatusDot(level, 14);
        dot.Margin = new Thickness(0, 0, 10, 0);
        Grid.SetColumn(dot, 0);
        var name = UiKit.Label(barn.Name, 20, FontWeights.SemiBold, UiKit.BrandText);
        Grid.SetColumn(name, 1);
        var pill = UiKit.Pill(AlarmLevelInfo.Name(level), level, solid: level >= AlarmLevel.Alarm);
        Grid.SetColumn(pill, 2);
        titleRow.Children.Add(dot);
        titleRow.Children.Add(name);
        titleRow.Children.Add(pill);
        stack.Children.Add(titleRow);

        stack.Children.Add(UiKit.Caption($"{barn.Location} · {barn.Structure} · 存量 {barn.StockTon:0.#} 吨 · 棚主 {barn.Owner}"));

        // 读数行
        // 展示值一律取引擎选定的自适应小数位：卡片不能让 799.6 ppm 看起来像「已经到 800」。
        var th = RuntimeState.Repo.Settings;
        var eval = RuntimeState.Monitor.EvaluatorFor(barn.Id).LastEvaluation;
        string smokeText, tempText;
        if (reading is null)
        {
            smokeText = "—";
            tempText = "—";
        }
        else if (eval is not null)
        {
            smokeText = ValueDisplay.Text(eval.SmokePpm, eval.SmokeDecimals) + " ppm";
            tempText = ValueDisplay.Text(eval.TempC, eval.TempDecimals) + " °C";
        }
        else
        {
            var sd = ValueDisplay.VsMulti(reading.SmokePpm, th.SmokeNoticePpm);
            var td = ValueDisplay.VsMulti(reading.TempC, th.TempNoticeC, th.TempAlarmC);
            smokeText = ValueDisplay.Text(sd.Value, sd.Decimals) + " ppm";
            tempText = ValueDisplay.Text(td.Value, td.Decimals) + " °C";
        }

        var readings = UiKit.EqualColumns(3, 10);
        var s0 = UiKit.Card(UiKit.VStack(2), 10);
        ((StackPanel)s0.Child).Children.Add(UiKit.Caption("烟雾浓度"));
        ((StackPanel)s0.Child).Children.Add(UiKit.Label(
            smokeText, 18, FontWeights.SemiBold, UiKit.BrandText));
        var s1 = UiKit.Card(UiKit.VStack(2), 10);
        ((StackPanel)s1.Child).Children.Add(UiKit.Caption("棚内温度"));
        ((StackPanel)s1.Child).Children.Add(UiKit.Label(
            tempText, 18, FontWeights.SemiBold, UiKit.BrandText));
        var s2 = UiKit.Card(UiKit.VStack(2), 10);
        ((StackPanel)s2.Child).Children.Add(UiKit.Caption("阀门状态"));
        var valveText = reading is null ? "未知" : ValveStateInfo.Name(reading.Valve);
        var valveBrush = reading?.Valve == ValveState.Open ? UiKit.Fire : UiKit.BrandText;
        ((StackPanel)s2.Child).Children.Add(UiKit.Label(valveText, 18, FontWeights.SemiBold, valveBrush));
        Grid.SetColumn(s0, 0);
        Grid.SetColumn(s1, 1);
        Grid.SetColumn(s2, 2);
        readings.Children.Add(s0);
        readings.Children.Add(s1);
        readings.Children.Add(s2);
        stack.Children.Add(readings);

        stack.Children.Add(UiKit.KeyValue("最近报警",
            lastAlarm is null ? "无报警记录" : $"{lastAlarm.TimeText}  {AlarmLevelInfo.Name(lastAlarm.Level)}",
            lastAlarm is null ? UiKit.Muted : UiKit.Level(lastAlarm.Level)));
        stack.Children.Add(UiKit.KeyValue("喷淋覆盖", barn.SprinklerCoverage));
        stack.Children.Add(UiKit.KeyValue("管网水压", $"{barn.WaterPressure:0.00} MPa"));
        stack.Children.Add(UiKit.KeyValue("最近巡检", barn.LastInspection.ToString("yyyy-MM-dd")));

        var go = UiKit.PlainButton("查看实时曲线", (_, _) =>
        {
            RuntimeState.Monitor.SetActiveBarn(barn.Id);
            if (Frame is not null)
            {
                Frame.Navigate(typeof(RealtimePage), barn.Id);
            }
        });
        var row2 = UiKit.HStack(10);
        row2.Children.Add(go);
        row2.Children.Add(UiKit.Pill(lastAlarm?.SprinklerFired == true ? "该棚曾联动喷淋" : "近期未联动喷淋",
            lastAlarm?.SprinklerFired == true ? AlarmLevel.Alarm : AlarmLevel.Normal));
        stack.Children.Add(row2);

        return UiKit.Card(stack);
    }

    private static Border BuildCriteriaCard()
    {
        var settings = RuntimeState.Repo.Settings;
        var rows = new List<(AlarmLevel Level, string Condition, string Action)>
        {
            (AlarmLevel.Notice, $"烟雾 ≥ {settings.SmokeNoticePpm:0} ppm 或 温度 ≥ {settings.TempNoticeC:0.0} °C（单项异常）",
                AlarmLevelInfo.Action(AlarmLevel.Notice)),
            (AlarmLevel.Suspect, $"烟雾与温度同时超限，但未满足确认火警条件",
                AlarmLevelInfo.Action(AlarmLevel.Suspect)),
            (AlarmLevel.Alarm,
                $"两项同时连续超限 ≥ {AlarmEvaluator.SprayGateSeconds:0} s，且满足三项之一：持续 ≥ {settings.ConfirmSeconds:0} s、温升 ≥ {settings.TempRiseCPerMinute:0.0} °C/min、温度 ≥ {settings.TempAlarmC:0.0} °C",
                AlarmLevelInfo.Action(AlarmLevel.Alarm)),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        var r = 0;
        foreach (var (level, condition, action) in rows)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var pill = UiKit.Pill(AlarmLevelInfo.Name(level), level, solid: level == AlarmLevel.Alarm);
            pill.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetRow(pill, r);
            Grid.SetColumn(pill, 0);
            var c = UiKit.Label(condition, 14, FontWeights.Normal, UiKit.Ink);
            c.Margin = new Thickness(0, 4, 12, 4);
            Grid.SetRow(c, r);
            Grid.SetColumn(c, 1);
            var a = UiKit.Label(action, 14, FontWeights.Normal, UiKit.Muted);
            a.Margin = new Thickness(0, 4, 0, 4);
            Grid.SetRow(a, r);
            Grid.SetColumn(a, 2);
            grid.Children.Add(pill);
            grid.Children.Add(c);
            grid.Children.Add(a);
            r++;
        }

        return UiKit.TitledCard("三级分级判断（前两级绝不喷淋）", grid,
            "判据由 AlarmEvaluator 纯逻辑类实现，可在「判据自检」页复现三条链路");
    }
}
