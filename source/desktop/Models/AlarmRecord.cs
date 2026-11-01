namespace Anpeng.Desktop.Models;

/// <summary>报警记录（含「触发 → 判断 → 动作」链路）。</summary>
public sealed class AlarmRecord
{
    public string Id { get; set; } = "";
    public string BarnId { get; set; } = "";
    public string BarnName { get; set; } = "";
    public DateTime Time { get; set; }
    public AlarmLevel Level { get; set; }

    /// <summary>触发值快照，例：烟雾 812 ppm / 温度 62.4 °C。</summary>
    public double SmokePpm { get; set; }
    public double TempC { get; set; }

    /// <summary>温升速率，°C/min（滑动窗口）。</summary>
    public double TempRisePerMin { get; set; }

    /// <summary>连续超限时长，s。</summary>
    public double OverLimitSeconds { get; set; }

    /// <summary>触发原因（中文，草料棚语境）。</summary>
    public string Trigger { get; set; } = "";

    /// <summary>是否执行了喷淋。</summary>
    public bool SprinklerFired { get; set; }

    /// <summary>处置动作描述。</summary>
    public string Action { get; set; } = "";

    /// <summary>处置人。</summary>
    public string Handler { get; set; } = "";

    public DateTime? HandledAt { get; set; }

    public string Remark { get; set; } = "";

    public string RobotTip => Trigger;

    public string TimeText => Time.ToString("yyyy-MM-dd HH:mm:ss");

    public string LevelName => AlarmLevelInfo.Name(Level);

    public string SprinklerText => SprinklerFired ? "已喷淋" : "未喷淋";

    // ── 展示小数位：由判定引擎在产生结果时自适应选定（见 ValueDisplay / APP-SPEC §4.2.1 第 1 条）──
    // 记录必须沿用引擎选定的位数，否则详情面板会把「799.6 ppm 未超限」显示成「800 ppm 已超限」。

    public int SmokeDecimals { get; set; }

    public int TempDecimals { get; set; } = 1;

    public int RiseDecimals { get; set; } = 2;

    public int OverLimitDecimals { get; set; } = 1;

    public string SmokeText => ValueDisplay.Text(SmokePpm, SmokeDecimals) + " ppm";

    public string TempText => ValueDisplay.Text(TempC, TempDecimals) + " °C";

    public string RiseText => ValueDisplay.Text(TempRisePerMin, RiseDecimals) + " °C/min";

    public string OverLimitText => ValueDisplay.Text(OverLimitSeconds, OverLimitDecimals) + " s";
}
