using Anpeng.Desktop.Models;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Anpeng.Desktop.Controls;

/// <summary>
/// 页面装配用的小工具集。所有颜色一律经令牌画刷取（<c>AnpengTokens.xaml</c>），
/// 页面代码里不得出现裸色值，保证 APP-SPEC 第 3 节的色值只有一处定义。
/// </summary>
public static class UiKit
{
    public static SolidColorBrush Res(string key) =>
        Application.Current.Resources.TryGetValue(key, out var v) && v is SolidColorBrush b
            ? b
            : new SolidColorBrush(Color.FromArgb(0xFF, 0x1A, 0x1A, 0x1A));

    public static Style? StyleOf(string key) =>
        Application.Current.Resources.TryGetValue(key, out var v) ? v as Style : null;

    /// <summary>安棚品牌色（主色）</summary>
    public static SolidColorBrush Brand => Res("BrandBlueBrush");

    /// <summary>安棚品牌色·可读文字版</summary>
    public static SolidColorBrush BrandText => Res("BrandBlueTextBrush");

    public static SolidColorBrush Ink => Res("InkBrush");
    public static SolidColorBrush Muted => Res("MutedBrush");
    public static SolidColorBrush Line => Res("LineBrush");
    public static SolidColorBrush Surface => Res("SurfaceBrush");
    public static SolidColorBrush BgAlt => Res("BgAltBrush");
    public static SolidColorBrush Fire => Res("BrandFireBrush");

    public static SolidColorBrush Level(AlarmLevel level) => new(AlarmLevelInfo.BrushColor(level));

    public static SolidColorBrush LevelSoft(AlarmLevel level) =>
        new(Color.FromArgb(0x24, AlarmLevelInfo.BrushColor(level).R,
            AlarmLevelInfo.BrushColor(level).G, AlarmLevelInfo.BrushColor(level).B));

    // ─────────────────────────── 文本 ───────────────────────────

    public static TextBlock Text(string text, string styleKey = "AnpengBodyStyle")
    {
        var tb = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        var style = StyleOf(styleKey);
        if (style is not null) tb.Style = style;
        return tb;
    }

    public static TextBlock SectionTitle(string text) => Text(text, "AnpengSectionTitleStyle");

    public static TextBlock Caption(string text) => Text(text, "AnpengCaptionStyle");

    public static TextBlock PageTitle(string text) => Text(text, "AnpengPageTitleStyle");

    public static TextBlock Readout(string text) => Text(text, "AnpengReadoutStyle");

    /// <summary>带指定大小与粗细的文本（用于大号读数旁的单位、次级说明）。</summary>
    public static TextBlock Label(string text, double size, Windows.UI.Text.FontWeight weight, Brush brush)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight,
            Foreground = brush,
            TextWrapping = TextWrapping.Wrap,
        };
    }

    // ─────────────────────────── 容器 ───────────────────────────

    /// <summary>卡片：白底 + 1px 分割线 + 圆角 8（APP-SPEC 3.3）。</summary>
    public static Border Card(UIElement child, double padding = 16)
    {
        var b = new Border { Padding = new Thickness(padding) };
        var style = StyleOf("AnpengCardStyle");
        if (style is not null) b.Style = style;
        b.Child = child;
        return b;
    }

    /// <summary>带标题的卡片。</summary>
    public static Border TitledCard(string title, UIElement child, string? hint = null)
    {
        var stack = new StackPanel { Spacing = 12 };
        var head = new StackPanel { Spacing = 4 };
        head.Children.Add(SectionTitle(title));
        if (!string.IsNullOrEmpty(hint)) head.Children.Add(Caption(hint!));
        stack.Children.Add(head);
        stack.Children.Add(child);
        return Card(stack);
    }

    public static StackPanel VStack(double spacing = 16) => new() { Spacing = spacing };

    public static StackPanel HStack(double spacing = 12)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
        return p;
    }

    public static Grid EqualColumns(int count, double spacing = 12)
    {
        var g = new Grid();
        for (var i = 0; i < count; i++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        g.ColumnSpacing = spacing;
        return g;
    }

    public static void PlaceAt(Grid grid, FrameworkElement child, int column) =>
        Grid.SetColumn(child, column);

    // ─────────────────────────── 部件 ───────────────────────────

    /// <summary>汇总带上的指标块：标签 + 大数值 + 可选副说明。</summary>
    public static Border Stat(string label, string value, string? sub = null, Brush? accent = null)
    {
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(Label(label, 12, FontWeights.Normal, Muted));
        stack.Children.Add(Label(value, 28, FontWeights.SemiBold, accent ?? BrandText));
        if (!string.IsNullOrEmpty(sub)) stack.Children.Add(Label(sub!, 12, FontWeights.Normal, Muted));
        return Card(stack);
    }

    /// <summary>告警级别药丸：实色底白字（Solid=true）或浅底同色字。</summary>
    public static Border Pill(string text, AlarmLevel level, bool solid = false)
    {
        var brush = Level(level);
        var b = new Border
        {
            Background = solid ? brush : LevelSoft(level),
            BorderBrush = brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 3, 10, 4),
            VerticalAlignment = VerticalAlignment.Center,
        };
        b.Child = Label(text, 12, FontWeights.SemiBold, solid ? Res("SurfaceBrush") : brush);
        return b;
    }

    /// <summary>状态灯（实心圆点）。</summary>
    public static Border StatusDot(AlarmLevel level, double size = 12)
    {
        var b = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Background = Level(level),
            VerticalAlignment = VerticalAlignment.Center,
        };
        return b;
    }

    /// <summary>键值一行（左标签右值）。</summary>
    public static Grid KeyValue(string key, string value, Brush? valueBrush = null)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var k = Label(key, 12, FontWeights.Normal, Muted);
        var v = Label(value, 14, FontWeights.SemiBold, valueBrush ?? Ink);
        Grid.SetColumn(k, 0);
        Grid.SetColumn(v, 1);
        g.Children.Add(k);
        g.Children.Add(v);
        return g;
    }

    /// <summary>表格：等宽列；firstWide=true 时首列占 2 份宽度（用于「时间 + 说明」这类布局）。</summary>
    public static Grid Table(string[] headers, IEnumerable<string[]> rows, bool firstWide = false)
    {
        var g = new Grid { Padding = new Thickness(0, 0, 0, 0) };
        for (var c = 0; c < headers.Length; c++)
        {
            var star = firstWide && c == 0 ? 2.0 : 1.0;
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(star, GridUnitType.Star) });
        }

        var r = 0;
        for (var c = 0; c < headers.Length; c++)
        {
            var tb = Label(headers[c], 12, FontWeights.SemiBold, Muted);
            tb.Margin = new Thickness(0, 0, 12, 6);
            Grid.SetRow(tb, r);
            Grid.SetColumn(tb, c);
            g.Children.Add(tb);
        }
        r++;

        foreach (var row in rows)
        {
            for (var c = 0; c < headers.Length && c < row.Length; c++)
            {
                var tb = Label(row[c], 14, FontWeights.Normal, Ink);
                tb.Margin = new Thickness(0, 4, 12, 4);
                Grid.SetRow(tb, r);
                Grid.SetColumn(tb, c);
                g.Children.Add(tb);
            }
            r++;
        }

        for (var i = 0; i < r; i++)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        return g;
    }

    /// <summary>横向分隔线（1px #DBE1E8）。</summary>
    public static Border Divider() => new()
    {
        Height = 1,
        Background = Line,
        Margin = new Thickness(0, 8, 0, 8),
    };

    /// <summary>一条横向条形（数值可视化）。</summary>
    public static Grid Bar(string label, double value, double max, Brush brush, string valueText)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });

        var name = Label(label, 14, FontWeights.Normal, Ink);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(name, 0);

        var track = new Border
        {
            Height = 14,
            CornerRadius = new CornerRadius(4),
            Background = BgAlt,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var ratio = max <= 0 ? 0 : Math.Clamp(value / max, 0, 1);

        // 填充宽度必须相对「轨道自身宽度」按比例分配，不能写死像素 ——
        // 写成固定像素时，轨道被拉伸到几百像素而填充只有 260 px，
        // 柱条会显示成一根截断的短桩（第二轮验收 D5 复核时暴露的缺陷）。
        var trackGrid = new Grid();
        trackGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(Math.Max(0.0001, ratio), GridUnitType.Star),
        });
        trackGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(Math.Max(0.0001, 1 - ratio), GridUnitType.Star),
        });
        var fill = new Border
        {
            CornerRadius = new CornerRadius(4),
            Background = brush,
        };
        Grid.SetColumn(fill, 0);
        trackGrid.Children.Add(fill);
        track.Child = trackGrid;
        // 轨道必须显式放到第 1 列：漏掉这一行它会落回第 0 列，与左侧标签叠在一起，
        // 视觉上表现为「标签被柱条盖住、柱条只是一根短桩」（D5 复核时暴露）。
        Grid.SetColumn(track, 1);

        var val = Label(valueText, 14, FontWeights.SemiBold, brush);
        val.TextAlignment = TextAlignment.Right;
        Grid.SetColumn(val, 2);

        g.Children.Add(name);
        g.Children.Add(track);
        g.Children.Add(val);
        return g;
    }

    /// <summary>简单纵向柱状图（用于喷淋趋势 / 级别堆叠）。</summary>
    public static Grid ColumnChart(IReadOnlyList<(string Label, double Value, Brush Brush)> bars, double height = 140)
    {
        var g = new Grid { Height = height + 28 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var i = 0; i < bars.Count; i++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        var max = Math.Max(1, bars.Count == 0 ? 1 : bars.Max(b => b.Value));
        for (var i = 0; i < bars.Count; i++)
        {
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Spacing = 4 };
            var bar = new Border
            {
                Width = 34,
                Height = Math.Max(2, bars[i].Value / max * height),
                CornerRadius = new CornerRadius(4),
                Background = bars[i].Brush,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var val = Label(bars[i].Value.ToString("0"), 12, FontWeights.SemiBold, Muted);
            val.TextAlignment = TextAlignment.Center;
            var name = Label(bars[i].Label, 12, FontWeights.Normal, Muted);
            name.TextAlignment = TextAlignment.Center;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            stack.Children.Add(val);
            stack.Children.Add(bar);
            stack.Children.Add(name);
            Grid.SetColumn(stack, i + 1);
            g.Children.Add(stack);
        }

        // 基线
        var baseLine = new Border { Height = 1, Background = Line, VerticalAlignment = VerticalAlignment.Bottom };
        Grid.SetColumn(baseLine, 0);
        Grid.SetColumnSpan(baseLine, bars.Count + 1);
        g.Children.Add(baseLine);
        return g;
    }

    public static Button PrimaryButton(string text, RoutedEventHandler onClick)
    {
        var b = new Button { Content = text };
        var style = StyleOf("AnpengPrimaryButtonStyle");
        if (style is not null) b.Style = style;
        b.Click += onClick;
        return b;
    }

    /// <summary>危险操作按钮（#EE5A2E，契约 3.1：白底上不当正文文字，但可作实心按钮底）。</summary>
    public static Button DangerButton(string text, RoutedEventHandler onClick)
    {
        var b = new Button { Content = text };
        var style = StyleOf("AnpengDangerButtonStyle");
        if (style is not null) b.Style = style;
        b.Click += onClick;
        return b;
    }

    public static Button PlainButton(string text, RoutedEventHandler onClick)
    {
        var b = new Button { Content = text };
        var style = StyleOf("AnpengPlainButtonStyle");
        if (style is not null) b.Style = style;
        b.Click += onClick;
        return b;
    }

    /// <summary>可滚动的采样 / 记录列表容器。</summary>
    public static ScrollViewer Scroll(UIElement child, double maxHeight)
    {
        return new ScrollViewer
        {
            Content = child,
            MaxHeight = maxHeight,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            // HorizontalScrollMode 必须显式关掉：只设 HorizontalScrollBarVisibility=Disabled
            // 时滚动模式仍是 Enabled，内容会按「无限宽度」测量，容器里的星号列随之塌缩，
            // 柱条被画成截断的短桩（D5 复核时暴露）。
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollMode = ScrollMode.Enabled,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
    }

    /// <summary>空态占位（不会出现占位空页：仅用于过滤结果确实为空时）。</summary>
    public static TextBlock Empty(string text) => Label(text, 14, FontWeights.Normal, Muted);
}
