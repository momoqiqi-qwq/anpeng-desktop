using Anpeng.Desktop.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Anpeng.Desktop;

/// <summary>
/// 导航目标解析（页面 Tag 字符串 → 页面类型），集中一处避免各页重复。
/// 按契约 §5，导航共 **8 项**：前 7 项是产品功能页，第 8 项「判据自检」是诊断页（不是产品功能页），
/// 因此它排在最后、标签里必须带「自检」二字，避免与产品页混同。
/// </summary>
public static class NavMap
{
    public static readonly string[] Order =
    {
        "overview", "realtime", "alarms", "sprinkler", "devices", "reports", "settings", "debug",
    };

    public static readonly string[] Titles =
    {
        "总览", "实时监测", "报警记录", "喷淋控制", "设备管理", "报表统计", "设置", "判据自检",
    };

    public static Type? Resolve(string? tag) => tag switch
    {
        "overview" => typeof(Pages.OverviewPage),
        "realtime" => typeof(Pages.RealtimePage),
        "alarms" => typeof(Pages.AlarmRecordsPage),
        "sprinkler" => typeof(Pages.SprinklerControlPage),
        "devices" => typeof(Pages.DeviceManagementPage),
        "reports" => typeof(Pages.ReportsPage),
        "settings" => typeof(Pages.SettingsPage),
        "debug" => typeof(Pages.DebugPage),
        _ => null,
    };

    public static string Title(string tag)
    {
        var index = Array.IndexOf(Order, tag);
        return index >= 0 ? Titles[index] : tag;
    }
}

/// <summary>应用入口。支持 --page / --selftest / --selftest-real / --exit-after 命令行开关。</summary>
public partial class App : Application
{
    public static Window? MainWindow { get; private set; }

    /// <summary>启动时要直接落到的页面 Tag（对应 NavigationView 菜单的 Tag）。</summary>
    public static string? StartupPageTag { get; private set; }

    /// <summary>命令行 --theme 指定的主题覆盖；null = 跟随系统。</summary>
    public static ElementTheme? ThemeOverride { get; private set; }

    /// <summary>命令行 --barn 指定的启动棚舍 Id；null = 用数据集里的第一个棚。</summary>
    public static string? StartupBarnId { get; private set; }

    /// <summary>命令行 --size WxH 指定的窗口尺寸；null = 按显示器工作区自适应。</summary>
    public static Windows.Graphics.SizeInt32? StartupSize { get; private set; }

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            AppLog.Unhandled("AppDomain", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString() ?? "unknown"));
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.Unhandled("TaskScheduler", e.Exception);
            e.SetObserved();
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        RuntimeState.Initialize();
        AppLog.Info("安棚草料棚火灾预警监控台启动");

        var argv = Environment.GetCommandLineArgs();
        var selftestChain = HasFlag(argv, "--selftest") || HasFlag(argv, "--chain");
        var selftestReal = HasFlag(argv, "--selftest-real");
        var page = ValueOf(argv, "--page");
        if (page is not null) StartupPageTag = page;
        var exitAfter = ValueOf(argv, "--exit-after");
        var scrollTo = ValueOf(argv, "--scroll");
        StartupBarnId = ValueOf(argv, "--barn");

        // --size 1440x1180：截图取证时用来保证整页内容不被窗口底边截断
        if (ValueOf(argv, "--size") is { } sizeText)
        {
            var parts = sizeText.Split('x', 'X', ',');
            if (parts.Length == 2
                && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h)
                && w >= 800 && h >= 600)
            {
                StartupSize = new Windows.Graphics.SizeInt32(w, h);
            }
            else
            {
                AppLog.Warn($"[--size] 无法解析 \"{sizeText}\"，应为 WxH（例如 1480x1180）");
            }
        }

        ThemeOverride = ValueOf(argv, "--theme")?.ToLowerInvariant() switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            "system" or null or "" => null,
            _ => null,
        };

        if (HasFlag(argv, "--reset-demo"))
        {
            RuntimeState.Repo.ResetToDemo();
            AppLog.Warn("[--reset-demo] 已按契约重建演示数据集");
            if (!HasFlag(argv, "--keep-open"))
            {
                Console.Out.WriteLine("已按契约重建演示数据集：" + RuntimeState.Repo.Directory);
                Environment.Exit(0);
                return;
            }
        }

        // --parity <path>：写出跨端判据 parity 证据（10 条固定命名用例），不建窗口
        if (ValueOf(argv, "--parity") is { Length: > 0 } parityPath)
        {
            try
            {
                var json = ParityCases.ToJson(RuntimeState.Repo.Settings.ToThresholds());
                var full = Path.GetFullPath(parityPath);
                Directory.CreateDirectory(Path.GetDirectoryName(full) ?? ".");
                File.WriteAllText(full, json, new System.Text.UTF8Encoding(false));
                var cases = ParityCases.Run(RuntimeState.Repo.Settings.ToThresholds());
                AppLog.Info($"[--parity] 已写出 {full}（{cases.Count(c => c.Pass)}/{cases.Count} 项符合期望）");
                foreach (var line in ParityCases.Describe(RuntimeState.Repo.Settings.ToThresholds()))
                    Console.Out.WriteLine(line);
                Environment.Exit(cases.All(c => c.Pass) ? 0 : 1);
            }
            catch (Exception ex)
            {
                AppLog.Error("[--parity] 写出失败", ex);
                Console.Error.WriteLine("[--parity] 写出失败：" + ex.Message);
                Environment.Exit(2);
            }
            return;
        }

        if (selftestChain || selftestReal)
        {
            var ok = SelfTest.Run(selftestChain, selftestReal);
            Environment.Exit(ok ? 0 : 1);
            return;
        }

        var shell = new MainWindow();
        MainWindow = shell;
        if (ThemeOverride is { } theme && shell.Content is FrameworkElement root)
        {
            root.RequestedTheme = theme;
            AppLog.Info($"[--theme] 强制主题：{theme}");
        }
        shell.Activate();
        shell.DispatcherQueue.TryEnqueue(shell.NavigateToStartupPage);

        if (string.Equals(scrollTo, "bottom", StringComparison.OrdinalIgnoreCase))
        {
            // 首屏渲染与页内布局要跑完才有可滚高度，所以延迟再滚。
            // 页面在每次采样周期（默认 2 s）会重建内容，滚动位置可能被重新布局带回去，
            // 因此这里反复滚若干次，直到截图时刻仍停在底部。
            var remaining = 14;
            var scrollTimer = shell.DispatcherQueue.CreateTimer();
            scrollTimer.Interval = TimeSpan.FromMilliseconds(700);
            scrollTimer.Tick += (_, _) =>
            {
                if (--remaining <= 0)
                {
                    scrollTimer.Stop();
                }

                shell.ScrollCurrentPageToBottom();
            };
            scrollTimer.Start();
        }

        if (int.TryParse(exitAfter, out var seconds) && seconds > 0)
        {
            var timer = shell.DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromSeconds(seconds);
            timer.Tick += (_, _) =>
            {
                AppLog.Info($"[--exit-after] 已运行 {seconds} s，正常退出");
                MainWindow?.Close();
            };
            timer.Start();
        }
    }

    private static bool HasFlag(string[] argv, string flag) =>
        argv.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

    private static string? ValueOf(string[] argv, string key)
    {
        for (var i = 0; i < argv.Length; i++)
        {
            if (string.Equals(argv[i], key, StringComparison.OrdinalIgnoreCase))
            {
                return i + 1 < argv.Length ? argv[i + 1] : null;
            }
            if (argv[i].StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
            {
                return argv[i][(key.Length + 1)..];
            }
        }
        return null;
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        AppLog.Unhandled("XamlApp", e.Exception);
        // 运行期异常写入日志后保持界面可用，避免直接把窗口打崩
        e.Handled = true;
    }

    /// <summary>被 MainWindow 关闭时调用，落盘并写退出日志。</summary>
    public static void OnClosing()
    {
        RuntimeState.Shutdown();
    }
}
