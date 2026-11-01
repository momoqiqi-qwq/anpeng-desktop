using Anpeng.Desktop.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Anpeng.Desktop.Controls;

/// <summary>
/// 级别徽章：药丸形底色 + 同级文字，颜色一律取自 AnpengTokens.xaml。
/// </summary>
public sealed partial class LevelBadge : UserControl
{
    private AlarmLevel _level = AlarmLevel.Normal;

    public LevelBadge()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
    }

    public AlarmLevel Level
    {
        get => _level;
        set
        {
            _level = value;
            Refresh();
        }
    }

    /// <summary>覆盖显示文案（默认取级别名）。</summary>
    public string? LevelText { get; set; }

    /// <summary>颜色是否取「实色底 + 白字」的反白样式。默认取浅底同色字。</summary>
    public bool Solid { get; set; }

    public void Refresh()
    {
        if (Host is null || Label is null) return;
        Label.Text = string.IsNullOrEmpty(LevelText) ? AlarmLevelInfo.Name(_level) : LevelText!;

        if (Solid)
        {
            Host.Background = (Brush)Application.Current.Resources[AlarmLevelInfo.BrushKey(_level)];
            Label.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
            Host.BorderThickness = new Thickness(0);
        }
        else
        {
            Host.Background = (Brush)Application.Current.Resources[AlarmLevelInfo.SoftBrushKey(_level)];
            Label.Foreground = (Brush)Application.Current.Resources[AlarmLevelInfo.BrushKey(_level)];
            Host.BorderThickness = new Thickness(1);
            Host.BorderBrush = (Brush)Application.Current.Resources[AlarmLevelInfo.BrushKey(_level)];
        }
    }
}
