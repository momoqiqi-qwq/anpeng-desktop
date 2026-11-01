using Anpeng.Desktop.Models;

namespace Anpeng.Desktop.Services;

/// <summary>
/// 三级分级判断引擎（APP-SPEC 4.2）——纯逻辑类，无 UI / 无 IO 依赖，可单测。
///
/// 本类与 Android 端 <c>com.anpeng.monitor.core.AlarmEvaluator</c> 使用【同一条判据】。
/// 两端对同一批采样必须得出同一级别、同一喷淋决定；任何一端改判据都必须同步另一端。
/// 机械比对的证据见 anpeng/_verify/parity-desktop.json（由 <c>--parity &lt;path&gt;</c> 生成）。
///
/// ── 判据 ─────────────────────────────────────────────────────────────────────
///   环境预警 NOTICE  ：烟雾 ≥ 预警阈值 与 温度 ≥ 预警阈值 中【恰好一项】成立 → 只提示，不喷淋
///   疑似火情 SUSPECT ：两项同时成立，但未满足确认条件 → 只报警，仍不喷淋
///   确认火警 ALARM   ：两项同时成立【且真实连续超限 ≥ <see cref="SprayGateSeconds"/> s】，
///                      并命中以下任一确认条件：
///                        a. 连续超限时长 ≥ ConfirmSeconds      （DURATION）
///                        b. 温升速率 ≥ TempRiseCPerMinute       （RISE，窗口真实跨度 ≥ 9 s）
///                        c. 温度 ≥ TempAlarmC                   （HARD_TEMP）
///                      → 蜂鸣器长鸣 + 开阀喷淋 + 推送
///   前两级绝不喷淋，只有确认火警才请求开阀。
///
/// ── 计时口径 ─────────────────────────────────────────────────────────────────
/// 所有「已持续 xx s」一律取【采样自带时间戳的真实时间差】，不用「调用次数 × 采样周期」累计：
/// 上报抖动、断连重连补发、重放不按节拍时，后者会把真实 57 s 的连续双超限算成 24 s，
/// 方向是**漏报**（该开阀却没开阀）。时间戳相等或倒退一律先重置计时器，
/// 绝不产生负数时长或 NaN；倒退时同时清空滑动窗口。
///
/// ── 两个刻意不随采样周期变的常量 ─────────────────────────────────────────────
/// <see cref="SprayGateSeconds"/> 是防误喷的**独立**连续性门槛，刻意不从采样周期派生：
/// 否则用户把采样周期改成 1 s，真实 2 s 的双超限就会被当成「已达门槛」而开阀。
/// <see cref="RiseWindowMinSeconds"/> 是温升速率参与判定的最小真实窗口跨度：
/// 没有它，两个相隔 1 ms、温差 0.05 °C 的样本会被放大成几十 °C/min 而误喷。
/// 两者分别对应 Android 端的 <c>bothSustainSeconds</c> 与 <c>riseWindowMinMs</c>。
/// </summary>
public sealed class AlarmEvaluator
{
    /// <summary>防误喷门槛：两项同时超限必须真实连续这么久才有资格进入确认判断（秒）。与 Android <c>bothSustainSeconds</c> 同值。</summary>
    public const double SprayGateSeconds = 4.0;

    /// <summary>温升速率参与判定所需的最小真实窗口跨度（秒）。与 Android <c>riseWindowMinMs = 9000</c> 同义。</summary>
    public const double RiseWindowMinSeconds = 9.0;

    /// <summary>滑动窗口的硬性容量上限（样本条数），防止时间戳卡死导致窗口无界增长。与 Android <c>windowMaxSize</c> 同值。</summary>
    public const int HistoryMaxSize = 64;

    /// <summary>温升速率滑动窗口的时间跨度上限（秒）。与 Android <c>windowSpanMs = 60000</c> 同义。</summary>
    public const int RiseWindowSeconds = 60;

    /// <summary>
    /// 相邻两次采样的**最大允许正向间隔**（毫秒）。超过它即视为时间戳跳变（APP-SPEC §4.2.1 第 3 条）。
    ///
    /// 取值理由（刻意是墙上时间的固定常量，不从 <see cref="AlarmThresholds.SampleSeconds"/> 派生）：
    /// · 下界：要容纳真实链路的上报抖动。ESP-01 经 OneNet 转发的实测最坏间隔约 4.75 s，
    ///   取 15 s 是它的 3 倍余量，正常抖动不会被误判成跳变。
    /// · 上界：必须严格小于确认时长 <see cref="AlarmThresholds.ConfirmSeconds"/>（默认 30 s）；
    ///   这里只取它的一半，于是「单次跳变」最多只能贡献不到 30 s 的连续超限，
    ///   凑不出「两项连续超限满 30 s」这个确认分支——跳变不可能凭自身把阀门吹开。
    /// 与 Android <c>maxSampleGapMs</c> 同值。
    /// </summary>
    public const long MaxSampleGapMs = 15000;

    /// <summary><see cref="MaxSampleGapMs"/> 的秒等价值（契约 §4.2.1 第 4 条要求两端同时导出两个键）。</summary>
    public const double MaxSampleGapSeconds = MaxSampleGapMs / 1000.0;

    private readonly List<(DateTime Time, double TempC)> _history = new();

    public AlarmThresholds Thresholds { get; }

    /// <summary>最近一次评估结果（只读快照，读取它不会推动任何计时）。尚未评估过时为 null。</summary>
    public AlarmEvaluation? LastEvaluation { get; private set; }

    /// <summary>最近一次评估是否遇到时间戳相等或倒退（供界面/自检取证）。</summary>
    public bool TimeAnomaly { get; private set; }

    /// <summary>当前滑动窗口内的样本条数（自检用：验证硬性容量上限确实生效）。</summary>
    public int WindowSize => _history.Count;

    public AlarmEvaluator(AlarmThresholds? thresholds = null)
    {
        Thresholds = thresholds ?? new AlarmThresholds();
    }

    /// <summary>
    /// 该节点「两项同时超限」的连续时长，s（无历史时为 0）。
    /// 这是 APP-SPEC 4.2 所说「两项连续超限」的计时口径：只有烟雾与温度**同时**超限才累计，
    /// 任一项回落即清零。确认火警判据读的就是它。
    /// </summary>
    public double CurrentOverLimitSeconds =>
        !_bothActive || _history.Count == 0 ? 0 : Math.Max(0, (_history[^1].Time - _bothStart).TotalSeconds);

    /// <summary>任一项超限的连续时长，s。仅用于「环境预警」的文案展示，不参与确认火警判据。</summary>
    public double CurrentAnyOverLimitSeconds =>
        !_anyActive || _history.Count == 0 ? 0 : Math.Max(0, (_history[^1].Time - _anyStart).TotalSeconds);

    /// <summary>两项同时超限的起算时刻。</summary>
    private DateTime _bothStart = DateTime.MinValue;
    private bool _bothActive;

    /// <summary>任一项超限的起算时刻。</summary>
    private DateTime _anyStart = DateTime.MinValue;
    private bool _anyActive;

    /// <summary>最近一次入窗采样的时间戳，用于识别相等/倒退。</summary>
    private DateTime _lastSampleTime = DateTime.MinValue;
    private bool _hasLastSample;

    /// <summary>
    /// 温升速率，°C/min。窗口取「最近 <see cref="RiseWindowSeconds"/> 秒」，
    /// 且要求窗口首末样本的**真实跨度 ≥ <see cref="RiseWindowMinSeconds"/> 秒**，否则返回 0
    /// （单点抖动、重复时间戳、刚启动时都不该产出温升结论）。
    /// 这是**判定用**的唯一口径，与 Android <c>riseRate()</c> 逐行同构。
    /// </summary>
    public double RiseRatePerMinute
    {
        get
        {
            if (_history.Count < 2) return 0;
            var end = _history[^1];
            var start = _history[0];
            var span = (end.Time - start.Time).TotalSeconds;
            if (span < RiseWindowMinSeconds) return 0;
            if (span <= 0) return 0;
            return (end.TempC - start.TempC) / (span / 60.0);
        }
    }

    /// <summary>
    /// 最近两点斜率，°C/min。**仅用于界面展示与文案**，刻意不参与判定：
    /// 它没有真实跨度下限，两个相隔极近的样本就能放大成几十 °C/min（误喷方向）。
    /// </summary>
    public double RecentRisePerMinute
    {
        get
        {
            if (_history.Count < 2) return 0;
            var last = _history[^1];
            var prev = _history[^2];
            var span = (last.Time - prev.Time).TotalSeconds;
            if (span <= 0) return 0;
            return (last.TempC - prev.TempC) / (span / 60.0);
        }
    }

    public void Reset()
    {
        _history.Clear();
        ResetTimers();
        _lastSampleTime = DateTime.MinValue;
        _hasLastSample = false;
        LastEvaluation = null;
        TimeAnomaly = false;
    }

    private void ResetTimers()
    {
        _bothStart = DateTime.MinValue;
        _bothActive = false;
        _anyStart = DateTime.MinValue;
        _anyActive = false;
    }

    /// <summary>
    /// 喂入一次采样，返回级别与动作。同一实例应按棚（节点）单独持有，以保留连续超限计时。
    /// </summary>
    public AlarmEvaluation Evaluate(SensorReading reading)
    {
        var result = EvaluateCore(reading);
        LastEvaluation = result;
        return result;
    }

    private AlarmEvaluation EvaluateCore(SensorReading reading)
    {
        TimeAnomaly = false;

        var result = new AlarmEvaluation
        {
            Time = reading.Time,
            SmokePpm = reading.SmokePpm,
            TempC = reading.TempC,
            Valve = reading.Valve,
        };

        // ── 离线：节点心跳丢失（数据不可信，但一律不开阀） ──
        if (!reading.Online)
        {
            _history.Clear();
            ResetTimers();
            _lastSampleTime = reading.Time;
            _hasLastSample = true;
            result.Level = AlarmLevel.Offline;
            result.SmokePpm = Round0(reading.SmokePpm);
            result.SmokeDecimals = 0;
            result.TempC = Round1(reading.TempC);
            result.TempDecimals = 1;
            result.Reasons.Add("ESP-01 心跳超时，节点失联，本次采样不具备判断条件");
            return result;
        }

        // ── 时间戳护栏（APP-SPEC §4.2.1 第 2、3 条）：相等 / 倒退 / 正向大跳变，三者都要重置计时。 ──
        // 三种异常都不得产生负时长、NaN，也不得让「无采样支撑的时间」被算成连续超限。
        if (_hasLastSample)
        {
            var deltaMs = (reading.Time - _lastSampleTime).TotalMilliseconds;

            if (reading.Time < _lastSampleTime)
            {
                // 倒退：清空窗口并重置全部计时。
                TimeAnomaly = true;
                _history.Clear();
                ResetTimers();
            }
            else if (reading.Time == _lastSampleTime)
            {
                // 相等：重复上报，重置全部计时（窗口保留，温差为 0，不影响温升结论）。
                TimeAnomaly = true;
                ResetTimers();
            }
            else if (deltaMs > MaxSampleGapMs)
            {
                // 正向大跳变：这中间**没有任何采样支撑**。若沿用旧计时，一次时钟跳变就会把
                // 平温的样本吹成「连续超限 3600 s」并凭 DURATION 分支开阀。
                // 因此清空窗口 + 三个计时器全清（_bothStart/_bothActive、_anyStart/_anyActive）。
                TimeAnomaly = true;
                _history.Clear();
                ResetTimers();
            }
        }
        _lastSampleTime = reading.Time;
        _hasLastSample = true;

        // ── 采样入窗：先按 60 s 时窗裁剪，再按硬性容量上限兜底 ──
        _history.Add((reading.Time, reading.TempC));
        while (_history.Count > 1
               && (reading.Time - _history[0].Time).TotalSeconds > RiseWindowSeconds)
        {
            _history.RemoveAt(0);
        }
        while (_history.Count > HistoryMaxSize) _history.RemoveAt(0);

        // ── 判定用**原始值**，展示用自适应取整值（APP-SPEC §4.2.1 第 1 条） ──
        // 旧实现把取整结果回灌给判据：69.96 °C 会被显示成 70.0 并且**真的**按「达到确认温度」开阀；
        // 799.6 ppm / 54.96 °C 同理。现在判据只认原始读数与原始温升速率，
        // 取整值仅回填给 result.* 供展示，且小数位自适应——保证「显示值 ≥ 阈值」与「原始值 ≥ 阈值」同真假。
        var smokeRaw = reading.SmokePpm;
        var tempRaw = reading.TempC;
        var riseRaw = RiseRatePerMinute;

        var smokeDisp = ValueDisplay.VsMulti(smokeRaw, Thresholds.SmokeNoticePpm);
        var tempDisp = ValueDisplay.VsMulti(tempRaw, Thresholds.TempNoticeC, Thresholds.TempAlarmC);
        var riseDisp = ValueDisplay.Vs(riseRaw, Thresholds.TempRiseCPerMinute);

        result.SmokePpm = smokeDisp.Value;
        result.SmokeDecimals = smokeDisp.Decimals;
        result.TempC = tempDisp.Value;
        result.TempDecimals = tempDisp.Decimals;
        result.RisePerMinute = riseDisp.Value;
        result.RiseDecimals = riseDisp.Decimals;
        result.RecentRisePerMinute = Round2(RecentRisePerMinute);
        result.TimeAnomaly = TimeAnomaly;

        if (TimeAnomaly)
        {
            result.Reasons.Add($"检测到时间戳异常（重复、倒退或正向跳变 > {MaxSampleGapSeconds:0.0} s）：已清空滑动窗口并重置全部连续计时，异常期间不产生确认结论");
        }

        var smokeOver = smokeRaw >= Thresholds.SmokeNoticePpm;
        var tempOver = tempRaw >= Thresholds.TempNoticeC;
        var bothOver = smokeOver && tempOver;
        var anyOver = smokeOver || tempOver;

        // ── 「两项连续超限」计时器（APP-SPEC 4.2 口径）：只有两项同时超限才累计，任一项回落即清零 ──
        if (bothOver)
        {
            if (!_bothActive)
            {
                _bothActive = true;
                _bothStart = reading.Time;
            }
        }
        else
        {
            _bothActive = false;
            _bothStart = reading.Time;
        }
        var bothSeconds = _bothActive
            ? Math.Max(0, (reading.Time - _bothStart).TotalSeconds)
            : 0;

        // ── 任一项超限的连续时长：只用于环境预警文案，不参与确认火警判据 ──
        if (anyOver)
        {
            if (!_anyActive)
            {
                _anyActive = true;
                _anyStart = reading.Time;
            }
        }
        else
        {
            _anyActive = false;
            _anyStart = reading.Time;
        }
        var anySeconds = _anyActive ? Math.Max(0, (reading.Time - _anyStart).TotalSeconds) : 0;

        // ── 确认火警：与 Android 端逐字一致的统一判据（**全部用原始值比较**） ──
        var confirmTemp = Thresholds.TempAlarmC;
        var gateSeconds = SprayGateSeconds;

        // 连续超限时长的展示值同样自适应：门槛 4.0 s 与确认时长 30 s 两个尺度都要同真假，
        // 否则 3.96 s 会被显示成「4.0 s」而文案同时声称「已达门槛」。
        var overDisp = ValueDisplay.VsMulti(bothSeconds, gateSeconds, Thresholds.ConfirmSeconds);
        result.OverLimitSeconds = overDisp.Value;
        result.OverLimitDecimals = overDisp.Decimals;

        var sustained = bothSeconds >= gateSeconds;
        var byDuration = bothSeconds >= Thresholds.ConfirmSeconds;
        var byRise = riseRaw >= Thresholds.TempRiseCPerMinute;
        var byHardTemp = tempRaw >= confirmTemp;

        // 供文案使用的展示串（与 result.* 完全同源，不会各说一套）
        var smokeTxt = ValueDisplay.Text(result.SmokePpm, result.SmokeDecimals);
        var tempTxt = ValueDisplay.Text(result.TempC, result.TempDecimals);
        var riseTxt = ValueDisplay.Text(result.RisePerMinute, result.RiseDecimals);
        var overTxt = ValueDisplay.Text(result.OverLimitSeconds, result.OverLimitDecimals);
        var anyTxt = ValueDisplay.Text(Round1(anySeconds), 1);

        if (bothOver && sustained && (byDuration || byRise || byHardTemp))
        {
            // 命中优先级与 Android 端一致：RISE > HARD_TEMP > DURATION
            result.ConfirmBy = byRise ? "RISE" : byHardTemp ? "HARD_TEMP" : "DURATION";
            result.Level = AlarmLevel.Alarm;
            result.RequestSprinkler = true;
            result.Escalated = true;
            result.Reasons.Add($"烟雾 {smokeTxt} ppm 与温度 {tempTxt} °C 两项连续超限 {overTxt} s（≥ 防误喷连续性门槛 {gateSeconds:0.0} s）");
            result.Reasons.Add(result.ConfirmBy switch
            {
                "RISE" => $"命中「温升速率」：{riseTxt} °C/min ≥ 阈值 {Thresholds.TempRiseCPerMinute:0.0} °C/min（窗口真实跨度 {RiseWindowSpanSeconds:0.0} s ≥ {RiseWindowMinSeconds:0.0} s）",
                "HARD_TEMP" => $"命中「温度确认火警阈值」：{tempTxt} °C ≥ {confirmTemp:0.0} °C",
                _ => $"命中「持续时长」：{overTxt} s ≥ 确认时长 {Thresholds.ConfirmSeconds:0.0} s",
            });
            return result;
        }

        // ── 疑似火情：烟温同时异常，只报警不喷淋 ──
        if (bothOver)
        {
            result.Level = AlarmLevel.Suspect;
            result.Escalated = true;
            result.Reasons.Add($"烟雾 {smokeTxt} ppm 与温度 {tempTxt} °C 同时超限（已持续 {overTxt} s）");
            if (!sustained)
            {
                result.Reasons.Add($"连续超限 {overTxt} s 尚未达到防误喷连续性门槛 {gateSeconds:0.0} s，保持不喷淋");
            }
            else
            {
                result.Reasons.Add($"持续时长 {overTxt} s 未达 {Thresholds.ConfirmSeconds:0.0} s、温升 {riseTxt} °C/min 未达 {Thresholds.TempRiseCPerMinute:0.0} °C/min、温度 {tempTxt} °C 未达 {confirmTemp:0.0} °C，三项确认条件均未命中，属怀疑阶段，保持不喷淋");
            }
            return result;
        }

        // ── 环境预警：单项异常 ──
        if (anyOver)
        {
            result.Level = AlarmLevel.Notice;
            result.Escalated = true;
            result.Reasons.Add(smokeOver
                ? $"仅烟雾 {smokeTxt} ppm ≥ 预警阈值 {Thresholds.SmokeNoticePpm:0} ppm 超限，温度 {tempTxt} °C < {Thresholds.TempNoticeC:0.0} °C 正常（烟雾超限已真实持续 {anyTxt} s）"
                : $"仅温度 {tempTxt} °C ≥ 预警阈值 {Thresholds.TempNoticeC:0.0} °C 超限，烟雾 {smokeTxt} ppm < {Thresholds.SmokeNoticePpm:0} ppm 正常（温度超限已真实持续 {anyTxt} s）");
            result.Reasons.Add("单项异常，草料扬尘与阴燃初期均可能出现；按环境预警处理，建议现场检查，不喷淋");
            return result;
        }

        result.Level = AlarmLevel.Normal;
        if (tempRaw >= Thresholds.TempNoticeC - 5 || smokeRaw >= Thresholds.SmokeNoticePpm * 0.85)
        {
            result.Reasons.Add("读数正常，但已接近预警阈值，继续观察");
        }
        else
        {
            result.Reasons.Add($"烟雾 {smokeTxt} ppm < {Thresholds.SmokeNoticePpm:0} ppm，温度 {tempTxt} °C < {Thresholds.TempNoticeC:0.0} °C，两项均在阈值内");
        }
        return result;
    }

    /// <summary>判定所用温升窗口的真实跨度（s），仅用于文案取证。</summary>
    private double RiseWindowSpanSeconds =>
        _history.Count < 2 ? 0 : Math.Max(0, (_history[^1].Time - _history[0].Time).TotalSeconds);

    // 判定与展示共用的取整口径（与 Android 的 r0/r1/r2 同构）
    private static double Round0(double d) => Math.Round(d, MidpointRounding.AwayFromZero);
    private static double Round1(double d) => Math.Round(d * 10.0, MidpointRounding.AwayFromZero) / 10.0;
    private static double Round2(double d) => Math.Round(d * 100.0, MidpointRounding.AwayFromZero) / 100.0;
}

/// <summary>阈值设置（APP-SPEC 4.4 默认值）。</summary>
public sealed class AlarmThresholds
{
    /// <summary>烟雾预警阈值，ppm（默认 800）。</summary>
    public double SmokeNoticePpm { get; set; } = 800;

    /// <summary>温度预警阈值，°C（默认 55.0）。</summary>
    public double TempNoticeC { get; set; } = 55.0;

    /// <summary>温度确认火警阈值，°C（默认 70.0）。</summary>
    public double TempAlarmC { get; set; } = 70.0;

    /// <summary>温升速率阈值，°C/min（默认 5.0）。</summary>
    public double TempRiseCPerMinute { get; set; } = 5.0;

    /// <summary>连续超限确认时长，s（默认 30）。</summary>
    public double ConfirmSeconds { get; set; } = 30;

    /// <summary>采样周期，s（默认 2）。</summary>
    public double SampleSeconds { get; set; } = 2;

    public AlarmThresholds Clone() => new()
    {
        SmokeNoticePpm = SmokeNoticePpm,
        TempNoticeC = TempNoticeC,
        TempAlarmC = TempAlarmC,
        TempRiseCPerMinute = TempRiseCPerMinute,
        ConfirmSeconds = ConfirmSeconds,
        SampleSeconds = SampleSeconds,
    };
}

public sealed class AlarmEvaluation
{
    public DateTime Time { get; set; }
    public AlarmLevel Level { get; set; } = AlarmLevel.Normal;
    public double SmokePpm { get; set; }
    public double TempC { get; set; }

    /// <summary>判定用温升速率（60 s 窗口、真实跨度 ≥ 9 s，否则 0）。</summary>
    public double RisePerMinute { get; set; }

    /// <summary>最近两点斜率。**仅供展示**，不参与判定。</summary>
    public double RecentRisePerMinute { get; set; }

    /// <summary>两项同时超限的连续时长，s。</summary>
    public double OverLimitSeconds { get; set; }

    /// <summary>确认火警命中的分支：RISE / HARD_TEMP / DURATION；非确认火警为空串。</summary>
    public string ConfirmBy { get; set; } = "";

    /// <summary>本次评估是否遇到时间戳相等或倒退。</summary>
    public bool TimeAnomaly { get; set; }

    public ValveState Valve { get; set; }

    /// <summary>确认火警时为 true —— 引擎唯一允许开阀的输出。</summary>
    public bool RequestSprinkler { get; set; }

    /// <summary>相比上一级别是否发生升级。</summary>
    public bool Escalated { get; set; }

    public List<string> Reasons { get; } = new();

    public string ReasonText => string.Join("；", Reasons);

    public string LevelName => AlarmLevelInfo.Name(Level);

    // ── 展示小数位（自适应，见 ValueDisplay） ──────────────────────────────────
    // 判据从不读这些值；它们只保证「界面显示的数字」不会与「引擎的真实结论」矛盾。

    /// <summary>烟雾展示小数位。</summary>
    public int SmokeDecimals { get; set; }

    /// <summary>温度展示小数位。</summary>
    public int TempDecimals { get; set; } = 1;

    /// <summary>温升速率展示小数位。</summary>
    public int RiseDecimals { get; set; } = 2;

    /// <summary>连续超限时长展示小数位。</summary>
    public int OverLimitDecimals { get; set; } = 1;

    /// <summary>烟雾展示串（含单位）。</summary>
    public string SmokeText => ValueDisplay.Text(SmokePpm, SmokeDecimals);

    /// <summary>温度展示串。</summary>
    public string TempText => ValueDisplay.Text(TempC, TempDecimals);

    /// <summary>温升速率展示串。</summary>
    public string RiseText => ValueDisplay.Text(RisePerMinute, RiseDecimals);

    /// <summary>连续超限时长展示串。</summary>
    public string OverLimitText => ValueDisplay.Text(OverLimitSeconds, OverLimitDecimals);
}
