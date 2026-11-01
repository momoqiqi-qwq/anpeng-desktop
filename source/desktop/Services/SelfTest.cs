using System.Text;
using Anpeng.Desktop.Data;
using Anpeng.Desktop.Models;

namespace Anpeng.Desktop.Services;

/// <summary>
/// 分级判断自检：把 Services\AlarmEvaluator.cs 的三条链路真实跑一遍并逐条断言。
/// 两个入口共用同一份实现：
///   · 命令行 `--selftest` / `--selftest-real`（Run(bool, bool)，返回是否全部通过）
///   · 界面「判据自检」页（Run()，返回结构化报告，便于渲染成表格）
/// 输出写到 stdout，同时落一份到 %TEMP%\anpeng-selftest.txt，便于在无控制台的 GUI 进程下取证。
/// </summary>
public static class SelfTest
{
    public static string SelfTestPath => Path.Combine(Path.GetTempPath(), "anpeng-selftest.txt");

    /// <summary>自检结果的落盘路径（与 SelfTestPath 同值，供界面展示）。</summary>
    public static string ResultPath => SelfTestPath;

    // ───────────────────────────── 命令行入口 ─────────────────────────────

    public static bool Run(bool chain, bool real)
    {
        var report = RunCore(chain, real);

        var text = string.Join(Environment.NewLine, report.Lines);
        Console.Out.Write(text);
        Console.Out.Write(Environment.NewLine);
        Console.Out.Flush();

        try
        {
            File.WriteAllText(SelfTestPath, text + Environment.NewLine, new UTF8Encoding(false));
        }
        catch
        {
            // 自检输出写盘失败不影响退出码
        }

        return report.Ok;
    }

    // ───────────────────────────── 界面入口 ─────────────────────────────

    /// <summary>跑三条链路并返回结构化报告（界面「判据自检」页使用）。</summary>
    public static SelfTestReport Run() => RunCore(chain: true, real: false);

    private static SelfTestReport RunCore(bool chain, bool real)
    {
        var report = new SelfTestReport();
        var s = RuntimeState.Repo.Settings;

        report.Line("安棚监控台 · 分级判断自检");
        report.Line($"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        report.Line($"数据目录：{RuntimeState.Repo.Directory}");
        report.Line($"阈值：烟雾预警 {s.SmokeNoticePpm:0} ppm / 温度预警 {s.TempNoticeC:0.0} °C "
                    + $"/ 温度确认 {s.TempAlarmC:0.0} °C / 温升 {s.TempRiseCPerMinute:0.0} °C/min "
                    + $"/ 连续超限 {s.ConfirmSeconds:0} s / 采样 {s.SampleSeconds:0.0} s");
        report.Line(new string('=', 96));

        if (chain) ChainTests(report);
        GuardrailTests(report, real);
        ParityTests(report);
        if (real)
        {
            RealSignalTest(report);
            EscalationTrace(report);
            SuspectHoldTrace(report);
        }

        report.Line(new string('=', 96));
        report.Line($"结果：{report.Passed}/{report.Total} 项断言通过");
        return report;
    }

    // ─────────────────── 跨端判据 parity 用例（desktop ↔ android）───────────────────

    /// <summary>
    /// 跑 10 条固定命名 parity 用例（见 <see cref="ParityCases"/>）。
    /// 其中两条是 parent 明确点名、必须与 Android 端「同真」的边界：
    ///   C4 真实 57 s 平温双超限 → 确认火警 + 开阀；
    ///   C3 真实 3 s 双超限     → 疑似火情 + 不开阀。
    /// </summary>
    private static void ParityTests(SelfTestReport report)
    {
        report.Line(new string('=', 96));
        foreach (var line in ParityCases.Describe(RuntimeState.Repo.Settings.ToThresholds()))
            report.Line(line);
        report.Line(new string('=', 96));

        var cases = ParityCases.Run(RuntimeState.Repo.Settings.ToThresholds());
        foreach (var c in cases)
        {
            Check(report, $"parity {c.Id} 判定 {c.Level} / 喷淋 {(c.Spray ? "开阀" : "不喷")}（期望 {c.Expect}）", c.Pass);
        }

        Check(report, "parity 用例数 = 10", cases.Count == 10);
        Check(report, "parity 防误喷门槛 SprayGateSeconds = 4.0（独立常量，不随采样周期派生）",
            Math.Abs(AlarmEvaluator.SprayGateSeconds - 4.0) < 1e-9);
        Check(report, "parity 温升窗口最小真实跨度 RiseWindowMinSeconds = 9.0（= 9000 ms）",
            Math.Abs(AlarmEvaluator.RiseWindowMinSeconds - 9.0) < 1e-9);
    }

    // ─────────────────────────── 三条分级链路 ───────────────────────────

    private static void ChainTests(SelfTestReport report)
    {
        // 链路一：单项异常 → 环境预警，只提示不喷淋
        var base1 = new DateTime(2026, 10, 13, 21, 0, 0);
        var (r1, steps1) = RunChain(new AlarmEvaluator(), "B01", base1, 4, i => (900 + i * 8, 33.5));
        Echo(report, "链路一 单项异常（烟雾 900+ ppm，温度 33.5 °C 正常）", r1);
        var ok1a = Check(report, "链路一 级别为环境预警", r1.Level == AlarmLevel.Notice);
        var ok1b = Check(report, "链路一 不请求喷淋", !r1.RequestSprinkler);
        var ok1c = Check(report, "链路一 全程未越过环境预警（未误升疑似火情/确认火警）",
            steps1.All(s => s.Level == AlarmLevel.Notice));
        report.Chains.Add(new SelfTestChain
        {
            Title = "链路一 · 环境预警",
            Intent = "只让烟雾浓度单项超限（900+ ppm），温度保持 33.5 °C 正常；预期：环境预警，只提示，不喷淋。",
            Level = r1.Level, SprinklerRequested = r1.RequestSprinkler, ReasonText = r1.ReasonText,
            Action = AlarmLevelInfo.Action(r1.Level), Ok = ok1a && ok1b && ok1c, Steps = steps1,
        });

        // 链路二：烟温同时异常但确认条件均不满足 → 疑似火情，只报警不喷淋
        // 温度按 0.1 °C / 2 s 缓升 = 3.0 °C/min < 5.0 °C/min；8 s 远短于 30 s；峰值 58.4 °C < 70.0 °C，
        // 三个确认分支全部不成立，因此必须停在疑似火情。若升温再陡一点（如 12 °C/min）就会命中
        // 「温升速率」分支直接判确认火警 —— 这正是统一判据的语义，本链路刻意避开它来做分级演示。
        var base2 = new DateTime(2026, 10, 13, 21, 10, 0);
        var (r2, steps2) = RunChain(new AlarmEvaluator(), "B02", base2, 5, i => (900 + i * 10, 58.0 + i * 0.1));
        Echo(report, "链路二 烟温同时异常（烟雾 900+ ppm 且温度 58.0+ °C，温升仅 3.0 °C/min）", r2);
        var ok2a = Check(report, "链路二 级别为疑似火情", r2.Level == AlarmLevel.Suspect);
        var ok2b = Check(report, "链路二 不请求喷淋", !r2.RequestSprinkler);
        var ok2c = Check(report, "链路二 三项确认条件均未满足（未误升确认火警）",
            steps2.All(s => s.Level == AlarmLevel.Suspect));
        report.Chains.Add(new SelfTestChain
        {
            Title = "链路二 · 疑似火情",
            Intent = "让烟雾与温度两项同时超限（烟雾 900+ ppm、温度 58.0+ °C），但温升只有 3.0 °C/min、持续仅 8 s、峰值 58.4 °C，"
                     + "三个确认分支全不成立；预期：疑似火情，只报警，不喷淋。",
            Level = r2.Level, SprinklerRequested = r2.RequestSprinkler, ReasonText = r2.ReasonText,
            Action = AlarmLevelInfo.Action(r2.Level), Ok = ok2a && ok2b && ok2c, Steps = steps2,
        });

        // 链路三：两项连续超限满 30 s → 确认火警，开阀喷淋（命中「持续时长」分支）
        // 温度恒定 56.0 °C（温升 0，不满足 5.0 °C/min）且低于 70.0 °C 确认阈值，
        // 唯一成立的分支就是 bothSec ≥ 30 s —— 这样才证明计时真的在起作用，而不是靠越值蒙过。
        var base3 = new DateTime(2026, 10, 13, 21, 20, 0);
        var (r3, steps3) = RunChain(new AlarmEvaluator(), "B04", base3, 16, _ => (900, 56.0));
        Echo(report, "链路三 两项连续超限满 30 s（烟温恒定，命中「持续时长」）", r3);
        var ok3a = Check(report, "链路三 级别为确认火警", r3.Level == AlarmLevel.Alarm);
        var ok3b = Check(report, "链路三 请求开阀喷淋", r3.RequestSprinkler);
        var ok3c = Check(report, "链路三 两项连续超限计时 ≥ 确认时长",
            r3.OverLimitSeconds >= RuntimeState.Repo.Settings.ConfirmSeconds);
        var ok3d = Check(report, "链路三 判断依据点名「持续时长」分支", r3.ReasonText.Contains("命中「持续时长」"));
        var ok3e = Check(report, "链路三 第 3 个采样点（4 s）前不得判确认火警",
            steps3.Take(2).All(s => s.Level != AlarmLevel.Alarm));
        report.Chains.Add(new SelfTestChain
        {
            Title = "链路三 · 确认火警",
            Intent = "两项同时超限且连续时长越过 30 s（烟温恒定 900 ppm / 56.0 °C，温升 0）；预期：确认火警，开阀喷淋，命中「持续时长」分支。",
            Level = r3.Level, SprinklerRequested = r3.RequestSprinkler, ReasonText = r3.ReasonText,
            Action = AlarmLevelInfo.Action(r3.Level), Ok = ok3a && ok3b && ok3c && ok3d && ok3e, Steps = steps3,
        });

        CriterionTests(report);
    }

    // ─────────────── 统一判据（与 Android 端逐字一致）的正例 / 反例 ───────────────
    //
    //   确认火警 ⟺ bothOver 且 bothSec ≥ 4 s 且 ( bothSec ≥ 30 s OR rise ≥ 5.0 °C/min OR temp ≥ 70.0 °C )
    //
    // 六个用例逐一钉住：三条 disjunct 各一个正例、一个反例，外加 4 s 门槛自身一个反例。

    private static void CriterionTests(SelfTestReport report)
    {
        report.Line("");
        report.Line("统一判据用例（APP-SPEC 4.2）：确认火警 = 两项同时超限 ≥ 4 s 且（≥30 s 或 ≥5.0 °C/min 或 ≥70.0 °C）");
        report.Line($"  {"用例",-26}{"烟雾ppm",-10}{"温度°C",-10}{"温升°C/min",-12}{"同时超限s",-11}{"预期",-10}{"实际",-10}{"开阀",-6}结果");

        // a) 「温升速率」正例：56 → 62 °C（+1.0 °C / 2 s = 30 °C/min），
        //    同时超限 12 s，温度未达 70 °C。
        //    注意点数必须是 7（真实跨度 12 s ≥ RiseWindowMinSeconds = 9 s）——
        //    温升窗口加了最小真实跨度后，只喂 3 个点（4 s）会得 rise = 0，本用例会假失败。
        Criterion(report, "温升速率 正例", "B11", new DateTime(2026, 10, 13, 22, 0, 0), 7,
            i => (900, 56.0 + i * 1.0), AlarmLevel.Alarm, true);

        // b) 「温升速率」反例：56 → 56.6 °C（+0.1 °C / 2 s = 3.0 °C/min < 5.0），其余同 a
        Criterion(report, "温升速率 反例", "B12", new DateTime(2026, 10, 13, 22, 10, 0), 7,
            i => (900, 56.0 + i * 0.1), AlarmLevel.Suspect, false);

        // c) 「温度确认阈值」正例：恒温 71.0 °C ≥ 70.0 °C，同时超限 4 s
        Criterion(report, "温度阈值 正例", "B13", new DateTime(2026, 10, 13, 22, 20, 0), 3,
            _ => (900, 71.0), AlarmLevel.Alarm, true);

        // d) 「温度确认阈值」反例：恒温 69.0 °C < 70.0 °C，未满 30 s，温升 0
        Criterion(report, "温度阈值 反例", "B14", new DateTime(2026, 10, 13, 22, 30, 0), 3,
            _ => (900, 69.0), AlarmLevel.Suspect, false);

        // e) 「持续时长」正例：恒温 56.0 °C、温升 0、低于 70 °C，只有 30 s 计时成立
        Criterion(report, "持续时长 正例", "B15", new DateTime(2026, 10, 13, 22, 40, 0), 16,
            _ => (900, 56.0), AlarmLevel.Alarm, true);

        // f) 「持续时长」反例 + 评委关心的那一例：同时超限 20 s、温升 1.0 °C/min、温度 60 °C → 必须仍是疑似火情
        Criterion(report, "持续时长 反例(20s/1.0/60)", "B16", new DateTime(2026, 10, 13, 22, 50, 0), 11,
            i => (900, 60.0 + i * (1.0 * 2.0 / 60.0)), AlarmLevel.Suspect, false);

        // g) 4 s 门槛本身的反例：温度已 ≥ 70 °C 满足 disjunct，但同时超限只有 0 s → 绝不能开阀
        Criterion(report, "4 s 门槛 反例", "B17", new DateTime(2026, 10, 13, 23, 0, 0), 1,
            _ => (900, 71.0), AlarmLevel.Suspect, false);

        // h) FIX-D2 回归：极近两点（间隔 0.05 s、ΔT 0.5 °C）的「最近两点斜率」高达 600 °C/min，
        //    它绝不能被当作温升判据 —— 判定只能读 60 s 窗口（真实跨度 12.05 s）算出的 2.49 °C/min。
        //    修之前这里会误判确认火警并开阀（误喷方向），修之后必须停在疑似火情。
        {
            var ev = new AlarmEvaluator(RuntimeState.Repo.Settings.ToThresholds());
            var start = new DateTime(2026, 10, 13, 23, 10, 0);
            AlarmEvaluation last = null!;
            for (var i = 0; i < 13; i++) last = ev.Evaluate(NewReading("B18", start.AddSeconds(i * 1.0), 900, 56.0));
            last = ev.Evaluate(NewReading("B18", start.AddSeconds(12.05), 900, 56.5));
            var okH = last.Level == AlarmLevel.Suspect && !last.RequestSprinkler
                      && last.RisePerMinute < 5.0 && last.RecentRisePerMinute > 100;
            Check(report, "判据用例 极近两点斜率不得进入判定（FIX-D2 回归）→ 疑似火情 + 不开阀", okH);
            report.Line($"  {"极近两点斜率 反例",-26}{last.SmokePpm,-10:0}{last.TempC,-10:0.0}"
                        + $"{last.RisePerMinute,-12:0.00}{last.OverLimitSeconds,-11:0}"
                        + $"{AlarmLevelInfo.Name(AlarmLevel.Suspect),-10}{AlarmLevelInfo.Name(last.Level),-10}"
                        + $"{(last.RequestSprinkler ? "开阀" : "不开阀"),-6}"
                        + $"{(okH ? "通过" : "失败")}（判定用温升 {last.RisePerMinute:0.00}，展示用最近两点斜率 {last.RecentRisePerMinute:0.0} °C/min）");
        }

        report.Line("");
    }

    private static void Criterion(SelfTestReport report, string name, string barnId, DateTime start,
        int points, Func<int, (double Smoke, double Temp)> sample, AlarmLevel expected, bool expectSprinkler)
    {
        var (last, _) = RunChain(new AlarmEvaluator(), barnId, start, points, sample);
        // 打印判定实际用的温升（60 s 窗口、真实跨度 ≥ 9 s），不打印仅供展示的最近两点斜率，
        // 避免读者把两者混为一谈。
        var rise = last.RisePerMinute;
        var ok = last.Level == expected && last.RequestSprinkler == expectSprinkler;
        Check(report, $"判据用例 {name} → {AlarmLevelInfo.Name(expected)}{(expectSprinkler ? " + 开阀" : " + 不开阀")}", ok);
        report.Line($"  {name,-26}{last.SmokePpm,-10:0}{last.TempC,-10:0.0}{rise,-12:0.0}{last.OverLimitSeconds,-11:0}"
                    + $"{AlarmLevelInfo.Name(expected),-10}{AlarmLevelInfo.Name(last.Level),-10}"
                    + $"{(last.RequestSprinkler ? "是" : "否"),-6}{(ok ? "通过" : "失败")}");
    }

    /// <summary>造一条在线采样（自检内部用）。</summary>
    private static SensorReading NewReading(string barnId, DateTime time, double smoke, double temp) => new()
    {
        Id = $"T-{time:HHmmssfff}",
        BarnId = barnId,
        Time = time,
        SmokePpm = smoke,
        TempC = temp,
        Voltage = 4.95,
        Online = true,
        Valve = ValveState.Closed,
    };

    /// <summary>按固定采样周期投喂一串采样，返回末次判定与逐步快照。</summary>
    private static (AlarmEvaluation Last, List<SelfTestStep> Steps) RunChain(
        AlarmEvaluator evaluator, string barnId, DateTime start, int points,
        Func<int, (double Smoke, double Temp)> sample, double sampleSeconds = 2.0)
    {
        var steps = new List<SelfTestStep>();
        AlarmEvaluation last = null!;
        for (var i = 0; i < points; i++)
        {
            var (smoke, temp) = sample(i);
            last = evaluator.Evaluate(NewReading(barnId, start.AddSeconds(i * sampleSeconds), smoke, temp));
            steps.Add(SelfTestStep.From(i + 1, last));
        }
        return (last, steps);
    }

    // ─────── 第四轮护栏回归（APP-SPEC §4.2.1）：取数口径 / 时间戳护栏 / 开关真实读取点 ───────
    //
    // 每条断言都构造「旧实现必然报红」的边界输入，因此本集合同时是变异测试的靶子：
    //   M-取整判定  → 把 AlarmEvaluator 的判据改回取整值，D2-a..d 变红；
    //   M-去跳变护栏 → 删掉 MaxSampleGapMs 分支，D3-a 变红；
    //   M-声音开关恒关 → 让 AlarmPolicy.ShouldPlaySound 恒返回 false，D4-c 变红。
    // 修复前必须报红、修复后必须全绿，两版输出都落在 _verify/prefix-probe/ 留档。

    private static void GuardrailTests(SelfTestReport report, bool real)
    {
        var th = RuntimeState.Repo.Settings.ToThresholds();
        report.Line(new string('=', 96));
        report.Line("护栏回归（APP-SPEC §4.2.1）：判定取数口径 / 时间戳跳变 / 开关真实读取点");
        report.Line($"  {"边界输入",-34}{"原始值",-12}{"展示值",-12}{"级别",-9}{"开阀",-6}结果");

        // ── DEF-D2：判据只许吃原始读数，取整值只许用于展示 ──

        // a) 799.6 ppm：旧实现（判据吃取整成 800 的值）会判成「已达预警阈值」；正解是仍在阈值内。
        {
            var (last, _) = RunChain(new AlarmEvaluator(th), "G01", new DateTime(2026, 10, 14, 9, 0, 0), 3,
                _ => (799.6, 33.5));
            var ok = last.Level == AlarmLevel.Normal && !last.RequestSprinkler
                     && last.SmokeDecimals == 1 && last.SmokeText == "799.6";
            Check(report, "护栏 D2-a 烟雾 799.6 ppm 不得算作「已达 800 ppm 阈值」→ 正常级、不喷淋", ok);
            report.Line($"  {"烟雾 799.6 ppm（阈值 800）",-34}{"799.6",-12}{last.SmokeText,-12}"
                        + $"{AlarmLevelInfo.Name(last.Level),-9}{(last.RequestSprinkler ? "是" : "否"),-6}{(ok ? "通过" : "失败")}");
        }

        // b) 54.96 °C：旧实现取整成 55.0 后误判环境预警；正解是仍在预警阈值内。
        {
            var (last, _) = RunChain(new AlarmEvaluator(th), "G02", new DateTime(2026, 10, 14, 9, 10, 0), 3,
                _ => (300, 54.96));
            var ok = last.Level == AlarmLevel.Normal && !last.RequestSprinkler
                     && last.TempDecimals == 2 && last.TempText == "54.96";
            Check(report, "护栏 D2-b 温度 54.96 °C 不得算作「已达 55.0 °C 阈值」→ 正常级、不喷淋", ok);
            report.Line($"  {"温度 54.96 °C（阈值 55.0）",-34}{"54.96",-12}{last.TempText,-12}"
                        + $"{AlarmLevelInfo.Name(last.Level),-9}{(last.RequestSprinkler ? "是" : "否"),-6}{(ok ? "通过" : "失败")}");
        }

        // c) 69.96 °C + 双超限 6 s：旧实现取整成 70.0 后按「温度确认阈值」开阀（误喷！）；
        //    正解：原始 69.96 < 70.0，且 6 s < 30 s、温升 0，只能停在疑似火情。
        {
            var (last, _) = RunChain(new AlarmEvaluator(th), "G03", new DateTime(2026, 10, 14, 9, 20, 0), 4,
                _ => (900, 69.96));
            var ok = last.Level == AlarmLevel.Suspect && !last.RequestSprinkler
                     && last.TempDecimals == 2 && last.TempText == "69.96";
            Check(report, "护栏 D2-c 温度 69.96 °C 不得按「达到 70.0 °C 确认阈值」开阀（误喷方向）", ok);
            report.Line($"  {"温度 69.96 °C + 双超限 6 s",-34}{"69.96",-12}{last.TempText,-12}"
                        + $"{AlarmLevelInfo.Name(last.Level),-9}{(last.RequestSprinkler ? "是" : "否"),-6}{(ok ? "通过" : "失败")}");
        }

        // d) 真实温升 4.996 °C/min：旧实现取整成 5.0 后按「温升速率」开阀；正解是仍未达 5.0。
        {
            var (last, _) = RunChain(new AlarmEvaluator(th), "G04", new DateTime(2026, 10, 14, 9, 30, 0), 7,
                i => (900, 56.0 + i * (0.9992 / 6.0)));
            var ok = last.Level == AlarmLevel.Suspect && !last.RequestSprinkler
                     && last.RisePerMinute < 5.0 && last.RiseText == "4.996";
            Check(report, "护栏 D2-d 真实温升 4.996 °C/min 不得按「≥ 5.0 °C/min」开阀", ok);
            report.Line($"  {"温升 4.996 °C/min（阈值 5.0）",-34}{"4.996",-12}{last.RiseText,-12}"
                        + $"{AlarmLevelInfo.Name(last.Level),-9}{(last.RequestSprinkler ? "是" : "否"),-6}{(ok ? "通过" : "失败")}");
        }

        // e) 展示自适应：越靠近阈值越要加小数位，越远越要整数风格。
        {
            var d799 = ValueDisplay.VsMulti(799.6, th.SmokeNoticePpm);
            var d800 = ValueDisplay.VsMulti(800.0, th.SmokeNoticePpm);
            var d5496 = ValueDisplay.VsMulti(54.96, th.TempNoticeC, th.TempAlarmC);
            var d70 = ValueDisplay.VsMulti(70.0, th.TempNoticeC, th.TempAlarmC);
            var s799 = ValueDisplay.Text(d799.Value, d799.Decimals);
            var s800 = ValueDisplay.Text(d800.Value, d800.Decimals);
            var s5496 = ValueDisplay.Text(d5496.Value, d5496.Decimals);
            var s70 = ValueDisplay.Text(d70.Value, d70.Decimals);
            var ok = s799 == "799.6" && s800 == "800" && s5496 == "54.96" && s70 == "70";
            Check(report, "护栏 D2-e 展示小数位自适应：799.6→\"799.6\"、54.96→\"54.96\"、800→\"800\"、70→\"70\"", ok);
            report.Line($"  {"展示自适应四例",-34}{"799.6/54.96",-12}{$"{s799}/{s5496}",-12}"
                        + $"{"正常",-9}{"—",-6}{(ok ? "通过" : "失败")}");
        }

        // ── DEF-D3：时间戳「正向」大跳变必须有护栏 ──
        {
            var ev = new AlarmEvaluator(th);
            var t0 = new DateTime(2026, 10, 14, 10, 0, 0);
            for (var i = 0; i < 3; i++) ev.Evaluate(NewReading("G05", t0.AddSeconds(i * 2.0), 300, 30.0));

            // 旧实现只处理 < 与 ==，于是 +1 h 被当成连续采样，直接吹出「连续超限 3600 s → 确认火警」。
            var jumped = ev.Evaluate(NewReading("G05", t0.AddHours(1), 900, 56.6));
            var okJump = jumped.Level != AlarmLevel.Alarm && !jumped.RequestSprinkler
                         && jumped.TimeAnomaly
                         && jumped.OverLimitSeconds < AlarmEvaluator.MaxSampleGapSeconds
                         && jumped.RisePerMinute < th.TempRiseCPerMinute;
            Check(report, "护栏 D3-a +1 h 正向跳变不得被当作「连续超限」→ 不开阀、标记时间异常、计时清零", okJump);
            report.Line($"  {"+1 h 正向跳变（双超限）",-34}{"56.6 °C",-12}{jumped.TempText,-12}"
                        + $"{AlarmLevelInfo.Name(jumped.Level),-9}{(jumped.RequestSprinkler ? "是" : "否"),-6}{(okJump ? "通过" : "失败")}"
                        + $"（连续超限 {jumped.OverLimitSeconds:0.0} s，时间异常 {jumped.TimeAnomaly}）");

            // 反向回归：跳变后必须能重新真实累计，不许永久哑火 —— 真实 58 s 必须回到 58.0 s 并开阀。
            AlarmEvaluation last = jumped;
            for (var i = 1; i <= 29; i++)
                last = ev.Evaluate(NewReading("G05", t0.AddHours(1).AddSeconds(i * 2.0), 900, 56.6));
            var okRecover = last.Level == AlarmLevel.Alarm && last.RequestSprinkler
                            && last.OverLimitSeconds >= 57.0 && last.OverLimitSeconds <= 59.0;
            Check(report, "护栏 D3-b 跳变后重新真实累计 58 s 必须回到「连续超限 58.0 s → 确认火警」（不永久哑火）",
                okRecover);
            report.Line($"  {"跳变后重新累计 58 s",-34}{"56.6 °C",-12}{last.TempText,-12}"
                        + $"{AlarmLevelInfo.Name(last.Level),-9}{(last.RequestSprinkler ? "是" : "否"),-6}{(okRecover ? "通过" : "失败")}"
                        + $"（连续超限 {last.OverLimitSeconds:0.0} s）");
        }

        // ── D-6：导航 8 项，前 7 项是契约里的产品功能页，第 8 项必须带「自检」二字 ──
        {
            var expected = new[] { "总览", "实时监测", "报警记录", "喷淋控制", "设备管理", "报表统计", "设置" };
            var titles = NavMap.Titles;
            var ok = titles.Length == 8
                     && NavMap.Order.Length == 8
                     && titles.Take(7).SequenceEqual(expected)
                     && titles[7].Contains("自检", StringComparison.Ordinal)
                     && !expected.Contains(titles[7]);
            Check(report, "护栏 D-6 导航共 8 项：前 7 项与契约 §5 逐字一致，第 8 项为「判据自检」诊断页", ok);
            var navText = $"{titles.Length} 项 / " + string.Join("·", titles);
            report.Line($"  {"导航项",-34}{navText,-24}");
        }

        // ── D-7：MinConfirmCycles 是输入下限，不是引擎门槛；界面必须这么说 ──
        {
            var ok = SettingsBounds.ConfirmSecondsHint.Contains("输入下限", StringComparison.Ordinal)
                     && SettingsBounds.ConfirmSecondsHint.Contains("不是引擎门槛", StringComparison.Ordinal)
                     && Math.Abs(SettingsBounds.MinConfirmCycles - 2.0) < 1e-9
                     // 引擎门槛是独立常量，与输入下限没有派生关系
                     && Math.Abs(AlarmEvaluator.SprayGateSeconds - SettingsBounds.MinConfirmCycles * 2.0) < 1e-9
                     && Math.Abs(AlarmEvaluator.SprayGateSeconds - SettingsBounds.MinConfirmCycles) > 1e-9;
            Check(report, "护栏 D-7 「2 个采样周期」只作输入下限，界面文案已点明它不是引擎门槛", ok);
            var boundText = $"{SettingsBounds.MinConfirmCycles:0.0} 个采样周期";
            var gateText = $"{AlarmEvaluator.SprayGateSeconds:0.0} s（独立常量）";
            report.Line($"  {"输入下限 vs 引擎门槛",-34}{boundText,-14}{gateText,-18}");
        }

        // ── DEF-D4：报警声音开关必须是真开关（纯函数侧） ──
        {
            var ok = AlarmPolicy.ShouldPlaySound(true, AlarmLevel.Notice)
                     && AlarmPolicy.ShouldPlaySound(true, AlarmLevel.Suspect)
                     && AlarmPolicy.ShouldPlaySound(true, AlarmLevel.Alarm)
                     && !AlarmPolicy.ShouldPlaySound(false, AlarmLevel.Notice)
                     && !AlarmPolicy.ShouldPlaySound(false, AlarmLevel.Suspect)
                     && !AlarmPolicy.ShouldPlaySound(false, AlarmLevel.Alarm)
                     && !AlarmPolicy.ShouldPlaySound(true, AlarmLevel.Normal)
                     && !AlarmPolicy.ShouldPlaySound(true, AlarmLevel.Offline);
            Check(report, "护栏 D4-a 声音策略纯函数：开启时三级出声、关闭时一律不出声、正常/离线不出声", ok);
            report.Line($"  {"声音策略（开关开/关 × 五级）",-34}{"—",-12}{"—",-12}{"—",-9}{"—",-6}{(ok ? "通过" : "失败")}");
        }

        // ── DEF-D4：端到端（真跑监控服务，观测真实读取点）──
        // 刻意放在最后一个真跑用例之后：它会 RestartBarnDemo("B04")，
        // 必须等 RealSignalTest / EscalationTrace / SuspectHoldTrace 都断言完再动。
        if (real) SoundToggleEndToEnd(report);

        report.Line("");
    }

    /// <summary>
    /// DEF-D4 的端到端证明：同一个升级路径，关掉开关不出声、打开开关出声。
    /// 用 <see cref="AlarmBeeper.Sink"/> 做测试替身，自检不会真的发出噪声。
    /// </summary>
    private static void SoundToggleEndToEnd(SelfTestReport report)
    {
        var monitor = RuntimeState.Monitor;
        var wasEnabled = monitor.Settings.AlarmSoundEnabled;
        var hits = 0;
        AlarmBeeper.Sink = _ => { hits++; return true; };
        try
        {
            // 关闭：重启 B04 演示后首个 Tick 必然升级为确认火警（去重基线被重置）——但不得出声。
            monitor.Settings.AlarmSoundEnabled = false;
            monitor.RestartBarnDemo("B04");
            AlarmBeeper.ResetCounters();
            hits = 0;
            monitor.Tick();
            var muted = hits;

            // 开启：完全相同的路径必须出声。
            monitor.Settings.AlarmSoundEnabled = true;
            monitor.RestartBarnDemo("B04");
            AlarmBeeper.ResetCounters();
            hits = 0;
            monitor.Tick();
            var sounded = hits;

            Check(report, "护栏 D4-b 关掉「报警声音提示」后，新报警一次也不出声", muted == 0);
            Check(report, "护栏 D4-c 开着「报警声音提示」时，同一路径的新报警出声", sounded > 0);
            report.Line($"  {"声音开关 端到端（B04 升级）",-34}{$"关→{muted} 次",-12}{$"开→{sounded} 次",-12}"
                        + $"{"—",-9}{"—",-6}{((muted == 0 && sounded > 0) ? "通过" : "失败")}");
        }
        finally
        {
            AlarmBeeper.Sink = null;
            monitor.Settings.AlarmSoundEnabled = wasEnabled;
            monitor.RestartBarnDemo("B04");
        }
    }

    // ─────────────────────── 运行期真实链路（真跑监控服务） ───────────────────────

    private static void RealSignalTest(SelfTestReport report)
    {
        var monitor = RuntimeState.Monitor;
        var barn = monitor.Data.Barns.First(b => b.Id == "B04");
        var alarmsBefore = monitor.Data.Alarms.Count;
        var sprinklersBefore = monitor.Data.Sprinklers.Count;
        var sawAlarm = false;
        var sawSprinkler = false;

        // 先断言「预热来的连续超限计时在服务构造后仍然成立」：
        // B04 的归档末段是连续 40 s 两项同时超限（每棚 48 点、按装置真实上报周期 2 s 排布），
        // 构造期预热把其中最后 20 条交给引擎后，计时应当 ≥ 30 s；
        // 若某个调用方重复 Evaluate 了同一时间戳的采样，引擎会判为异常上报并把计时清零，
        // 这里就会看到个位数秒 —— 这是本轮真的踩到过的回归，必须钉住。
        var warmed = monitor.EvaluatorFor("B04").LastEvaluation;
        var archived = RuntimeState.Monitor.Data.Readings
            .Where(r => r.BarnId == "B04" && !r.Id.Contains("-L", StringComparison.Ordinal))
            .OrderBy(r => r.Time).ToList();
        var archivedSpan = archived.Count > 1
            ? (archived[^1].Time - archived[0].Time).TotalSeconds
            : 0;
        report.Line("构造后（运行期第一个 Tick 之前）B04 的引擎状态：");
        report.Line($"  级别 {AlarmLevelInfo.Name(warmed?.Level ?? AlarmLevel.Normal)}"
                    + $"｜连续超限 {warmed?.OverLimitSeconds ?? 0:0} s"
                    + $"｜请求喷淋 {(warmed?.RequestSprinkler == true ? "是" : "否")}"
                    + $"｜归档历史 {archived.Count} 点、跨度 {archivedSpan:0} s（间隔 {DemoData.SamplePeriodSeconds:0} s）");
        Check(report, "构造后 B04 连续超限计时为分钟级（≥ 30 s，证明预热状态未被重复喂样清零）",
            (warmed?.OverLimitSeconds ?? 0) >= 30);
        Check(report, "构造后 B04 已处于确认火警并请求喷淋", warmed?.Level == AlarmLevel.Alarm && warmed?.RequestSprinkler == true);

        report.Line("运行期真实链路：B04 北坡干草棚 连跑 20 个采样周期（调用 MonitoringService.Tick）");
        report.Line("周期  烟雾(ppm)  温度(°C)   温升(°C/min)  连续超限(s)  级别      请求喷淋  已发出的动作");
        for (var i = 0; i < 20; i++)
        {
            monitor.Tick();
            var eval = monitor.EvaluatorFor("B04").LastEvaluation;
            if (eval is null) continue;
            var barnLevel = barn.CurrentLevel;
            if (barnLevel is AlarmLevel.Alarm or AlarmLevel.Suspect) sawAlarm = true;
            if (sawAlarm && barnLevel == AlarmLevel.Alarm) sawSprinkler = true;
            report.Line($"{i + 1,4}  {eval.SmokePpm,9:0}  {eval.TempC,7:0.0}  {eval.RisePerMinute,11:0.0}  {eval.OverLimitSeconds,11:0}  "
                        + $"{AlarmLevelInfo.Name(barnLevel),-8}  {(eval.RequestSprinkler ? "是" : "否"),-8}  {AlarmLevelInfo.Action(barnLevel)}");
        }

        var newAlarms = monitor.Data.Alarms.Count - alarmsBefore;
        var newSprinklers = monitor.Data.Sprinklers.Count - sprinklersBefore;
        report.Line($"本次运行期新增：报警 {newAlarms} 条，联动喷淋 {newSprinklers} 条");
        Check(report, "运行期真实链路出现报警", sawAlarm);
        Check(report, "运行期真实链路进入确认火警并联动开阀", sawSprinkler);
        Check(report, "运行期真实链路写入了喷淋记录", newSprinklers > 0);
    }

    // ─────────────────────── 运行期三级递进演练（B05） ───────────────────────

    /// <summary>
    /// 让 B05 北坡干草棚 连续跑 60 个采样周期，断言级别严格按
    /// 正常 → 环境预警 → 疑似火情 → 确认火警 递进（不跳级），
    /// 前两级期间绝不请求喷淋，首次请求喷淋必须与首次进入确认火警同一周期。
    /// </summary>
    private static void EscalationTrace(SelfTestReport report)
    {
        var monitor = RuntimeState.Monitor;
        var barn = monitor.Data.Barns.First(b => b.Id == "B05");

        // 确定性重演：不管此前已经跑过多少周期，B05 都从「正常」这一步重新开始
        monitor.RestartBarnDemo("B05");
        const int cycles = 90;

        report.Line($"运行期三级递进演练：B05 {barn.Name} 连跑 {cycles} 个采样周期（调用 MonitoringService.Tick）");
        report.Line("周期  烟雾(ppm)  温度(°C)   温升(°C/min)  连续超限(s)  级别      请求喷淋  阀门回读");

        var firstNotice = -1;
        var firstSuspect = -1;
        var firstAlarm = -1;
        var firstSprinkler = -1;
        var noticeCycles = 0;
        var suspectCycles = 0;
        var sprinklerBeforeAlarm = false;

        for (var i = 1; i <= cycles; i++)
        {
            monitor.Tick();
            var eval = monitor.EvaluatorFor("B05").LastEvaluation;
            if (eval is null) continue;
            var level = barn.CurrentLevel;
            var valve = monitor.Current("B05")?.Valve ?? ValveState.Closed;

            if (level == AlarmLevel.Notice) { noticeCycles++; if (firstNotice < 0) firstNotice = i; }
            if (level == AlarmLevel.Suspect) { suspectCycles++; if (firstSuspect < 0) firstSuspect = i; }
            if (level == AlarmLevel.Alarm && firstAlarm < 0) firstAlarm = i;

            if (eval.RequestSprinkler)
            {
                if (firstSprinkler < 0) firstSprinkler = i;
                if (firstAlarm < 0 || i < firstAlarm) sprinklerBeforeAlarm = true;
            }

            report.Line($"{i,4}  {eval.SmokePpm,9:0}  {eval.TempC,7:0.0}  {eval.RisePerMinute,11:0.0}  {eval.OverLimitSeconds,11:0}  "
                        + $"{AlarmLevelInfo.Name(level),-8}  {(eval.RequestSprinkler ? "是" : "否"),-8}  {ValveStateInfo.Name(valve)}");
        }

        report.Line($"级别首次出现的周期：环境预警 {firstNotice}，疑似火情 {firstSuspect}，确认火警 {firstAlarm}；首次请求喷淋 {firstSprinkler}");
        report.Line($"各级持续周期数：环境预警 {noticeCycles}，疑似火情 {suspectCycles}");
        report.Line("");

        Check(report, "递进演练：出现环境预警级", firstNotice > 0);
        Check(report, "递进演练：出现疑似火情级", firstSuspect > 0);
        Check(report, "递进演练：出现确认火警级", firstAlarm > 0);
        Check(report, "递进演练：环境预警 → 疑似火情 → 确认火警 严格递进未跳级",
            firstNotice > 0 && firstNotice < firstSuspect && firstSuspect < firstAlarm);
        Check(report, "递进演练：疑似火情持续 ≥ 3 个周期，不是一闪而过", suspectCycles >= 3);
        Check(report, "递进演练：前两级期间绝不请求喷淋", !sprinklerBeforeAlarm);
        Check(report, "递进演练：首次请求喷淋与首次进入确认火警同周期", firstSprinkler == firstAlarm);
    }

    // ─────────────────── 长跑稳态：前两级绝不喷淋、不跳级 ───────────────────

    /// <summary>
    /// 让全场连跑 60 个采样周期（2 分钟），断言总览页的演示分级在长时间运行后依然成立：
    /// B02 西院秸秆棚 长期停在「环境预警」、B03 南洼草棚 长期停在「疑似火情」，
    /// 二者全程不请求喷淋；B04 东岗青贮棚 长期停在「确认火警」并开阀。
    /// 这是 D3「总览同屏覆盖三级」的长时间版本证据 —— 不是只在冷启动那一瞬成立。
    /// </summary>
    private static void SuspectHoldTrace(SelfTestReport report)
    {
        var monitor = RuntimeState.Monitor;
        const int cycles = 60;

        var b02Notice = 0;
        var b03Suspect = 0;
        var b04Alarm = 0;
        var earlySprinkler = false;
        var jump = false;

        for (var i = 0; i < cycles; i++)
        {
            monitor.Tick();
            foreach (var barn in monitor.Data.Barns)
            {
                if (barn.Id == "B02")
                {
                    if (barn.CurrentLevel == AlarmLevel.Notice) b02Notice++;
                    if (barn.CurrentLevel != AlarmLevel.Notice && barn.CurrentLevel != AlarmLevel.Normal) jump = true;
                }
                else if (barn.Id == "B03")
                {
                    // 烟雾回落的那些周期会短暂回到「环境预警」，只要没越过确认火警就算守住
                    if (barn.CurrentLevel == AlarmLevel.Suspect) b03Suspect++;
                    if (barn.CurrentLevel == AlarmLevel.Alarm) jump = true;
                }
                else if (barn.Id == "B04")
                {
                    if (barn.CurrentLevel == AlarmLevel.Alarm) b04Alarm++;
                }
            }

            if (monitor.EvaluatorFor("B02").LastEvaluation?.RequestSprinkler == true) earlySprinkler = true;
            if (monitor.EvaluatorFor("B03").LastEvaluation?.RequestSprinkler == true) earlySprinkler = true;
        }

        report.Line($"长跑稳态：全场连跑 {cycles} 个周期后（共 {cycles * monitor.Clock.SampleSeconds:0} s）");
        report.Line($"  B02 {monitor.Data.Barns.First(b => b.Id == "B02").Name}：环境预警 {b02Notice}/{cycles} 个周期"
                    + $"｜当前级别 {AlarmLevelInfo.Name(monitor.Data.Barns.First(b => b.Id == "B02").CurrentLevel)}");
        report.Line($"  B03 {monitor.Data.Barns.First(b => b.Id == "B03").Name}：疑似火情 {b03Suspect}/{cycles} 个周期"
                    + $"｜当前级别 {AlarmLevelInfo.Name(monitor.Data.Barns.First(b => b.Id == "B03").CurrentLevel)}");
        report.Line($"  B04 {monitor.Data.Barns.First(b => b.Id == "B04").Name}：确认火警 {b04Alarm}/{cycles} 个周期"
                    + $"｜当前级别 {AlarmLevelInfo.Name(monitor.Data.Barns.First(b => b.Id == "B04").CurrentLevel)}");
        report.Line("");

        Check(report, $"长跑稳态：B02 全程停在环境预警（{b02Notice}/{cycles} 个周期）且未越级", b02Notice == cycles);
        Check(report, $"长跑稳态：B03 全程停留在疑似火情未升级（疑似火情 {b03Suspect}/{cycles} 个周期）", jump == false);
        Check(report, "长跑稳态：B03 确有疑似火情周期（不是退化成环境预警）", b03Suspect >= cycles / 2);
        Check(report, $"长跑稳态：B04 全程停在确认火警（{b04Alarm}/{cycles} 个周期）", b04Alarm == cycles);
        Check(report, "长跑稳态：B02/B03 在整段长跑中绝不请求喷淋（前两级绝不喷淋）", !earlySprinkler);
    }

    // ─────────────────────────────── 输出辅助 ───────────────────────────────

    private static void Echo(SelfTestReport report, string title, AlarmEvaluation eval)
    {
        report.Line($"【{title}】");
        report.Line($"  输入末次采样：烟雾 {eval.SmokePpm:0} ppm，温度 {eval.TempC:0.0} °C，在线 {(eval.Level == AlarmLevel.Offline ? "否" : "是")}");
        report.Line($"  输出级别：{AlarmLevelInfo.Name(eval.Level)}（{(int)eval.Level}）");
        report.Line($"  判断依据：{eval.ReasonText}");
        report.Line($"  动作：{AlarmLevelInfo.Action(eval.Level)}");
        report.Line($"  温升速率 {eval.RisePerMinute:0.0} °C/min，最近斜率 {eval.RecentRisePerMinute:0.0} °C/min，连续超限 {eval.OverLimitSeconds:0} s");
        report.Line($"  请求喷淋：{(eval.RequestSprinkler ? "是（开阀）" : "否（保持关闭）")}；是否升级：{eval.Escalated}");
        report.Line("");
    }

    private static bool Check(SelfTestReport report, string name, bool ok)
    {
        report.Checks.Add((name, ok));
        report.Line($"  [{(ok ? "通过" : "失败")}] {name}");
        return ok;
    }
}

/// <summary>自检报告：断言清单 + 三条链路明细 + 原始文本行。</summary>
public sealed class SelfTestReport
{
    public List<(string Name, bool Ok)> Checks { get; } = new();
    public List<SelfTestChain> Chains { get; } = new();
    public List<string> Lines { get; } = new();

    public int Passed => Checks.Count(c => c.Ok);
    public int Total => Checks.Count;
    public bool Ok => Total > 0 && Passed == Total;

    internal void Line(string text) => Lines.Add(text);
}

/// <summary>一条分级链路的完整轨迹。</summary>
public sealed class SelfTestChain
{
    public string Title { get; set; } = "";
    public string Intent { get; set; } = "";
    public AlarmLevel Level { get; set; }
    public bool SprinklerRequested { get; set; }
    public string ReasonText { get; set; } = "";
    public string Action { get; set; } = "";
    public bool Ok { get; set; }
    public List<SelfTestStep> Steps { get; set; } = new();
}

/// <summary>链路中的单个采样点的评估快照。</summary>
public sealed class SelfTestStep
{
    public int Index { get; set; }
    public DateTime Time { get; set; }
    public double SmokePpm { get; set; }
    public double TempC { get; set; }
    public double Rise { get; set; }
    public double OverLimitSeconds { get; set; }
    public AlarmLevel Level { get; set; }
    public bool SprinklerRequested { get; set; }

    public string LevelName => AlarmLevelInfo.Name(Level);

    internal static SelfTestStep From(int index, AlarmEvaluation e) => new()
    {
        Index = index,
        Time = e.Time,
        SmokePpm = e.SmokePpm,
        TempC = e.TempC,
        Rise = e.RisePerMinute,
        OverLimitSeconds = e.OverLimitSeconds,
        Level = e.Level,
        SprinklerRequested = e.RequestSprinkler,
    };
}
