
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GraveOps.Core.Hosts;
using GraveOps.Platform.Linux;

namespace GraveOps.Desktop.Linux;

public enum LinuxHostKind
{
    Local,
    RemoteLinux
}

public enum LinuxHostAuthentication
{
    Agent,
    PrivateKey,
    Password
}

public sealed class LinuxHostProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Linux host";
    public LinuxHostKind Kind { get; set; } = LinuxHostKind.RemoteLinux;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 22;
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = "Server";
    public LinuxHostAuthentication Authentication { get; set; } =
        LinuxHostAuthentication.Agent;
    public string PrivateKeyPath { get; set; } = string.Empty;
    public string HostKeyFingerprint { get; set; } = string.Empty;
    public DateTimeOffset? LastDetectedAt { get; set; }

    public bool IsLocal => Kind == LinuxHostKind.Local;

    public string DisplayName =>
        string.IsNullOrWhiteSpace(Name)
            ? IsLocal
                ? Environment.MachineName
                : Host
            : Name;

    public string KindLabel =>
        IsLocal
            ? "Local Linux"
            : "Remote Linux · SSH";

    public string AuthenticationLabel =>
        Authentication switch
        {
            LinuxHostAuthentication.Agent => "SSH agent",
            LinuxHostAuthentication.PrivateKey => "Private key",
            LinuxHostAuthentication.Password => "Password",
            _ => Authentication.ToString()
        };

    public string ConnectionSummary =>
        IsLocal
            ? $"{KindLabel} · {Role}"
            : $"{Username}@{Host}:{Port} · {Role}";
}

public sealed record LinuxLanCandidate(
    string Address,
    string Device,
    string MacAddress,
    string State)
{
    public string Summary =>
        string.IsNullOrWhiteSpace(MacAddress)
            ? $"{Address} · {Device} · {State}"
            : $"{Address} · {MacAddress} · {Device} · {State}";
}

public sealed record LinuxHostKeyScanResult(
    bool Success,
    string Fingerprint,
    string KeyLine,
    string Summary,
    string Detail);

public sealed record LinuxConnectionTestResult(
    bool Success,
    string Summary,
    string Detail,
    string Fingerprint);

public sealed class ControlPlaneActivityRow
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public string Kind { get; set; } = "System";
    public string Target { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string NavigationName { get; set; } = string.Empty;
    public bool IsUnread { get; set; } = true;

    public string DisplayTime =>
        Timestamp.ToLocalTime().ToString("g");

    public string ReadState =>
        IsUnread ? "NEW" : string.Empty;
}

public sealed class ControlPlaneJobRow
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string State { get; set; } = "Queued";
    public int Progress { get; set; }
    public string Detail { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? CompletedAt { get; set; }
    public bool Background { get; set; }

    public string ProgressText =>
        $"{Math.Clamp(Progress, 0, 100)}%";

    public string DurationText
    {
        get
        {
            var end = CompletedAt ?? DateTimeOffset.Now;
            var duration = end - StartedAt;

            if (duration.TotalHours >= 1)
                return $"{duration.TotalHours:0.0}h";

            if (duration.TotalMinutes >= 1)
                return $"{duration.TotalMinutes:0.0}m";

            return $"{Math.Max(0, duration.TotalSeconds):0}s";
        }
    }

    public string DisplayTime =>
        StartedAt.ToLocalTime().ToString("g");
}

public sealed class LinuxHostProfileStore
{
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true
    };

    private List<LinuxHostProfile> _profiles;

    public LinuxHostProfileStore(string configDirectory)
    {
        Directory.CreateDirectory(configDirectory);
        FilePath = Path.Combine(
            configDirectory,
            "hosts.json");
        _profiles = Load();
        EnsureLocalProfile();
    }

    public string FilePath { get; }

    public IReadOnlyList<LinuxHostProfile> Profiles =>
        _profiles
            .OrderBy(profile => profile.IsLocal ? 0 : 1)
            .ThenBy(profile => profile.DisplayName)
            .ToArray();

    public LinuxHostProfile? Find(string id) =>
        _profiles.FirstOrDefault(profile =>
            profile.Id.Equals(
                id,
                StringComparison.OrdinalIgnoreCase));

    public LinuxHostProfile Upsert(LinuxHostProfile profile)
    {
        Validate(profile);

        var existing = Find(profile.Id);

        if (existing is null)
        {
            _profiles.Add(profile);
        }
        else
        {
            var index = _profiles.IndexOf(existing);
            _profiles[index] = profile;
        }

        Save();
        return profile;
    }

    public bool Delete(string id)
    {
        var profile = Find(id);

        if (profile is null || profile.IsLocal)
            return false;

        var removed = _profiles.Remove(profile);

        if (removed)
            Save();

        return removed;
    }

    public void TouchDetection(
        string id,
        DateTimeOffset capturedAt)
    {
        var profile = Find(id);

        if (profile is null)
            return;

        profile.LastDetectedAt = capturedAt;
        Save();
    }

    public static void Validate(
        LinuxHostProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Id))
            throw new InvalidOperationException(
                "Host profile ID is required.");

        if (string.IsNullOrWhiteSpace(profile.Name))
            throw new InvalidOperationException(
                "Display name is required.");

        if (profile.IsLocal)
            return;

        if (string.IsNullOrWhiteSpace(profile.Host))
            throw new InvalidOperationException(
                "Remote host or IP address is required.");

        if (profile.Port is < 1 or > 65535)
            throw new InvalidOperationException(
                "SSH port must be between 1 and 65535.");

        if (string.IsNullOrWhiteSpace(profile.Username))
            throw new InvalidOperationException(
                "SSH username is required.");

        if (profile.Authentication ==
                LinuxHostAuthentication.PrivateKey &&
            string.IsNullOrWhiteSpace(
                profile.PrivateKeyPath))
        {
            throw new InvalidOperationException(
                "Private-key authentication requires a key path.");
        }
    }

    private List<LinuxHostProfile> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new List<LinuxHostProfile>();

            return JsonSerializer.Deserialize<
                       List<LinuxHostProfile>>(
                       File.ReadAllText(FilePath),
                       _json) ??
                   new List<LinuxHostProfile>();
        }
        catch
        {
            return new List<LinuxHostProfile>();
        }
    }

    private void EnsureLocalProfile()
    {
        var local = _profiles.FirstOrDefault(profile =>
            profile.IsLocal);

        if (local is null)
        {
            _profiles.Insert(
                0,
                new LinuxHostProfile
                {
                    Id = "local",
                    Name = Environment.MachineName,
                    Kind = LinuxHostKind.Local,
                    Host = "127.0.0.1",
                    Port = 22,
                    Username = Environment.UserName,
                    Role = "Local control plane",
                    Authentication =
                        LinuxHostAuthentication.Agent
                });
            Save();
            return;
        }

        local.Id = "local";

        if (string.IsNullOrWhiteSpace(local.Name))
            local.Name = Environment.MachineName;

        if (string.IsNullOrWhiteSpace(local.Username))
            local.Username = Environment.UserName;

        Save();
    }

    private void Save()
    {
        var temporary = FilePath + ".tmp";

        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(
                _profiles,
                _json));

        File.Move(
            temporary,
            FilePath,
            overwrite: true);
    }
}

public sealed class LinuxCredentialStore
{
    public bool IsAvailable =>
        CommandExists("secret-tool");

    public string CapabilityText =>
        IsAvailable
            ? "Secret Service keyring available"
            : "secret-tool unavailable · secrets cannot be saved";

    public async Task<string?> LookupAsync(
        string hostId,
        string kind,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
            return null;

        var result = await RunAsync(
            "secret-tool",
            new[]
            {
                "lookup",
                "application",
                "GraveOps",
                "host-id",
                hostId,
                "kind",
                kind
            },
            standardInput: null,
            cancellationToken);

        return result.ExitCode == 0 &&
               !string.IsNullOrWhiteSpace(result.StandardOutput)
            ? result.StandardOutput.TrimEnd()
            : null;
    }

    public async Task SaveAsync(
        string hostId,
        string kind,
        string secret,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException(
                "Secret Service is unavailable. Install libsecret-tools or use SSH agent authentication.");
        }

        if (string.IsNullOrEmpty(secret))
            return;

        var result = await RunAsync(
            "secret-tool",
            new[]
            {
                "store",
                $"--label=GraveOps {kind} for {hostId}",
                "application",
                "GraveOps",
                "host-id",
                hostId,
                "kind",
                kind
            },
            secret + Environment.NewLine,
            cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(result.StandardError)
                    ? "The desktop keyring rejected the secret."
                    : result.StandardError.Trim());
        }
    }

    public async Task ClearAsync(
        string hostId,
        string kind,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
            return;

        await RunAsync(
            "secret-tool",
            new[]
            {
                "clear",
                "application",
                "GraveOps",
                "host-id",
                hostId,
                "kind",
                kind
            },
            standardInput: null,
            cancellationToken);
    }

    private static bool CommandExists(string command)
    {
        var path =
            Environment.GetEnvironmentVariable("PATH") ??
            string.Empty;

        return path.Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries)
            .Any(directory =>
                File.Exists(
                    Path.Combine(
                        directory,
                        command)));
    }

    private static async Task<ProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string? standardInput,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                RedirectStandardInput =
                    standardInput is not null,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.Start();

        if (standardInput is not null)
        {
            await process.StandardInput.WriteAsync(
                standardInput);
            process.StandardInput.Close();
        }

        var stdout =
            process.StandardOutput.ReadToEndAsync(
                cancellationToken);
        var stderr =
            process.StandardError.ReadToEndAsync(
                cancellationToken);

        await process.WaitForExitAsync(
            cancellationToken);

        return new ProcessResult(
            process.ExitCode,
            await stdout,
            await stderr);
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}

public sealed class LinuxControlPlaneStateStore
{
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;
    private ControlPlaneDocument _document;

    public LinuxControlPlaneStateStore(
        string configDirectory)
    {
        Directory.CreateDirectory(configDirectory);
        _filePath = Path.Combine(
            configDirectory,
            "control-plane-state.json");
        _document = Load();

        foreach (var job in _document.Jobs.Where(job =>
                     job.State.Equals(
                         "Running",
                         StringComparison.OrdinalIgnoreCase)))
        {
            job.State = "Interrupted";
            job.Detail =
                "GraveOps closed before the job completed.";
            job.CompletedAt = DateTimeOffset.Now;
        }

        Save();
    }

    public string ActiveHostId =>
        string.IsNullOrWhiteSpace(
            _document.ActiveHostId)
            ? "local"
            : _document.ActiveHostId;

    public DateTimeOffset? MaintenanceUntil =>
        _document.MaintenanceUntil;

    public bool IsMaintenanceActive =>
        MaintenanceUntil is { } until &&
        until > DateTimeOffset.Now;

    public TimeSpan MaintenanceRemaining =>
        IsMaintenanceActive
            ? MaintenanceUntil!.Value -
              DateTimeOffset.Now
            : TimeSpan.Zero;

    public IReadOnlyList<ControlPlaneActivityRow>
        Activities =>
            _document.Activities
                .OrderByDescending(row => row.Timestamp)
                .ToArray();

    public IReadOnlyList<ControlPlaneJobRow> Jobs =>
        _document.Jobs
            .OrderByDescending(row => row.StartedAt)
            .ToArray();

    public int UnreadActivityCount =>
        _document.Activities.Count(row =>
            row.IsUnread);

    public int RunningJobCount =>
        _document.Jobs.Count(row =>
            row.State.Equals(
                "Running",
                StringComparison.OrdinalIgnoreCase) ||
            row.State.Equals(
                "Queued",
                StringComparison.OrdinalIgnoreCase));

    public void SetActiveHost(string hostId)
    {
        _document.ActiveHostId =
            string.IsNullOrWhiteSpace(hostId)
                ? "local"
                : hostId;
        Save();
    }

    public void SetMaintenance(TimeSpan? duration)
    {
        _document.MaintenanceUntil =
            duration is null
                ? null
                : DateTimeOffset.Now + duration.Value;
        Save();
    }

    public bool ExpireMaintenanceIfNeeded()
    {
        if (_document.MaintenanceUntil is not { } until ||
            until > DateTimeOffset.Now)
        {
            return false;
        }

        _document.MaintenanceUntil = null;
        Save();
        return true;
    }

    public ControlPlaneActivityRow RecordActivity(
        string kind,
        string target,
        string title,
        string detail,
        string navigationName = "",
        bool unread = true)
    {
        var row = new ControlPlaneActivityRow
        {
            Kind = kind,
            Target = target,
            Title = title,
            Detail = detail,
            NavigationName = navigationName,
            IsUnread = unread
        };

        _document.Activities.Insert(
            0,
            row);

        _document.Activities = _document.Activities
            .OrderByDescending(item => item.Timestamp)
            .Take(300)
            .ToList();

        Save();
        return row;
    }

    public string StartJob(
        string name,
        string target,
        string detail,
        bool background)
    {
        if (background)
        {
            _document.Jobs.RemoveAll(job =>
                job.Background &&
                job.Name.Equals(
                    name,
                    StringComparison.OrdinalIgnoreCase) &&
                job.Target.Equals(
                    target,
                    StringComparison.OrdinalIgnoreCase) &&
                !job.State.Equals(
                    "Running",
                    StringComparison.OrdinalIgnoreCase));
        }

        var row = new ControlPlaneJobRow
        {
            Name = name,
            Target = target,
            State = "Running",
            Progress = 5,
            Detail = detail,
            Background = background
        };

        _document.Jobs.Insert(
            0,
            row);

        TrimJobs();
        Save();
        return row.Id;
    }

    public void UpdateJob(
        string id,
        int progress,
        string detail)
    {
        var job = _document.Jobs.FirstOrDefault(row =>
            row.Id.Equals(
                id,
                StringComparison.OrdinalIgnoreCase));

        if (job is null)
            return;

        job.Progress = Math.Clamp(progress, 0, 100);
        job.Detail = detail;
        Save();
    }

    public void CompleteJob(
        string id,
        bool success,
        string detail)
    {
        var job = _document.Jobs.FirstOrDefault(row =>
            row.Id.Equals(
                id,
                StringComparison.OrdinalIgnoreCase));

        if (job is null)
            return;

        job.State = success
            ? "Completed"
            : "Failed";
        job.Progress = success
            ? 100
            : Math.Max(job.Progress, 1);
        job.Detail = detail;
        job.CompletedAt = DateTimeOffset.Now;
        TrimJobs();
        Save();
    }

    public void MarkAllActivitiesRead()
    {
        foreach (var activity in _document.Activities)
            activity.IsUnread = false;

        Save();
    }

    public void ClearActivities()
    {
        _document.Activities.Clear();
        Save();
    }

    public void ClearCompletedJobs()
    {
        _document.Jobs.RemoveAll(job =>
            !job.State.Equals(
                "Running",
                StringComparison.OrdinalIgnoreCase) &&
            !job.State.Equals(
                "Queued",
                StringComparison.OrdinalIgnoreCase));
        Save();
    }

    private void TrimJobs()
    {
        _document.Jobs = _document.Jobs
            .OrderByDescending(item => item.StartedAt)
            .Take(100)
            .ToList();
    }

    private ControlPlaneDocument Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new ControlPlaneDocument();

            var document =
                JsonSerializer.Deserialize<
                    ControlPlaneDocument>(
                    File.ReadAllText(_filePath),
                    _json) ??
                new ControlPlaneDocument();

            document.Activities ??=
                new List<ControlPlaneActivityRow>();
            document.Jobs ??=
                new List<ControlPlaneJobRow>();

            return document;
        }
        catch
        {
            return new ControlPlaneDocument();
        }
    }

    private void Save()
    {
        var temporary = _filePath + ".tmp";

        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(
                _document,
                _json));

        File.Move(
            temporary,
            _filePath,
            overwrite: true);
    }

    private sealed class ControlPlaneDocument
    {
        public string ActiveHostId { get; set; } =
            "local";
        public DateTimeOffset? MaintenanceUntil
        {
            get;
            set;
        }
        public List<ControlPlaneActivityRow> Activities
        {
            get;
            set;
        } = new();
        public List<ControlPlaneJobRow> Jobs
        {
            get;
            set;
        } = new();
    }
}

public sealed class LinuxControlPlaneCoordinator
{
    private readonly LocalLinuxHostProbe _localProbe =
        new();

    public LinuxControlPlaneCoordinator()
    {
        var home = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        var configRoot =
            Environment.GetEnvironmentVariable(
                "XDG_CONFIG_HOME");

        if (string.IsNullOrWhiteSpace(configRoot))
            configRoot = Path.Combine(home, ".config");

        ConfigDirectory = Path.Combine(
            configRoot,
            "GraveOps");
        KnownHostsDirectory = Path.Combine(
            ConfigDirectory,
            "known-hosts");

        Directory.CreateDirectory(
            ConfigDirectory);
        Directory.CreateDirectory(
            KnownHostsDirectory);

        Profiles = new LinuxHostProfileStore(
            ConfigDirectory);
        State = new LinuxControlPlaneStateStore(
            ConfigDirectory);
        Credentials = new LinuxCredentialStore();

        if (Profiles.Find(State.ActiveHostId) is null)
            State.SetActiveHost("local");
    }

    public string ConfigDirectory { get; }
    public string KnownHostsDirectory { get; }
    public LinuxHostProfileStore Profiles { get; }
    public LinuxControlPlaneStateStore State { get; }
    public LinuxCredentialStore Credentials { get; }

    public LinuxHostProfile ActiveProfile =>
        Profiles.Find(State.ActiveHostId) ??
        Profiles.Find("local") ??
        throw new InvalidOperationException(
            "The local GraveOps host profile is missing.");

    public void SetActive(string hostId)
    {
        if (Profiles.Find(hostId) is null)
        {
            throw new InvalidOperationException(
                "The selected host profile no longer exists.");
        }

        State.SetActiveHost(hostId);
    }

    public async Task<HostSnapshot> CaptureActiveAsync(
        CancellationToken cancellationToken = default)
    {
        var profile = ActiveProfile;

        if (profile.IsLocal)
        {
            return await _localProbe.CaptureAsync(
                cancellationToken);
        }

        var probe = new RemoteLinuxHostProbe(
            profile,
            Credentials,
            KnownHostsDirectory);

        return await probe.CaptureAsync(
            cancellationToken);
    }

    public async Task<LinuxHostKeyScanResult>
        ScanFingerprintAsync(
            LinuxHostProfile profile,
            CancellationToken cancellationToken = default) =>
        await LinuxSshTransport.ScanFingerprintAsync(
            profile,
            cancellationToken);

    public async Task<LinuxConnectionTestResult>
        TestAsync(
            LinuxHostProfile profile,
            string? suppliedSecret = null,
            CancellationToken cancellationToken = default)
    {
        LinuxHostProfileStore.Validate(profile);

        if (profile.IsLocal)
        {
            var snapshot =
                await _localProbe.CaptureAsync(
                    cancellationToken);

            return new LinuxConnectionTestResult(
                true,
                "Local Linux provider is available.",
                $"{snapshot.Hostname} · {snapshot.OperatingSystem}",
                "local");
        }

        var scan = await ScanFingerprintAsync(
            profile,
            cancellationToken);

        if (!scan.Success)
        {
            return new LinuxConnectionTestResult(
                false,
                scan.Summary,
                scan.Detail,
                scan.Fingerprint);
        }

        if (string.IsNullOrWhiteSpace(
                profile.HostKeyFingerprint))
        {
            return new LinuxConnectionTestResult(
                false,
                "Pin the scanned host-key fingerprint before testing authentication.",
                "Copy the fingerprint into the profile, save it, then run Test Connection again.",
                scan.Fingerprint);
        }

        if (!profile.HostKeyFingerprint.Equals(
                scan.Fingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            return new LinuxConnectionTestResult(
                false,
                "SSH host-key fingerprint mismatch.",
                $"Expected {profile.HostKeyFingerprint}; received {scan.Fingerprint}. Connection was blocked.",
                scan.Fingerprint);
        }

        try
        {
            var result =
                await LinuxSshTransport.RunScriptAsync(
                    profile,
                    Credentials,
                    KnownHostsDirectory,
                    """
                    set -e
                    printf '__GRAVEOPS_OK__\n'
                    hostname
                    . /etc/os-release 2>/dev/null || true
                    printf '%s\n' "${PRETTY_NAME:-Linux}"
                    """,
                    suppliedSecret,
                    cancellationToken);

            var lines = result.StandardOutput
                .Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries);

            if (lines.Length < 3 ||
                !lines[0].Equals(
                    "__GRAVEOPS_OK__",
                    StringComparison.Ordinal))
            {
                return new LinuxConnectionTestResult(
                    false,
                    "The SSH session connected but returned an unexpected response.",
                    result.StandardOutput,
                    scan.Fingerprint);
            }

            return new LinuxConnectionTestResult(
                true,
                "SSH connection and provider command execution succeeded.",
                $"{lines[1]} · {lines[2]}",
                scan.Fingerprint);
        }
        catch (Exception exception)
        {
            return new LinuxConnectionTestResult(
                false,
                "SSH connection failed.",
                exception.Message,
                scan.Fingerprint);
        }
    }

    public async Task SaveProfileAsync(
        LinuxHostProfile profile,
        string secret,
        bool saveSecret,
        CancellationToken cancellationToken = default)
    {
        LinuxHostProfileStore.Validate(profile);

        if (profile.IsLocal)
        {
            profile.Id = "local";
            profile.Host = "127.0.0.1";
            profile.Port = 22;
            profile.Username =
                string.IsNullOrWhiteSpace(
                    profile.Username)
                    ? Environment.UserName
                    : profile.Username;
            profile.Authentication =
                LinuxHostAuthentication.Agent;
            profile.PrivateKeyPath =
                string.Empty;
            profile.HostKeyFingerprint =
                string.Empty;
        }

        Profiles.Upsert(profile);

        if (!profile.IsLocal &&
            saveSecret &&
            !string.IsNullOrEmpty(secret))
        {
            var kind =
                profile.Authentication ==
                LinuxHostAuthentication.Password
                    ? "password"
                    : "passphrase";

            await Credentials.SaveAsync(
                profile.Id,
                kind,
                secret,
                cancellationToken);
        }
    }

    public async Task DeleteProfileAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var profile = Profiles.Find(id);

        if (profile is null || profile.IsLocal)
            return;

        await Credentials.ClearAsync(
            id,
            "password",
            cancellationToken);
        await Credentials.ClearAsync(
            id,
            "passphrase",
            cancellationToken);

        Profiles.Delete(id);

        var knownHostsPath = Path.Combine(
            KnownHostsDirectory,
            $"{Regex.Replace(id, @"[^A-Za-z0-9_.-]", "_")}.known_hosts");

        try
        {
            File.Delete(knownHostsPath);
        }
        catch
        {
            // Known-host cleanup is best effort.
        }

        if (State.ActiveHostId.Equals(
                id,
                StringComparison.OrdinalIgnoreCase))
        {
            State.SetActiveHost("local");
        }
    }

    public OpsBackupSnapshot
        CreateRemoteBackupSnapshot(
            HostSnapshot snapshot)
    {
        return new OpsBackupSnapshot(
            OpsSeverity.Info,
            "REMOTE",
            "Remote Linux provider",
            "Remote backup inventory is not captured by the V4.2 host foundation.",
            new[]
            {
                $"Target · {ActiveProfile.ConnectionSummary}",
                $"Host capture · {snapshot.CapturedAt.ToLocalTime():g}",
                "Backup mutations remain disabled for remote targets."
            },
            Array.Empty<OpsBackupUnit>(),
            Array.Empty<OpsBackupArtifact>());
    }

    public async Task<IReadOnlyList<LinuxLanCandidate>>
        DiscoverLanAsync(
            CancellationToken cancellationToken = default)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ip",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.StartInfo.ArgumentList.Add(
                "neigh");
            process.StartInfo.ArgumentList.Add(
                "show");

            process.Start();

            var stdout =
                process.StandardOutput.ReadToEndAsync(
                    cancellationToken);

            await process.WaitForExitAsync(
                cancellationToken);

            if (process.ExitCode != 0)
                return Array.Empty<LinuxLanCandidate>();

            var rows = new List<LinuxLanCandidate>();

            foreach (var line in
                     (await stdout).Split(
                         '\n',
                         StringSplitOptions.RemoveEmptyEntries))
            {
                var match = Regex.Match(
                    line,
                    @"^(?<ip>\S+)\s+dev\s+(?<dev>\S+)(?:\s+lladdr\s+(?<mac>\S+))?\s+(?<state>\S+)$");

                if (!match.Success)
                    continue;

                var state =
                    match.Groups["state"].Value;

                if (state.Equals(
                        "FAILED",
                        StringComparison.OrdinalIgnoreCase) ||
                    state.Equals(
                        "INCOMPLETE",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                rows.Add(
                    new LinuxLanCandidate(
                        match.Groups["ip"].Value,
                        match.Groups["dev"].Value,
                        match.Groups["mac"].Value,
                        state));
            }

            return rows
                .GroupBy(
                    row => row.Address,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(row => row.Address)
                .ToArray();
        }
        catch
        {
            return Array.Empty<LinuxLanCandidate>();
        }
    }
}

internal sealed class RemoteLinuxHostProbe :
    ILocalHostProbe
{
    private static readonly (
        string Name,
        string[] Tokens)[] IntegrationCatalog =
    {
        ("Plex", new[] { "plex" }),
        ("Tautulli", new[] { "tautulli" }),
        ("Kometa", new[] { "kometa", "plex-meta-manager" }),
        ("Sonarr", new[] { "sonarr" }),
        ("Radarr", new[] { "radarr" }),
        ("Lidarr", new[] { "lidarr" }),
        ("Prowlarr", new[] { "prowlarr" }),
        ("Readarr", new[] { "readarr" }),
        ("Whisparr", new[] { "whisparr" }),
        ("Mylar3", new[] { "mylar3", "mylar" }),
        ("Bazarr", new[] { "bazarr" }),
        ("SABnzbd", new[] { "sabnzbd" }),
        ("qBittorrent", new[] { "qbittorrent" }),
        ("Decypharr", new[] { "decypharr" }),
        ("Recyclarr", new[] { "recyclarr" }),
        ("Configarr", new[] { "configarr" }),
        ("Profilarr", new[] { "profilarr" }),
        ("Cleanuparr", new[] { "cleanuparr" }),
        ("Maintainerr", new[] { "maintainerr" }),
        ("Unpackerr", new[] { "unpackerr" }),
        ("autobrr", new[] { "autobrr" }),
        ("Zurg", new[] { "zurg" }),
        ("Tdarr", new[] { "tdarr" }),
        ("Seerr", new[] { "seerr", "overseerr", "jellyseerr" }),
        ("DUMB", new[] { "dumb" })
    };

    private const char Separator = '\u001f';

    private const string CaptureScript =
        """
        set +e
        export LC_ALL=C

        emit() {
          printf '%s\037%s\n' "$1" "$2"
        }

        emit HOSTNAME "$(hostname 2>/dev/null)"
        if [ -r /etc/os-release ]; then
          . /etc/os-release
          emit OS "${PRETTY_NAME:-Linux}"
        else
          emit OS "Linux"
        fi
        emit KERNEL "$(uname -r 2>/dev/null)"
        emit UPTIME "$(uptime -p 2>/dev/null)"
        emit SYSTEM "$(systemctl is-system-running 2>/dev/null || true)"
        emit DOCKER "$(systemctl is-active docker 2>/dev/null || printf 'not-found')"
        emit CPU "$(lscpu 2>/dev/null | awk -F: '/Model name/ {sub(/^[ \t]+/, "", $2); print $2; exit}')"
        emit LOAD "$(awk '{print $1 " " $2 " " $3}' /proc/loadavg 2>/dev/null)"
        emit MEMORY "$(free -h 2>/dev/null | awk '/^Mem:/ {print $3 " used / " $2 " total"}')"
        emit IPS "$(hostname -I 2>/dev/null | xargs)"

        printf '__STORAGE__\n'
        df -PTh \
          -x tmpfs \
          -x devtmpfs \
          -x squashfs \
          -x overlay \
          -x proc \
          -x sysfs \
          -x cgroup2 \
          2>/dev/null |
          tail -n +2 |
          while read -r source filesystem size used available percent mountpoint; do
            printf '%s\037%s\037%s\037%s\037%s\037%s\037%s\n' \
              "$source" \
              "$filesystem" \
              "$size" \
              "$used" \
              "$available" \
              "$percent" \
              "$mountpoint"
          done

        printf '__SERVICES__\n'
        base_units='docker.service ssh.service sshd.service plexmediaserver.service sonarr.service radarr.service lidarr.service prowlarr.service readarr.service whisparr.service bazarr.service sabnzbd.service sabnzbdplus.service qbittorrent.service qbittorrent-nox.service'
        discovered_units="$(
          systemctl list-unit-files \
            --type=service \
            --no-legend \
            --no-pager \
            2>/dev/null |
            awk '{print $1}' |
            grep -Ei '(plex|tautulli|kometa|sonarr|radarr|lidarr|prowlarr|readarr|whisparr|mylar|bazarr|sabnzbd|qbittorrent|decypharr|recyclarr|configarr|profilarr|cleanuparr|maintainerr|unpackerr|autobrr|zurg|tdarr|seerr|overseerr|jellyseerr|dumb)' ||
          true
        )'

        printf '%s\n' $base_units $discovered_units |
          sed '/^$/d' |
          sort -u |
          while read -r unit; do
            description="$(systemctl show "$unit" --property=Description --value 2>/dev/null)"
            active="$(systemctl show "$unit" --property=ActiveState --value 2>/dev/null)"
            sub="$(systemctl show "$unit" --property=SubState --value 2>/dev/null)"
            enabled="$(systemctl show "$unit" --property=UnitFileState --value 2>/dev/null)"

            if [ -n "$description$active$sub$enabled" ]; then
              printf '%s\037%s\037%s\037%s\037%s\n' \
                "$unit" \
                "$description" \
                "$active" \
                "$sub" \
                "$enabled"
            fi
          done

        printf '__CONTAINERS__\n'
        if command -v docker >/dev/null 2>&1; then
          docker ps -a \
            --format '{{.Names}}\037{{.Image}}\037{{.State}}\037{{.Status}}\037{{.Ports}}' \
            2>/dev/null ||
          true
        fi

        printf '__FAILED__\n'
        systemctl --failed \
          --no-legend \
          --no-pager \
          2>/dev/null |
          awk '{print $1}' ||
        true

        printf '__LOGS__\n'
        journalctl \
          -p warning..alert \
          -n 80 \
          --no-pager \
          -o short-iso \
          2>/dev/null ||
        true
        """;

    private readonly LinuxHostProfile _profile;
    private readonly LinuxCredentialStore _credentials;
    private readonly string _knownHostsDirectory;

    public RemoteLinuxHostProbe(
        LinuxHostProfile profile,
        LinuxCredentialStore credentials,
        string knownHostsDirectory)
    {
        _profile = profile;
        _credentials = credentials;
        _knownHostsDirectory =
            knownHostsDirectory;
    }

    public async Task<HostSnapshot> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        var result =
            await LinuxSshTransport.RunScriptAsync(
                _profile,
                _credentials,
                _knownHostsDirectory,
                CaptureScript,
                suppliedSecret: null,
                cancellationToken);

        return ParseSnapshot(
            result.StandardOutput,
            result.StandardError);
    }

    private static HostSnapshot ParseSnapshot(
        string output,
        string standardError)
    {
        var header =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
        var storage =
            new List<StorageVolumeSnapshot>();
        var services =
            new List<ServiceSnapshot>();
        var containers =
            new List<DockerContainerSnapshot>();
        var failed =
            new List<string>();
        var logs =
            new List<string>();
        var warnings =
            new List<string>();

        var section = "HEADER";

        foreach (var rawLine in output.Split('\n'))
        {
            var line =
                rawLine.TrimEnd('\r');

            if (line.Length == 0)
                continue;

            if (line.StartsWith(
                    "__",
                    StringComparison.Ordinal) &&
                line.EndsWith(
                    "__",
                    StringComparison.Ordinal))
            {
                section = line;
                continue;
            }

            var parts = line.Split(Separator);

            switch (section)
            {
                case "HEADER":
                    if (parts.Length >= 2)
                        header[parts[0]] = parts[1];
                    break;

                case "__STORAGE__":
                    if (parts.Length >= 7)
                    {
                        storage.Add(
                            new StorageVolumeSnapshot(
                                parts[0],
                                parts[1],
                                parts[2],
                                parts[3],
                                parts[4],
                                parts[5],
                                parts[6]));
                    }
                    break;

                case "__SERVICES__":
                    if (parts.Length >= 5)
                    {
                        services.Add(
                            new ServiceSnapshot(
                                parts[0],
                                parts[1],
                                parts[2],
                                parts[3],
                                parts[4]));
                    }
                    break;

                case "__CONTAINERS__":
                    if (parts.Length >= 5)
                    {
                        containers.Add(
                            new DockerContainerSnapshot(
                                parts[0],
                                parts[1],
                                parts[2],
                                parts[3],
                                parts[4]));
                    }
                    break;

                case "__FAILED__":
                    failed.Add(line);
                    break;

                case "__LOGS__":
                    logs.Add(line);
                    break;
            }
        }

        if (!string.IsNullOrWhiteSpace(
                standardError))
        {
            warnings.Add(
                standardError.Trim());
        }

        var integrations =
            DetectIntegrations(
                services,
                containers);

        string Read(
            string key,
            string fallback) =>
            header.TryGetValue(
                key,
                out var value) &&
            !string.IsNullOrWhiteSpace(value)
                ? value
                : fallback;

        return new HostSnapshot(
            DateTimeOffset.Now,
            Read("HOSTNAME", "remote-linux"),
            Read("OS", "Linux"),
            Read("KERNEL", "unknown"),
            Read("UPTIME", "unknown"),
            Read("SYSTEM", "unknown"),
            Read("DOCKER", "unknown"),
            Read("CPU", "unknown"),
            Read("LOAD", "unknown"),
            Read("MEMORY", "unknown"),
            Read("IPS", "unknown"),
            storage,
            services,
            containers,
            integrations,
            failed,
            logs,
            warnings);
    }

    private static IReadOnlyList<IntegrationSnapshot>
        DetectIntegrations(
            IReadOnlyList<ServiceSnapshot> services,
            IReadOnlyList<DockerContainerSnapshot> containers)
    {
        var rows =
            new List<IntegrationSnapshot>();

        foreach (var rule in IntegrationCatalog)
        {
            foreach (var service in services)
            {
                var identity =
                    $"{service.Unit} {service.Description}";

                if (!rule.Tokens.Any(token =>
                        identity.Contains(
                            token,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                rows.Add(
                    new IntegrationSnapshot(
                        rule.Name,
                        "systemd",
                        $"{service.ActiveState}/{service.SubState}",
                        service.Unit));
            }

            foreach (var container in containers)
            {
                var identity =
                    $"{container.Name} {container.Image}";

                if (!rule.Tokens.Any(token =>
                        identity.Contains(
                            token,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                rows.Add(
                    new IntegrationSnapshot(
                        rule.Name,
                        "Docker",
                        container.Status,
                        container.Name));
            }
        }

        return rows
            .GroupBy(
                row =>
                    $"{row.Name}|{row.Kind}|{row.Evidence}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(row => row.Name)
            .ThenBy(row => row.Evidence)
            .ToArray();
    }
}

internal static class LinuxSshTransport
{
    public static async Task<LinuxHostKeyScanResult>
        ScanFingerprintAsync(
            LinuxHostProfile profile,
            CancellationToken cancellationToken)
    {
        if (profile.IsLocal)
        {
            return new LinuxHostKeyScanResult(
                true,
                "local",
                string.Empty,
                "Local provider does not use SSH.",
                string.Empty);
        }

        try
        {
            var scan = await RunProcessAsync(
                "ssh-keyscan",
                new[]
                {
                    "-T",
                    "6",
                    "-p",
                    profile.Port.ToString(
                        CultureInfo.InvariantCulture),
                    "-t",
                    "ed25519,rsa,ecdsa",
                    profile.Host
                },
                standardInput: null,
                environment: null,
                cancellationToken);

            if (scan.ExitCode != 0 ||
                string.IsNullOrWhiteSpace(
                    scan.StandardOutput))
            {
                return new LinuxHostKeyScanResult(
                    false,
                    string.Empty,
                    string.Empty,
                    "SSH host-key scan failed.",
                    string.IsNullOrWhiteSpace(
                        scan.StandardError)
                        ? "No host key was returned."
                        : scan.StandardError.Trim());
            }

            var keyLines = scan.StandardOutput
                .Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries)
                .Where(line =>
                    !line.StartsWith(
                        "#",
                        StringComparison.Ordinal))
                .ToArray();

            var keyLine =
                keyLines.FirstOrDefault(line =>
                    line.Contains(
                        " ssh-ed25519 ",
                        StringComparison.Ordinal)) ??
                keyLines.FirstOrDefault(line =>
                    line.Contains(
                        " ssh-rsa ",
                        StringComparison.Ordinal)) ??
                keyLines.FirstOrDefault();

            if (string.IsNullOrWhiteSpace(keyLine))
            {
                return new LinuxHostKeyScanResult(
                    false,
                    string.Empty,
                    string.Empty,
                    "SSH host-key scan returned no usable key.",
                    scan.StandardOutput);
            }

            var fingerprintResult =
                await RunProcessAsync(
                    "ssh-keygen",
                    new[]
                    {
                        "-lf",
                        "-",
                        "-E",
                        "sha256"
                    },
                    keyLine + Environment.NewLine,
                    environment: null,
                    cancellationToken);

            var match = Regex.Match(
                fingerprintResult.StandardOutput,
                @"SHA256:[A-Za-z0-9+/=]+");

            if (!match.Success)
            {
                return new LinuxHostKeyScanResult(
                    false,
                    string.Empty,
                    keyLine,
                    "Could not calculate the SSH host-key fingerprint.",
                    fingerprintResult.StandardOutput +
                    fingerprintResult.StandardError);
            }

            return new LinuxHostKeyScanResult(
                true,
                match.Value,
                keyLine,
                "SSH host-key fingerprint scanned.",
                keyLine);
        }
        catch (Exception exception)
        {
            return new LinuxHostKeyScanResult(
                false,
                string.Empty,
                string.Empty,
                "SSH host-key scan failed.",
                exception.Message);
        }
    }

    public static async Task<SshCommandResult>
        RunScriptAsync(
            LinuxHostProfile profile,
            LinuxCredentialStore credentials,
            string knownHostsDirectory,
            string script,
            string? suppliedSecret,
            CancellationToken cancellationToken)
    {
        LinuxHostProfileStore.Validate(profile);

        if (profile.IsLocal)
        {
            throw new InvalidOperationException(
                "The SSH transport cannot run against the local provider.");
        }

        var scan = await ScanFingerprintAsync(
            profile,
            cancellationToken);

        if (!scan.Success)
        {
            throw new InvalidOperationException(
                $"{scan.Summary} {scan.Detail}");
        }

        if (string.IsNullOrWhiteSpace(
                profile.HostKeyFingerprint))
        {
            throw new InvalidOperationException(
                $"SSH fingerprint is not pinned. Scanned fingerprint: {scan.Fingerprint}");
        }

        if (!profile.HostKeyFingerprint.Equals(
                scan.Fingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"SSH host-key fingerprint mismatch. Expected {profile.HostKeyFingerprint}; received {scan.Fingerprint}.");
        }

        Directory.CreateDirectory(
            knownHostsDirectory);

        var knownHostsPath = Path.Combine(
            knownHostsDirectory,
            $"{SanitizeFileName(profile.Id)}.known_hosts");

        File.WriteAllText(
            knownHostsPath,
            scan.KeyLine + Environment.NewLine);

        try
        {
            File.SetUnixFileMode(
                knownHostsPath,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite);
        }
        catch
        {
            // Permission tightening is best effort on non-POSIX filesystems.
        }

        var secretKind =
            profile.Authentication ==
            LinuxHostAuthentication.Password
                ? "password"
                : "passphrase";

        var secret =
            profile.Authentication ==
            LinuxHostAuthentication.Agent
                ? null
                : !string.IsNullOrEmpty(suppliedSecret)
                    ? suppliedSecret
                    : await credentials.LookupAsync(
                        profile.Id,
                        secretKind,
                        cancellationToken);

        if (profile.Authentication ==
                LinuxHostAuthentication.Password &&
            string.IsNullOrEmpty(secret))
        {
            throw new InvalidOperationException(
                "No password is stored for this host. Save it through the Secret Service keyring or use SSH agent authentication.");
        }

        var sshArguments = new List<string>
        {
            "-p",
            profile.Port.ToString(
                CultureInfo.InvariantCulture),
            "-o",
            "ConnectTimeout=8",
            "-o",
            "ConnectionAttempts=1",
            "-o",
            "StrictHostKeyChecking=yes",
            "-o",
            $"UserKnownHostsFile={knownHostsPath}",
            "-o",
            "GlobalKnownHostsFile=/dev/null",
            "-o",
            "LogLevel=ERROR"
        };

        switch (profile.Authentication)
        {
            case LinuxHostAuthentication.Agent:
                sshArguments.AddRange(
                    new[]
                    {
                        "-o",
                        "BatchMode=yes",
                        "-o",
                        "PreferredAuthentications=publickey"
                    });
                break;

            case LinuxHostAuthentication.PrivateKey:
                sshArguments.AddRange(
                    new[]
                    {
                        "-i",
                        Environment.ExpandEnvironmentVariables(
                            profile.PrivateKeyPath),
                        "-o",
                        "IdentitiesOnly=yes",
                        "-o",
                        string.IsNullOrEmpty(secret)
                            ? "BatchMode=yes"
                            : "BatchMode=no",
                        "-o",
                        "PreferredAuthentications=publickey",
                        "-o",
                        "NumberOfPasswordPrompts=1"
                    });
                break;

            case LinuxHostAuthentication.Password:
                sshArguments.AddRange(
                    new[]
                    {
                        "-o",
                        "BatchMode=no",
                        "-o",
                        "PubkeyAuthentication=no",
                        "-o",
                        "PreferredAuthentications=password,keyboard-interactive",
                        "-o",
                        "NumberOfPasswordPrompts=1"
                    });
                break;
        }

        sshArguments.Add(
            $"{profile.Username}@{profile.Host}");
        sshArguments.Add("bash");
        sshArguments.Add("-s");

        ProcessResult result;
        string? askPassPath = null;

        try
        {
            if (string.IsNullOrEmpty(secret))
            {
                result = await RunProcessAsync(
                    "ssh",
                    sshArguments,
                    script,
                    environment: null,
                    cancellationToken);
            }
            else
            {
                askPassPath = Path.Combine(
                    Path.GetTempPath(),
                    $"graveops-askpass-{Guid.NewGuid():N}.sh");

                await File.WriteAllTextAsync(
                    askPassPath,
                    """
                    #!/bin/sh
                    printf '%s\n' "$GRAVEOPS_SSH_SECRET"
                    """,
                    cancellationToken);

                try
                {
                    File.SetUnixFileMode(
                        askPassPath,
                        UnixFileMode.UserRead |
                        UnixFileMode.UserWrite |
                        UnixFileMode.UserExecute);
                }
                catch
                {
                    // Permission tightening is best effort.
                }

                var environment =
                    new Dictionary<string, string?>
                    {
                        ["SSH_ASKPASS"] =
                            askPassPath,
                        ["SSH_ASKPASS_REQUIRE"] =
                            "force",
                        ["DISPLAY"] =
                            Environment.GetEnvironmentVariable(
                                "DISPLAY") ?? ":0",
                        ["GRAVEOPS_SSH_SECRET"] =
                            secret
                    };

                var setsidArguments =
                    new List<string>
                    {
                        "-w",
                        "ssh"
                    };
                setsidArguments.AddRange(
                    sshArguments);

                result = await RunProcessAsync(
                    "setsid",
                    setsidArguments,
                    script,
                    environment,
                    cancellationToken);
            }
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(
                    askPassPath))
            {
                try
                {
                    File.Delete(askPassPath);
                }
                catch
                {
                    // Temporary askpass cleanup is best effort.
                }
            }
        }

        if (result.ExitCode != 0)
        {
            var message =
                string.IsNullOrWhiteSpace(
                    result.StandardError)
                    ? $"SSH exited with code {result.ExitCode}."
                    : result.StandardError.Trim();

            throw new InvalidOperationException(
                message);
        }

        return new SshCommandResult(
            result.StandardOutput,
            result.StandardError);
    }

    private static string SanitizeFileName(
        string value) =>
        Regex.Replace(
            value,
            @"[^A-Za-z0-9_.-]",
            "_");

    private static async Task<ProcessResult>
        RunProcessAsync(
            string executable,
            IReadOnlyList<string> arguments,
            string? standardInput,
            IReadOnlyDictionary<string, string?>?
                environment,
            CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                RedirectStandardInput =
                    standardInput is not null,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        if (environment is not null)
        {
            foreach (var item in environment)
                process.StartInfo.Environment[item.Key] =
                    item.Value;
        }

        process.Start();

        if (standardInput is not null)
        {
            await process.StandardInput.WriteAsync(
                standardInput);
            process.StandardInput.Close();
        }

        var stdout =
            process.StandardOutput.ReadToEndAsync(
                cancellationToken);
        var stderr =
            process.StandardError.ReadToEndAsync(
                cancellationToken);

        await process.WaitForExitAsync(
            cancellationToken);

        return new ProcessResult(
            process.ExitCode,
            await stdout,
            await stderr);
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);

    public sealed record SshCommandResult(
        string StandardOutput,
        string StandardError);
}

public static class LinuxDesktopNotifier
{
    public static bool IsAvailable
    {
        get
        {
            var path =
                Environment.GetEnvironmentVariable(
                    "PATH") ??
                string.Empty;

            return path.Split(
                    Path.PathSeparator,
                    StringSplitOptions.RemoveEmptyEntries)
                .Any(directory =>
                    File.Exists(
                        Path.Combine(
                            directory,
                            "notify-send")));
        }
    }

    public static async Task NotifyAsync(
        string title,
        string body,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
            return;

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "notify-send",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.StartInfo.ArgumentList.Add(
                "--app-name=GraveOps");
            process.StartInfo.ArgumentList.Add(
                "--urgency=critical");
            process.StartInfo.ArgumentList.Add(
                title);
            process.StartInfo.ArgumentList.Add(
                body);

            process.Start();
            await process.WaitForExitAsync(
                cancellationToken);
        }
        catch
        {
            // Desktop notifications never interrupt control-plane work.
        }
    }
}

