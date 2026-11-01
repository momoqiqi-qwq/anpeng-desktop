using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Anpeng.Desktop.Controls;

/// <summary>
/// 安棚主标志 A 的矢量控件。显示尺寸由 <see cref="DisplaySize"/> 控制（正方形，最小 32 px）。
/// <see cref="Inverse"/> = true 时用反白版（深色底：圆角方底透明、棚保持白色描边、描边加粗至 44 补偿视觉重量）。
/// </summary>
public sealed partial class AnpengLogo : UserControl
{
    /// <summary>标准版圆角方底 #14538C（照抄 APP-SPEC 2）</summary>
    private static readonly Color BlueBackplate = Color.FromArgb(0xFF, 0x14, 0x53, 0x8C);

    /// <summary>反白版在深色页面上的底衬（同色相提亮，保证与深底仍有边界）</summary>
    private static readonly Color BlueBackplateOnDark = Color.FromArgb(0xFF, 0x1B, 0x66, 0xAB);

    /// <summary>棚屋白色描边</summary>
    private static readonly Color RoofWhite = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);

    /// <summary>火苗 #EE5A2E</summary>
    private static readonly Color FlameOrange = Color.FromArgb(0xFF, 0xEE, 0x5A, 0x2E);

    /// <summary>水滴 #6FCBEE</summary>
    private static readonly Color DropLightBlue = Color.FromArgb(0xFF, 0x6F, 0xCB, 0xEE);

    private double _displaySize = 64;

    public AnpengLogo()
    {
        InitializeComponent();
        Apply();
    }

    /// <summary>显示边长（像素，正方形）。</summary>
    public double DisplaySize
    {
        get => _displaySize;
        set
        {
            _displaySize = Math.Max(32, value);
            SetSize(_displaySize);
        }
    }

    /// <summary>反白版（深色底上使用）。</summary>
    public bool Inverse { get; set; }

    /// <summary>用深色底变体（标准版几何不变，仅提亮圆角方底）。</summary>
    public bool DarkBackplate { get; set; }

    private void SetSize(double size)
    {
        if (RootScale is null) return;
        RootScale.Width = size;
        RootScale.Height = size;
        Width = size;
        Height = size;
    }

    /// <summary>按规格上色；构造与属性变更后调用。</summary>
    public void Apply()
    {
        SetSize(_displaySize);

        if (Inverse)
        {
            Backplate.Background = new SolidColorBrush(Color.FromArgb(0x00, 0x00, 0x00, 0x00));
        }
        else
        {
            Backplate.Background = new SolidColorBrush(DarkBackplate ? BlueBackplateOnDark : BlueBackplate);
        }

        var roofBrush = new SolidColorBrush(RoofWhite);
        var strokeWidth = Inverse ? 44.0 : 34.0;
        foreach (var wall in new[] { Roof, WallLeft, WallRight })
        {
            wall.Stroke = roofBrush;
            wall.StrokeThickness = strokeWidth;
        }

        Flame.Fill = new SolidColorBrush(FlameOrange);
        DropShape.Fill = new SolidColorBrush(DropLightBlue);
    }
}
