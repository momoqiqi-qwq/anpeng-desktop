namespace Anpeng.Desktop.Models;

/// <summary>一次采样（APP-SPEC 4.3 数据点）。</summary>
public sealed class SensorReading
{
    public string Id { get; set; } = "";
    public string BarnId { get; set; } = "";
    public DateTime Time { get; set; }

    /// <summary>MQ-2 烟雾浓度，ppm 等效。</summary>
    public double SmokePpm { get; set; }

    /// <summary>PT100 + MAX31865 温度，°C（一位小数）。</summary>
    public double TempC { get; set; }

    /// <summary>节点供电电压，V。</summary>
    public double Voltage { get; set; } = 5.0;

    /// <summary>节点是否在线（ESP-01 心跳）。</summary>
    public bool Online { get; set; } = true;

    /// <summary>阀门状态（继电器回读）。</summary>
    public ValveState Valve { get; set; } = ValveState.Closed;

    public string TimeText => Time.ToString("MM-dd HH:mm:ss");

    public string SmokeText => $"{SmokePpm:0} ppm";

    public string TempText => $"{TempC:0.0} °C";
}
