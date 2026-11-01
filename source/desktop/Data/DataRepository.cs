using System.Text.Json;
using System.Text.Json.Serialization;
using Anpeng.Desktop.Services;

namespace Anpeng.Desktop.Data;

/// <summary>应用设置（阈值 + 开关），持久化到 %LOCALAPPDATA%\安棚\settings.json。</summary>
public sealed class AppSettings
{
    // ── 阈值（APP-SPEC 4.4） ──
    public double SmokeNoticePpm { get; set; } = 800;
    public double TempNoticeC { get; set; } = 55.0;
    public double TempAlarmC { get; set; } = 70.0;
    public double TempRiseCPerMinute { get; set; } = 5.0;
    public double ConfirmSeconds { get; set; } = 30;
    public double SampleSeconds { get; set; } = 2;

    // ── 开关（DEF-D4：每个开关都必须有真实读取点，否则界面不得声称它会做事）──
    // 写入点统一在 Pages\SettingsPage.xaml.cs 的 ToggleRow 回调里；读取点如下：

    /// <summary>报警声音提示。读取点：Services\MonitoringService.cs 的 RaiseAlarm —— <c>AlarmPolicy.ShouldPlaySound</c>。</summary>
    public bool AlarmSoundEnabled { get; set; } = true;

    /// <summary>确认火警自动开阀喷淋。读取点：Services\MonitoringService.cs 的 Tick。</summary>
    public bool AutoSprinklerEnabled { get; set; } = true;

    /// <summary>
    /// 远程推送棚主。**当前不生效**：按契约 R4，本监控台完全本地运行、不联网，
    /// 推送由现场 ESP-01 → OneNet 链路承担。保留此开关只为记录实机部署意图，
    /// 界面上必须标为「预留项，当前不生效」，任何位置都不得声称本机已推送。
    /// </summary>
    public bool PushEnabled { get; set; } = true;

    /// <summary>实时轮询。读取点：RuntimeState.StartTicking / StopTicking。</summary>
    public bool RealtimeRunning { get; set; } = true;

    // ── 数据与账号（本地闭环，不上云） ──
    public string SiteName { get; set; } = "安棚示范场（一队养殖小区）";
    public string Operator { get; set; } = "李秀云（运维）";
    public string CloudNote { get; set; } = "本地运行中：检测、判断、报警、喷淋全部本地闭环，断网不影响";

    public AlarmThresholds ToThresholds() => new()
    {
        SmokeNoticePpm = SmokeNoticePpm,
        TempNoticeC = TempNoticeC,
        TempAlarmC = TempAlarmC,
        TempRiseCPerMinute = TempRiseCPerMinute,
        ConfirmSeconds = ConfirmSeconds,
        SampleSeconds = SampleSeconds,
    };

    public const string FileName = "settings.json";
}

/// <summary>%LOCALAPPDATA%\安棚 下的数据仓库（JSON 落盘 + 首次自带演示数据）。</summary>
public sealed class DataRepository
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string Directory { get; }
    public string LogPath => Path.Combine(Directory, "运行日志.txt");

    public AppSettings Settings { get; private set; } = new();
    public DemoDataSet Data { get; private set; } = new();

    /// <summary>本次是否由首次启动生成演示数据。</summary>
    public bool SeededThisRun { get; private set; }

    public DataRepository(string? directory = null)
    {
        Directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "安棚");
        System.IO.Directory.CreateDirectory(Directory);
    }

    public void Load()
    {
        Settings = LoadJson<AppSettings>(Path.Combine(Directory, AppSettings.FileName)) ?? new AppSettings();
        var data = LoadJson<DemoDataSet>(Path.Combine(Directory, "data.json"));
        if (data is null || data.Barns.Count < 4 || data.Alarms.Count < 30 ||
            data.Sprinklers.Count < 20 || data.Readings.Count < 200)
        {
            Data = DemoData.Build();
            SeededThisRun = true;
            SaveAll();
        }
        else
        {
            Data = data;
            SeededThisRun = false;
        }
    }

    public void SaveAll()
    {
        SaveJson(Path.Combine(Directory, AppSettings.FileName), Settings);
        SaveJson(Path.Combine(Directory, "data.json"), Data);
    }

    public void SaveSettings() => SaveJson(Path.Combine(Directory, AppSettings.FileName), Settings);

    public void SaveData() => SaveJson(Path.Combine(Directory, "data.json"), Data);

    public void ResetToDemo()
    {
        Data = DemoData.Build();
        SeededThisRun = true;
        SaveAll();
    }

    private static T? LoadJson<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"读取 {Path.GetFileName(path)} 失败，将用演示数据重建：{ex.Message}");
            return null;
        }
    }

    private static void SaveJson<T>(string path, T value)
    {
        try
        {
            var tmp = path + ".tmp";
            // 直接写入 UTF-8 流，避免数据量增大时分配完整的 JSON 字符串。
            // 必须先关闭临时文件，再替换目标文件（Windows 不允许移动仍打开的文件）。
            using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value, Options);
            }
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            AppLog.Error("写入数据失败", ex);
        }
    }
}
