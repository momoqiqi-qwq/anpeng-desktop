using Anpeng.Desktop.Controls;
using Anpeng.Desktop.Models;
using Anpeng.Desktop.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Anpeng.Desktop.Pages;

/// <summary>
/// 判据自检：把 Services\AlarmEvaluator.cs 的三条链路在界面上现场跑一遍并逐条断言，
/// 与 `安棚 1.0.0.exe --selftest` 命令行自检同源（服务层 SelfTest.Run）。
/// </summary>
public sealed partial class DebugPage : Page
{
    private SelfTestReport? _report;

    public DebugPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (_report is null) _report = SelfTest.Run();
            Rebuild();
        };
    }

    private void Rebuild()
    {
        Host.Children.Clear();

        var head = UiKit.VStack(4);
        head.Children.Add(UiKit.PageTitle("判据自检"));
        head.Children.Add(UiKit.Caption(
            "产品内核是可单测的纯逻辑类 Services\\AlarmEvaluator.cs：输入「一次采样 + 当前状态」，输出级别与动作。" +
            "本页现场构造三条链路并逐条断言，用来证伪「假 if-else」。"));
        Host.Children.Add(head);

        var bar = UiKit.HStack(12);
        bar.VerticalAlignment = VerticalAlignment.Center;
        bar.Children.Add(UiKit.PrimaryButton("重新跑三条链路", (_, _) =>
        {
            _report = SelfTest.Run();
            Rebuild();
        }));
        if (_report is not null)
        {
            bar.Children.Add(UiKit.Pill(
                _report.Passed == _report.Total ? $"全部 {_report.Total} 项断言通过" : $"{_report.Passed}/{_report.Total} 项断言通过",
                _report.Passed == _report.Total ? AlarmLevel.Normal : AlarmLevel.Alarm,
                solid: _report.Passed != _report.Total));
        }
        bar.Children.Add(UiKit.Caption($"命令行等价入口：Anpeng.Desktop.exe --selftest（结果同时写入 {SelfTest.ResultPath}）"));
        Host.Children.Add(UiKit.Card(bar));

        if (_report is null)
        {
            Host.Children.Add(UiKit.Card(UiKit.Empty("正在执行自检…")));
            return;
        }

        // ── 断言清单 ──
        var asserts = UiKit.VStack(4);
        foreach (var (name, ok) in _report.Checks)
        {
            var row = UiKit.HStack(10);
            row.Children.Add(UiKit.Label(ok ? "通过" : "失败", 13, FontWeights.SemiBold,
                ok ? UiKit.Level(AlarmLevel.Normal) : UiKit.Level(AlarmLevel.Alarm)));
            row.Children.Add(UiKit.Label(name, 13, FontWeights.Normal, UiKit.Ink));
            asserts.Children.Add(row);
        }
        Host.Children.Add(UiKit.TitledCard($"断言清单（{_report.Passed} / {_report.Total}）", asserts,
            "任一断言失败都会在命令行以非零退出码结束，可用于自动化验收"));

        // ── 三条链路 ──
        foreach (var chain in _report.Chains)
        {
            Host.Children.Add(BuildChain(chain));
        }

        // ── 原始输出 ──
        var outputs = UiKit.VStack(6);
        foreach (var line in _report.Lines)
        {
            outputs.Children.Add(UiKit.Label(line, 12, FontWeights.Normal, UiKit.Ink));
        }
        Host.Children.Add(UiKit.TitledCard("自检原始输出", UiKit.Scroll(UiKit.Card(outputs, 12), 380),
            "与 `--selftest` 写到控制台 / 文件的内容一致"));
    }

    private static Border BuildChain(SelfTestChain chain)
    {
        var body = UiKit.VStack(12);

        var headRow = UiKit.HStack(10);
        headRow.Children.Add(UiKit.Label(chain.Title, 16, FontWeights.SemiBold, UiKit.BrandText));
        headRow.Children.Add(UiKit.Pill(AlarmLevelInfo.Name(chain.Level), chain.Level,
            solid: chain.Level == AlarmLevel.Alarm));
        headRow.Children.Add(UiKit.Pill(chain.SprinklerRequested ? "请求开阀喷淋" : "不喷淋",
            chain.SprinklerRequested ? AlarmLevel.Alarm : AlarmLevel.Normal, solid: chain.SprinklerRequested));
        body.Children.Add(headRow);
        body.Children.Add(UiKit.Caption(chain.Intent));

        var rows = chain.Steps.Select(s => new[]
        {
            s.Index.ToString(),
            s.Time.ToString("HH:mm:ss"),
            $"{s.SmokePpm:0}",
            $"{s.TempC:0.0}",
            $"{s.Rise:0.0}",
            $"{s.OverLimitSeconds:0}",
            s.LevelName,
            s.SprinklerRequested ? "是" : "否",
        }).ToList();
        body.Children.Add(UiKit.Scroll(UiKit.Table(
            new[] { "#", "时间", "烟雾(ppm)", "温度(°C)", "温升(°C/min)", "连续超限(s)", "级别", "请求喷淋" }, rows), 300));

        body.Children.Add(UiKit.Label("末次判断依据：" + chain.ReasonText, 13, FontWeights.Normal, UiKit.Ink));
        body.Children.Add(UiKit.Label("末次动作：" + chain.Action, 13, FontWeights.Normal, UiKit.Muted));
        body.Children.Add(UiKit.Label(
            chain.Ok ? "结论：链路表现与契约 4.2 一致。" : "结论：链路表现与契约 4.2 不一致，属于内核缺陷。",
            13, FontWeights.SemiBold, chain.Ok ? UiKit.Level(AlarmLevel.Normal) : UiKit.Level(AlarmLevel.Alarm)));

        return UiKit.TitledCard(chain.Title, body, chain.Intent);
    }
}
