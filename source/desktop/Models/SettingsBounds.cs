namespace Anpeng.Desktop.Models;

/// <summary>
/// 设置页输入的**合法性边界**（D-7）。
///
/// 这些值只约束用户在设置页里能填什么，**不驱动任何判定**——
/// 引擎的防误喷门槛是 <see cref="Services.AlarmEvaluator.SprayGateSeconds"/>（4.0 s，独立常量，
/// 刻意不从采样周期派生，否则把采样周期改成 3 s 就会把门槛悄悄抬到 6 s）。
/// 之所以单独成类并配公开文案，是为了让界面把「输入下限」与「引擎门槛」讲清楚，
/// 避免评委把 2 个采样周期误读成判据门槛。
/// </summary>
public static class SettingsBounds
{
    /// <summary>「连续超限确认时长」输入框至少要覆盖的采样周期数（输入下限，非判据门槛）。</summary>
    public const double MinConfirmCycles = 2.0;

    /// <summary>「连续超限确认时长」输入框的绝对下限（秒）。</summary>
    public const double MinConfirmSeconds = 1.0;

    /// <summary>设置页上必须逐字出现这句说明，否则用户会把输入下限当成引擎门槛。</summary>
    public const string ConfirmSecondsHint =
        "输入下限 2 个采样周期：这是输入合法性钳制，不是引擎门槛。"
        + "引擎的防误喷门槛是独立常量 SprayGateSeconds = 4.0 s，改动采样周期不会改变它。";
}
