# Optimization regression checks

This dependency-free console harness compiles the actual linked application logic from `source/desktop`. It does not start WinUI or call the production `RuntimeState`, and every `DataRepository` receives an explicit directory under this harness's `artifacts` folder. A test-only `RuntimeState` fixture supplies an isolated repository and monitor for the existing self-test. It never opens the application's default user data directory.

The only external references are the Windows SDK / WinRT managed assemblies already present in the application's Release output. `NuGet.Config` clears all package sources; restoration requires no new NuGet packages.

From this directory, after building the application:

```powershell
dotnet restore .\OptimizationChecks.csproj --configfile .\NuGet.Config
dotnet run --project .\OptimizationChecks.csproj --configuration Release --no-restore
```

Coverage:

- Summary versus all three original statistics APIs, with Day/Week/Month windows, empty and excluded sequences, inclusive bounds and adjacent ticks, deterministic random data, NaN/infinities, rounding cases, and DateTime overflow short-circuit behavior.
- One traversal for the summary versus three traversals for the original APIs.
- Exact saved UTF-8 byte equality with the original string serialization; 1,200 readings, long Unicode text, settings/data roundtrips, repeated overwrites, no remaining temporary files.
- One selection notification on a change and none when selecting the same ID again.
- Existing `SelfTest.RunCore(chain: true, real: true)`, the implementation used by the public CLI entry point, with all alarm and sprinkler chain assertions. The structured report is saved locally; buzzer output is replaced by the application's existing `AlarmBeeper.Sink` test hook.
  Its navigation-array assertion reads the actual `Order` / `Titles` initializers from `App.xaml.cs` without starting the WinUI application; it does not exercise page navigation.
- Five alternating measurement rounds after warmup, each using 10,000 operations over 1,200 readings. Allocations use `GC.GetAllocatedBytesForCurrentThread`; timing is a local microbenchmark, not a UI responsiveness guarantee or a pass/fail threshold.

Each execution keeps isolated data and a JSON evidence record in `artifacts/run-*`. `artifacts/latest-results.json` points to the newest result and records hashes of the three tested application source files.
