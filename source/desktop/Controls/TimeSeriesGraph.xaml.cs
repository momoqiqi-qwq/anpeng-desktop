using Anpeng.Desktop.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace Anpeng.Desktop.Controls;

/// <summary>
/// 实时监测页与总览页共用的双曲线趋势图。数据来自 <see cref="Readings"/>，
/// 阈值参考线来自 <see cref="SmokeNoticePpm"/> / <see cref="TempNoticeC"/> / <see cref="TempAlarmC"/>。
/// </summary>
public sealed partial class TimeSeriesGraph : UserControl
{
    private static readonly Color SmokeColor = Color.FromArgb(0xFF, 0x2F, 0x8F, 0xD0);
    private static readonly Color TempColor = Color.FromArgb(0xFF, 0xEE, 0x5A, 0x2E);
    private static readonly Color NoticeColor = Color.FromArgb(0xFF, 0xC7, 0x9A, 0x00);
    private static readonly Color AlarmColor = Color.FromArgb(0xFF, 0xD9, 0x30, 0x25);

    private IReadOnlyList<SensorReading> _readings = Array.Empty<SensorReading>();
    private string _barnName = string.Empty;

    public TimeSeriesGraph()
    {
        InitializeComponent();
        Plot.SizeChanged += (_, _) => Redraw();
        Loaded += (_, _) => Redraw();
    }

    /// <summary>绘图用的采样序列（按时间升序）。</summary>
    public IReadOnlyList<SensorReading> Readings
    {
        get => _readings;
        set
        {
            _readings = value ?? Array.Empty<SensorReading>();
            Redraw();
        }
    }

    /// <summary>图内小标题里显示的棚舍名。</summary>
    public string BarnName
    {
        get => _barnName;
        set
        {
            _barnName = value ?? string.Empty;
            UpdateRangeText();
        }
    }

    public double SmokeNoticePpm { get; set; } = 800;
    public double TempNoticeC { get; set; } = 55.0;
    public double TempAlarmC { get; set; } = 70.0;

    /// <summary>当前级别，用于给最新采样点着色。</summary>
    public AlarmLevel Level { get; set; } = AlarmLevel.Normal;

    public void RequestRedraw() => Redraw();

    public void Redraw()
    {
        if (Plot is null || Plot.ActualWidth < 40 || Plot.ActualHeight < 40) return;
        Plot.Children.Clear();

        var w = Plot.ActualWidth - 1;
        var h = Plot.ActualHeight - 1;

        if (_readings.Count < 2)
        {
            AddLabel("暂无采样数据", w / 2 - 34, h / 2 - 10, 13);
            return;
        }

        var smokeMax = Math.Max(_readings.Max(r => r.SmokePpm) * 1.15, SmokeNoticePpm * 1.35);
        var tempMax = Math.Max(_readings.Max(r => r.TempC) * 1.1, TempAlarmC * 1.2);
        var tempMin = Math.Max(0, Math.Min(_readings.Min(r => r.TempC), TempNoticeC) - 8);

        // 网格（4 行）
        var gridBrush = new SolidColorBrush(Color.FromArgb(0x30, 0x5F, 0x5E, 0x5A));
        for (var i = 1; i < 4; i++)
        {
            var y = h * i / 4.0;
            Plot.Children.Add(new Line
            {
                X1 = 0, Y1 = y, X2 = w, Y2 = y,
                Stroke = gridBrush, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 2, 4 },
            });
        }

        // 阈值参考线：烟雾预警 / 温度预警 / 温度确认火警
        AddRefLine(w, y: Y(SmokeNoticePpm, 0, smokeMax, h), NoticeColor, 0.55);
        AddRefLine(w, y: Y(TempNoticeC, tempMin, tempMax, h), NoticeColor, 0.55);
        AddRefLine(w, y: Y(TempAlarmC, tempMin, tempMax, h), AlarmColor, 0.75);

        AddPolyline(_readings.Select((r, i) => Pt(i, _readings.Count, w, Y(r.SmokePpm, 0, smokeMax, h))), SmokeColor, 2.2);
        AddPolyline(_readings.Select((r, i) => Pt(i, _readings.Count, w, Y(r.TempC, tempMin, tempMax, h))), TempColor, 2.2);

        // 最新点
        var last = _readings[^1];
        AddDot(w - 2, Y(last.TempC, tempMin, tempMax, h), TempColor);
        AddDot(w - 2, Y(last.SmokePpm, 0, smokeMax, h), SmokeColor);

        // 横轴时间标注
        AddLabel(_readings[0].Time.ToString("HH:mm:ss"), 4, h - 18, 11);
        var tail = _readings[^1].Time.ToString("HH:mm:ss");
        AddLabel(tail, Math.Max(4, w - 14 - tail.Length * 6), h - 18, 11);

        UpdateRangeText(smokeMax, tempMin, tempMax);
    }

    private void UpdateRangeText(double smokeMax = 0, double tempMin = 0, double tempMax = 0)
    {
        if (SmokeRangeText is null) return;
        if (smokeMax <= 0)
        {
            var localSmoke = _readings.Count == 0 ? SmokeNoticePpm * 1.35 : Math.Max(_readings.Max(r => r.SmokePpm) * 1.15, SmokeNoticePpm * 1.35);
            var localTemp = _readings.Count == 0 ? TempAlarmC * 1.2 : Math.Max(_readings.Max(r => r.TempC) * 1.1, TempAlarmC * 1.2);
            var localMin = _readings.Count == 0 ? TempNoticeC - 8 : Math.Max(0, Math.Min(_readings.Min(r => r.TempC), TempNoticeC) - 8);
            smokeMax = localSmoke;
            tempMin = localMin;
            tempMax = localTemp;
        }
        SmokeRangeText.Text = $"烟雾量程 0 - {smokeMax:0} ppm　（预警线 {SmokeNoticePpm:0} ppm）";
        TempRangeText.Text = $"温度量程 {tempMin:0.0} - {tempMax:0.0} °C　（预警线 {TempNoticeC:0.0} °C，确认线 {TempAlarmC:0.0} °C）";
    }

    private static double Y(double value, double min, double max, double height)
    {
        if (max - min < 0.001) return height / 2;
        var t = (value - min) / (max - min);
        return height - Math.Clamp(t, 0, 1) * height;
    }

    private static Point Pt(int index, int count, double width, double y)
    {
        var x = count <= 1 ? 0 : width * index / (count - 1.0);
        return new Point(x, y);
    }

    private void AddPolyline(IEnumerable<Point> points, Color color, double thickness)
    {
        var line = new Polyline
        {
            Stroke = new SolidColorBrush(color),
            StrokeThickness = thickness,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
        foreach (var p in points) line.Points.Add(p);
        Plot.Children.Add(line);
    }

    private void AddRefLine(double width, double y, Color color, double opacity)
    {
        if (double.IsNaN(y)) return;
        Plot.Children.Add(new Line
        {
            X1 = 0, Y1 = y, X2 = width, Y2 = y,
            Stroke = new SolidColorBrush(color) { Opacity = opacity },
            StrokeThickness = 1.4,
            StrokeDashArray = new DoubleCollection { 5, 3 },
        });
    }

    private void AddDot(double x, double y, Color color)
    {
        var dot = new Ellipse
        {
            Width = 9, Height = 9,
            Fill = new SolidColorBrush(color),
            Stroke = new SolidColorBrush(AlarmLevelInfo.BrushColor(Level)),
            StrokeThickness = 2,
        };
        Canvas.SetLeft(dot, Math.Max(0, x - 4));
        Canvas.SetTop(dot, Math.Max(0, y - 4));
        Plot.Children.Add(dot);
    }

    private void AddLabel(string text, double x, double y, double fontSize)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            Foreground = (Brush)Application.Current.Resources["MutedBrush"],
        };
        Canvas.SetLeft(block, Math.Max(0, x));
        Canvas.SetTop(block, Math.Max(0, y));
        Plot.Children.Add(block);
    }
}
