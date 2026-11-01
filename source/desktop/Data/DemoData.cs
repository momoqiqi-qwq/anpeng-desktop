using Anpeng.Desktop.Models;

namespace Anpeng.Desktop.Data;

/// <summary>
/// 首次启动自带的完整演示数据（APP-SPEC 5 节下限：≥4 棚 / ≥30 报警 / ≥20 喷淋 / ≥200 采样点）。
/// 全部中文、草料棚语境；固定种子，保证每次生成一致，便于截图与报告复核。
/// </summary>
public static class DemoData
{
    public static readonly DateTime BaseTime =
        DateTime.Today.AddHours(DateTime.Now.Hour).AddMinutes(DateTime.Now.Minute / 5 * 5);

    /// <summary>
    /// 演示采样点的时间步长（秒），与装置真实上报周期一致（契约 §4.4 采样周期 2 s）。
    /// 判据的连续性证据要求相邻样本间隔 ≤ <c>AlarmEvaluator.MaxSampleGapMs</c>，
    /// 因此演示数据必须按这个步长排布，否则引擎会（正确地）把稀疏样本判为不连续。
    /// </summary>
    public const double SamplePeriodSeconds = 2.0;

    private static readonly string[] BarnNames =
    {
        "一队草料棚", "西院秸秆棚", "南洼草棚", "东岗青贮棚", "北坡干草棚",
    };

    private static readonly string[] BarnLocations =
    {
        "一队村东 300 米", "二队西院墙内", "南洼洼地北侧", "东岗台地", "北坡晒场西",
    };

    private static readonly string[] BarnStructures =
    {
        "土坯墙 + 彩钢顶，南北通风", "砖混墙 + 石棉瓦顶", "彩钢棚架，四面半开敞",
        "砖墙 + 保温板顶，密闭性较好", "木架 + 彩钢顶，屋面坡度 15°",
    };

    private static readonly string[] BarnOwners = { "王守田", "李秀云", "赵长海", "孙德旺", "周振山" };

    private static readonly string[] BarnPhones =
    {
        "138****6721", "139****3408", "159****7752", "137****1190", "135****2286",
    };

    private static readonly string[] BarnCoverage =
    {
        "4 只开式喷头，覆盖草捆堆放区，避开东侧配电箱",
        "3 只开式喷头，覆盖主堆放区，避开西门插座",
        "5 只开式喷头，覆盖全棚，避开南侧牛舍",
        "4 只开式喷头，覆盖垛体中上部，避开料车通道",
        "3 只开式喷头，覆盖垛体上部，避开北侧羊圈",
    };

    public static DemoDataSet Build()
    {
        var rnd = new Random(20261013);
        var data = new DemoDataSet { BaseTime = BaseTime };

        for (var i = 0; i < BarnNames.Length; i++)
        {
            data.Barns.Add(new Barn
            {
                Id = $"B{i + 1:00}",
                Name = BarnNames[i],
                Location = BarnLocations[i],
                Area = 120 + i * 45,
                Structure = BarnStructures[i],
                StockTon = 42 + i * 17,
                Owner = BarnOwners[i],
                OwnerPhone = BarnPhones[i],
                LastInspection = BaseTime.Date.AddDays(-(3 + i * 4)),
                SprinklerCoverage = BarnCoverage[i],
                Enabled = true,
                WaterPressure = Math.Round(0.28 + i * 0.03, 2),
            });
        }

        data.Nodes.AddRange(BuildNodes(data.Barns));
        data.Readings.AddRange(BuildReadings(data.Barns, rnd));
        data.Alarms.AddRange(BuildAlarms());
        data.Sprinklers.AddRange(BuildSprinklers());
        data.Calibrations.AddRange(BuildCalibrations());
        data.Inspections.AddRange(BuildInspections());
        return data;
    }

    // ─────────────────────────── 节点 ───────────────────────────

    private static IEnumerable<DeviceNode> BuildNodes(List<Barn> barns)
    {
        foreach (var barn in barns)
        {
            var seed = barn.Id[^1] - '0';
            yield return new DeviceNode
            {
                Id = $"{barn.Id}-N1", BarnId = barn.Id, BarnName = barn.Name,
                Kind = NodeKind.SmokeGas, Model = "MQ-2 烟雾/可燃气体传感器",
                MountPosition = "棚内中部，离地 3.2 m，避开粉尘直冲面",
                FirmwareVersion = "STC89C52 固件 v2.4", SignalDbm = -52 - seed * 3,
                Online = true, LastHeartbeat = BaseTime.AddSeconds(-4),
            };
            yield return new DeviceNode
            {
                Id = $"{barn.Id}-N2", BarnId = barn.Id, BarnName = barn.Name,
                Kind = NodeKind.Temperature, Model = "PT100 铂热电阻 + MAX31865 三线制",
                MountPosition = "垛体内部插入式，探杆入堆 0.8 m",
                FirmwareVersion = "STC89C52 固件 v2.4", SignalDbm = -55 - seed * 2,
                Online = true, LastHeartbeat = BaseTime.AddSeconds(-4),
            };
            yield return new DeviceNode
            {
                Id = $"{barn.Id}-V1", BarnId = barn.Id, BarnName = barn.Name,
                Kind = NodeKind.Valve, Model = "DC12V 常闭式电磁阀（5V 继电器驱动）",
                MountPosition = "棚外主管道进水口，断电即关",
                FirmwareVersion = "继电器板 v1.2", SignalDbm = -60 - seed,
                Online = seed != 2, LastHeartbeat = BaseTime.AddSeconds(-6),
            };
            yield return new DeviceNode
            {
                Id = $"{barn.Id}-G1", BarnId = barn.Id, BarnName = barn.Name,
                Kind = NodeKind.Gateway, Model = "ESP-01 WiFi 模块（OneNet 云平台）",
                MountPosition = "棚内配电箱旁，天线朝上",
                FirmwareVersion = "ESP-01 AT 固件 v1.7", SignalDbm = -48 - seed * 4,
                Online = true, LastHeartbeat = BaseTime.AddSeconds(-4),
            };
        }
    }

    // ─────────────────────────── 采样点（每棚 ≥ 200 点的分布基础） ───────────────────────────

    private static IEnumerable<SensorReading> BuildReadings(List<Barn> barns, Random rnd)
    {
        var list = new List<SensorReading>();
        // 每棚 48 点，全部按**装置真实上报周期**（2 s）排布：单点装置每 2 s 采一次样，
        // 因此相邻样本的时间差就是判据可以采信的连续性证据。
        // 旧的「1 分钟一点」写法与契约 §4.2.1 的护栏直接冲突——相邻样本差 60 s > maxSampleGapMs(15 s)，
        // 引擎会正确地把它们判成不连续（节点静默 60 s 不能算「连续超限」），
        // 于是预热回放拿不到连续状态、演示数据也无法自证「连续超限 ≥ 30 s」。
        var steps = 36;
        var extra = 12;

        for (var b = 0; b < barns.Count; b++)
        {
            var barn = barns[b];
            var baseTemp = 24.0 + b * 1.6;
            var baseSmoke = 180.0 + b * 45.0;

            for (var i = steps + extra - 1; i >= 0; i--)
            {
                var t = BaseTime.AddSeconds(-i * SamplePeriodSeconds);
                double temp, smoke;

                // 当前读数：区分各棚状态，让首页一屏之内同时看到
                // 正常 / 环境预警 / 疑似火情 / 确认火警 四级差异
                var (curTemp, curSmoke) = b switch
                {
                    0 => (28.4, 262),   // 正常
                    1 => (57.2, 318),   // 环境预警（仅温度单项超限；烟雾恒低于 800 ppm）
                    2 => (60.5, 980),   // 疑似火情（烟温同时超限，但未越任何确认值）
                    3 => (68.5, 1712),  // 确认火警（温度低于 70 °C 确认阈值、温升平缓，确认命中「持续时长」分支）
                    _ => (30.1, 244),   // 正常（北坡干草棚，运行期做三级递进演示）
                };

                if (i == 0)
                {
                    temp = curTemp;
                    smoke = curSmoke;
                }
                else if (i <= 20)
                {
                    // 末段 20 个采样（按 2 s 步长 = 连续 40 s）保持稳态，温度一律平直，
                    // 使判据的两条分支在演示里可分辨：
                    //   · 温升速率 ≈ 0 °C/min（远低于 5.0 °C/min），因此「确认火警」不能
                    //     靠温升凑正例，只能靠持续时长或越过确认值；
                    //   · B02 烟雾恒低于 800 ppm → 仅温度单项越限 → 环境预警（不喷淋）；
                    //   · B03 烟雾在末条之前恒低于 800 ppm，只有当前读数同时越限 →
                    //     「两项连续超限」仅 0 s，未达 4 s/30 s 门槛 → 疑似火情（不喷淋）；
                    //   · B04 两项连续 40 s 同时越限（> 确认时长 30 s）→ 命中「持续时长」分支 → 确认火警（开阀）。
                    (temp, smoke) = b switch
                    {
                        1 => (curTemp, 318.0),   // 西院秸秆棚：烟雾始终不越限
                        2 => (curTemp, 700.0),   // 南洼草棚：烟雾在末条之前一直不越限
                        _ => (curTemp, curSmoke),
                    };
                }
                else
                {
                    // 更早的历史：低频正弦 + 噪声，仍带缓慢升温趋势
                    var phase = i * 0.35 + b;
                    temp = baseTemp + Math.Sin(phase) * 1.4 + (steps + extra - i) * 0.02 + rnd.NextDouble() * 0.6;
                    smoke = baseSmoke + Math.Cos(phase * 0.8) * 42 + (steps + extra - i) * 1.6 + rnd.NextDouble() * 24;
                }

                if (barn.Id == "B01" && i is >= 4 and <= 6)
                {
                    // 一队草料棚约 5 分钟前的阴燃迹象（与报警记录呼应）。
                    // 刻意放在末段回溯之外：若贴着当前读数，会破坏 B01 的温升速率读数。
                    temp = 63.4 + (6 - i) * 1.2;
                    smoke = 1780 + (6 - i) * 60;
                }

                list.Add(new SensorReading
                {
                    Id = $"{barn.Id}-R{t:HHmmss}",
                    BarnId = barn.Id,
                    Time = t,
                    SmokePpm = Math.Round(Math.Max(20, smoke), 0),
                    TempC = Math.Round(Math.Max(-10, temp), 1),
                    Voltage = Math.Round(4.92 + rnd.NextDouble() * 0.12, 2),
                    // 离线节点放在末段 12 条之外：既保证首页「19/20 节点在线」，
                    // 又不会把离线采样喂进判定引擎预热（离线采样不具备判断条件）。
                    Online = !(barn.Id == "B03" && i is > 11 and < 14),
                    Valve = ValveState.Closed,
                });
            }
        }

        return list.OrderBy(r => r.Time).ThenBy(r => r.BarnId, StringComparer.Ordinal);
    }

    // ─────────────────────────── 报警记录（三级齐全，共 36 条） ───────────────────────────

    private static IEnumerable<AlarmRecord> BuildAlarms()
    {
        var rows = new[]
        {
            ("B01", 34, AlarmLevel.Alarm, 2655.0, 79.6, 6.8, 138, "草捆内部阴燃，烟雾浓度连续 138 s 超限且温升 6.8 °C/min，判定确认火警", "已开阀喷淋 4 分 10 秒，明火扑灭后转入 2 小时看护", "王守田", "草捆堆芯阴燃，翻垛检查 3 处发热点"),
            ("B01", 49, AlarmLevel.Notice, 908.0, 34.2, 0.4, 6, "烟雾 908 ppm 超预警阈值 800 ppm（单项异常，温度正常）", "现场查看，属扬尘扰动，未喷淋", "李秀云", "夜间起风，草屑扬尘导致读数抬升，已清洁探头护罩"),
            ("B01", 96, AlarmLevel.Suspect, 1242.0, 58.9, 2.1, 22, "烟雾 1242 ppm 与温度 58.9 °C 同时超限，持续 22 s 不足确认时长，判定疑似火情", "推送棚主并电话复核，未喷淋", "赵长海", "棚顶照明灯镇流器过热，断电后温度回落"),
            ("B01", 178, AlarmLevel.Notice, 633.0, 56.4, 0.9, 14, "温度 56.4 °C 超预警阈值 55.0 °C（单项异常，烟雾正常）", "加强通风，未喷淋", "王守田", "午后日晒棚内积温，开启南北对口通风窗"),
            ("B01", 287, AlarmLevel.Suspect, 1520.0, 62.5, 3.4, 41, "烟雾 1520 ppm 与温度 62.5 °C 同时超限 41 s，仍判定疑似火情（未越确认阈值）", "推送棚主并到场核查，未喷淋", "李秀云", "邻棚焚烧秸秆的烟气飘入，已劝阻并清理火点"),
            ("B02", 21, AlarmLevel.Suspect, 1176.0, 60.2, 2.6, 26, "烟雾 1176 ppm 与温度 60.2 °C 同时超限，温度逼近确认阈值，判定疑似火情", "推送棚主并现场核查，未喷淋", "周振山", "秸秆垛湿度偏高、内部堆腐发热，已翻垛晾晒"),
            ("B02", 58, AlarmLevel.Notice, 874.0, 41.8, 0.3, 8, "烟雾 874 ppm 超预警阈值（单项异常）", "现场查看，未喷淋", "周振山", "进场车辆尾气短时干扰"),
            ("B02", 142, AlarmLevel.Notice, 512.0, 57.9, 1.1, 19, "温度 57.9 °C 超预警阈值（单项异常）", "开启喷雾降温，未喷淋", "孙德旺", "西院彩钢顶隔热层老化，已排入检修计划"),
            ("B02", 233, AlarmLevel.Suspect, 1388.0, 63.7, 3.1, 33, "烟雾 1388 ppm 与温度 63.7 °C 同时超限 33 s，因温升未达 5.0 °C/min 未升级", "推送棚主，未喷淋", "周振山", "堆垛中心含水率复测 18%，重排垛位加大间距"),
            ("B02", 311, AlarmLevel.Alarm, 2110.0, 74.3, 5.6, 74, "草捆内部阴燃，烟雾浓度连续 74 s 超限、温升 5.6 °C/min 且温度越过 70.0 °C，判定确认火警", "已开阀喷淋 3 分 30 秒，喷后翻垛隔离热源", "周振山", "喷淋后 20 分钟温度回落至 38.6 °C"),
            ("B02", 7, AlarmLevel.Notice, 968.4, 61.1, 1.6, 24, "温度 61.1 °C 超预警阈值（单项异常）", "现场检查，未喷淋", "周振山", "靠近烟囱一侧积热"),
            ("B03", 12, AlarmLevel.Suspect, 1305.0, 64.8, 2.9, 37, "烟雾 1305 ppm 与温度 64.8 °C 同时超限 37 s，判定疑似火情", "推送棚主并到场核查，未喷淋", "孙德旺", "南洼棚湿度大，垛底霉变发热，已清出霉变捆"),
            ("B03", 63, AlarmLevel.Notice, 755.0, 58.3, 1.2, 21, "温度 58.3 °C 超预警阈值（单项异常）", "开启侧面通风口，未喷淋", "孙德旺", "棚内空气滞留，加装两台轴流风机后恢复"),
            ("B03", 129, AlarmLevel.Notice, 912.0, 39.5, 0.5, 9, "烟雾 912 ppm 超预警阈值（单项异常）", "清洁探头，未喷淋", "赵长海", "MQ-2 探头积尘，清洁后基线回到 210 ppm"),
            ("B03", 204, AlarmLevel.Alarm, 2480.0, 76.9, 6.1, 96, "草捆内部阴燃，烟雾浓度连续 96 s 超限且温升 6.1 °C/min，判定确认火警", "已开阀喷淋 4 分 40 秒，联动断电后处置", "孙德旺", "喷淋方向已避开南侧牛舍，牲畜无影响"),
            ("B03", 268, AlarmLevel.Suspect, 1298.0, 59.4, 2.2, 25, "烟雾与温度同时超限，持续 25 s，判定疑似火情", "推送棚主并电话复核，未喷淋", "李秀云", "邻棚作业扬尘叠加，复核后为干扰"),
            ("B03", 5, AlarmLevel.Notice, 866.0, 42.7, 0.6, 11, "烟雾 866 ppm 超预警阈值（单项异常）", "现场查看，未喷淋", "孙德旺", "秸秆破碎作业扬尘"),
            ("B04", 18, AlarmLevel.Alarm, 2890.0, 81.2, 7.4, 152, "草捆内部阴燃，烟雾浓度连续 152 s 超限、两项均越确认阈值，判定确认火警", "已开阀喷淋 5 分 20 秒，随后翻垛转场", "赵长海", "上次喷淋后仍未彻底降温，转运至晒场摊开"),
            ("B04", 44, AlarmLevel.Suspect, 1204.0, 66.1, 3.0, 29, "烟雾 1204 ppm 与温度 66.1 °C 同时超限 29 s，判定疑似火情", "推送棚主并到场核查，未喷淋", "赵长海", "青贮窖排气口串味，密闭后读数恢复"),
            ("B04", 88, AlarmLevel.Notice, 980.0, 51.2, 0.7, 13, "烟雾 980 ppm 超预警阈值（单项异常）", "现场查看，未喷淋", "孙德旺", "东岗风大，扬尘进入棚内"),
            ("B04", 156, AlarmLevel.Suspect, 1330.0, 61.8, 2.7, 31, "烟雾与温度同时超限 31 s，温升 2.7 °C/min 未达阈值，判定疑似火情", "推送棚主，未喷淋", "赵长海", "保温板接缝渗热，加装反射膜"),
            ("B04", 219, AlarmLevel.Alarm, 2320.0, 72.4, 5.2, 62, "草捆内部阴燃，两项连续超限 62 s 且温升 5.2 °C/min，判定确认火警", "已开阀喷淋 3 分 50 秒，联动关闭配电箱前断路器", "赵长海", "喷淋覆盖垛体中上部，无复燃"),
            ("B04", 295, AlarmLevel.Notice, 690.0, 55.8, 0.8, 16, "温度 55.8 °C 超预警阈值（单项异常）", "开启顶窗通风，未喷淋", "李秀云", "午后棚内积温，通风后回落 4.2 °C"),
            ("B04", 2, AlarmLevel.Suspect, 1412.0, 62.9, 3.3, 38, "烟雾 1412 ppm 与温度 62.9 °C 同时超限 38 s，判定疑似火情", "推送棚主并到场核查，未喷淋", "赵长海", "垛体内部发热点定位到第 3 排第 7 捆，已单独隔离"),
            ("B05", 27, AlarmLevel.Notice, 742.0, 56.9, 1.0, 17, "温度 56.9 °C 超预警阈值（单项异常）", "现场检查，未喷淋", "周振山", "北坡棚西晒，遮阳网破损已更换"),
            ("B05", 71, AlarmLevel.Suspect, 1180.0, 60.6, 2.5, 27, "烟雾与温度同时超限 27 s，判定疑似火情", "推送棚主并电话复核，未喷淋", "周振山", "木架受热散发气味，属结构性隐患，已列入加固计划"),
            ("B05", 118, AlarmLevel.Notice, 866.0, 33.4, 0.4, 7, "烟雾 866 ppm 超预警阈值（单项异常）", "清洁探头护罩，未喷淋", "王守田", "扬尘干扰，清洁后基线恢复"),
            ("B05", 187, AlarmLevel.Alarm, 2050.0, 73.6, 5.4, 68, "草捆内部阴燃，烟雾浓度连续 68 s 超限且温升 5.4 °C/min，判定确认火警", "已开阀喷淋 3 分 20 秒，喷后保持 2 小时看护", "周振山", "喷淋方向避开北侧羊圈，牲畜无惊扰"),
            ("B05", 251, AlarmLevel.Suspect, 1256.0, 65.2, 3.2, 35, "烟雾 1256 ppm 与温度 65.2 °C 同时超限 35 s，判定疑似火情", "推送棚主并到场核查，未喷淋", "李秀云", "遮阳网内积存干草碎屑受日照引热，已清理"),
            ("B05", 4, AlarmLevel.Notice, 921.0, 44.8, 0.6, 10, "烟雾 921 ppm 超预警阈值（单项异常）", "现场查看，未喷淋", "孙德旺", "场内拖拉机怠速尾气干扰，短时即恢复"),
            ("B01", 143, AlarmLevel.Suspect, 1685.0, 68.4, 4.4, 52, "烟雾 1685 ppm 与温度 68.4 °C 同时超限 52 s，温升 4.4 °C/min 差 0.6 °C/min 未升级，判定疑似火情", "推送棚主并到场翻查垛体，未喷淋", "王守田", "温差 0.6 °C/min 之差阻断误喷，现场确认为堆内局部发热"),
            ("B02", 372, AlarmLevel.Alarm, 2260.0, 75.2, 5.9, 88, "草捆内部阴燃，两项连续超限 88 s、温升 5.9 °C/min，判定确认火警", "已开阀喷淋 3 分 45 秒，喷后测温回落至 41.3 °C", "周振山", "夜间无人值守，全靠装置自动开阀，无人员受伤"),
            ("B03", 331, AlarmLevel.Alarm, 1975.0, 71.6, 5.1, 57, "南洼草棚湿度偏高垛底霉变自燃，两项连续超限 57 s、温升 5.1 °C/min，判定确认火警", "已开阀喷淋 3 分 10 秒，喷后清出霉变捆", "孙德旺", "喷淋后复查垛底无阴燃点"),
            ("B04", 402, AlarmLevel.Suspect, 1140.0, 57.6, 2.0, 23, "烟雾 1140 ppm 与温度 57.6 °C 同时超限 23 s，未达确认时长，判定疑似火情", "推送棚主并电话复核，未喷淋", "赵长海", "青贮窖密闭不严串味，密闭后读数恢复"),
            ("B05", 508, AlarmLevel.Notice, 806.0, 47.4, 0.5, 5, "烟雾 806 ppm 刚刚越线 6 ppm（单项异常）", "现场查看，未喷淋", "周振山", "临界值提示，属干燥扬尘，已清洁探头"),
            ("B01", 613, AlarmLevel.Alarm, 3120.0, 84.5, 8.1, 205, "草捆堆芯阴燃蔓延，烟雾浓度连续 205 s 超限、温升 8.1 °C/min，判定确认火警", "已开阀喷淋 6 分 0 秒，联动断电，消防到场后彻底扑灭", "王守田", "装置争取到的 6 分钟为初期控制窗口"),
        };

        var list = new List<AlarmRecord>();
        var n = 0;
        foreach (var r in rows)
        {
            n++;
            var barnName = BarnNames[int.Parse(r.Item1[^1].ToString()) - 1];
            var level = r.Item3;
            var time = BaseTime.AddMinutes(-r.Item2);
            list.Add(new AlarmRecord
            {
                Id = $"A{n:000}",
                BarnId = r.Item1,
                BarnName = barnName,
                Time = time,
                Level = level,
                SmokePpm = r.Item4,
                TempC = r.Item5,
                TempRisePerMin = r.Item6,
                OverLimitSeconds = r.Item7,
                Trigger = r.Item8,
                SprinklerFired = r.Item3 == AlarmLevel.Alarm,
                Action = r.Item9,
                Handler = r.Item10,
                HandledAt = time.AddMinutes(4 + rndMinutes(n)),
                Remark = r.Item11,
            });
        }

        return list.OrderByDescending(a => a.Time).ThenBy(a => a.BarnId, StringComparer.Ordinal);
    }

    private static int rndMinutes(int n) => (n * 7) % 23;

    // ─────────────────────────── 喷淋记录（22 条） ───────────────────────────

    private static IEnumerable<SprinklerRecord> BuildSprinklers()
    {
        var rows = new[]
        {
            ("B01", -34, 250, 12.4, "草捆内部阴燃，确认火警联动开阀", "自动", "系统自动", "明火扑灭，无复燃"),
            ("B01", -178, 180, 0, "管路冬季放水后功能自检喷放", "手动", "李秀云（运维）", "每只喷头出水正常"),
            ("B02", -311, 210, 10.8, "草捆内部阴燃，确认火警联动开阀", "自动", "系统自动", "翻垛隔离后温度稳定"),
            ("B02", -142, 120, 0, "喷淋支管更换后试压喷放", "手动", "周振山（棚主）", "接头无渗漏"),
            ("B03", -204, 280, 11.6, "草捆内部阴燃，确认火警联动开阀", "自动", "系统自动", "联动断电后处置，无复燃"),
            ("B03", -129, 90, 0, "雨后喷头检查性喷放", "手动", "孙德旺（棚主）", "南侧两只喷头有泥沙，已拆洗"),
            ("B04", -18, 320, 13.2, "草捆内部阴燃，确认火警联动开阀", "自动", "系统自动", "转运晒场摊开，未复燃"),
            ("B04", -219, 230, 10.4, "草捆内部阴燃，确认火警联动开阀", "自动", "系统自动", "关闭配电箱前断路器后喷淋，无复燃"),
            ("B04", -88, 150, 0, "月度喷淋覆盖试验", "手动", "赵长海（运维）", "覆盖垛体中上部，地面无大量积水"),
            ("B05", -187, 200, 9.8, "草捆内部阴燃，确认火警联动开阀", "自动", "系统自动", "保持 2 小时看护后温度稳定"),
            ("B05", -71, 100, 0, "入冬前排空试验", "手动", "周振山（棚主）", "管路排空彻底，阀门回读到关位"),
            ("B01", -287, 240, 8.6, "邻棚飘入烟气触发前置降温", "自动", "系统自动", "干预后烟气散去，温度回落"),
            ("B02", -233, 110, 0, "干草垛翻垛前预防性湿润", "手动", "周振山", "湿润均匀，无过水"),
            ("B03", -268, 130, 0, "喷头堵塞排查试验", "手动", "李秀云（运维）", "南侧 1 只喷头出水量偏小，已更换"),
            ("B04", -156, 160, 6.4, "保温板接缝渗热前置降温", "自动", "系统自动", "加装反射膜后不再触发"),
            ("B04", -44, 120, 0, "消防泵联动测试", "手动", "赵长海", "5 s 内出水，压力 0.31 MPa"),
            ("B05", -251, 140, 5.2, "遮阳网内侧草屑受热前置降温", "自动", "系统自动", "清理草屑后恢复常态"),
            ("B01", -96, 100, 0, "喷淋头角度复核后试喷", "手动", "王守田（棚主）", "已避开东侧配电箱，无电气风险"),
            ("B02", -58, 90, 0, "继电器更换后功能验证", "手动", "周振山", "回读状态与指令一致"),
            ("B03", -63, 95, 0, "轴流风机安装后联调", "手动", "孙德旺", "通风与喷淋互不冲突"),
            ("B05", -27, 130, 0, "遮阳网更换后覆盖复核", "手动", "王守田（运维）", "喷淋覆盖未受影响"),
            ("B01", -7, 75, 0, "春季开阀前阀门动作自检", "手动", "李秀云（运维）", "开阀到位，回读正常"),
        };

        var list = new List<SprinklerRecord>();
        var i = 0;
        foreach (var r in rows)
        {
            i++;
            var start = BaseTime.AddMinutes(r.Item2);
            list.Add(new SprinklerRecord
            {
                Id = $"S{i:000}",
                BarnId = r.Item1,
                BarnName = BarnNames[int.Parse(r.Item1[^1].ToString()) - 1],
                StartTime = start,
                EndTime = start.AddSeconds(r.Item3),
                DurationSeconds = r.Item3,
                Reason = r.Item5,
                TriggerLevel = r.Item6 == "自动" ? AlarmLevel.Alarm : AlarmLevel.Normal,
                TempDrop = r.Item4,
                Mode = r.Item6,
                Operator = r.Item7,
                Outcome = r.Item8,
            });
        }

        return list.OrderByDescending(s => s.StartTime);
    }

    // ─────────────────────────── 校准 / 巡检记录 ───────────────────────────

    private static IEnumerable<CalibrationRecord> BuildCalibrations()
    {
        var rows = new[]
        {
            ("B01", "MQ-2 烟雾传感器", 812.0, 800.0, "ppm", -12.0, "李秀云", "标准气袋 800 ppm 甲烷标定，三次读数取均值", "合格"),
            ("B01", "PT100 温度探头", 55.4, 55.0, "°C", -0.4, "李秀云", "干体炉 55.0 °C 定点，三线制补偿后复测", "合格"),
            ("B02", "MQ-2 烟雾传感器", 806.0, 800.0, "ppm", -6.0, "赵长海", "标准气袋标定", "合格"),
            ("B02", "PT100 温度探头", 60.8, 60.0, "°C", -0.8, "赵长海", "干体炉 60.0 °C 定点", "合格"),
            ("B03", "MQ-2 烟雾传感器", 918.0, 800.0, "ppm", -118.0, "赵长海", "探头积尘严重，清洁后重新标定，偏置写入节点", "清洁后合格"),
            ("B03", "PT100 温度探头", 54.2, 55.0, "°C", 0.8, "赵长海", "干体炉 55.0 °C 定点", "合格"),
            ("B04", "MQ-2 烟雾传感器", 823.0, 800.0, "ppm", -23.0, "孙德旺", "标准气袋标定", "合格"),
            ("B04", "PT100 温度探头", 70.6, 70.0, "°C", -0.6, "孙德旺", "干体炉 70.0 °C 确认阈值点复核", "合格"),
            ("B05", "MQ-2 烟雾传感器", 795.0, 800.0, "ppm", 5.0, "周振山", "标准气袋标定", "合格"),
            ("B05", "PT100 温度探头", 55.1, 55.0, "°C", -0.1, "周振山", "干体炉 55.0 °C 定点", "合格"),
            ("B01", "系统时钟（ESP-01 NTP）", 0.0, 0.0, "s", 0.0, "李秀云", "与标准时钟比对，偏差 < 1 s", "合格"),
            ("B04", "系统时钟（ESP-01 NTP）", 2.0, 0.0, "s", -2.0, "赵长海", "与标准时钟比对，已在设备管理页校时", "校时后合格"),
        };

        var list = new List<CalibrationRecord>();
        var i = 0;
        foreach (var r in rows)
        {
            i++;
            list.Add(new CalibrationRecord
            {
                Id = $"C{i:000}",
                BarnId = r.Item1,
                BarnName = BarnNames[int.Parse(r.Item1[^1].ToString()) - 1],
                Device = r.Item2,
                Time = BaseTime.Date.AddDays(-(10 + i * 9)),
                RawValue = r.Item3,
                ReferenceValue = r.Item4,
                Unit = r.Item5,
                Offset = r.Item6,
                Operator = r.Item7,
                Method = r.Item8,
                Result = r.Item9,
            });
        }

        return list.OrderByDescending(c => c.Time);
    }

    private static IEnumerable<NozzleInspectionRecord> BuildInspections()
    {
        var rows = new[]
        {
            ("B01", "P-01～P-04", NozzleState.Normal, "0.31 MPa", "李秀云", "4 只喷头出水均匀，覆盖草捆堆放区"),
            ("B01", "P-02", NozzleState.Blocked, "0.28 MPa", "李秀云", "出水偏小，已拆洗滤网，下次巡检复核"),
            ("B02", "P-01～P-03", NozzleState.Normal, "0.29 MPa", "赵长海", "覆盖主堆放区，避开西门插座"),
            ("B02", "P-03", NozzleState.Unknown, "—", "赵长海", "垛体遮挡，需翻垛后复核"),
            ("B03", "P-01～P-05", NozzleState.Normal, "0.33 MPa", "孙德旺", "覆盖全棚，避开南侧牛舍，牲畜无惊扰"),
            ("B03", "P-04", NozzleState.Blocked, "0.30 MPa", "孙德旺", "泥沙堵塞，已更换喷头"),
            ("B04", "P-01～P-04", NozzleState.Normal, "0.35 MPa", "赵长海", "覆盖垛体中上部，料车通道无积水"),
            ("B05", "P-01～P-03", NozzleState.Normal, "0.30 MPa", "周振山", "覆盖垛体上部，避开北侧羊圈"),
            ("B05", "P-02", NozzleState.Unknown, "—", "周振山", "遮阳网更换期间遮挡，待复核"),
        };

        var list = new List<NozzleInspectionRecord>();
        var i = 0;
        foreach (var r in rows)
        {
            i++;
            list.Add(new NozzleInspectionRecord
            {
                Id = $"I{i:000}",
                BarnId = r.Item1,
                BarnName = BarnNames[int.Parse(r.Item1[^1].ToString()) - 1],
                Time = BaseTime.Date.AddDays(-(2 + i * 5)),
                NozzleCode = r.Item2,
                State = r.Item3,
                WaterPressure = r.Item4,
                Inspector = r.Item5,
                Note = r.Item6,
            });
        }

        return list.OrderByDescending(r => r.Time);
    }
}

/// <summary>演示/持久化数据集合。</summary>
public sealed class DemoDataSet
{
    public DateTime BaseTime { get; set; }
    public List<Barn> Barns { get; set; } = new();
    public List<DeviceNode> Nodes { get; set; } = new();
    public List<SensorReading> Readings { get; set; } = new();
    public List<AlarmRecord> Alarms { get; set; } = new();
    public List<SprinklerRecord> Sprinklers { get; set; } = new();
    public List<CalibrationRecord> Calibrations { get; set; } = new();
    public List<NozzleInspectionRecord> Inspections { get; set; } = new();
}
