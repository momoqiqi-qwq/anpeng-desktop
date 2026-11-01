using Anpeng.Desktop.Data;
using Anpeng.Desktop.Models;

namespace Anpeng.Desktop.Services;

/// <summary>
/// 监控服务：驱动设备时钟、逐周期产生采样、调用 <see cref="AlarmEvaluator"/> 得到级别，
/// 并按契约 4.2 的「动作」列执行：确认火警才开阀；前两级只报警不喷淋。
/// </summary>
public sealed class MonitoringService
{
    private readonly DataRepository _repo;
    private readonly Dictionary<string, AlarmEvaluator> _evaluators = new();
    private readonly Dictionary<string, List<SensorReading>> _live = new();
    private readonly Dictionary<string, int> _openTicks = new();

    /// <summary>阀门回读状态（由本服务维护：电磁阀是执行器，不属于判定引擎的状态）。</summary>
    private readonly Dictionary<string, ValveState> _valves = new();

    /// <summary>每棚上一次的判定级别，用于「只在级别升级时生成一条报警记录」。
    /// 否则棚舍持续处于确认火警时，每个采样周期都会写一条记录，把报警列表刷爆。</summary>
    private readonly Dictionary<string, AlarmLevel> _lastLevel = new();

    /// <summary>各棚已完成的采样周期数。分级递进演练的阶段判定用它而不是全局时钟，
    /// 这样无论监控服务从第几个全局周期开始跑（例如自检先跑了别的棚），阶段边界都一致。</summary>
    private readonly Dictionary<string, int> _barnTicks = new();

    private const int MaxLivePerBarn = 240;

    /// <summary>确认火警自动开阀后的保持时长，**秒**。
    /// 刻意写成独立常量而不是「若干个采样周期」：设置页允许把采样周期改成 1~60 s，
    /// 若按时长派生周期数，周期一改，「保持多少秒」就会跟着漂移，界面文案就成了假话。
    /// 开阀周期数由 <see cref="CyclesFor"/> 从本常量换算得到。</summary>
    public const double AutoSprinklerHoldSeconds = 16.0;

    /// <summary>手动开阀后的保持时长，**秒**（同上，独立常量）。</summary>
    public const double ManualSprinklerHoldSeconds = 24.0;

    /// <summary>把「保持多少秒」换算成当前采样周期下的周期数（至少 1 个周期）。</summary>
    private int CyclesFor(double seconds) =>
        Math.Max(1, (int)Math.Round(seconds / Math.Max(1.0, Clock.SampleSeconds)));

    public DeviceClock Clock { get; }
    public DemoDataSet Data => _repo.Data;
    public AppSettings Settings => _repo.Settings;

    /// <summary>当前选中的棚 Id。</summary>
    public string? ActiveBarnId { get; private set; }

    public bool IsRunning { get; private set; }

    public event Action? Changed;
    public event Action<AlarmRecord>? AlarmRaised;
    public event Action<SprinklerRecord>? SprinklerActivated;

    public MonitoringService(DataRepository repo)
    {
        _repo = repo;
        Clock = new DeviceClock(repo.Data.BaseTime, repo.Settings.SampleSeconds);
        foreach (var barn in repo.Data.Barns)
        {
            _evaluators[barn.Id] = new AlarmEvaluator(repo.Settings.ToThresholds());
            _live[barn.Id] = new List<SensorReading>();
            _openTicks[barn.Id] = 0;
            _valves[barn.Id] = ValveState.Closed;
            _lastLevel[barn.Id] = AlarmLevel.Normal;
            _barnTicks[barn.Id] = 0;
            WarmUpEvaluator(barn);
        }

        ActiveBarnId = repo.Data.Barns.FirstOrDefault()?.Id;

        // 用最近的真实采样初始化当前值，保证首页与未启动时也有数
        foreach (var barn in repo.Data.Barns)
        {
            var recent = repo.Data.Readings.Where(r => r.BarnId == barn.Id).OrderBy(r => r.Time).TakeLast(30).ToList();
            _live[barn.Id].AddRange(recent);
            if (recent.Count > 0)
            {
                // 预热时已经喂过 recent[^1] 这一条，这里只读结果、绝不再 Evaluate 一次：
                // 判定引擎把「时间戳与上一条相同」视为异常上报并重置连续超限计时，
                // 重复喂样会把预热刚建立起来的计时打回 0（实测症状：B04 预热后
                // 连续超限从 660 s 掉到 0 s，确认火警降级成疑似火情）。
                var eval = _evaluators[barn.Id].LastEvaluation;
                // 演示数据已落在确认火警的棚，阀门回读必须是「开」——契约 4.2 只有确认火警才开阀，
                // 前两级一律「关」。否则首页会出现「确认火警 + 阀门关」的自相矛盾状态。
                if (eval is { RequestSprinkler: true })
                {
                    _valves[barn.Id] = ValveState.Open;
                    recent[^1].Valve = ValveState.Open;
                    // 开阀周期数刻意不在这里预置：留给第一个 Tick 走「首次开阀 → 写喷淋记录」
                    // 这条正常路径，否则启动时已处于确认火警的棚不会留下联动喷淋记录。
                }
            }

            RefreshBarnLevel(barn);
        }
    }

    /// <summary>
    /// 用该棚的最近历史喂入引擎，使「连续超限计时」带真实状态进入运行期。
    /// 取 20 条（= 19 个真实采样间隔 × 2 s = 38 s ≥ ConfirmSeconds 30 s），
    /// 这样处于确认火警的棚在构造后立刻就是「真·连续超限 38 s」，
    /// 而不是靠单条样本凑出来的假连续。
    /// </summary>
    private void WarmUpEvaluator(Barn barn)
    {
        var history = ArchivedFor(barn.Id)
            .OrderBy(r => r.Time)
            .TakeLast(WarmUpSamples);
        foreach (var r in history) _evaluators[barn.Id].Evaluate(r);
    }

    /// <summary>预热回放的归档样本条数：必须覆盖 ≥ ConfirmSeconds 的真实时间跨度。</summary>
    private const int WarmUpSamples = 20;

    /// <summary>
    /// 取某棚的归档采样，排除运行期实时生成的采样（Id 形如 B01-L0123）。
    /// 实时采样只服务曲线与报表，若混进预热/重演会污染判据的连续超限计时。
    /// </summary>
    private IEnumerable<SensorReading> ArchivedFor(string barnId) =>
        Data.Readings.Where(r => r.BarnId == barnId && !r.Id.Contains("-L", StringComparison.Ordinal));

    /// <summary>
    /// 把某个棚复位到演示数据集记录的状态：清空其实时缓冲、重建判定引擎、归零递进阶段计数。
    /// 仅用于自检与截图取证的确定性重演 —— 保证不管此前已经跑过多少个采样周期，
    /// 该棚的三级递进都从「正常」这一步重新开始。
    /// </summary>
    public void RestartBarnDemo(string barnId)
    {
        var barn = Data.Barns.FirstOrDefault(b => b.Id == barnId);
        if (barn is null) return;

        var history = ArchivedFor(barnId)
            .OrderBy(r => r.Time)
            .ToList();
        if (history.Count == 0) return;

        _evaluators[barnId] = new AlarmEvaluator(Settings.ToThresholds());
        _live[barnId] = history.TakeLast(30).ToList();
        _openTicks[barnId] = 0;
        _valves[barnId] = ValveState.Closed;
        _lastLevel[barnId] = AlarmLevel.Normal;
        _barnTicks[barnId] = 0;

        WarmUpEvaluator(barn);
        // 预热已喂过 _live[^1]，只读结果（重复喂同一时间戳会被引擎判为异常并重置计时）
        barn.CurrentLevel = _evaluators[barnId].LastEvaluation?.Level ?? AlarmLevel.Normal;

        AppLog.Info($"[重演] {barn.Name} 已复位到演示起点，级别 {barn.CurrentLevel}");
        Changed?.Invoke();
    }

    public void SetActiveBarn(string barnId)
    {
        if (ActiveBarnId == barnId) return;
        ActiveBarnId = barnId;
        Changed?.Invoke();
    }

    public Barn? ActiveBarn => Data.Barns.FirstOrDefault(b => b.Id == ActiveBarnId);

    public AlarmEvaluator EvaluatorFor(string barnId) => _evaluators[barnId];

    public IReadOnlyList<SensorReading> ReadingsFor(string barnId) =>
        _live.TryGetValue(barnId, out var list) ? list : Array.Empty<SensorReading>();

    public SensorReading? Current(string barnId) =>
        _live.TryGetValue(barnId, out var list) && list.Count > 0 ? list[^1] : null;

    public void Start() => IsRunning = true;

    public void Stop() => IsRunning = false;

    /// <summary>推进一个采样周期：产生采样 → 分级判断 → 执行动作。</summary>
    public void Tick()
    {
        var now = Clock.Advance();
        foreach (var barn in Data.Barns.ToList())
        {
            var barnTick = ++_barnTicks[barn.Id];
            var reading = NextReading(barn, now, barnTick);
            UpsertReading(reading);

            var evaluator = _evaluators[barn.Id];
            evaluator.Thresholds.SmokeNoticePpm = Settings.SmokeNoticePpm;
            evaluator.Thresholds.TempNoticeC = Settings.TempNoticeC;
            evaluator.Thresholds.TempAlarmC = Settings.TempAlarmC;
            evaluator.Thresholds.TempRiseCPerMinute = Settings.TempRiseCPerMinute;
            evaluator.Thresholds.ConfirmSeconds = Settings.ConfirmSeconds;
            evaluator.Thresholds.SampleSeconds = Settings.SampleSeconds;

            var wasOpen = _openTicks[barn.Id] > 0;
            if (wasOpen) _openTicks[barn.Id]--;
            var valve = _openTicks[barn.Id] > 0 ? ValveState.Open : _valves[barn.Id];

            var eval = evaluator.Evaluate(reading);
            barn.CurrentLevel = eval.Level;

            // 阀门状态回写到当前采样
            var valveState = _openTicks[barn.Id] > 0 ? ValveState.Open : ValveState.Closed;
            reading.Valve = valveState;
            _valves[barn.Id] = valveState;

            if (eval.RequestSprinkler && Settings.AutoSprinklerEnabled)
            {
                if (_openTicks[barn.Id] <= 0)
                {
                    _openTicks[barn.Id] = CyclesFor(AutoSprinklerHoldSeconds);
                    reading.Valve = ValveState.Open;
                }
                if (eval.Escalated && !wasOpen)
                {
                    RaiseSprinkler(barn, eval, "自动");
                }
            }

            // 只在级别相对上一周期「升级」时落一条报警记录；级别回落（如火情解除）后
            // 基线跟着复位，再次升级会重新记录。避免同一场火情每 2 s 刷一条记录。
            if (eval.Escalated && eval.Level != AlarmLevel.Normal
                && eval.Level > _lastLevel[barn.Id])
            {
                RaiseAlarm(barn, eval, reading.Valve);
            }
            _lastLevel[barn.Id] = eval.Level;
        }

        Changed?.Invoke();
    }

    /// <summary>产生下一周期的采样值（在最近读数上做连续变化，保证曲线连贯）。</summary>
    /// <param name="barnTick">该棚已完成的采样周期数（从 1 开始），用于分级递进阶段判定。</param>
    private SensorReading NextReading(Barn barn, DateTime now, int barnTick)
    {
        var prev = Current(barn.Id);
        var list = _live[barn.Id];
        var window = list.TakeLast(20).ToList();

        // 基础趋势：向该棚本周期的目标基线缓慢收敛 + 正弦扰动 + 小噪声
        var (targetTemp, targetSmoke) = Scenario(barn.Id, barnTick);
        var baseTemp = prev?.TempC ?? targetTemp;
        var baseSmoke = prev?.SmokePpm ?? targetSmoke;

        var temp = baseTemp + (targetTemp - baseTemp) * 0.18 + Math.Sin(now.Minute + Clock.Tick * 0.4) * 0.06;
        var smoke = baseSmoke + (targetSmoke - baseSmoke) * 0.18 + Math.Cos(now.Minute * 0.7 + Clock.Tick * 0.3) * 6;

        // 逼近阈值附近时收敛，避免长时间贴在阈值上抖动
        if (Math.Abs(temp - targetTemp) < 0.25) temp = targetTemp + (temp - targetTemp) * 0.2;
        if (Math.Abs(smoke - targetSmoke) < 4) smoke = targetSmoke + (smoke - targetSmoke) * 0.2;

        return new SensorReading
        {
            Id = $"{barn.Id}-L{Clock.Tick:0000}",
            BarnId = barn.Id,
            Time = now,
            SmokePpm = Math.Round(Math.Max(20, smoke), 0),
            TempC = Math.Round(temp, 1),
            Voltage = Math.Round(4.94 + (Clock.Tick % 7) * 0.01, 2),
            Online = true,
            Valve = ValveState.Closed,
        };
    }

    /// <summary>
    /// 各棚在本采样周期的稳态目标值。分区口径对齐 APP-SPEC 4.2 / 4.4：
    /// 烟雾预警 800 ppm、温度预警 55 °C、温度确认 70 °C、连续超限确认 30 s。
    /// 前 4 座棚取「恒定稳态」，让总览页任何时候都能同屏覆盖正常 / 环境预警 / 疑似火情 / 确认火警；
    /// B05 取「分阶段递进」，用于演示运行期真实的三级升级顺序（正常 → 环境预警 → 疑似火情 → 确认火警），
    /// 每一阶段只可能落在对应级别上，不会越过中间级别跳级。
    /// </summary>
    private static (double Temp, double Smoke) Scenario(string barnId, int tick) => barnId switch
    {
        // 正常：烟雾与温度都在预警阈值以内
        "B01" => (28.4, 262),

        // 环境预警：仅温度单项越限（目标烟雾 318 ppm 恒低于 800 ppm，故永远不会升级）
        "B02" => (57.2, 318),

        // 疑似火情：烟雾与温度同时越限（980 ppm / 60.5 °C），但温度低于确认值 70 °C、温升平缓。
        // 关键在烟雾按 36 s 周期漂移（14 个周期 980 ppm + 4 个周期 380 ppm，模拟草料表层阴燃与扬尘交替）：
        // 「两项同时超限」计时最多累积 24 s，永远够不到 30 s 确认时长，于是本棚长期停在
        // 「疑似火情」，用于验证「前两级绝不喷淋」——不会因为跑得久而跳级成确认火警。
        "B03" => ((tick - 1) % 18) < 14 ? (60.5, 980.0) : (60.5, 380.0),

        // 确认火警：两项同时越限并持续到确认时长；温度 68.5 °C 刻意保持在温度确认阈值 70 °C 以下、
        // 温升也平缓，使确认只可能命中「持续时长」那一支（连续超限远超 30 s），
        // 而不是靠温升速率或温度确认阈值凑正例。确认火警只有这三条 disjunct，没有烟雾确认阈值。
        "B04" => (68.5, 1712),

        // 运行期三级递进演示棚（起手为正常，与该棚历史采样一致）
        //
        // 阶段设计要点：疑似火情阶段刻意让温度目标与上一阶段完全相同，使温度平直、
        // 温升速率 ≈ 0 < 5.0 °C/min，只有烟雾越过 800 ppm 形成「两项同时超限」。
        // 于是确认火警只可能由「持续时长」那一支成立（连续超限满 30 s），
        // 不可能靠温升速率或温度确认阈值凑正例 —— 用来证明「持续时长」那一支判据真的在跑。
        "B05" => tick switch
        {
            < 4 => (30.1, 244),      // 正常
            < 50 => (56.4, 430),     // 环境预警：仅温度单项越限，烟雾恒低于 800 ppm
            < 66 => (56.4, 980),     // 疑似火情：烟温同时超限，温度平直 → 绝不喷淋
            _ => (56.4, 1800),       // 确认火警：两项连续超限满 30 s，命中「持续时长」分支
        },

        _ => (30.1, 244),
    };

    private void UpsertReading(SensorReading reading)
    {
        var list = _live[reading.BarnId];
        list.Add(reading);
        if (list.Count > MaxLivePerBarn) list.RemoveRange(0, list.Count - MaxLivePerBarn);

        // 同步进数据集合（供报表使用），并保持总量上限
        _repo.Data.Readings.Add(reading);
        if (_repo.Data.Readings.Count > 1200)
        {
            _repo.Data.Readings.RemoveRange(0, _repo.Data.Readings.Count - 1200);
        }
    }

    private void RaiseAlarm(Barn barn, AlarmEvaluation eval, ValveState valve)
    {
        var level = eval.Level;

        // DEF-D4 的**真实读取点**：设置页「报警声音提示」开关在这里生效。
        // 关闭后本机不再发出任何报警提示音；开启时按三级节奏发声（短促单响 / 连续双响 / 长鸣）。
        if (AlarmPolicy.ShouldPlaySound(Settings.AlarmSoundEnabled, level)) AlarmBeeper.Play(level);

        var record = new AlarmRecord
        {
            Id = $"A{DateTime.Now:HHmmss}{barn.Id}",
            BarnId = barn.Id,
            BarnName = barn.Name,
            Time = eval.Time,
            Level = level,
            SmokePpm = eval.SmokePpm,
            TempDecimals = eval.TempDecimals,
            RiseDecimals = eval.RiseDecimals,
            OverLimitDecimals = eval.OverLimitDecimals,
            TempC = eval.TempC,
            TempRisePerMin = eval.RisePerMinute,
            OverLimitSeconds = eval.OverLimitSeconds,
            Trigger = eval.ReasonText,
            SprinklerFired = level == AlarmLevel.Alarm && _openTicks[barn.Id] > 0,
            Action = AlarmLevelInfo.Action(level),
            Handler = level == AlarmLevel.Alarm ? "系统自动" : "李秀云（运维）",
            HandledAt = eval.Time.AddMinutes(2),
            // DEF-D4：不联网的本监控台不得声称「已推送」。推送由现场 ESP-01 → OneNet 链路承担，
            // 这里如实写清「本机做了什么」与「什么不由本机做」。
            Remark = level switch
            {
                AlarmLevel.Alarm => $"自动开阀，阀门回读 {ValveStateInfo.Name(valve)}；本机已按三级节奏长鸣；棚主远程推送由现场 ESP-01 → OneNet 链路承担，本监控台离线运行，不声称已推送",
                AlarmLevel.Suspect => "本机已按三级节奏连续双响；棚主推送同理由现场 ESP-01 → OneNet 链路承担；保持不喷淋",
                _ => "界面已给出黄色「建议检查」提示，本机已短促单响；保持不喷淋",
            },
        };

        _repo.Data.Alarms.Insert(0, record);
        AppLog.Info($"报警升级：{barn.Name} → {AlarmLevelInfo.Name(level)}｜{record.Trigger}");
        AlarmRaised?.Invoke(record);
    }

    private void RaiseSprinkler(Barn barn, AlarmEvaluation eval, string mode)
    {
        var drop = _live[barn.Id].TakeLast(20).Select(r => r.TempC).DefaultIfEmpty(eval.TempC).Max() - eval.TempC;
        var record = new SprinklerRecord
        {
            Id = $"S{DateTime.Now:HHmmss}{barn.Id}",
            BarnId = barn.Id,
            BarnName = barn.Name,
            StartTime = eval.Time,
            EndTime = eval.Time.AddSeconds(AutoSprinklerHoldSeconds),
            DurationSeconds = AutoSprinklerHoldSeconds,
            Reason = $"确认火警联动开阀：{eval.ReasonText}",
            TriggerLevel = AlarmLevel.Alarm,
            TempDrop = Math.Round(Math.Max(0, drop), 1),
            Mode = mode,
            Operator = mode == "自动" ? "系统自动" : Settings.Operator,
            Outcome = "喷淋进行中，待喷后复核",
        };
        _repo.Data.Sprinklers.Insert(0, record);
        AppLog.Info($"开阀喷淋：{barn.Name}｜{record.Reason}");
        SprinklerActivated?.Invoke(record);
    }

    private void RefreshBarnLevel(Barn barn)
    {
        var evaluator = _evaluators[barn.Id];

        // 只读最近一次判定结果，绝不重复 Evaluate 同一条采样：引擎把「时间戳与上一条相同」
        // 当作异常上报并重置连续超限计时，重复喂样会把自己刚建立的计时打回 0。
        if (evaluator.LastEvaluation is { } last)
        {
            barn.CurrentLevel = last.Level;
            return;
        }

        // 从未有过采样（数据目录被清空等）：用一个离线采样得出安全级别，绝不据此开阀。
        var eval0 = evaluator.Evaluate(new SensorReading
        {
            BarnId = barn.Id, Time = Clock.Now, SmokePpm = 0, TempC = 0, Online = false,
        });
        barn.CurrentLevel = eval0.Level;
    }

    /// <summary>手动开阀（喷淋控制页，危险动作，调用方必须先二次确认）。</summary>
    public SprinklerRecord ManualOpenValve(Barn barn, string operatorName, string reason)
    {
        _openTicks[barn.Id] = CyclesFor(ManualSprinklerHoldSeconds);
        var reading = Current(barn.Id);
        if (reading is not null) reading.Valve = ValveState.Open;
        if (_valves[barn.Id] == ValveState.Open) { /* 保持开阀周期 */ }

        var record = new SprinklerRecord
        {
            Id = $"S{DateTime.Now:HHmmss}{barn.Id}M",
            BarnId = barn.Id,
            BarnName = barn.Name,
            StartTime = Clock.Now,
            EndTime = Clock.Now.AddSeconds(ManualSprinklerHoldSeconds),
            DurationSeconds = ManualSprinklerHoldSeconds,
            Reason = reason,
            TriggerLevel = AlarmLevel.Normal,
            TempDrop = 0,
            Mode = "手动",
            Operator = operatorName,
            Outcome = "手动喷淋执行中",
        };
        _repo.Data.Sprinklers.Insert(0, record);
        AppLog.Warn($"手动开阀：{barn.Name}｜操作人 {operatorName}｜原因 {reason}");
        Changed?.Invoke();
        return record;
    }

    /// <summary>手动关阀。</summary>
    public void ManualCloseValve(Barn barn, string operatorName)
    {
        _openTicks[barn.Id] = 0;
        var reading = Current(barn.Id);
        if (reading is not null) reading.Valve = ValveState.Closed;
        _valves[barn.Id] = ValveState.Closed;
        AppLog.Warn($"手动关阀：{barn.Name}｜操作人 {operatorName}");
        Changed?.Invoke();
    }

    /// <summary>管路自检：逐只喷头给出结果（不真实开阀）。</summary>
    public List<(string Item, string Result)> RunPipeSelfTest(Barn barn)
    {
        var results = new List<(string, string)>();
        var coverage = barn.SprinklerCoverage;
        var nozzles = coverage.Split('，')[0].Replace("只开式喷头", "").Trim();
        var count = int.TryParse(nozzles, out var c) ? c : 3;
        for (var i = 1; i <= count; i++)
        {
            var ok = !(barn.Id == "B03" && i == 4);
            results.Add(($"喷头 P-{i:00}", ok
                ? $"出水正常，压力 {barn.WaterPressure:0.00} MPa"
                : "出水量偏小，疑似泥沙堵塞，建议拆洗滤网"));
        }
        results.Add(("主管路保压", $"30 s 压降 {barn.WaterPressure * 0.02:0.000} MPa，合格"));
        results.Add(("电磁阀动作", "开/关到位，继电器回读一致"));
        results.Add(("喷淋方向复核", coverage.Contains('，') ? coverage[(coverage.IndexOf('，') + 1)..] : coverage));
        AppLog.Info($"管路自检完成：{barn.Name}，{results.Count} 项");
        return results;
    }

    /// <summary>汇总带数据。</summary>
    public (int OnlineNodes, int TodayAlarms, int TodaySprinklers, double AvgTemp) Summary()
    {
        var today = Clock.Now.Date;
        var online = Data.Nodes.Count(n => n.Online);
        var alarms = Data.Alarms.Count(a => a.Time.Date == today);
        var sprinklers = Data.Sprinklers.Count(s => s.StartTime.Date == today);
        var avg = Data.Readings.Count == 0 ? 0 : Math.Round(Data.Readings.TakeLast(200).Average(r => r.TempC), 1);
        return (online, alarms, sprinklers, avg);
    }

    /// <summary>
    /// 跑一个采样周期的「真实现场」演示（不推进设备时钟、不写库），返回本次的分级结论。
    /// 用于 <c>--selftest-real</c> 自证三级链路与动作列一致。
    /// </summary>
    public AlarmEvaluation DispatchReal(string barnId)
    {
        var barn = Data.Barns.First(b => b.Id == barnId);
        var now = Clock.Now.AddSeconds(Clock.SampleSeconds);
        var reading = NextReading(barn, now, ++_barnTicks[barnId]);
        var evaluator = _evaluators[barnId];
        evaluator.Thresholds.SmokeNoticePpm = Settings.SmokeNoticePpm;
        evaluator.Thresholds.TempNoticeC = Settings.TempNoticeC;
        evaluator.Thresholds.TempAlarmC = Settings.TempAlarmC;
        evaluator.Thresholds.TempRiseCPerMinute = Settings.TempRiseCPerMinute;
        evaluator.Thresholds.ConfirmSeconds = Settings.ConfirmSeconds;
        evaluator.Thresholds.SampleSeconds = Settings.SampleSeconds;
        var eval = evaluator.Evaluate(reading);
        barn.CurrentLevel = eval.Level;
        return eval;
    }

    public void Save()
    {
        _repo.Data.BaseTime = Clock.Now;
        _repo.SaveData();
    }
}
