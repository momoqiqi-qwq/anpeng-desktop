using Anpeng.Desktop.Data;
using Anpeng.Desktop.Services;
using Microsoft.UI.Dispatching;

namespace Anpeng.Desktop;

/// <summary>安棚监控台运行时状态：数据仓库、监控服务、实时轮询节拍。</summary>
public static class RuntimeState
{
    private static DataRepository? _repo;
    private static MonitoringService? _monitor;
    private static DispatcherQueueTimer? _timer;
    private static DispatcherQueue? _queue;

    public static DataRepository Repo => _repo ??= Init();
    public static MonitoringService Monitor => _monitor ??= new MonitoringService(Repo);

    /// <summary>启动前显式初始化（避免首帧在页面构造函数里同步读盘）。</summary>
    public static void Initialize()
    {
        _ = Repo;
        _ = Monitor;
    }

    private static DataRepository Init()
    {
        var repo = new DataRepository();
        repo.Load();
        AppLog.Attach(repo.LogPath);
        AppLog.Info($"数据目录：{repo.Directory}");
        return repo;
    }

    public static bool IsTicking => _timer?.IsRunning ?? false;

    public static void StartTicking(DispatcherQueue queue)
    {
        _queue = queue;
        _timer ??= queue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(Math.Max(1, Repo.Settings.SampleSeconds));
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
        Repo.Settings.RealtimeRunning = true;
        Monitor.Start();
        AppLog.Info($"实时监测已启动，采样周期 {Repo.Settings.SampleSeconds:0.0} s");
    }

    public static void StopTicking()
    {
        if (_timer is not null) _timer.Stop();
        Monitor.Stop();
        Repo.Settings.RealtimeRunning = false;
        Repo.SaveSettings();
    }

    private static void OnTick(DispatcherQueueTimer sender, object args)
    {
        try
        {
            Monitor.Tick();
        }
        catch (Exception ex)
        {
            AppLog.Error("实时轮询异常", ex);
        }
    }

    public static void Shutdown()
    {
        try
        {
            if (_timer is not null) _timer.Stop();
            Monitor.Save();
            AppLog.Info("安棚监控台正常退出");
        }
        catch (Exception ex)
        {
            AppLog.Error("退出清理异常", ex);
        }
    }
}
