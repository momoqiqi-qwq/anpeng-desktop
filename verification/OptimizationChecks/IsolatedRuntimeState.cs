using Anpeng.Desktop.Data;
using Anpeng.Desktop.Services;

namespace Anpeng.Desktop;

// Test fixture only: no WinUI dispatchers and no default user-directory initialization.
internal static class RuntimeState
{
    private static DataRepository? _repo;
    private static MonitoringService? _monitor;

    public static DataRepository Repo => _repo ?? throw new InvalidOperationException("Isolated runtime is not configured");

    public static MonitoringService Monitor
    {
        get
        {
            // The existing buzzer test restores Sink to null; reinstate a silent sink before later chains.
            AlarmBeeper.Sink ??= _ => true;
            return _monitor ?? throw new InvalidOperationException("Isolated runtime is not configured");
        }
    }

    public static void Configure(string directory)
    {
        _repo = new DataRepository(directory);
        _repo.Load();
        _monitor = new MonitoringService(_repo);
    }
}
