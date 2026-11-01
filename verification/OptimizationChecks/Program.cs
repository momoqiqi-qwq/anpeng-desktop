using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anpeng.Desktop.Data;
using Anpeng.Desktop.Models;
using Anpeng.Desktop.Services;
using ReportRange = Anpeng.Desktop.Services.ReportService.Range;

internal static class Program
{
    private static readonly DateTime Now = new(2026, 10, 26, 13, 47, 35, DateTimeKind.Local);
    private static readonly List<object> Results = [];
    private static int _assertions;
    private static double _sink;

    public static int Main()
    {
        // Derive isolation from this harness, never from LocalApplicationData or application defaults.
        var project = FindProjectDirectory();
        var artifactRoot = Path.Combine(project, "artifacts");
        Directory.CreateDirectory(artifactRoot);
        var runRoot = Path.Combine(artifactRoot, "run-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(runRoot);
        AppLog.Attach(Path.Combine(runRoot, "harness.log"));
        AlarmBeeper.Sink = _ => true;
        var exitCode = 0;
        try
        {
            CheckStatistics();
            CheckRepository(runRoot);
            CheckSelection(runRoot);
            CheckExistingSelfTest(runRoot);
            BenchmarkStatistics();
            Console.WriteLine($"PASS: {_assertions} assertions; isolated data: {runRoot}");
        }
        catch (Exception exception)
        {
            exitCode = 1;
            Results.Add(new { Name = "Failure", Error = exception.ToString() });
            Console.Error.WriteLine(exception);
        }
        finally
        {
            var sourceRoot = Path.GetFullPath(Path.Combine(project, "..", "..", "source", "desktop"));
            var sourceFiles = new[] { "Services/ReportService.cs", "Data/DataRepository.cs", "Services/MonitoringService.cs" };
            var evidence = new
            {
                Passed = exitCode == 0,
                Assertions = _assertions,
                Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                SourceMode = "Compiles linked application source; no copied implementation or application startup",
                SourceHashes = sourceFiles.ToDictionary(file => file, file => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(sourceRoot, file))))),
                DataDirectory = runRoot,
                Results,
            };
            var json = JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(runRoot, "results.json"), json);
            File.WriteAllText(Path.Combine(artifactRoot, "latest-results.json"), json);
        }
        return exitCode;
    }

    private static void CheckStatistics()
    {
        var cases = 0;
        foreach (var range in Enum.GetValues<ReportRange>())
        {
            var start = ReportService.RangeStart(range, Now);
            var end = Now.AddMinutes(5);
            Compare([], range, "empty");
            Compare([Reading(start.AddTicks(-1), 1000, 10000), Reading(end.AddTicks(1), 2000, 20000)], range, "excluded");
            Compare([Reading(start, -10, 5), Reading(start.AddTicks(1), 2, 10), Reading(end.AddTicks(-1), 25, 15), Reading(end, 40, 20)], range, "inclusive edges");
            var boundaryReadings = new[] { start.AddTicks(-1), start, start.AddTicks(1), end.AddTicks(-1), end, end.AddTicks(1) }
                .Select((time, index) => Reading(time, index * 10, index * 100)).ToArray();
            Compare(boundaryReadings, range, "all boundaries");
            var expectedBoundary = ReportService.SummarizeReadings(boundaryReadings, range, Now);
            Require(expectedBoundary == new ReportService.ReadingStatistics(25, 40, 400), "Boundary inclusion regression");
            cases += 4;

            double[][] unusual =
            [
                [double.NaN],
                [double.NaN, double.NaN],
                [double.NaN, 1, 2, double.NaN],
                [1, double.NaN, 2],
                [double.PositiveInfinity, 2],
                [double.NegativeInfinity, 2],
                [double.PositiveInfinity, double.NegativeInfinity],
                [double.NaN, double.PositiveInfinity, double.NegativeInfinity, 12, double.NaN],
                [0.0, -0.0, 0.0],
                [-0.0, 0.0, -0.0],
                [1.25, 1.25],
                [-1.25, -1.25],
                [double.MaxValue, double.MaxValue],
                [double.MaxValue, -double.MaxValue, 1],
            ];
            foreach (var values in unusual)
            {
                Compare(values.Select((value, index) => Reading(start.AddSeconds(index), value, values[values.Length - 1 - index])).ToArray(), range, "non-finite/rounding");
                cases++;
            }

            var random = new Random(55261026 + (int)range);
            for (var iteration = 0; iteration < 100; iteration++)
            {
                var readings = Enumerable.Range(0, random.Next(0, 250)).Select(index =>
                {
                    var time = start.AddSeconds(random.NextDouble() * ((end - start).TotalSeconds + 20) - 10);
                    var temperature = Math.Round(random.NextDouble() * 180 - 40, 4);
                    var smoke = Math.Round(random.NextDouble() * 5000 - 100, 4);
                    if (iteration % 7 == 0 && index % 11 == 0) temperature = double.NaN;
                    if (iteration % 11 == 0 && index % 13 == 0) smoke = double.PositiveInfinity;
                    return Reading(time, temperature, smoke);
                }).ToArray();
                Compare(readings, range, $"random {iteration}");
                cases++;
            }

            var counted = new CountingEnumerable<SensorReading>(boundaryReadings, allowRepeat: false);
            _ = ReportService.SummarizeReadings(counted, range, Now);
            Require(counted.Enumerations == 1 && counted.Visited == boundaryReadings.Length, "Summary must enumerate once");
            var oldCounted = new CountingEnumerable<SensorReading>(boundaryReadings, allowRepeat: true);
            _ = Legacy(oldCounted, range, Now);
            Require(oldCounted.Enumerations == 3 && oldCounted.Visited == 3 * boundaryReadings.Length, "Legacy baseline must enumerate three times");
        }

        // Existing Where predicates defer AddMinutes until a reading reaches the lower bound.
        var nearMax = DateTime.MaxValue;
        Require(ReportService.SummarizeReadings([], ReportRange.Day, nearMax) == Legacy([], ReportRange.Day, nearMax), "Empty max-date behavior");
        Require(ReportService.SummarizeReadings([Reading(nearMax.Date.AddTicks(-1), 1, 1)], ReportRange.Day, nearMax)
            == Legacy([Reading(nearMax.Date.AddTicks(-1), 1, 1)], ReportRange.Day, nearMax), "Excluded max-date behavior");
        Require(Throws<ArgumentOutOfRangeException>(() => ReportService.SummarizeReadings([Reading(nearMax, 1, 1)], ReportRange.Day, nearMax)), "Candidate max-date must preserve exception");
        Require(Throws<ArgumentNullException>(() => ReportService.SummarizeReadings(null!, ReportRange.Day, Now)), "Null enumerable validation");
        Results.Add(new { Name = "Reading statistics", Cases = cases, Ranges = "Day, Week, Month", NewEnumerations = 1, LegacyEnumerations = 3, Passed = true });
        Console.WriteLine($"PASS statistics: {cases} datasets across Day/Week/Month, boundaries, NaN/infinities, random; one enumeration");
    }

    private static void CheckRepository(string runRoot)
    {
        var directory = Path.Combine(runRoot, "repository");
        var repository = new DataRepository(directory);
        repository.Load();
        Require(repository.SeededThisRun, "New isolated directory must seed");
        repository.Settings.SiteName = "序列化核验 <棚> & 引号\" \\ 换行\n😀";
        repository.Settings.CloudNote = new string('棚', 70000);
        repository.Settings.SampleSeconds = 3;
        var sample = repository.Data.Readings[0];
        while (repository.Data.Readings.Count < 1200)
            repository.Data.Readings.Add(Reading(sample.Time.AddTicks(repository.Data.Readings.Count), 23.75, 512.5));
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters = { new JsonStringEnumConverter() },
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };
        for (var repeat = 0; repeat < 3; repeat++)
        {
            repository.Settings.Operator = $"重复保存 {repeat}";
            repository.Data.Readings[0].TempC = 18.5 + repeat;
            repository.SaveAll();
            var expectedSettings = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(repository.Settings, options));
            var expectedData = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(repository.Data, options));
            Require(File.ReadAllBytes(Path.Combine(directory, "settings.json")).AsSpan().SequenceEqual(expectedSettings), "Settings bytes changed from original string serialization");
            Require(File.ReadAllBytes(Path.Combine(directory, "data.json")).AsSpan().SequenceEqual(expectedData), "Data bytes changed from original string serialization");
            Require(!Directory.EnumerateFiles(directory, "*.tmp").Any(), "Successful save must move temporary file");
            var reloaded = new DataRepository(directory);
            reloaded.Load();
            Require(!reloaded.SeededThisRun, "Reload must read saved data, not regenerate");
            Require(JsonSerializer.Serialize(reloaded.Settings, options) == Encoding.UTF8.GetString(expectedSettings), "Settings roundtrip mismatch");
            Require(JsonSerializer.Serialize(reloaded.Data, options) == Encoding.UTF8.GetString(expectedData), "Data roundtrip mismatch");
        }
        repository.SaveSettings();
        repository.SaveData();
        Require(!Directory.EnumerateFiles(directory, "*.tmp").Any(), "Individual saves leave no tmp");
        Results.Add(new { Name = "Repository persistence", RepeatedSaves = 3, Readings = repository.Data.Readings.Count, ByteIdentical = true, Roundtrip = true, Passed = true });
        Console.WriteLine("PASS persistence: exact UTF-8 bytes, 1,200 readings, long Unicode strings, reload and repeated replacement");
    }

    private static void CheckSelection(string runRoot)
    {
        var repository = new DataRepository(Path.Combine(runRoot, "selection"));
        repository.Load();
        var monitor = new MonitoringService(repository);
        var notifications = 0;
        monitor.Changed += () => notifications++;
        var original = monitor.ActiveBarnId!;
        monitor.SetActiveBarn(original);
        Require(notifications == 0, "Selecting current barn must not notify");
        var next = repository.Data.Barns.First(barn => barn.Id != original).Id;
        monitor.SetActiveBarn(next);
        Require(notifications == 1 && monitor.ActiveBarnId == next, "Actual selection must notify exactly once");
        monitor.SetActiveBarn(new string(next.ToCharArray()));
        Require(notifications == 1, "Equal ID string must not notify again");
        monitor.SetActiveBarn(original);
        Require(notifications == 2 && monitor.ActiveBarnId == original, "Switching back must notify once");
        Results.Add(new { Name = "Selection notifications", ActualChanges = 2, RepeatedSelections = 2, Notifications = notifications, Passed = true });
        Console.WriteLine("PASS selection: one notification per change, zero for repeated/equal IDs");
    }

    private static void BenchmarkStatistics()
    {
        var readings = Enumerable.Range(0, 1200).Select(index => Reading(Now.AddSeconds(-index * 2), 20 + index % 70 / 10.0, 150 + index % 1000)).ToArray();
        const int iterations = 10000;
        Func<ReportService.ReadingStatistics> legacy = () => Legacy(readings, ReportRange.Day, Now);
        Func<ReportService.ReadingStatistics> optimized = () => ReportService.SummarizeReadings(readings, ReportRange.Day, Now);
        for (var index = 0; index < 3000; index++) { Consume(legacy()); Consume(optimized()); }
        var oldRuns = new List<Measurement>();
        var newRuns = new List<Measurement>();
        for (var repeat = 0; repeat < 5; repeat++)
        {
            if (repeat % 2 == 0) { oldRuns.Add(Measure(legacy, iterations)); newRuns.Add(Measure(optimized, iterations)); }
            else { newRuns.Add(Measure(optimized, iterations)); oldRuns.Add(Measure(legacy, iterations)); }
        }
        var oldMedian = oldRuns.OrderBy(measurement => measurement.NanosecondsPerCall).ElementAt(2);
        var newMedian = newRuns.OrderBy(measurement => measurement.NanosecondsPerCall).ElementAt(2);
        var speedup = oldMedian.NanosecondsPerCall / newMedian.NanosecondsPerCall;
        Results.Add(new { Name = "1200-reading microbenchmark", IterationsPerRound = iterations, Rounds = 5, Legacy = oldMedian, Optimized = newMedian, Speedup = speedup, LegacyRuns = oldRuns, OptimizedRuns = newRuns });
        Console.WriteLine($"BENCH 1200 readings median: legacy {oldMedian.NanosecondsPerCall:F0} ns / {oldMedian.BytesPerCall:F0} B; optimized {newMedian.NanosecondsPerCall:F0} ns / {newMedian.BytesPerCall:F0} B; speedup {speedup:F2}x");
        GC.KeepAlive(_sink);
    }

    private static void CheckExistingSelfTest(string runRoot)
    {
        var sourceRoot = Path.GetFullPath(Path.Combine(FindProjectDirectory(), "..", "..", "source", "desktop"));
        Anpeng.Desktop.NavMap.ReadApplicationArrays(Path.Combine(sourceRoot, "App.xaml.cs"));
        Anpeng.Desktop.RuntimeState.Configure(Path.Combine(runRoot, "existing-selftest"));
        // Same implementation as Run(chain: true, real: true), without its default TEMP output path.
        var runCore = typeof(SelfTest).GetMethod("RunCore", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("SelfTest.RunCore");
        var report = (SelfTestReport)runCore.Invoke(null, [true, true])!;
        File.WriteAllLines(Path.Combine(runRoot, "anpeng-selftest.txt"), report.Lines, new UTF8Encoding(false));
        Results.Add(new
        {
            Name = "Existing SelfTest.RunCore(chain:true, real:true)",
            report.Passed,
            report.Total,
            report.Ok,
            Checks = report.Checks.Select(check => new { check.Name, check.Ok }).ToArray(),
        });
        Require(report.Ok, $"Existing self-test failed: {report.Passed}/{report.Total}; see anpeng-selftest.txt");
        Console.WriteLine($"PASS existing self-test: {report.Passed}/{report.Total} assertions, including real alarm/sprinkler chain");
    }

    private static Measurement Measure(Func<ReportService.ReadingStatistics> operation, int iterations)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        for (var index = 0; index < iterations; index++) Consume(operation());
        var elapsed = Stopwatch.GetTimestamp() - start;
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        return new Measurement((double)elapsed / Stopwatch.Frequency * 1e9 / iterations, (double)bytes / iterations);
    }

    private static ReportService.ReadingStatistics Legacy(IEnumerable<SensorReading> readings, ReportRange range, DateTime now) =>
        new(ReportService.AverageTemp(readings, range, now), ReportService.PeakTemp(readings, range, now), ReportService.PeakSmoke(readings, range, now));

    private static void Compare(SensorReading[] readings, ReportRange range, string label)
    {
        var expected = Legacy(readings, range, Now);
        var actual = ReportService.SummarizeReadings(readings, range, Now);
        Require(expected.AverageTemp.Equals(actual.AverageTemp), $"{range}/{label} average: {expected.AverageTemp:R} != {actual.AverageTemp:R}");
        Require(expected.PeakTemp.Equals(actual.PeakTemp), $"{range}/{label} peak temp: {expected.PeakTemp:R} != {actual.PeakTemp:R}");
        Require(expected.PeakSmoke.Equals(actual.PeakSmoke), $"{range}/{label} peak smoke: {expected.PeakSmoke:R} != {actual.PeakSmoke:R}");
    }

    private static SensorReading Reading(DateTime time, double temperature, double smoke) => new() { Time = time, TempC = temperature, SmokePpm = smoke, BarnId = "B01" };
    private static void Consume(ReportService.ReadingStatistics statistics) => _sink = statistics.AverageTemp + statistics.PeakTemp + statistics.PeakSmoke;
    private static bool Throws<T>(Action action) where T : Exception { try { action(); return false; } catch (T) { return true; } }
    private static void Require(bool condition, string message) { _assertions++; if (!condition) throw new InvalidOperationException(message); }

    private static string FindProjectDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "OptimizationChecks.csproj"))) return directory.FullName;
        throw new InvalidOperationException("Run from a build beneath verification/OptimizationChecks.");
    }

    private sealed record Measurement(double NanosecondsPerCall, double BytesPerCall);

    private sealed class CountingEnumerable<T>(IEnumerable<T> values, bool allowRepeat) : IEnumerable<T>
    {
        public int Enumerations { get; private set; }
        public int Visited { get; private set; }
        public IEnumerator<T> GetEnumerator()
        {
            Enumerations++;
            if (!allowRepeat && Enumerations > 1) throw new InvalidOperationException("Source was enumerated more than once");
            return Enumerate().GetEnumerator();
        }
        private IEnumerable<T> Enumerate() { foreach (var value in values) { Visited++; yield return value; } }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
