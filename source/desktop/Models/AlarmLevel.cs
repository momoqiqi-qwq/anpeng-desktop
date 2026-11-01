using Anpeng.Desktop.Services;

namespace Anpeng.Desktop.Models;

/// <summary>三级分级判断 + 正常/离线，共 5 态（APP-SPEC 3.2 三级告警色）。</summary>
public enum AlarmLevel
{
    Offline = 0,
    Normal = 1,
    Notice = 2,
    Suspect = 3,
    Alarm = 4,
}

public static class AlarmLevelInfo
{
    public static string Name(AlarmLevel level) => level switch
    {
        AlarmLevel.Normal => "正常",
        AlarmLevel.Notice => "环境预警",
        AlarmLevel.Suspect => "疑似火情",
        AlarmLevel.Alarm => "确认火警",
        _ => "离线",
    };

    /// <summary>契约 4.2 的「动作」列文案。</summary>
    public static string Action(AlarmLevel level) => level switch
    {
        AlarmLevel.Normal => "无需动作，继续采样",
        AlarmLevel.Notice => "蜂鸣器短促单响；界面黄色提示「建议检查」；不喷淋",
        AlarmLevel.Suspect => "蜂鸣器连续双响；界面橙色报警；推送棚主；不喷淋",
        AlarmLevel.Alarm => "蜂鸣器长鸣；开阀喷淋；远程推送；写入处置记录",
        _ => "节点失联，维持阀门关闭并派人核查",
    };

    /// <summary>
    /// 契约 4.2 三级判定的「判据」文案（详情面板里与动作列成对展示）。
    ///
    /// DEF-D5：文案里的数字**全部从判定引擎的真实常量与当前阈值格式化而来**，一个都不硬编码。
    /// 改 <see cref="AlarmEvaluator.SprayGateSeconds"/> 或改设置页阈值后，界面承诺自动跟着变，
    /// 不会再出现「界面说 ≥ 2 个采样周期、引擎实际用 4.0 s」这类「界面说假话」。
    /// </summary>
    public static string Criteria(AlarmLevel level, AlarmThresholds? thresholds = null)
    {
        var th = thresholds ?? new AlarmThresholds();
        var gate = ValueDisplay.Text(AlarmEvaluator.SprayGateSeconds, 1);
        return level switch
        {
            AlarmLevel.Normal => "烟雾与温度均在预警阈值内，未触发任何分级",
            AlarmLevel.Notice =>
                $"烟雾或温度中任一项单独超限（另一项未超限）：烟雾 ≥ {ValueDisplay.Text(th.SmokeNoticePpm, 0)} ppm 或温度 ≥ {ValueDisplay.Text(th.TempNoticeC, 1)} °C，按环境预警处理，不喷淋",
            AlarmLevel.Suspect =>
                $"烟雾与温度两项同时超限，但「两项同时连续超限」尚不足防误喷连续性门槛 {gate} s，或三项确认条件均未命中，按疑似火情处理，只报警不喷淋",
            AlarmLevel.Alarm =>
                $"两项同时连续超限 ≥ {gate} s，且满足三选一：持续时长 ≥ {ValueDisplay.Text(th.ConfirmSeconds, 0)} s、温升速率 ≥ {ValueDisplay.Text(th.TempRiseCPerMinute, 1)} °C/min、温度 ≥ {ValueDisplay.Text(th.TempAlarmC, 1)} °C（三项一律按原始读数比较，不使用展示取整值）",
            _ => "节点心跳超时，本次采样不具备判断条件",
        };
    }

    /// <summary>对应 AnpengTokens.xaml 里的告警色画刷键。</summary>
    public static string BrushKey(AlarmLevel level) => level switch
    {
        AlarmLevel.Normal => "LevelNormalBrush",
        AlarmLevel.Notice => "LevelNoticeBrush",
        AlarmLevel.Suspect => "LevelSuspectBrush",
        AlarmLevel.Alarm => "LevelAlarmBrush",
        _ => "LevelOfflineBrush",
    };

    public static string SoftBrushKey(AlarmLevel level) => level switch
    {
        AlarmLevel.Normal => "LevelNormalSoftBrush",
        AlarmLevel.Notice => "LevelNoticeSoftBrush",
        AlarmLevel.Suspect => "LevelSuspectSoftBrush",
        AlarmLevel.Alarm => "LevelAlarmSoftBrush",
        _ => "LevelOfflineSoftBrush",
    };

    public static int Rank(AlarmLevel level) => (int)level;

    /// <summary>告警色的实际色值（供 XAML 之外需要直接绘制的控件使用，与令牌表保持一致）。</summary>
    public static Windows.UI.Color BrushColor(AlarmLevel level) => level switch
    {
        AlarmLevel.Normal => Windows.UI.Color.FromArgb(0xFF, 0x2E, 0x9E, 0x5B),
        AlarmLevel.Notice => Windows.UI.Color.FromArgb(0xFF, 0xC7, 0x9A, 0x00),
        AlarmLevel.Suspect => Windows.UI.Color.FromArgb(0xFF, 0xE8, 0x76, 0x0F),
        AlarmLevel.Alarm => Windows.UI.Color.FromArgb(0xFF, 0xD9, 0x30, 0x25),
        _ => Windows.UI.Color.FromArgb(0xFF, 0x8A, 0x8A, 0x8A),
    };
}
