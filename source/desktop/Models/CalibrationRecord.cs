namespace Anpeng.Desktop.Models;

/// <summary>传感器校准记录。</summary>
public sealed class CalibrationRecord
{
    public string Id { get; set; } = "";
    public string BarnId { get; set; } = "";
    public string BarnName { get; set; } = "";
    public string Device { get; set; } = "";
    public DateTime Time { get; set; }

    /// <summary>本机读数（校准前）。</summary>
    public double RawValue { get; set; }

    /// <summary>标准器具读数。</summary>
    public double ReferenceValue { get; set; }

    public string Unit { get; set; } = "";

    /// <summary>写入节点的修正量（参考值 − 本机读数）。</summary>
    public double Offset { get; set; }

    public string Operator { get; set; } = "";
    public string Method { get; set; } = "";
    public string Result { get; set; } = "合格";

    public string TimeText => Time.ToString("yyyy-MM-dd");

    public string ReadingText => $"{RawValue:0.#} → {ReferenceValue:0.#} {Unit}";

    public string OffsetText => Offset == 0 ? "0" : $"{(Offset > 0 ? "+" : "")}{Offset:0.##} {Unit}";
}

/// <summary>喷头巡检记录。</summary>
public sealed class NozzleInspectionRecord
{
    public string Id { get; set; } = "";
    public string BarnId { get; set; } = "";
    public string BarnName { get; set; } = "";
    public DateTime Time { get; set; }
    public string NozzleCode { get; set; } = "";
    public NozzleState State { get; set; } = NozzleState.Normal;
    public string WaterPressure { get; set; } = "";
    public string Inspector { get; set; } = "";
    public string Note { get; set; } = "";

    public string TimeText => Time.ToString("yyyy-MM-dd");

    public string StateName => NozzleStateInfo.Name(State);
}
