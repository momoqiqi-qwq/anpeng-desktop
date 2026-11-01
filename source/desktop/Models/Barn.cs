namespace Anpeng.Desktop.Models;

/// <summary>草料棚 / 秸秆堆放棚。</summary>
public sealed class Barn
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>所在位置（例：一队村东）。</summary>
    public string Location { get; set; } = "";

    /// <summary>建筑面积，㎡。</summary>
    public double Area { get; set; }

    /// <summary>耐火等级 / 结构说明（土坯墙 + 彩钢顶 等）。</summary>
    public string Structure { get; set; } = "";

    /// <summary>堆存量，吨（草捆 / 秸秆）。</summary>
    public double StockTon { get; set; }

    /// <summary>棚主。</summary>
    public string Owner { get; set; } = "";

    public string OwnerPhone { get; set; } = "";

    /// <summary>最近一次巡检日期。</summary>
    public DateTime LastInspection { get; set; }

    /// <summary>联动喷淋覆盖情况描述。</summary>
    public string SprinklerCoverage { get; set; } = "";

    public bool Enabled { get; set; } = true;

    /// <summary>该棚当前最高级别（由监控服务刷新）。</summary>
    public AlarmLevel CurrentLevel { get; set; } = AlarmLevel.Normal;

    /// <summary>管网水压，MPa。</summary>
    public double WaterPressure { get; set; } = 0.32;

    public string LevelName => AlarmLevelInfo.Name(CurrentLevel);
}
