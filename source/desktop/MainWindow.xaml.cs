using Anpeng.Desktop.Models;
using Anpeng.Desktop.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Windows.Graphics;
using Windows.UI;
namespace Anpeng.Desktop;

/// <summary>主窗口外壳：自定义标题栏（安棚矢量标志）+ NavigationView + Frame。</summary>
public sealed partial class MainWindow : Window
{
    private Type? _currentPageType;

    public MainWindow()
    {
        InitializeComponent();
        Title = "安棚 1.0.0 · 草料棚火灾预警监控台";

        ConfigureWindowing();
        ApplyChromeTheme();

        NavView.SelectedItem = NavView.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(i => (string?)i.Tag == "overview");

        RuntimeState.Monitor.Changed += OnMonitorChanged;
        RuntimeState.StartTicking(DispatcherQueue);
        RefreshChips();

        Closed += (_, _) =>
        {
            RuntimeState.Monitor.Changed -= OnMonitorChanged;
            App.OnClosing();
        };
    }

    // ─────────────────────────── 窗口与标题栏 ───────────────────────────

    private void ConfigureWindowing()
    {
        try
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            // 标题栏永远是品牌蓝底，内置 Title/Subtitle 文字按深色元素主题取白色，
            // 不受应用整体亮/暗主题影响。
            AppTitleBar.RequestedTheme = ElementTheme.Dark;
        }
        catch (Exception ex)
        {
            AppLog.Error("启用自定义标题栏失败", ex);
        }

        try
        {
            AppWindow.Title = "安棚 1.0.0 · 草料棚火灾预警监控台";

            // 默认按显示器工作区给足高度，避免棚舍卡片墙/报警列表在纵向被窗口底边截断。
            // --size WxH 可显式覆盖（截图取证时用来保证整页内容完整可读）。
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            var width = App.StartupSize?.Width ?? Math.Clamp(area.Width - 80, 1000, 1920);
            var height = App.StartupSize?.Height ?? Math.Clamp(area.Height - 70, 700, 1500);
            AppWindow.Resize(new SizeInt32(width, height));
            AppLog.Info($"窗口尺寸 {width}×{height}（工作区 {area.Width}×{area.Height}）");

            var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(icon)) AppWindow.SetIcon(icon);
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"窗口初始化降级：{ex.Message}");
        }
    }

    /// <summary>标题栏在品牌蓝底上用白字，最大化按钮区跟随亮/暗主题。</summary>
    private void ApplyChromeTheme()
    {
        try
        {
            var bar = AppWindow.TitleBar;
            if (bar is null) return;
            var background = Color.FromArgb(0xFF, 0x14, 0x53, 0x8C);
            bar.BackgroundColor = background;
            bar.InactiveBackgroundColor = background;
            bar.ForegroundColor = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
            bar.InactiveForegroundColor = Color.FromArgb(0xFF, 0xE8, 0xEF, 0xF6);
            bar.ButtonBackgroundColor = Color.FromArgb(0x00, 0x00, 0x00, 0x00);
            bar.ButtonInactiveBackgroundColor = Color.FromArgb(0x00, 0x00, 0x00, 0x00);
            bar.ButtonHoverBackgroundColor = Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF);
            bar.ButtonPressedBackgroundColor = Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF);
            bar.ButtonForegroundColor = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
            bar.ButtonHoverForegroundColor = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
            bar.ButtonPressedForegroundColor = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
            bar.ButtonInactiveForegroundColor = Color.FromArgb(0xFF, 0xE8, 0xEF, 0xF6);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"标题栏配色降级：{ex.Message}");
        }
    }

    // ─────────────────────────── 导航 ───────────────────────────

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            Navigate("settings");
            return;
        }
        if (args.SelectedItem is NavigationViewItem { Tag: string tag })
        {
            Navigate(tag);
        }
    }

    private void Navigate(string tag)
    {
        var target = NavMap.Resolve(tag);
        if (target is null || target == _currentPageType) return;
        if (ContentFrame.Navigate(target, null, new SuppressNavigationTransitionInfo()))
        {
            _currentPageType = target;
            UpdateBackButton();
        }
    }

    private void UpdateBackButton()
    {
        try { AppTitleBar.IsBackButtonVisible = ContentFrame?.CanGoBack ?? false; }
        catch { /* 标题栏在个别系统版本上不可定制，忽略 */ }
    }

    private void AppTitleBar_PaneToggleRequested(TitleBar sender, object args) =>
        NavView.IsPaneOpen = !NavView.IsPaneOpen;

    private void AppTitleBar_BackRequested(TitleBar sender, object args)
    {
        if (ContentFrame.CanGoBack)
        {
            ContentFrame.GoBack();
            _currentPageType = ContentFrame.CurrentSourcePageType;
            UpdateBackButton();
        }
    }

    /// <summary>供 App 在带 --page 启动时直接落到指定页。</summary>
    public void NavigateToStartupPage()
    {
        // --barn：先把选定棚设为活动棚，再导航，页面 Rebuild 时读到的就是它。
        if (App.StartupBarnId is { Length: > 0 } barnId
            && RuntimeState.Monitor.Data.Barns.Any(b => b.Id == barnId))
        {
            RuntimeState.Monitor.SetActiveBarn(barnId);
            AppLog.Info($"[--barn] 启动棚舍设为 {barnId}");
        }

        var tag = App.StartupPageTag;
        if (string.IsNullOrEmpty(tag)) return;
        var item = NavView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => (string?)i.Tag == tag);
        if (item is not null)
        {
            NavView.SelectedItem = item;
        }
        else
        {
            Navigate(tag!);
        }
    }

    /// <summary>供 App 在带 --scroll bottom 启动时把当前页滚到底（用于截到设置页的「关于」块）。</summary>
    public void ScrollCurrentPageToBottom()
    {
        try
        {
            if (ContentFrame?.Content is not DependencyObject root) return;
            var viewer = FindScrollViewer(root);
            viewer?.ChangeView(null, viewer.ScrollableHeight, null, true);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"页面滚动失败：{ex.Message}");
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject node)
    {
        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is ScrollViewer viewer && viewer.ScrollableHeight > 1) return viewer;
            var found = FindScrollViewer(child);
            if (found is not null) return found;
        }
        return null;
    }

    // ─────────────────────────── 标题栏状态芯片 ───────────────────────────

    /// <summary>窗口标题（任务栏与 Alt+Tab 显示，也是自动化测试断言用的 MainWindowTitle）。</summary>
    private const string Caption = "安棚 1.0.0 · 草料棚火灾预警监控台";

    /// <summary>
    /// 兜住一个实测到的坑：非打包应用一旦可执行文件被改名（交付要求把它命名为
    /// 「安棚 1.0.0.exe」），Windows App SDK 会用可执行文件名初始化窗口标题，
    /// 而且只认自己那一次写入 —— 之后再赋值 Window.Title / AppWindow.Title 都不会
    /// 真正落到 HWND 上（实测窗口标题一直停在「安棚 1.0.0.exe」）。
    /// 这里直接对 HWND 调 SetWindowText 把标题钉死，任务栏与 Alt+Tab 都显示产品名。
    /// </summary>
    private void EnsureCaption()
    {
        if (!string.Equals(Title, Caption, StringComparison.Ordinal))
        {
            Title = Caption;
        }

        try
        {
            if (!string.Equals(AppWindow.Title, Caption, StringComparison.Ordinal))
            {
                AppWindow.Title = Caption;
            }
        }
        catch
        {
            // AppWindow 在极早期可能尚未就绪；下面还有 HWND 直写兜底。
        }

        try
        {
            var hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(AppWindow.Id);
            if (hwnd != IntPtr.Zero)
            {
                var sb = new System.Text.StringBuilder(256);
                GetWindowTextW(hwnd, sb, sb.Capacity);
                if (!string.Equals(sb.ToString(), Caption, StringComparison.Ordinal))
                {
                    SetWindowTextW(hwnd, Caption);
                    AppLog.Info($"窗口标题已纠正：{sb} → {Caption}");
                }
            }
        }
        catch
        {
            // 直写失败不影响功能，标题最多退化成可执行文件名。
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool SetWindowTextW(IntPtr hWnd, string lpString);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    private void OnMonitorChanged()
    {
        DispatcherQueue.TryEnqueue(RefreshChips);
    }

    private void RefreshChips()
    {
        try
        {
            EnsureCaption();
            var monitor = RuntimeState.Monitor;
            var online = monitor.Data.Nodes.Count(n => n.Online);
            var total = monitor.Data.Nodes.Count;
            OnlineChipText.Text = $"节点 {online}/{total} 在线";

            var worst = monitor.Data.Barns.Count == 0 ? AlarmLevel.Normal : monitor.Data.Barns.Max(b => b.CurrentLevel);
            LevelChipText.Text = worst switch
            {
                AlarmLevel.Alarm => "确认火警",
                AlarmLevel.Suspect => "疑似火情",
                AlarmLevel.Notice => "环境预警",
                AlarmLevel.Offline => "节点离线",
                _ => "全场正常",
            };
            LevelChipText.Foreground = (Brush)Application.Current.Resources[AlarmLevelInfo.BrushKey(worst)];
            LevelChip.Background = (Brush)Application.Current.Resources[AlarmLevelInfo.SoftBrushKey(worst)];

            RunStateText.Text = RuntimeState.IsTicking
                ? $"本地运行中 · 采样 {RuntimeState.Repo.Settings.SampleSeconds:0.0} s"
                : "本地运行中 · 实时轮询已暂停";
        }
        catch (Exception ex)
        {
            AppLog.Warn($"标题栏状态刷新失败：{ex.Message}");
        }
    }
}
