using System.Globalization;

namespace Anpeng.Desktop.Models;

/// <summary>
/// 展示值与判定值的分离工具（对应契约 APP-SPEC §4.2.1 第 1 条）。
///
/// 判据一律使用**原始读数**比较阈值；本类只负责把原始读数压成「不会说假话」的展示字符串：
/// 从基础小数位起逐位试探，选**最小**小数位使「显示值 ≥ 阈值」与「原始值 ≥ 阈值」同真假。
/// 于是 799.6 ppm 显示成 799.6（不会假装成 800），而 800.0 ppm 仍显示成整数风格。
/// Android 侧同名实现为 fmtVs / fmtVsMulti，两端口径一致。
/// </summary>
public static class ValueDisplay
{
    /// <summary>自适应尝试的最大小数位。再往上就没有可读性收益了。</summary>
    public const int MaxDecimals = 4;

    /// <summary>
    /// 针对单个阈值的自适应展示值。
    /// 返回的 Value 已按选定的 Decimals 取整，Decimals 供格式化使用。
    /// </summary>
    public static (double Value, int Decimals) Vs(double value, double threshold)
    {
        var rawOver = value >= threshold;
        for (var d = 0; d <= MaxDecimals; d++)
        {
            var rounded = Round(value, d);
            if ((rounded >= threshold) == rawOver) return (rounded, d);
        }
        return (Round(value, MaxDecimals), MaxDecimals);
    }

    /// <summary>
    /// 针对多个阈值的自适应展示值：必须同时满足「显示值 ≥ 阈值 i」与「原始值 ≥ 阈值 i」同真假，
    /// 避免同一读数在「预警阈值」上说真话、在「确认阈值」上说假话。
    /// </summary>
    public static (double Value, int Decimals) VsMulti(double value, params double[] thresholds)
    {
        if (thresholds.Length == 0) return (Round(value, 1), 1);

        var rawOvers = new bool[thresholds.Length];
        for (var i = 0; i < thresholds.Length; i++) rawOvers[i] = value >= thresholds[i];

        for (var d = 0; d <= MaxDecimals; d++)
        {
            var rounded = Round(value, d);
            var ok = true;
            for (var i = 0; i < thresholds.Length; i++)
            {
                if ((rounded >= thresholds[i]) == rawOvers[i]) continue;
                ok = false;
                break;
            }
            if (ok) return (rounded, d);
        }
        return (Round(value, MaxDecimals), MaxDecimals);
    }

    /// <summary>按指定小数位格式化（不受地区设置影响，小数点固定为 "."）。</summary>
    public static string Text(double value, int decimals) =>
        value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    /// <summary>带单位的展示串，例如 "4.0 s"。</summary>
    public static string Text(double value, int decimals, string unit) =>
        Text(value, decimals) + " " + unit;

    /// <summary>定点四舍五入，刻意避开银行家舍入，与判定引擎内的取整口径一致。</summary>
    internal static double Round(double value, int decimals) =>
        Math.Round(value, decimals, MidpointRounding.AwayFromZero);
}
