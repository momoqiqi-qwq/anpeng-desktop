using System.Reflection;

namespace Anpeng.Desktop.Services;

/// <summary>
/// 构建与阈值快照信息。parity JSON、自检日志与「关于」页共用同一份口径，
/// 避免三处各写一套版本号/阈值而互相漂移。
/// </summary>
public static class BuildInfo
{
    /// <summary>应用版本（取程序集 InformationalVersion，回退 AssemblyVersion）。</summary>
    public static string Version { get; } = ResolveVersion();

    /// <summary>主程序集文件时间（近似构建时间）。</summary>
    public static string BuiltAt { get; } = ResolveBuildTime();

    private static string ResolveVersion()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info)) return info;
            return asm.GetName().Version?.ToString() ?? "1.0.0";
        }
        catch
        {
            return "1.0.0";
        }
    }

    private static string ResolveBuildTime()
    {
        try
        {
            var path = Assembly.GetExecutingAssembly().Location;
            return string.IsNullOrEmpty(path) || !File.Exists(path)
                ? "unknown"
                : File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm:ss");
        }
        catch
        {
            return "unknown";
        }
    }

    /// <summary>
    /// 判据阈值的**实际生效值**快照。字段名与 Android 侧 parity JSON 逐字对齐，供逐字段 diff。
    /// </summary>
    public static object ThresholdsJson(AlarmThresholds th) => new
    {
        smokeWarn = th.SmokeNoticePpm,
        tempWarn = th.TempNoticeC,
        tempAlarm = th.TempAlarmC,
        riseRate = th.TempRiseCPerMinute,
        confirmSeconds = th.ConfirmSeconds,
        samplePeriod = th.SampleSeconds,
        bothSustainSeconds = AlarmEvaluator.SprayGateSeconds,
        riseWindowMinMs = (long)(AlarmEvaluator.RiseWindowMinSeconds * 1000),
        riseWindowMinSeconds = AlarmEvaluator.RiseWindowMinSeconds,
        windowSpanMs = AlarmEvaluator.RiseWindowSeconds * 1000L,
        windowSpanSeconds = AlarmEvaluator.RiseWindowSeconds,
        windowMaxSize = AlarmEvaluator.HistoryMaxSize,
        // APP-SPEC §4.2.1 第 4 条：两端必须同时导出时间戳跳变护栏的两个键。
        maxSampleGapMs = AlarmEvaluator.MaxSampleGapMs,
        maxSampleGapSeconds = AlarmEvaluator.MaxSampleGapSeconds,
        unit = new
        {
            smoke = "ppm",
            temp = "°C",
            rise = "°C/min",
            seconds = "s",
        },
    };
}
