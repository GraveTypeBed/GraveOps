using System.Text;
using System.Text.Json;
using GraveOps.Core.Hosts;
using GraveOps.Core.Providers;
using GraveOps.Core.Snapshots;
using GraveOps.Core.Targets;
using GraveOps.Platform.Windows;

var tests =
    new List<(string Name, Func<Task> Run)>
    {
        (
            "collector parses Windows fixture inventory",
            CollectorParsesFixtureAsync),
        (
            "collector exposes privacy-safe extended inventory",
            CollectorExposesSafeExtendedInventoryAsync),
        (
            "collector discovers canonical Windows applications",
            CollectorDiscoversApplicationsAsync),
        (
            "collector preserves PowerShell warnings",
            CollectorPreservesWarningsAsync),
        (
            "collector rejects malformed JSON safely",
            CollectorRejectsMalformedJsonAsync),
        (
            "collector honors cancellation",
            CollectorHonorsCancellationAsync),
        (
            "Windows inventory script remains read only",
            InventoryScriptRemainsReadOnlyAsync),
        (
            "local Windows advertises implemented capabilities",
            LocalWindowsCapabilitiesAsync),
        (
            "local Windows provider handles only local Windows",
            ProviderHandlesOnlyLocalWindowsAsync),
        (
            "provider registry resolves local Windows",
            ProviderRegistryResolvesWindowsAsync),
        (
            "provider capture preserves target lease",
            ProviderCapturePreservesLeaseAsync)
    };

if (args.Contains(
        "--live",
        StringComparer.OrdinalIgnoreCase))
{
    tests.Add(
        (
            "native Windows provider captures the current host",
            NativeWindowsProbeAsync));
}

var failures = 0;

foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine(
            $"PASS: {test.Name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine(
            $"FAIL: {test.Name}");
        Console.Error.WriteLine(
            exception);
    }
}

if (failures > 0)
{
    Console.Error.WriteLine(
        $"{failures} Windows provider contract test(s) failed.");

    return 1;
}

Console.WriteLine(
    $"All {tests.Count} Windows provider contract tests passed.");

return 0;

static async Task CollectorParsesFixtureAsync()
{
    var snapshot =
        await new WindowsSnapshotCollector(
                FixtureWindowsRunner.Create())
            .CaptureAsync();

    Equal(
        "fixture-windows",
        snapshot.Hostname,
        "hostname");
    Equal(
        "Microsoft Windows 11 Pro 10.0.26100 Build 26100",
        snapshot.OperatingSystem,
        "operating system");
    Equal(
        "10.0.26100 Build 26100",
        snapshot.Kernel,
        "kernel");
    Equal(
        "Fixture CPU",
        snapshot.CpuModel,
        "CPU model");
    Equal(
        "12.5% · 16 logical CPUs",
        snapshot.LoadAverage,
        "CPU load");
    Equal(
        "16.0 GiB used · 16.0 GiB available · 32.0 GiB total",
        snapshot.MemorySummary,
        "memory");
    Equal(
        2,
        snapshot.Storage.Count,
        "storage count");
    Equal(
        2,
        snapshot.Services.Count,
        "service count");
    Equal(
        1,
        snapshot.Containers.Count,
        "container count");
    Equal(
        1,
        snapshot.FailedUnits.Count,
        "failed service count");
    True(
        snapshot.RecentLogs.Any(line =>
            line.Contains(
                "fixture event",
                StringComparison.Ordinal)),
        "event log line");

    True(
        !snapshot.RecentLogs.Any(line =>
            line.Contains(
                "do-not-store",
                StringComparison.Ordinal)),
        "event log secrets redacted");
    Equal(
        "Docker 27.0.0 · 1 running",
        snapshot.DockerState,
        "Docker summary");
}

static async Task CollectorExposesSafeExtendedInventoryAsync()
{
    var snapshot =
        await new WindowsSnapshotCollector(
                FixtureWindowsRunner.Create())
            .CaptureAsync();

    Equal(
        3,
        snapshot.Processes.Count,
        "process count");
    Equal(
        2,
        snapshot.InstalledApplications.Count,
        "installed application count");
    Equal(
        2,
        snapshot.NetworkListeners.Count,
        "listener count");

    True(
        snapshot.Processes.Any(process =>
            process.Name.Equals(
                "qBittorrent.exe",
                StringComparison.OrdinalIgnoreCase)),
        "qBittorrent process");

    True(
        snapshot.NetworkListeners.Any(listener =>
            listener.LocalPort == 32400 &&
            listener.ProcessName.Equals(
                "Plex Media Server.exe",
                StringComparison.OrdinalIgnoreCase)),
        "Plex listener");

    True(
        typeof(ProcessSnapshot)
            .GetProperty("CommandLine") is null,
        "process inventory excludes command lines");

    True(
        typeof(ProcessSnapshot)
            .GetProperty("Owner") is null,
        "process inventory excludes owners");
}

static async Task CollectorDiscoversApplicationsAsync()
{
    var snapshot =
        await new WindowsSnapshotCollector(
                FixtureWindowsRunner.Create())
            .CaptureAsync();

    var names =
        snapshot.Integrations
            .Select(item =>
                item.Name)
            .ToHashSet(
                StringComparer.OrdinalIgnoreCase);

    True(
        names.Contains(
            "Plex"),
        "Plex discovery");
    True(
        names.Contains(
            "qBittorrent"),
        "qBittorrent discovery");
    True(
        names.Contains(
            "Sonarr"),
        "Sonarr discovery");

    var plex =
        snapshot.Integrations.Single(item =>
            item.Name.Equals(
                "Plex",
                StringComparison.OrdinalIgnoreCase));

    True(
        plex.Kind.Contains(
            "Windows service",
            StringComparison.OrdinalIgnoreCase),
        "Plex service identity");

    var qbit =
        snapshot.Integrations.Single(item =>
            item.Name.Equals(
                "qBittorrent",
                StringComparison.OrdinalIgnoreCase));

    True(
        qbit.Kind.Contains(
            "Native process",
            StringComparison.OrdinalIgnoreCase),
        "qBittorrent desktop identity");

    True(
        !snapshot.Integrations.Any(item =>
            item.Evidence.Contains(
                "do-not-store",
                StringComparison.Ordinal)),
        "application evidence secrets redacted");
}

static async Task CollectorPreservesWarningsAsync()
{
    var runner =
        FixtureWindowsRunner.Create();

    runner.Result =
        runner.Result with
        {
            ExitCode =
                5,
            StandardError =
                "fixture PowerShell warning"
        };

    var snapshot =
        await new WindowsSnapshotCollector(
                runner)
            .CaptureAsync();

    True(
        snapshot.Warnings.Any(warning =>
            warning.Contains(
                "fixture PowerShell warning",
                StringComparison.Ordinal)),
        "PowerShell stderr warning");

    True(
        snapshot.Warnings.Any(warning =>
            warning.Contains(
                "code 5",
                StringComparison.Ordinal)),
        "PowerShell exit-code warning");

    Equal(
        "fixture-windows",
        snapshot.Hostname,
        "valid output preserved after warning");
}

static async Task CollectorRejectsMalformedJsonAsync()
{
    var runner =
        FixtureWindowsRunner.Create();

    runner.Result =
        new WindowsPowerShellResult(
            0,
            "{ definitely not JSON",
            string.Empty);

    var snapshot =
        await new WindowsSnapshotCollector(
                runner)
            .CaptureAsync();

    Equal(
        "fixture-fallback",
        snapshot.Hostname,
        "malformed fixture fallback host");

    True(
        snapshot.Warnings.Any(warning =>
            warning.Contains(
                "could not be parsed",
                StringComparison.OrdinalIgnoreCase)),
        "malformed JSON warning");
}

static async Task CollectorHonorsCancellationAsync()
{
    using var cancellation =
        new CancellationTokenSource();

    cancellation.Cancel();

    await ThrowsAsync<OperationCanceledException>(
        () =>
            new WindowsSnapshotCollector(
                    FixtureWindowsRunner.Create())
                .CaptureAsync(
                    cancellation.Token));
}

static Task InventoryScriptRemainsReadOnlyAsync()
{
    var script =
        WindowsInventoryPowerShell.Script;

    foreach (var forbidden in new[]
             {
                 "Set-Item",
                 "New-Item",
                 "Remove-Item",
                 "Start-Service",
                 "Stop-Service",
                 "Restart-Service",
                 "Set-Service",
                 "Invoke-Command",
                 "Win32_Product",
                 "CommandLine"
             })
    {
        True(
            !script.Contains(
                forbidden,
                StringComparison.OrdinalIgnoreCase),
            $"forbidden script operation {forbidden}");
    }

    True(
        script.Contains(
            "Get-CimInstance",
            StringComparison.Ordinal),
        "CIM read path");

    True(
        script.Contains(
            "Get-WinEvent",
            StringComparison.Ordinal),
        "event-log read path");

    True(
        script.Contains(
            "Get-ItemProperty",
            StringComparison.Ordinal),
        "uninstall-registry read path");

    const string sample =
        "Write-Output 'fixture with spaces and symbols ; & |'";

    var encoded =
        WindowsPowerShellEncoding.EncodeScript(
            sample);

    var decoded =
        Encoding.Unicode.GetString(
            Convert.FromBase64String(
                encoded));

    Equal(
        sample,
        decoded,
        "encoded PowerShell round trip");

    return Task.CompletedTask;
}

static Task LocalWindowsCapabilitiesAsync()
{
    var capabilities =
        WindowsTargetCapabilityCatalog.ForLocalTarget();

    foreach (var capability in new[]
             {
                 CapabilityIds.HostSummaryRead,
                 CapabilityIds.StorageRead,
                 CapabilityIds.ServicesRead,
                 CapabilityIds.ProcessesRead,
                 CapabilityIds.InstalledApplicationsRead,
                 CapabilityIds.NetworkListenersRead,
                 CapabilityIds.ContainersRead,
                 CapabilityIds.EventLogRead,
                 CapabilityIds.ApplicationDiscovery
             })
    {
        True(
            capabilities.Supports(
                capability),
            capability);
    }

    True(
        !capabilities.Supports(
            CapabilityIds.JournalRead),
        "Windows omits journal capability");

    True(
        !capabilities.Supports(
            CapabilityIds.ApplicationApiTelemetry),
        "host foundation does not over-advertise application APIs");

    True(
        !capabilities.Supports(
            CapabilityIds.BackupInventoryRead),
        "host foundation does not over-advertise backup inventory");

    return Task.CompletedTask;
}

static Task ProviderHandlesOnlyLocalWindowsAsync()
{
    var provider =
        new LocalWindowsHostProvider(
            FixtureWindowsRunner.Create());

    True(
        provider.CanHandle(
            LocalWindowsTarget()),
        "local Windows target");

    True(
        !provider.CanHandle(
            LocalLinuxTarget()),
        "local Linux target rejected");

    True(
        !provider.CanHandle(
            RemoteWindowsTarget()),
        "remote Windows target rejected");

    return Task.CompletedTask;
}

static Task ProviderRegistryResolvesWindowsAsync()
{
    var provider =
        new LocalWindowsHostProvider(
            FixtureWindowsRunner.Create());

    var registry =
        new HostProviderRegistry(
            new[]
            {
                provider
            });

    var resolved =
        registry.Resolve(
            LocalWindowsTarget());

    Equal(
        HostProviderIds.LocalWindows,
        resolved.Descriptor.Id,
        "resolved provider ID");

    return Task.CompletedTask;
}

static async Task ProviderCapturePreservesLeaseAsync()
{
    var provider =
        new LocalWindowsHostProvider(
            FixtureWindowsRunner.Create());

    var target =
        LocalWindowsTarget();

    var lease =
        new TargetRefreshLease(
            target.Id,
            SelectionGeneration: 3,
            RefreshGeneration: 7,
            RefreshId:
                Guid.NewGuid());

    var probe =
        await provider.ProbeAsync(
            target);

    True(
        probe.Capabilities.Supports(
            CapabilityIds.EventLogRead),
        "provider probe capability");

    var envelope =
        await provider.CaptureAsync(
            target,
            lease);

    Equal(
        lease,
        envelope.Lease,
        "refresh lease");
    Equal(
        HostProviderIds.LocalWindows,
        envelope.ProviderId,
        "provider ID");
    Equal(
        "fixture-windows",
        envelope.Snapshot.Hostname,
        "captured hostname");
    True(
        envelope.Capabilities.Supports(
            CapabilityIds.ProcessesRead),
        "captured capabilities");
}

static async Task NativeWindowsProbeAsync()
{
    if (!OperatingSystem.IsWindows())
    {
        throw new InvalidOperationException(
            "--live requires a Windows host.");
    }

    var snapshot =
        await new LocalWindowsHostProbe()
            .CaptureAsync();

    True(
        !string.IsNullOrWhiteSpace(
            snapshot.Hostname),
        "native hostname");

    True(
        snapshot.OperatingSystem.Contains(
            "Windows",
            StringComparison.OrdinalIgnoreCase),
        "native operating system");

    Console.WriteLine(
        $"LIVE: {snapshot.Hostname} · " +
        $"{snapshot.OperatingSystem} · " +
        $"{snapshot.Storage.Count} volumes · " +
        $"{snapshot.Services.Count} relevant services · " +
        $"{snapshot.Processes.Count} processes · " +
        $"{snapshot.Integrations.Count} applications");
}

static TargetProfile LocalWindowsTarget() =>
    new(
        "local-windows",
        "Local Windows",
        HostProviderIds.LocalWindows,
        TargetPlatform.Windows,
        TargetLocation.Local,
        TargetConnectionProfile.Local);

static TargetProfile LocalLinuxTarget() =>
    new(
        "local-linux",
        "Local Linux",
        HostProviderIds.LocalLinux,
        TargetPlatform.Linux,
        TargetLocation.Local,
        TargetConnectionProfile.Local);

static TargetProfile RemoteWindowsTarget() =>
    new(
        "remote-windows",
        "Remote Windows",
        HostProviderIds.RemoteWindows,
        TargetPlatform.Windows,
        TargetLocation.Remote,
        new TargetConnectionProfile(
            TransportIds.WinRmHttps,
            Host: "windows.example.invalid",
            Port: 5986));

static void True(
    bool condition,
    string name)
{
    if (!condition)
    {
        throw new InvalidOperationException(
            $"Expected true: {name}");
    }
}

static void Equal<T>(
    T expected,
    T actual,
    string name)
{
    if (!EqualityComparer<T>.Default.Equals(
            expected,
            actual))
    {
        throw new InvalidOperationException(
            $"Mismatch for {name}. " +
            $"Expected {expected}; actual {actual}.");
    }
}

static async Task ThrowsAsync<TException>(
    Func<Task> action)
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

internal sealed class FixtureWindowsRunner :
    IWindowsPowerShellRunner
{
    public string RunnerId =>
        "fixture.windows";

    public bool IsWindowsTarget { get; set; } =
        true;

    public string MachineNameFallback =>
        "fixture-fallback";

    public WindowsPowerShellResult Result { get; set; } =
        new(
            0,
            string.Empty,
            string.Empty);

    public WindowsPowerShellRequest? LastRequest { get; private set; }

    public static FixtureWindowsRunner Create()
    {
        var document =
            new
            {
                Hostname =
                    "fixture-windows",
                OperatingSystem =
                    "Microsoft Windows 11 Pro 10.0.26100 Build 26100",
                Kernel =
                    "10.0.26100 Build 26100",
                Uptime =
                    "2d 4h 30m",
                SystemState =
                    "Running",
                CpuModel =
                    "Fixture CPU",
                CpuLoadPercent =
                    12.5,
                LogicalProcessorCount =
                    16,
                TotalMemoryKilobytes =
                    33_554_432L,
                FreeMemoryKilobytes =
                    16_777_216L,
                IpAddresses =
                    new[]
                    {
                        "192.0.2.20",
                        "2001:db8::20"
                    },
                DockerVersion =
                    "27.0.0",
                Storage =
                    new object[]
                    {
                        new
                        {
                            DeviceId =
                                "C:",
                            VolumeName =
                                "Windows",
                            FileSystem =
                                "NTFS",
                            Size =
                                1_099_511_627_776L,
                            FreeSpace =
                                549_755_813_888L
                        },
                        new
                        {
                            DeviceId =
                                "D:",
                            VolumeName =
                                "Media",
                            FileSystem =
                                "NTFS",
                            Size =
                                2_199_023_255_552L,
                            FreeSpace =
                                1_649_267_441_664L
                        }
                    },
                Services =
                    new object[]
                    {
                        new
                        {
                            Name =
                                "PlexUpdateService",
                            DisplayName =
                                "Plex Update Service",
                            State =
                                "Running",
                            StartMode =
                                "Auto",
                            PathName =
                                @"""C:\Program Files\Plex\Plex Media Server\Plex Update Service.exe"" --token do-not-store"
                        },
                        new
                        {
                            Name =
                                "Sonarr",
                            DisplayName =
                                "Sonarr",
                            State =
                                "Stopped",
                            StartMode =
                                "Auto",
                            PathName =
                                @"C:\ProgramData\Sonarr\bin\Sonarr.exe"
                        }
                    },
                Processes =
                    new object[]
                    {
                        new
                        {
                            ProcessId =
                                100,
                            Name =
                                "Plex Media Server.exe",
                            ExecutablePath =
                                @"C:\Program Files\Plex\Plex Media Server\Plex Media Server.exe",
                            WorkingSetSize =
                                536_870_912L,
                            KernelModeTime =
                                20_000_000L,
                            UserModeTime =
                                80_000_000L
                        },
                        new
                        {
                            ProcessId =
                                200,
                            Name =
                                "qBittorrent.exe",
                            ExecutablePath =
                                @"C:\Program Files\qBittorrent\qbittorrent.exe",
                            WorkingSetSize =
                                268_435_456L,
                            KernelModeTime =
                                10_000_000L,
                            UserModeTime =
                                40_000_000L
                        },
                        new
                        {
                            ProcessId =
                                300,
                            Name =
                                "powershell.exe",
                            ExecutablePath =
                                @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
                            WorkingSetSize =
                                134_217_728L,
                            KernelModeTime =
                                5_000_000L,
                            UserModeTime =
                                10_000_000L
                        }
                    },
                InstalledApplications =
                    new object[]
                    {
                        new
                        {
                            Name =
                                "qBittorrent",
                            Version =
                                "5.2.3",
                            Publisher =
                                "The qBittorrent project",
                            InstallLocation =
                                @"C:\Program Files\qBittorrent",
                            Source =
                                "HKLM uninstall registry"
                        },
                        new
                        {
                            Name =
                                "Docker Desktop",
                            Version =
                                "4.50.0",
                            Publisher =
                                "Docker Inc.",
                            InstallLocation =
                                @"C:\Program Files\Docker\Docker",
                            Source =
                                "HKLM uninstall registry"
                        }
                    },
                NetworkListeners =
                    new object[]
                    {
                        new
                        {
                            Protocol =
                                "TCP",
                            LocalAddress =
                                "0.0.0.0",
                            LocalPort =
                                32400,
                            OwningProcess =
                                100,
                            ProcessName =
                                "Plex Media Server.exe"
                        },
                        new
                        {
                            Protocol =
                                "TCP",
                            LocalAddress =
                                "127.0.0.1",
                            LocalPort =
                                8081,
                            OwningProcess =
                                200,
                            ProcessName =
                                "qBittorrent.exe"
                        }
                    },
                Containers =
                    new object[]
                    {
                        new
                        {
                            Name =
                                "sonarr",
                            Image =
                                "linuxserver/sonarr",
                            State =
                                "running",
                            Status =
                                "Up 2 hours",
                            Ports =
                                "0.0.0.0:8989->8989/tcp"
                        }
                    },
                FailedServices =
                    new[]
                    {
                        "Sonarr"
                    },
                Events =
                    new object[]
                    {
                        new
                        {
                            TimeCreated =
                                "2026-08-04T20:00:00-07:00",
                            Id =
                                1001,
                            Provider =
                                "Fixture Provider",
                            Level =
                                "Warning",
                            Message =
                                "fixture event token=do-not-store"
                        }
                    }
            };

        return new FixtureWindowsRunner
        {
            Result =
                new WindowsPowerShellResult(
                    0,
                    JsonSerializer.Serialize(
                        document),
                    string.Empty)
        };
    }

    public Task<WindowsPowerShellResult> ExecuteAsync(
        WindowsPowerShellRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        LastRequest =
            request;

        return Task.FromResult(
            Result);
    }
}
