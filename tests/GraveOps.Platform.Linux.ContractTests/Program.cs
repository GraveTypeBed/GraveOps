using GraveOps.Core.Hosts;
using GraveOps.Platform.Linux;

var tests = new (string Name, Func<Task> Run)[]
{
    ("collector parses the existing Linux snapshot shape", CollectorParsesSnapshotAsync),
    ("compatibility probe delegates to the collector", CompatibilityProbeMatchesCollectorAsync),
    ("collector preserves nonzero warning behavior", CollectorPreservesWarningsAsync),
    ("runner cancellation reaches the collector", CollectorHonorsCancellationAsync)
};

var failures = 0;

foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS: {test.Name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL: {test.Name}");
        Console.Error.WriteLine(exception);
    }
}

if (failures > 0)
{
    Console.Error.WriteLine($"{failures} contract test(s) failed.");
    return 1;
}

Console.WriteLine($"All {tests.Length} Linux collector contract tests passed.");
return 0;

static async Task CollectorParsesSnapshotAsync()
{
    var cachePath = TemporaryCachePath();

    try
    {
        var snapshot =
            await new LinuxSnapshotCollector(
                    FixtureRunner.Create(),
                    cachePath)
                .CaptureAsync();

        Equal("fixture-host", snapshot.Hostname, "hostname");
        Equal("Fixture Linux", snapshot.OperatingSystem, "operating system");
        Equal("6.12.0-fixture", snapshot.Kernel, "kernel");
        Equal("Fixture CPU", snapshot.CpuModel, "CPU model");
        Equal("0.10  0.20  0.30 · 8 logical CPUs", snapshot.LoadAverage, "load average");
        Equal("4.0 GiB used · 4.0 GiB available · 8.0 GiB total", snapshot.MemorySummary, "memory");
        Equal(1, snapshot.Storage.Count, "storage count");
        Equal(1, snapshot.Services.Count, "service count");
        Equal(1, snapshot.Containers.Count, "container count");
        Equal(1, snapshot.Integrations.Count, "integration count");
        Equal("Plex", snapshot.Integrations[0].Name, "integration identity");
        Equal(1, snapshot.FailedUnits.Count, "failed-unit count");
        True(snapshot.RecentLogs.Any(line => line.Contains("fixture warning", StringComparison.Ordinal)), "journal line");
    }
    finally
    {
        File.Delete(cachePath);
    }
}

static async Task CompatibilityProbeMatchesCollectorAsync()
{
    var directCache = TemporaryCachePath();
    var adapterCache = TemporaryCachePath();

    try
    {
        var direct =
            await new LinuxSnapshotCollector(
                    FixtureRunner.Create(),
                    directCache)
                .CaptureAsync();
        var adapter =
            await new LocalLinuxHostProbe(
                    new LinuxSnapshotCollector(
                        FixtureRunner.Create(),
                        adapterCache))
                .CaptureAsync();

        Equal(direct.Hostname, adapter.Hostname, "hostname parity");
        Equal(direct.OperatingSystem, adapter.OperatingSystem, "OS parity");
        Equal(direct.Kernel, adapter.Kernel, "kernel parity");
        Equal(direct.Storage.ToArray(), adapter.Storage.ToArray(), "storage parity");
        Equal(direct.Services.ToArray(), adapter.Services.ToArray(), "service parity");
        Equal(direct.Containers.ToArray(), adapter.Containers.ToArray(), "container parity");
        Equal(direct.Integrations.ToArray(), adapter.Integrations.ToArray(), "integration parity");
    }
    finally
    {
        File.Delete(directCache);
        File.Delete(adapterCache);
    }
}

static async Task CollectorPreservesWarningsAsync()
{
    var runner = FixtureRunner.Create();
    runner.Results[FixtureRunner.Key("df")] =
        new LinuxCommandResult(
            1,
            string.Empty,
            "fixture df failure");

    var cachePath = TemporaryCachePath();

    try
    {
        var snapshot =
            await new LinuxSnapshotCollector(
                    runner,
                    cachePath)
                .CaptureAsync();

        True(
            snapshot.Warnings.Contains(
                "storage: fixture df failure"),
            "nonzero warning");
    }
    finally
    {
        File.Delete(cachePath);
    }
}

static async Task CollectorHonorsCancellationAsync()
{
    using var cancellation =
        new CancellationTokenSource();
    cancellation.Cancel();

    await ThrowsAsync<OperationCanceledException>(
        () => new LinuxSnapshotCollector(
                FixtureRunner.Create(),
                TemporaryCachePath())
            .CaptureAsync(cancellation.Token));
}

static string TemporaryCachePath() =>
    Path.Combine(
        Path.GetTempPath(),
        $"graveops-linux-collector-{Guid.NewGuid():N}.json");

static void True(bool condition, string name)
{
    if (!condition)
        throw new InvalidOperationException($"Expected true: {name}");
}

static void Equal<T>(T expected, T actual, string name)
{
    if (expected is Array expectedArray &&
        actual is Array actualArray)
    {
        if (!expectedArray.Cast<object?>().SequenceEqual(actualArray.Cast<object?>()))
            throw new InvalidOperationException($"Mismatch: {name}");
        return;
    }

    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            $"Mismatch for {name}. Expected {expected}; actual {actual}.");
    }
}

static async Task ThrowsAsync<TException>(Func<Task> action)
    where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(
        $"Expected {typeof(TException).Name}.");
}

internal sealed class FixtureRunner : ILinuxCommandRunner
{
    public Dictionary<string, LinuxCommandResult> Results { get; } =
        new(StringComparer.Ordinal);

    private readonly Dictionary<string, LinuxTextFileResult> _files =
        new(StringComparer.Ordinal);

    public string RunnerId => "fixture.linux";
    public string CacheKey => "fixture";
    public bool IsLinuxTarget => true;
    public string MachineNameFallback => "fixture-fallback";

    public static FixtureRunner Create()
    {
        var runner = new FixtureRunner();

        runner.Results[Key("hostname")] = Ok("fixture-host");
        runner.Results[Key("uname", "-r")] = Ok("6.12.0-fixture");
        runner.Results[Key("uptime", "-p")] = Ok("up 2 hours");
        runner.Results[Key("systemctl", "is-system-running")] = Ok("running");
        runner.Results[Key("docker", "version", "--format", "{{.Server.Version}}")] = Ok("27.0.0");
        runner.Results[Key("hostname", "-I")] = Ok("192.0.2.10");
        runner.Results[Key("df")] = Ok(
            "Filesystem Type Size Used Avail Use% Mounted on\n/dev/sda2 ext4 100G 40G 60G 40% /");
        runner.Results[Key("systemctl", "list-unit-files")] = Ok(
            "plexmediaserver.service enabled");
        runner.Results[Key("systemctl", "show")] = Ok(
            "Id=plexmediaserver.service\nDescription=Plex Media Server\nLoadState=loaded\nActiveState=active\nSubState=running\nUnitFileState=enabled");
        runner.Results[Key("docker", "ps")] = Ok(
            "plex\tplexinc/pms-docker\trunning\tUp 2 hours\t0.0.0.0:32400->32400/tcp");
        runner.Results[Key("systemctl", "--failed")] = Ok(
            "fixture.service loaded failed failed Fixture");
        runner.Results[Key("journalctl")] = Ok(
            "2026-08-04T17:00:00-0700 fixture warning\n-- cursor: fixture-cursor");

        runner._files["/etc/os-release"] = FileOk(
            "PRETTY_NAME=\"Fixture Linux\"\n");
        runner._files["/proc/cpuinfo"] = FileOk(
            "processor : 0\nmodel name : Fixture CPU\n");
        runner._files["/proc/loadavg"] = FileOk(
            "0.10 0.20 0.30 1/100 123\n");
        runner._files["/proc/meminfo"] = FileOk(
            "MemTotal: 8388608 kB\nMemAvailable: 4194304 kB\n");
        runner._files["/proc/sys/kernel/random/boot_id"] = FileOk(
            "fixture-boot\n");

        return runner;
    }

    public Task<int> GetLogicalProcessorCountAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(8);
    }

    public Task<LinuxTextFileResult> ReadTextFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            _files.TryGetValue(path, out var result)
                ? result
                : new LinuxTextFileResult(false, string.Empty));
    }

    public Task<LinuxCommandResult> ExecuteAsync(
        LinuxCommandRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var exact = Key(
            request.Executable,
            request.Arguments.ToArray());
        var broad = Key(
            request.Executable,
            request.Arguments.FirstOrDefault() ?? string.Empty);
        var executable = Key(request.Executable);

        var result =
            Results.TryGetValue(exact, out var exactResult)
                ? exactResult
                : Results.TryGetValue(broad, out var broadResult)
                    ? broadResult
                    : Results.TryGetValue(executable, out var executableResult)
                        ? executableResult
                        : Ok(string.Empty);

        return Task.FromResult(result);
    }

    public static string Key(
        string executable,
        params string[] arguments)
    {
        var significant =
            arguments.FirstOrDefault() ??
            string.Empty;
        return string.IsNullOrEmpty(significant)
            ? executable
            : $"{executable}|{significant}";
    }

    private static LinuxCommandResult Ok(string output) =>
        new(0, output, string.Empty);

    private static LinuxTextFileResult FileOk(string content) =>
        new(true, content);
}
