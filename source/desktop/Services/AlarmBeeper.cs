using System.Runtime.InteropServices;
using Anpeng.Desktop.Models;

namespace Anpeng.Desktop.Services;

/// <summary>
/// 报警声音提示的**真实开关策略**（DEF-D4）。
///
/// 抽成纯函数是为了能被探针与变异测试直接命中：把返回值改成恒 false 就等于「开关失效」，
/// 探针必须报红。判定逻辑本身只有一条义务——开关关闭时绝不出声。
/// </summary>
public static class AlarmPolicy
{
    private static readonly long SoundMask =
        (1L << (int)AlarmLevel.Notice) |
        (1L << (int)AlarmLevel.Suspect) |
        (1L << (int)AlarmLevel.Alarm);

    /// <summary>本次升级是否应当发声。仅前三级（环境预警/疑似火情/确认火警）出声，正常与离线不出声。</summary>
    public static bool ShouldPlaySound(bool alarmSoundEnabled, AlarmLevel level)
    {
        if (!alarmSoundEnabled) return false;
        var bit = (int)level;
        if (bit < 0 || bit > 62) return false;
        return (SoundMask & (1L << bit)) != 0;
    }
}

/// <summary>
/// 三级鸣响节奏的发声器。
///
/// 说明（避免「界面说假话」）：棚内真正的蜂鸣器挂在 STC89C52 单片机上，由单片机直接驱动；
/// 这里发声的是**本监控台所在机器**的系统提示音，用来在值班室把三级节奏复现出来。
/// 两条链路互不替代，设置页文案已如实区分。
/// </summary>
public static class AlarmBeeper
{
    private const uint MbIconHand = 0x00000010;        // 长鸣
    private const uint MbIconExclamation = 0x00000030; // 双响
    private const uint MbIconAsterisk = 0x00000040;    // 单响

    [DllImport("user32.dll", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MessageBeep(uint uType);

    /// <summary>累计发声次数（自检与探针的观测点）。</summary>
    public static int PlayCount { get; private set; }

    /// <summary>最近一次发声对应的级别。</summary>
    public static AlarmLevel? LastLevel { get; private set; }

    /// <summary>累计的「按级别分组发声次数」。</summary>
    public static int CountFor(AlarmLevel level) => _byLevel.TryGetValue(level, out var n) ? n : 0;

    private static readonly Dictionary<AlarmLevel, int> _byLevel = new();

    /// <summary>
    /// 测试替身：非 null 时用它替代真实 MessageBeep，避免自检真的发出噪声。
    /// 返回 true 表示「已发声」。
    /// </summary>
    public static Func<AlarmLevel, bool>? Sink { get; set; }

    /// <summary>按级别鸣响，返回是否真的发出（被 Sink 拦截时以 Sink 的返回值为准）。</summary>
    public static bool Play(AlarmLevel level)
    {
        PlayCount++;
        LastLevel = level;
        _byLevel[level] = CountFor(level) + 1;

        if (Sink is { } sink) return sink(level);

        return level switch
        {
            AlarmLevel.Notice => MessageBeep(MbIconAsterisk),
            AlarmLevel.Suspect => MessageBeep(MbIconExclamation) & MessageBeep(MbIconExclamation),
            AlarmLevel.Alarm => MessageBeep(MbIconHand) & MessageBeep(MbIconHand) & MessageBeep(MbIconHand),
            _ => false,
        };
    }

    /// <summary>清空计数（自检与探针在每个用例前调用）。</summary>
    public static void ResetCounters()
    {
        PlayCount = 0;
        LastLevel = null;
        _byLevel.Clear();
    }
}
