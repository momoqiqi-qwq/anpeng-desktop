using System.Text;
using System.Text.Json;
using Anpeng.Desktop.Models;

namespace Anpeng.Desktop.Services;

/// <summary>
/// 跨端判据 parity 用例集（desktop 侧）。
///
/// 契约要求桌面端（WinUI 3）与 Android 手持端对同一批采样得出同一级别、同一喷淋决定。
/// 本类用**固定命名**的 10 条用例（C1–C10）驱动 <see cref="AlarmEvaluator"/>，
/// 产出可机械比对的 JSON（<c>--parity &lt;path&gt;</c>），与 Android 侧同名文件逐字段 diff。
///
/// 用例全部按【真实时间戳】构造，不依赖采样节拍：C8 的样本间隔刻意不均匀，
/// C9 刻意让时间戳倒退，用于证明两端都在用真实时间差而非调用次数计时。
/// </summary>
public static class ParityCases
{
    /// <summary>一条用例的判定结果（字段名即 JSON 键名，便于两端 diff）。</summary>
    public sealed class CaseResult
    {
        public string Id { get; set; } = "";
        public string Scenario { get; set; } = "";
        public string Expect { get; set; } = "";

        /// <summary>判定级别 token（与 Android Level 枚举名逐字一致：NORMAL/NOTICE/SUSPECT/ALARM/OFFLINE）。</summary>
        public string Level { get; set; } = "";
        public string LevelName { get; set; } = "";
        public int LevelCode { get; set; }

        /// <summary>是否请求开阀喷淋。引擎唯一允许开阀的输出。</summary>
        public bool Spray { get; set; }

        /// <summary>确认火警命中的分支：RISE / HARD_TEMP / DURATION；非确认火警为空串。</summary>
        public string ConfirmBy { get; set; } = "";

        public double SmokePpm { get; set; }
        public double TempC { get; set; }
        public double RisePerMinute { get; set; }
        public double OverLimitSeconds { get; set; }
        public bool TimeAnomaly { get; set; }
        public int WindowSize { get; set; }

        /// <summary>本条用例是否满足期望（自检断言用）。</summary>
        public bool Pass { get; set; }
        public string Reason { get; set; } = "";
    }

    /// <summary>级别 token 名（与 Android <c>Level</c> 枚举名一致）。</summary>
    private static string Token(AlarmLevel level) => level switch
    {
        AlarmLevel.Offline => "OFFLINE",
        AlarmLevel.Notice => "NOTICE",
        AlarmLevel.Suspect => "SUSPECT",
        AlarmLevel.Alarm => "ALARM",
        _ => "NORMAL",
    };

    private static SensorReading Sample(DateTime t, double smoke, double temp, bool online = true) => new()
    {
        Id = $"P-{t:HHmmssfff}",
        BarnId = "PARITY",
        Time = t,
        SmokePpm = smoke,
        TempC = temp,
        Voltage = 4.95,
        Online = online,
        Valve = ValveState.Closed,
    };

    /// <summary>以 1 s 间隔喂一段平温历史（烟雾、温度恒定），返回末次评估结果。</summary>
    private static AlarmEvaluation FeedFlat(AlarmEvaluator ev, DateTime start, int samples, double smoke, double temp)
    {
        AlarmEvaluation last = null!;
        for (var i = 0; i < samples; i++) last = ev.Evaluate(Sample(start.AddSeconds(i), smoke, temp));
        return last;
    }

    /// <summary>跑全部 10 条 parity 用例。</summary>
    public static List<CaseResult> Run(AlarmThresholds? thresholds = null)
    {
        var th = thresholds ?? new AlarmThresholds();
        var results = new List<CaseResult>();
        var t0 = new DateTime(2026, 10, 13, 8, 0, 0, DateTimeKind.Unspecified);

        // ── C1 仅烟雾超限：烟雾 900 ppm、温度 30 ℃、rise 0 → 环境预警 / 不喷 ──
        {
            var ev = new AlarmEvaluator(th.Clone());
            var last = FeedFlat(ev, t0, 12, 900, 30);
            results.Add(Record("C1", "仅烟雾超限（900 ppm / 30 ℃ / 温升 0）", "NOTICE / 不喷", last, ev,
                last.Level == AlarmLevel.Notice && !last.RequestSprinkler));
        }

        // ── C2 仅温度超限：烟雾 200 ppm、温度 60 ℃、rise 0 → 环境预警 / 不喷 ──
        {
            var ev = new AlarmEvaluator(th.Clone());
            var last = FeedFlat(ev, t0, 12, 200, 60);
            results.Add(Record("C2", "仅温度超限（200 ppm / 60 ℃ / 温升 0）", "NOTICE / 不喷", last, ev,
                last.Level == AlarmLevel.Notice && !last.RequestSprinkler));
        }

        // ── C3 双超限真实 3 s、平温、rise 0 → 疑似火情 / 不喷（未达 4 s 防误喷门槛）──
        // D-8：此前桌面端只投 3 个双超限样本，真实时长算成 2.0 s，与 Android 记录的 3.0 s 差一个采样周期。
        // 现改为与 Android 同构造：双超限段投 4 个样本（t0+10..t0+13），真实时长恰为 3.0 s。
        {
            var ev = new AlarmEvaluator(th.Clone());
            FeedFlat(ev, t0, 10, 200, 60);                          // t0..t0+9s：温度超限、烟雾正常
            var last = FeedFlat(ev, t0.AddSeconds(10), 4, 900, 60);  // t0+10..t0+13：两项同时超限 = 3.0 s
            results.Add(Record("C3", "两项同时超限真实 3.0 s（平温、温升 0、温度 60 ℃）", "SUSPECT / 不喷", last, ev,
                last.Level == AlarmLevel.Suspect && !last.RequestSprinkler
                && Math.Abs(last.OverLimitSeconds - 3.0) < 0.001));
        }

        // ── C4 双超限真实 57 s、平温、温度 60 ℃、rise 0 → 确认火警(DURATION) / 开阀 ──
        {
            var ev = new AlarmEvaluator(th.Clone());
            var last = FeedFlat(ev, t0, 58, 900, 60);            // 每秒一点，末点与首点相隔 57 s
            results.Add(Record("C4", "两项同时超限真实 57 s（平温、温升 0、温度 60 ℃）", "ALARM(DURATION) / 开阀", last, ev,
                last.Level == AlarmLevel.Alarm && last.RequestSprinkler
                && last.ConfirmBy == "DURATION" && last.OverLimitSeconds >= 57));
        }

        // ── C5 双超限真实 8 s、温升 8.0 ℃/min → 确认火警(RISE) / 开阀 ──
        // 与 Android 同构造：14 个样本（t0..t0+13），烟雾自 i=5 起越限 ⇒ 双超限真实 8.0 s；
        // 温度整段按 8.0 ℃/min 的斜率上行，窗口真实跨度 13 s ⇒ rise 恰为 8.0，唯一可解释形态是 RISE。
        {
            var ev = new AlarmEvaluator(th.Clone());
            AlarmEvaluation last = null!;
            for (var i = 0; i < 14; i++)
            {
                var smoke = i >= 5 ? 900 : 200;                  // 末 8 s 才越烟雾阈值 → 双超限 8.0 s
                var temp = 55.0 + i * (8.0 / 60.0);              // 每秒升 8/60 ℃ = 8.0 ℃/min
                last = ev.Evaluate(Sample(t0.AddSeconds(i), smoke, temp));
            }
            results.Add(Record("C5", "两项同时超限真实 8.0 s + 温升 8.0 ℃/min（窗口真实跨度 13 s）", "ALARM(RISE) / 开阀", last, ev,
                last.Level == AlarmLevel.Alarm && last.RequestSprinkler
                && last.ConfirmBy == "RISE" && Math.Abs(last.RisePerMinute - 8.0) < 0.05
                && Math.Abs(last.OverLimitSeconds - 8.0) < 0.001));
        }

        // ── C6 双超限真实 8 s、温度 72 ℃、rise 0 → 确认火警(HARD_TEMP) / 开阀 ──
        // 与 Android 同构造：同样 14 个样本、同样 8.0 s 双超限，唯一可解释形态是 HARD_TEMP。
        {
            var ev = new AlarmEvaluator(th.Clone());
            AlarmEvaluation last = null!;
            for (var i = 0; i < 14; i++)
            {
                var smoke = i >= 5 ? 900 : 200;                  // 末 8 s 才越烟雾阈值 → 双超限 8.0 s
                last = ev.Evaluate(Sample(t0.AddSeconds(i), smoke, 72));
            }
            results.Add(Record("C6", "两项同时超限真实 8.0 s + 温度 72 ℃（平温、温升 0）", "ALARM(HARD_TEMP) / 开阀", last, ev,
                last.Level == AlarmLevel.Alarm && last.RequestSprinkler
                && last.ConfirmBy == "HARD_TEMP" && Math.Abs(last.OverLimitSeconds - 8.0) < 0.001));
        }

        // ── C7 双超限真实 20 s、平温、rise 0 → 疑似火情 / 不喷（未满 confirmSeconds）──
        {
            var ev = new AlarmEvaluator(th.Clone());
            var last = FeedFlat(ev, t0, 21, 900, 60);
            results.Add(Record("C7", "两项同时超限真实 20 s（平温、温升 0、温度 60 ℃）", "SUSPECT / 不喷", last, ev,
                last.Level == AlarmLevel.Suspect && !last.RequestSprinkler));
        }

        // ── C8 真实 57 s 但样本间隔不均匀（抖动/批量补发）→ 确认火警 / 开阀 ──
        {
            var ev = new AlarmEvaluator(th.Clone());
            // 间隔序列刻意不均匀：2,0.5,3,0.2,1.5,4,0.3,2.5,1,0.8 … 三段各 15.8 s，
            // 末段补 2.4 s 收口，合计恰好 57.0 s（真实时间跨度，不是「采样点数 × 采样周期」）。
            double[] gaps = { 2, 0.5, 3, 0.2, 1.5, 4, 0.3, 2.5, 1, 0.8, 2, 0.5, 3, 0.2, 1.5, 4, 0.3, 2.5, 1, 0.8,
                              2, 0.5, 3, 0.2, 1.5, 4, 0.3, 2.5, 1, 0.8, 2, 0.5, 3, 0.2, 1.5, 2.4 };
            var cursor = t0;
            var first = ev.Evaluate(Sample(cursor, 900, 60));    // 首点，双超限计时从这里起算
            AlarmEvaluation last = first;
            foreach (var g in gaps)
            {
                cursor = cursor.AddSeconds(g);
                last = ev.Evaluate(Sample(cursor, 900, 60));
            }
            // 反证：若按「采样点数 × 采样周期」算，会得到 (36+1)×2 = 74 s，
            // 与真实跨度 57 s 相差 17 s（>15 s），足以证明引擎走的是真实时间跨度。
            var byCount = (gaps.Length + 1) * th.SampleSeconds;
            var realSpan = (cursor - t0).TotalSeconds;
            results.Add(Record("C8", $"两项同时超限真实 {last.OverLimitSeconds:0.0} s、样本间隔不均匀（36 个抖动间隔；真实跨度 {realSpan:0} s，按点数算会得 {byCount:0} s）",
                "ALARM / 开阀", last, ev,
                last.Level == AlarmLevel.Alarm && last.RequestSprinkler
                && Math.Abs(last.OverLimitSeconds - realSpan) < 0.05
                && Math.Abs(realSpan - byCount) > 15.0));
        }

        // ── C9 时间戳倒退 → 不崩溃、无负数时长、计时重置 ──
        {
            var ev = new AlarmEvaluator(th.Clone());
            var last = FeedFlat(ev, t0, 11, 900, 60);            // 双超限 10 s
            var beforeAnomaly = last.OverLimitSeconds;
            last = ev.Evaluate(Sample(t0.AddSeconds(5), 900, 60)); // 时间戳倒退 5 s
            var negative = last.OverLimitSeconds < 0
                           || double.IsNaN(last.OverLimitSeconds)
                           || last.RisePerMinute < 0
                           || double.IsNaN(last.RisePerMinute);
            results.Add(Record("C9", "时间戳倒退 5 s（倒退前双超限 10 s）",
                "不崩溃 / 计时重置 / 无负数时长", last, ev,
                !negative && last.TimeAnomaly && last.OverLimitSeconds == 0
                && beforeAnomaly >= 10 && last.Level == AlarmLevel.Suspect && !last.RequestSprinkler));
        }

        // ── C10 全部正常 → 正常 / 不喷 ──
        {
            var ev = new AlarmEvaluator(th.Clone());
            var last = FeedFlat(ev, t0, 12, 200, 30);
            results.Add(Record("C10", "全部正常（200 ppm / 30 ℃ / 温升 0）", "NORMAL / 不喷", last, ev,
                last.Level == AlarmLevel.Normal && !last.RequestSprinkler));
        }

        return results;
    }

    private static CaseResult Record(string id, string scenario, string expect,
        AlarmEvaluation eval, AlarmEvaluator ev, bool pass)
    {
        var r = new CaseResult
        {
            Id = id,
            Scenario = scenario,
            Expect = expect,
            Level = Token(eval.Level),
            LevelName = AlarmLevelInfo.Name(eval.Level),
            LevelCode = (int)eval.Level,
            Spray = eval.RequestSprinkler,
            ConfirmBy = eval.ConfirmBy,
            SmokePpm = eval.SmokePpm,
            TempC = eval.TempC,
            RisePerMinute = eval.RisePerMinute,
            OverLimitSeconds = eval.OverLimitSeconds,
            TimeAnomaly = eval.TimeAnomaly,
            WindowSize = ev.WindowSize,
            Pass = pass,
            Reason = eval.ReasonText,
        };
        return r;
    }

    /// <summary>把 10 条用例结果序列化成可机械比对的 JSON。</summary>
    public static string ToJson(AlarmThresholds? thresholds = null)
    {
        var th = thresholds ?? new AlarmThresholds();
        var cases = Run(th);
        var payload = new
        {
            platform = "windows-desktop",
            engine = "Anpeng.Desktop.Services.AlarmEvaluator",
            version = BuildInfo.Version,
            builtAt = BuildInfo.BuiltAt,
            generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            thresholds = BuildInfo.ThresholdsJson(th),
            cases = cases.Select(c => new
            {
                id = c.Id,
                scenario = c.Scenario,
                expect = c.Expect,
                level = c.Level,
                levelName = c.LevelName,
                levelCode = c.LevelCode,
                spray = c.Spray,
                confirmBy = c.ConfirmBy,
                smokePpm = c.SmokePpm,
                tempC = c.TempC,
                risePerMinute = c.RisePerMinute,
                overLimitSeconds = c.OverLimitSeconds,
                timeAnomaly = c.TimeAnomaly,
                windowSize = c.WindowSize,
                pass = c.Pass,
                reason = c.Reason,
            }).ToList(),
        };
        return JsonSerializer.Serialize(payload, JsonOpts);
    }

    /// <summary>把 10 条用例写成人读清单（自检日志用）。</summary>
    public static List<string> Describe(AlarmThresholds? thresholds = null)
    {
        var lines = new List<string>
        {
            "跨端判据 parity 用例（desktop ↔ android，逐字段可比对）：",
            $"  阈值实际生效值：烟雾预警 {thresholds?.SmokeNoticePpm ?? 800:0} ppm / 温度预警 {thresholds?.TempNoticeC ?? 55.0:0.0} ℃ / "
            + $"温度确认 {thresholds?.TempAlarmC ?? 70.0:0.0} ℃ / "
            + $"温升阈值 {thresholds?.TempRiseCPerMinute ?? 5.0:0.0} ℃/min / 确认时长 {thresholds?.ConfirmSeconds ?? 30:0.0} s / "
            + $"采样周期 {thresholds?.SampleSeconds ?? 2.0:0.0} s",
            $"  防误喷连续性门槛 SprayGateSeconds = {AlarmEvaluator.SprayGateSeconds:0.0} s（独立常量，不随采样周期变）",
            $"  温升窗口最小真实跨度 RiseWindowMinSeconds = {AlarmEvaluator.RiseWindowMinSeconds:0.0} s（= 9000 ms，与 Android riseWindowMinMs 同义）",
            $"  滑动窗口：跨度上限 {AlarmEvaluator.RiseWindowSeconds} s / 容量上限 {AlarmEvaluator.HistoryMaxSize} 条",
        };
        foreach (var c in Run(thresholds))
        {
            lines.Add($"  [{(c.Pass ? "通过" : "失败")}] {c.Id} {c.Scenario}");
            lines.Add($"         判定 {c.Level}（{c.LevelName}）/ 请求喷淋 {(c.Spray ? "是" : "否")}"
                      + (string.IsNullOrEmpty(c.ConfirmBy) ? "" : $" / 命中分支 {c.ConfirmBy}")
                      + $" / 温升 {c.RisePerMinute:0.00} ℃/min / 连续超限 {c.OverLimitSeconds:0.0} s"
                      + $" / 窗口 {c.WindowSize} 点 => 期望 {c.Expect}");
        }
        var ok = Run(thresholds).Count(c => c.Pass);
        lines.Add($"  parity 用例结果：{ok}/10 项符合期望");
        return lines;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
