namespace Anpeng.Desktop.Models;

/// <summary>硬件节点类型（APP-SPEC 4.1 链路）。</summary>
public enum NodeKind
{
    SmokeGas,
    Temperature,
    Valve,
    Gateway,
}

public sealed class DeviceNode
{
    public string Id { get; set; } = "";
    public string BarnId { get; set; } = "";
    public string BarnName { get; set; } = "";
    public NodeKind Kind { get; set; }

    /// <summary>型号：MQ-2 / PT100+MAX31865 / DC12V 常闭式电磁阀 / ESP-01。</summary>
    public string Model { get; set; } = "";

    /// <summary>安装位置（棚内高度、朝向）。</summary>
    public string MountPosition { get; set; } = "";

    public string FirmwareVersion { get; set; } = "";

    /// <summary>信号强度，dBm。</summary>
    public int SignalDbm { get; set; } = -58;

    public bool Online { get; set; } = true;

    public DateTime LastHeartbeat { get; set; }

    public string KindName => Kind switch
    {
        NodeKind.SmokeGas => "烟雾/可燃气体",
        NodeKind.Temperature => "温度采集",
        NodeKind.Valve => "电磁阀",
        _ => "网关",
    };

    public string Glyph => Kind switch
    {
        NodeKind.SmokeGas => "\uE7BA",
        NodeKind.Temperature => "\uE7A7",
        NodeKind.Valve => "\uE945",
        _ => "\uE701",
    };

    public string StatusName => Online ? "在线" : "离线";

    public string SignalText => Online ? $"{SignalDbm} dBm" : "—";
}
