namespace Anpeng.Desktop.Models;

/// <summary>喷淋（开阀放水）记录。</summary>
public sealed class SprinklerRecord
{
    public string Id { get; set; } = "";
    public string BarnId { get; set; } = "";
    public string BarnName { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }

    /// <summary>喷淋时长，s。</summary>
    public double DurationSeconds { get; set; }

    /// <summary>喷淋原因（中文语境）。</summary>
    public string Reason { get; set; } = "";

    public AlarmLevel TriggerLevel { get; set; } = AlarmLevel.Alarm;

    /// <summary>本次喷淋让节点温度回落，°C。</summary>
    public double TempDrop { get; set; }

    /// <summary>操作方式：自动 / 手动。</summary>
    public string Mode { get; set; } = "自动";

    public string Operator { get; set; } = "系统自动";

    /// <summary>喷淋后复核结论。</summary>
    public string Outcome { get; set; } = "";

    public string StartText => StartTime.ToString("yyyy-MM-dd HH:mm:ss");

    public string DurationText
    {
        get
        {
            var total = (int)Math.Round(DurationSeconds);
            var m = total / 60;
            var s = total % 60;
            return m > 0 ? $"{m} 分 {s:00} 秒" : $"{s} 秒";
        }
    }

    public string LevelName => AlarmLevelInfo.Name(TriggerLevel);

    public string TempDropText => TempDrop > 0 ? $"回落 {TempDrop:0.0} °C" : "—";
}
