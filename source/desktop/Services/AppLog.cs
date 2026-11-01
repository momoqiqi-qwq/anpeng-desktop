namespace Anpeng.Desktop.Services;

/// <summary>运行期日志（同时写 %LOCALAPPDATA%\安棚\运行日志.txt）。用于证明无未捕获异常。</summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static string? _path;

    public static void Attach(string path) => _path = path;

    public static void Info(string message) => Write("INFO ", message);

    public static void Warn(string message) => Write("WARN ", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message} :: {ex.GetType().Name}: {ex.Message}");

    public static void Unhandled(string source, Exception ex) =>
        Write("FATAL", $"[{source}] {ex.GetType().FullName}: {ex.Message}\r\n{ex.StackTrace}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}";
        try
        {
            System.Diagnostics.Debug.WriteLine(line);
            if (_path is null) return;
            lock (Gate)
            {
                File.AppendAllText(_path, line + Environment.NewLine);
            }
        }
        catch
        {
            // 日志失败不得影响主流程
        }
    }
}
