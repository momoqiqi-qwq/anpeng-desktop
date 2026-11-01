namespace Anpeng.Desktop.Services;

/// <summary>
/// 演示用设备时钟。实时轮询按采样周期推进，一次 tick = 一个采样周期（默认 2 s）。
/// </summary>
public sealed class DeviceClock
{
    public DateTime Now { get; private set; }

    public double SampleSeconds { get; set; } = 2.0;

    public int Tick { get; private set; }

    public DeviceClock(DateTime start, double sampleSeconds = 2.0)
    {
        Now = start;
        SampleSeconds = sampleSeconds;
        Tick = 0;
    }

    public TimeSpan SampleDuration => TimeSpan.FromSeconds(SampleSeconds);

    public DateTime Advance()
    {
        Tick++;
        Now = Now.Add(SampleDuration);
        return Now;
    }

    public void Reset(DateTime now)
    {
        Now = now;
        Tick = 0;
    }
}
