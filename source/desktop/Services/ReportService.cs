using System.Globalization;
using System.Text;
using Anpeng.Desktop.Models;

namespace Anpeng.Desktop.Services;

/// <summary>报表统计（日报 / 周报 / 月报 + CSV 导出）。纯逻辑，可单测。</summary>
public static class ReportService
{
    public enum Range { Day, Week, Month }

    public static string RangeName(Range range) => range switch
    {
        Range.Day => "日报",
        Range.Week => "周报",
        _ => "月报",
    };

    public static DateTime RangeStart(Range range, DateTime now) => range switch
    {
        Range.Day => now.Date,
        Range.Week => now.Date.AddDays(-6),
        _ => now.Date.AddDays(-29),
    };

    public static List<AlarmRecord> Filter(IEnumerable<AlarmRecord> alarms, Range range, DateTime now)
    {
        var start = RangeStart(range, now);
        return alarms.Where(a => a.Time >= start && a.Time <= now.AddMinutes(5))
                     .OrderByDescending(a => a.Time)
                     .ToList();
    }

    public static List<SprinklerRecord> Filter(IEnumerable<SprinklerRecord> records, Range range, DateTime now)
    {
        var start = RangeStart(range, now);
        return records.Where(s => s.StartTime >= start && s.StartTime <= now.AddMinutes(5))
                      .OrderByDescending(s => s.StartTime)
                      .ToList();
    }

    /// <summary>按级别统计。</summary>
    public static Dictionary<AlarmLevel, int> ByLevel(IEnumerable<AlarmRecord> alarms)
    {
        var dict = new Dictionary<AlarmLevel, int>
        {
            [AlarmLevel.Notice] = 0, [AlarmLevel.Suspect] = 0, [AlarmLevel.Alarm] = 0,
        };
        foreach (var a in alarms)
        {
            if (!dict.ContainsKey(a.Level)) dict[a.Level] = 0;
            dict[a.Level]++;
        }
        return dict;
    }

    /// <summary>按棚统计总数（降序）。</summary>
    public static List<(string BarnName, int Count)> ByBarn(IEnumerable<AlarmRecord> alarms) =>
        alarms.GroupBy(a => a.BarnName)
              .Select(g => (BarnName: g.Key, Count: g.Count()))
              .OrderByDescending(x => x.Count)
              .ThenBy(x => x.BarnName, StringComparer.Ordinal)
              .ToList();

    /// <summary>逐日喷淋次数趋势。</summary>
    public static List<(DateTime Day, int Count)> SprinklerTrend(
        IEnumerable<SprinklerRecord> records, Range range, DateTime now)
    {
        var start = RangeStart(range, now).Date;
        var days = Math.Max(1, (int)(now.Date - start).TotalDays + 1);
        var buckets = Enumerable.Range(0, days).Select(i => start.AddDays(i)).ToList();
        var list = buckets.Select(d => (Day: d, Count: 0)).ToList();
        foreach (var s in records)
        {
            var idx = (int)(s.StartTime.Date - start).TotalDays;
            if (idx >= 0 && idx < list.Count) list[idx] = (list[idx].Day, list[idx].Count + 1);
        }
        return list;
    }

    /// <summary>平均温度（用指定时间窗内的采样）。</summary>
    public static double AverageTemp(IEnumerable<SensorReading> readings, Range range, DateTime now)
    {
        var start = RangeStart(range, now);
        var values = readings.Where(r => r.Time >= start && r.Time <= now.AddMinutes(5)).Select(r => r.TempC).ToList();
        return values.Count == 0 ? 0 : Math.Round(values.Average(), 1);
    }

    public static double PeakTemp(IEnumerable<SensorReading> readings, Range range, DateTime now)
    {
        var start = RangeStart(range, now);
        var values = readings.Where(r => r.Time >= start && r.Time <= now.AddMinutes(5)).Select(r => r.TempC).ToList();
        return values.Count == 0 ? 0 : Math.Round(values.Max(), 1);
    }

    public static double PeakSmoke(IEnumerable<SensorReading> readings, Range range, DateTime now)
    {
        var start = RangeStart(range, now);
        var values = readings.Where(r => r.Time >= start && r.Time <= now.AddMinutes(5)).Select(r => r.SmokePpm).ToList();
        return values.Count == 0 ? 0 : Math.Round(values.Max(), 0);
    }

    public readonly record struct ReadingStatistics(double AverageTemp, double PeakTemp, double PeakSmoke);

    /// <summary>一次遍历计算报表采样指标，避免为三个指标重复筛选、分配临时列表。</summary>
    public static ReadingStatistics SummarizeReadings(IEnumerable<SensorReading> readings, Range range, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(readings);
        var start = RangeStart(range, now);
        DateTime? end = null;
        long count = 0;
        double totalTemp = 0;
        var peakTemp = double.NaN;
        var peakSmoke = double.NaN;

        foreach (var reading in readings)
        {
            if (reading.Time < start) continue;
            // 和原筛选保持相同短路顺序，只在遇到候选采样时计算上界。
            end ??= now.AddMinutes(5);
            if (reading.Time > end.Value) continue;

            count++;
            totalTemp += reading.TempC;
            // 与 Enumerable.Max 一致：有数值时忽略 NaN，全为 NaN 时保留 NaN。
            if (reading.TempC > peakTemp || double.IsNaN(peakTemp)) peakTemp = reading.TempC;
            if (reading.SmokePpm > peakSmoke || double.IsNaN(peakSmoke)) peakSmoke = reading.SmokePpm;
        }

        return count == 0 ? default : new ReadingStatistics(
            Math.Round(totalTemp / count, 1), Math.Round(peakTemp, 1), Math.Round(peakSmoke, 0));
    }

    /// <summary>导出报警记录 CSV（UTF-8 BOM，Excel 中文不乱码）。</summary>
    public static string ExportAlarmsCsv(IEnumerable<AlarmRecord> alarms)
    {
        var sb = new StringBuilder();
        sb.AppendLine("时间,棚舍,级别,烟雾(ppm),温度(°C),温升(°C/min),连续超限(s),是否喷淋,处置动作,处置人,备注");
        foreach (var a in alarms)
        {
            sb.AppendLine(string.Join(',',
                Csv(a.TimeText), Csv(a.BarnName), Csv(a.LevelName), a.SmokePpm.ToString("0", CultureInfo.InvariantCulture),
                a.TempC.ToString("0.0", CultureInfo.InvariantCulture), a.TempRisePerMin.ToString("0.0", CultureInfo.InvariantCulture),
                a.OverLimitSeconds.ToString("0", CultureInfo.InvariantCulture), Csv(a.SprinklerText),
                Csv(a.Action), Csv(a.Handler), Csv(a.Remark)));
        }
        return sb.ToString();
    }

    /// <summary>导出喷淋记录 CSV。</summary>
    public static string ExportSprinklersCsv(IEnumerable<SprinklerRecord> records)
    {
        var sb = new StringBuilder();
        sb.AppendLine("开始时间,棚舍,时长(s),原因,触发级别,方式,操作人,温度回落,复核结论");
        foreach (var s in records)
        {
            sb.AppendLine(string.Join(',',
                Csv(s.StartText), Csv(s.BarnName),
                s.DurationSeconds.ToString("0", CultureInfo.InvariantCulture), Csv(s.Reason), Csv(s.LevelName),
                Csv(s.Mode), Csv(s.Operator), Csv(s.TempDropText), Csv(s.Outcome)));
        }
        return sb.ToString();
    }

    /// <summary>导出采样数据 CSV。</summary>
    public static string ExportReadingsCsv(IEnumerable<SensorReading> readings, IReadOnlyList<Barn> barns)
    {
        var map = barns.ToDictionary(b => b.Id, b => b.Name);
        var sb = new StringBuilder();
        sb.AppendLine("时间,棚舍,烟雾(ppm),温度(°C),电压(V),在线状态,阀门状态");
        foreach (var r in readings.OrderByDescending(r => r.Time))
        {
            sb.AppendLine(string.Join(',',
                Csv(r.Time.ToString("yyyy-MM-dd HH:mm:ss")),
                Csv(map.TryGetValue(r.BarnId, out var n) ? n : r.BarnId),
                r.SmokePpm.ToString("0", CultureInfo.InvariantCulture),
                r.TempC.ToString("0.0", CultureInfo.InvariantCulture),
                r.Voltage.ToString("0.00", CultureInfo.InvariantCulture),
                Csv(r.Online ? "在线" : "离线"), Csv(ValveStateInfo.Name(r.Valve))));
        }
        return sb.ToString();
    }

    private static string Csv(string value)
    {
        var v = value.Replace("\"", "\"\"");
        return v.Contains(',') || v.Contains('"') || v.Contains('\n') ? $"\"{v}\"" : v;
    }
}
