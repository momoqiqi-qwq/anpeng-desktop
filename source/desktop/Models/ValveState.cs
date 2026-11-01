namespace Anpeng.Desktop.Models;

/// <summary>DC12V 常闭式电磁阀状态（断电即关）。</summary>
public enum ValveState
{
    Closed = 0,
    Open = 1,
    Fault = 2,
}

public static class ValveStateInfo
{
    public static string Name(ValveState state) => state switch
    {
        ValveState.Open => "开",
        ValveState.Fault => "故障",
        _ => "关",
    };
}

/// <summary>喷头状态（巡检录入）。</summary>
public enum NozzleState
{
    Normal = 0,
    Blocked = 1,
    Unknown = 2,
}

public static class NozzleStateInfo
{
    public static string Name(NozzleState state) => state switch
    {
        NozzleState.Normal => "正常",
        NozzleState.Blocked => "堵",
        _ => "未知",
    };
}
