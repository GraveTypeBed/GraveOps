
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GraveOps.Core.Hosts;
using GraveOps.Core.Providers;
using GraveOps.Core.Security;
using GraveOps.Core.Snapshots;
using GraveOps.Core.Targets;
using GraveOps.Platform.Linux;
using GraveOps.Platform.Windows;

namespace GraveOps.Desktop.Linux;

public enum LinuxHostKind
{
    Local = 0,
    RemoteLinux = 1,
    RemoteWindows = 2,
    LocalWindows = 3
}

public enum LinuxHostAuthentication
{
    Agent = 0,
    PrivateKey = 1,
    Password = 2,
    WinRmNegotiate = 3,
    WinRmBasic = 4
}

public sealed class LinuxHostProfile
{
    public string Id { get; set; } =
        Guid.NewGuid().ToString("N");

    public string Name { get; set; } =
        "Managed target";

    public LinuxHostKind Kind { get; set; } =
        LinuxHostKind.RemoteLinux;

    public string Host { get; set; } =
        string.Empty;

    public int Port { get; set; } =
        22;

    public string Username { get; set; } =
        string.Empty;

    public string Role { get; set; } =
        "Server";

    public LinuxHostAuthentication Authentication { get; set; } =
        LinuxHostAuthentication.Agent;

    public string PrivateKeyPath { get; set; } =
        string.Empty;

    public string HostKeyFingerprint { get; set; } =
        string.Empty;

    public int OperationTimeoutSeconds { get; set; } =
        60;

    public string CredentialReference { get; set; } =
        string.Empty;

    public DateTimeOffset? LastDetectedAt { get; set; }

    public bool IsLocal =>
        Kind is
            LinuxHostKind.Local or
            LinuxHostKind.LocalWindows;

    public bool IsLocalLinux =>
        Kind ==
        LinuxHostKind.Local;

    public bool IsRemoteLinux =>
        Kind ==
        LinuxHostKind.RemoteLinux;

    public bool IsWindows =>
        Kind is
            LinuxHostKind.RemoteWindows or
            LinuxHostKind.LocalWindows;

    public bool IsRemoteWindows =>
        Kind ==
        LinuxHostKind.RemoteWindows;

    public bool RequiresCredential =>
        !IsLocal &&
        Authentication !=
            LinuxHostAuthentication.Agent;

    public string CredentialKind =>
        Authentication ==
            LinuxHostAuthentication.PrivateKey
            ? "passphrase"
            : "password";

    public string EffectiveCredentialReference =>
        string.IsNullOrWhiteSpace(
            CredentialReference)
            ? CredentialReferenceFor(
                Id,
                CredentialKind).Value
            : CredentialReference.Trim();

    public string DisplayName =>
        string.IsNullOrWhiteSpace(
            Name)
            ? IsLocal
                ? Environment.MachineName
                : Host
            : Name;

    public string KindLabel =>
        Kind switch
        {
            LinuxHostKind.Local =>
                "Local Linux",
            LinuxHostKind.RemoteLinux =>
                "Remote Linux · SSH",
            LinuxHostKind.RemoteWindows =>
                "Remote Windows · WinRM HTTPS",
            LinuxHostKind.LocalWindows =>
                "Local Windows",
            _ =>
                Kind.ToString()
        };

    public string AuthenticationLabel =>
        Authentication switch
        {
            LinuxHostAuthentication.Agent =>
                "SSH agent",
            LinuxHostAuthentication.PrivateKey =>
                "Private key",
            LinuxHostAuthentication.Password =>
                "Password",
            LinuxHostAuthentication.WinRmNegotiate =>
                "WinRM Negotiate",
            LinuxHostAuthentication.WinRmBasic =>
                "WinRM Basic over HTTPS",
            _ =>
                Authentication.ToString()
        };

    public string ConnectionSummary =>
        Kind switch
        {
            LinuxHostKind.Local =>
                $"{KindLabel} · {Role}",
            LinuxHostKind.LocalWindows =>
                $"{KindLabel} · {Role}",
            LinuxHostKind.RemoteWindows =>
                $"WinRM HTTPS · {Username}@{Host}:{Port} · {Role}",
            _ =>
                $"{Username}@{Host}:{Port} · {Role}"
        };

    public TargetProfile ToTargetProfile()
    {
        ValidateForLinuxClient(
            this);

        var providerId =
            Kind switch
            {
                LinuxHostKind.Local =>
                    HostProviderIds.LocalLinux,
                LinuxHostKind.RemoteLinux =>
                    HostProviderIds.RemoteLinuxSsh,
                LinuxHostKind.LocalWindows =>
                    HostProviderIds.LocalWindows,
                LinuxHostKind.RemoteWindows =>
                    HostProviderIds.RemoteWindows,
                _ =>
                    throw new InvalidOperationException(
                        "Unsupported target kind.")
            };

        var platform =
            IsWindows
                ? TargetPlatform.Windows
                : TargetPlatform.Linux;

        var location =
            IsLocal
                ? TargetLocation.Local
                : TargetLocation.Remote;

        var metadata =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["role"] =
                    string.IsNullOrWhiteSpace(
                        Role)
                        ? "Server"
                        : Role.Trim()
            };

        if (LastDetectedAt is { } detected)
        {
            metadata["last-detected-at"] =
                detected.ToString("O");
        }

        if (IsLocal)
        {
            return new TargetProfile(
                Id.Trim(),
                DisplayName,
                providerId,
                platform,
                location,
                TargetConnectionProfile.Local,
                metadata);
        }

        var options =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        if (IsRemoteLinux)
        {
            options["authentication"] =
                Authentication.ToString();

            if (!string.IsNullOrWhiteSpace(
                    PrivateKeyPath))
            {
                options["private-key-path"] =
                    PrivateKeyPath.Trim();
            }
        }
        else
        {
            options["authentication"] =
                Authentication ==
                    LinuxHostAuthentication.WinRmBasic
                    ? "Basic"
                    : "Negotiate";
            options["operation-timeout-seconds"] =
                OperationTimeoutSeconds.ToString(
                    CultureInfo.InvariantCulture);
        }

        return new TargetProfile(
            Id.Trim(),
            DisplayName,
            providerId,
            platform,
            location,
            new TargetConnectionProfile(
                IsRemoteWindows
                    ? TransportIds.WinRmHttps
                    : TransportIds.Ssh,
                Host.Trim(),
                Port,
                Username.Trim(),
                RequiresCredential
                    ? EffectiveCredentialReference
                    : null,
                string.IsNullOrWhiteSpace(
                    HostKeyFingerprint)
                    ? null
                    : HostKeyFingerprint.Trim(),
                options),
            metadata);
    }

    public static LinuxHostProfile FromTargetProfile(
        TargetProfile target)
    {
        ArgumentNullException.ThrowIfNull(
            target);
        target.Validate();

        var kind =
            target.Platform switch
            {
                TargetPlatform.Windows
                    when target.Location ==
                         TargetLocation.Local =>
                    LinuxHostKind.LocalWindows,
                TargetPlatform.Windows =>
                    LinuxHostKind.RemoteWindows,
                TargetPlatform.Linux
                    when target.Location ==
                         TargetLocation.Local =>
                    LinuxHostKind.Local,
                _ =>
                    LinuxHostKind.RemoteLinux
            };

        var authentication =
            ParseAuthentication(
                kind,
                Option(
                    target.Connection.Options,
                    "authentication"));

        var role =
            Metadata(
                target.Metadata,
                "role");

        var detectedText =
            Metadata(
                target.Metadata,
                "last-detected-at");

        DateTimeOffset? detected =
            DateTimeOffset.TryParse(
                detectedText,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsedDetected)
                ? parsedDetected
                : null;

        var profile =
            new LinuxHostProfile
            {
                Id =
                    target.Id,
                Name =
                    target.DisplayName,
                Kind =
                    kind,
                Host =
                    target.Connection.Host ??
                    (kind ==
                        LinuxHostKind.Local
                        ? "127.0.0.1"
                        : string.Empty),
                Port =
                    target.Connection.Port ??
                    (kind ==
                        LinuxHostKind.RemoteWindows
                        ? RemoteWindowsConnectionParser
                            .DefaultWinRmHttpsPort
                        : 22),
                Username =
                    target.Connection.Username ??
                    string.Empty,
                Role =
                    string.IsNullOrWhiteSpace(
                        role)
                        ? "Server"
                        : role,
                Authentication =
                    authentication,
                PrivateKeyPath =
                    Option(
                        target.Connection.Options,
                        "private-key-path") ??
                    string.Empty,
                HostKeyFingerprint =
                    target.Connection.PinnedIdentity ??
                    string.Empty,
                OperationTimeoutSeconds =
                    ParseTimeout(
                        Option(
                            target.Connection.Options,
                            "operation-timeout-seconds")),
                CredentialReference =
                    target.Connection.CredentialReference ??
                    string.Empty,
                LastDetectedAt =
                    detected
            };

        ValidateForLinuxClient(
            profile,
            allowLocalWindows: true);

        return profile;
    }

    public static CredentialReference CredentialReferenceFor(
        string targetId,
        string kind)
    {
        if (string.IsNullOrWhiteSpace(
                targetId) ||
            string.IsNullOrWhiteSpace(
                kind))
        {
            throw new InvalidOperationException(
                "Credential references require a target ID and kind.");
        }

        return new CredentialReference(
            $"graveops/target/" +
            $"{targetId.Trim()}/" +
            $"{kind.Trim().ToLowerInvariant()}");
    }

    public static void ValidateForLinuxClient(
        LinuxHostProfile profile,
        bool allowLocalWindows = false)
    {
        ArgumentNullException.ThrowIfNull(
            profile);

        if (string.IsNullOrWhiteSpace(
                profile.Id))
        {
            throw new InvalidOperationException(
                "Target profile ID is required.");
        }

        if (string.IsNullOrWhiteSpace(
                profile.Name))
        {
            throw new InvalidOperationException(
                "Display name is required.");
        }

        if (profile.Kind ==
                LinuxHostKind.LocalWindows &&
            !allowLocalWindows)
        {
            throw new InvalidOperationException(
                "A local Windows target cannot be created from the Linux client.");
        }

        if (profile.IsLocal)
            return;

        if (string.IsNullOrWhiteSpace(
                profile.Host))
        {
            throw new InvalidOperationException(
                "Remote host or IP address is required.");
        }

        if (profile.Port is < 1 or > 65535)
        {
            throw new InvalidOperationException(
                "Remote target port must be between 1 and 65535.");
        }

        if (string.IsNullOrWhiteSpace(
                profile.Username))
        {
            throw new InvalidOperationException(
                "Remote username is required.");
        }

        if (profile.IsRemoteLinux)
        {
            if (profile.Authentication is
                not LinuxHostAuthentication.Agent and
                not LinuxHostAuthentication.PrivateKey and
                not LinuxHostAuthentication.Password)
            {
                throw new InvalidOperationException(
                    "Remote Linux authentication must use SSH agent, private key or password.");
            }

            if (profile.Authentication ==
                    LinuxHostAuthentication.PrivateKey &&
                string.IsNullOrWhiteSpace(
                    profile.PrivateKeyPath))
            {
                throw new InvalidOperationException(
                    "Private-key authentication requires a key path.");
            }

            return;
        }

        if (profile.Authentication is
            not LinuxHostAuthentication.WinRmNegotiate and
            not LinuxHostAuthentication.WinRmBasic)
        {
            throw new InvalidOperationException(
                "Remote Windows authentication must use WinRM Negotiate or Basic over HTTPS.");
        }

        if (profile.OperationTimeoutSeconds is < 10 or > 300)
        {
            throw new InvalidOperationException(
                "The remote Windows operation timeout must be between 10 and 300 seconds.");
        }

        if (!string.IsNullOrWhiteSpace(
                profile.HostKeyFingerprint))
        {
            profile.HostKeyFingerprint =
                RemoteWindowsConnectionParser
                    .NormalizeSha256Pin(
                        profile.HostKeyFingerprint) ??
                string.Empty;
        }
    }

    private static LinuxHostAuthentication ParseAuthentication(
        LinuxHostKind kind,
        string? value)
    {
        if (kind ==
            LinuxHostKind.RemoteWindows)
        {
            return value?.Equals(
                       "Basic",
                       StringComparison.OrdinalIgnoreCase) ==
                   true
                ? LinuxHostAuthentication.WinRmBasic
                : LinuxHostAuthentication.WinRmNegotiate;
        }

        return Enum.TryParse<
                   LinuxHostAuthentication>(
                   value,
                   ignoreCase: true,
                   out var parsed) &&
               parsed is
                   LinuxHostAuthentication.Agent or
                   LinuxHostAuthentication.PrivateKey or
                   LinuxHostAuthentication.Password
            ? parsed
            : LinuxHostAuthentication.Agent;
    }

    private static int ParseTimeout(
        string? value) =>
        int.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var seconds)
            ? Math.Clamp(
                seconds,
                10,
                300)
            : 60;

    private static string? Option(
        IReadOnlyDictionary<string, string>? options,
        string key)
    {
        if (options is null)
            return null;

        foreach (var pair in options)
        {
            if (pair.Key.Equals(
                    key,
                    StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static string Metadata(
        IReadOnlyDictionary<string, string>? metadata,
        string key) =>
        Option(
            metadata,
            key) ??
        string.Empty;
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

public sealed class LinuxHostProfileStore :
    ITargetRegistry
{
    private readonly object _gate =
        new();

    private readonly JsonSerializerOptions _json =
        new()
        {
            WriteIndented =
                true,
            PropertyNameCaseInsensitive =
                true
        };

    private List<TargetProfile> _targets;

    public LinuxHostProfileStore(
        string configDirectory)
    {
        Directory.CreateDirectory(
            configDirectory);

        FilePath =
            Path.Combine(
                configDirectory,
                "targets.json");

        LegacyFilePath =
            Path.Combine(
                configDirectory,
                "hosts.json");

        _targets =
            Load();

        EnsureLocalProfile();
        Save();
    }

    public string FilePath { get; }

    public string LegacyFilePath { get; }

    public IReadOnlyList<LinuxHostProfile> Profiles
    {
        get
        {
            lock (_gate)
            {
                return _targets
                    .Select(
                        LinuxHostProfile
                            .FromTargetProfile)
                    .Where(profile =>
                        profile.Kind !=
                            LinuxHostKind.LocalWindows)
                    .OrderBy(profile =>
                        profile.IsLocal
                            ? 0
                            : 1)
                    .ThenBy(profile =>
                        profile.IsWindows
                            ? 1
                            : 0)
                    .ThenBy(profile =>
                        profile.DisplayName,
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
    }

    public LinuxHostProfile? Find(
        string id)
    {
        lock (_gate)
        {
            var target =
                _targets.FirstOrDefault(item =>
                    item.Id.Equals(
                        id,
                        StringComparison.OrdinalIgnoreCase));

            if (target is null)
                return null;

            var profile =
                LinuxHostProfile.FromTargetProfile(
                    target);

            return profile.Kind ==
                    LinuxHostKind.LocalWindows
                ? null
                : profile;
        }
    }

    public LinuxHostProfile Upsert(
        LinuxHostProfile profile)
    {
        Validate(
            profile);

        var target =
            profile.ToTargetProfile();

        lock (_gate)
        {
            UpsertCore(
                target);
            Save();
        }

        return LinuxHostProfile
            .FromTargetProfile(
                target);
    }

    public bool Delete(
        string id)
    {
        lock (_gate)
        {
            var target =
                _targets.FirstOrDefault(item =>
                    item.Id.Equals(
                        id,
                        StringComparison.OrdinalIgnoreCase));

            if (target is null ||
                target.Location ==
                    TargetLocation.Local)
            {
                return false;
            }

            var removed =
                _targets.Remove(
                    target);

            if (removed)
                Save();

            return removed;
        }
    }

    public void TouchDetection(
        string id,
        DateTimeOffset capturedAt)
    {
        lock (_gate)
        {
            var target =
                _targets.FirstOrDefault(item =>
                    item.Id.Equals(
                        id,
                        StringComparison.OrdinalIgnoreCase));

            if (target is null)
                return;

            var metadata =
                new Dictionary<string, string>(
                    target.Metadata ??
                    new Dictionary<string, string>(),
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["last-detected-at"] =
                        capturedAt.ToString("O")
                };

            UpsertCore(
                target with
                {
                    Metadata =
                        metadata
                });

            Save();
        }
    }

    public static void Validate(
        LinuxHostProfile profile) =>
        LinuxHostProfile.ValidateForLinuxClient(
            profile);

    public Task<IReadOnlyList<TargetProfile>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return Task.FromResult<
                IReadOnlyList<TargetProfile>>(
                _targets.ToArray());
        }
    }

    public Task<TargetProfile?> FindAsync(
        string targetId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return Task.FromResult(
                _targets.FirstOrDefault(item =>
                    item.Id.Equals(
                        targetId,
                        StringComparison.OrdinalIgnoreCase)));
        }
    }

    public Task UpsertAsync(
        TargetProfile target,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        target.Validate();

        var profile =
            LinuxHostProfile.FromTargetProfile(
                target);

        Validate(
            profile);

        lock (_gate)
        {
            UpsertCore(
                profile.ToTargetProfile());
            Save();
        }

        return Task.CompletedTask;
    }

    public Task<bool> RemoveAsync(
        string targetId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            Delete(
                targetId));
    }

    private List<TargetProfile> Load()
    {
        if (File.Exists(
                FilePath))
        {
            try
            {
                return JsonSerializer.Deserialize<
                           List<TargetProfile>>(
                           File.ReadAllText(
                               FilePath),
                           _json) ??
                       new List<TargetProfile>();
            }
            catch
            {
                PreserveCorruptFile(
                    FilePath);

                return MigrateLegacyProfiles();
            }
        }

        return MigrateLegacyProfiles();
    }

    private List<TargetProfile> MigrateLegacyProfiles()
    {
        if (!File.Exists(
                LegacyFilePath))
        {
            return new List<TargetProfile>();
        }

        try
        {
            var legacy =
                JsonSerializer.Deserialize<
                    List<LinuxHostProfile>>(
                    File.ReadAllText(
                        LegacyFilePath),
                    _json) ??
                new List<LinuxHostProfile>();

            var migrated =
                new List<TargetProfile>();

            foreach (var profile in legacy)
            {
                try
                {
                    profile.Kind =
                        profile.Kind ==
                            LinuxHostKind.Local
                            ? LinuxHostKind.Local
                            : LinuxHostKind.RemoteLinux;

                    if (profile.RequiresCredential)
                    {
                        profile.CredentialReference =
                            LinuxHostProfile
                                .CredentialReferenceFor(
                                    profile.Id,
                                    profile.CredentialKind)
                                .Value;
                    }

                    migrated.Add(
                        profile.ToTargetProfile());
                }
                catch
                {
                    // One malformed legacy target must not discard valid profiles.
                }
            }

            return migrated;
        }
        catch
        {
            return new List<TargetProfile>();
        }
    }

    private void EnsureLocalProfile()
    {
        var local =
            _targets.FirstOrDefault(target =>
                target.Platform ==
                    TargetPlatform.Linux &&
                target.Location ==
                    TargetLocation.Local);

        if (local is null)
        {
            _targets.Insert(
                0,
                new LinuxHostProfile
                {
                    Id =
                        "local",
                    Name =
                        Environment.MachineName,
                    Kind =
                        LinuxHostKind.Local,
                    Host =
                        "127.0.0.1",
                    Port =
                        22,
                    Username =
                        Environment.UserName,
                    Role =
                        "Local control plane",
                    Authentication =
                        LinuxHostAuthentication.Agent
                }.ToTargetProfile());

            return;
        }

        var profile =
            LinuxHostProfile.FromTargetProfile(
                local);

        profile.Id =
            "local";
        profile.Kind =
            LinuxHostKind.Local;
        profile.Name =
            string.IsNullOrWhiteSpace(
                profile.Name)
                ? Environment.MachineName
                : profile.Name;
        profile.Host =
            "127.0.0.1";
        profile.Port =
            22;
        profile.Username =
            string.IsNullOrWhiteSpace(
                profile.Username)
                ? Environment.UserName
                : profile.Username;
        profile.Authentication =
            LinuxHostAuthentication.Agent;
        profile.CredentialReference =
            string.Empty;
        profile.PrivateKeyPath =
            string.Empty;
        profile.HostKeyFingerprint =
            string.Empty;

        _targets.Remove(
            local);
        _targets.Insert(
            0,
            profile.ToTargetProfile());
    }

    private void UpsertCore(
        TargetProfile target)
    {
        var existing =
            _targets.FirstOrDefault(item =>
                item.Id.Equals(
                    target.Id,
                    StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            _targets.Add(
                target);
            return;
        }

        var index =
            _targets.IndexOf(
                existing);

        _targets[index] =
            target;
    }

    private void Save()
    {
        var temporary =
            FilePath +
            ".tmp";

        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(
                _targets,
                _json));

        File.Move(
            temporary,
            FilePath,
            overwrite: true);

        TryHarden(
            FilePath);
    }

    private static void PreserveCorruptFile(
        string path)
    {
        try
        {
            var destination =
                path +
                ".corrupt-" +
                DateTimeOffset.UtcNow
                    .ToString(
                        "yyyyMMddHHmmss",
                        CultureInfo.InvariantCulture);

            File.Move(
                path,
                destination,
                overwrite: false);
        }
        catch
        {
            // The unreadable file remains in place when recovery cannot move it.
        }
    }

    private static void TryHarden(
        string path)
    {
        if (OperatingSystem.IsWindows())
            return;

        try
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite);
        }
        catch
        {
            // Unsupported filesystems keep their existing ACLs.
        }
    }
}

public sealed class LinuxCredentialStore :
    ICredentialVault
{
    public string VaultId =>
        "linux.secret-service";

    public bool IsAvailable =>
        CommandExists("secret-tool");

    public string CapabilityText =>
        IsAvailable
            ? "Secret Service keyring available"
            : "secret-tool unavailable · secrets cannot be saved";

    public async Task StoreAsync(
        CredentialReference reference,
        SecretValue secret,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            secret);

        var parsed =
            ParseCredentialReference(
                reference);

        // LinuxCredentialStore's established compatibility API accepts a
        // string. The secret is still persisted only through Secret Service.
        var value =
            new string(
                secret.Reveal().Span);

        await SaveAsync(
            parsed.TargetId,
            parsed.Kind,
            value,
            cancellationToken);
    }

    public async Task<SecretValue?> RetrieveAsync(
        CredentialReference reference,
        CancellationToken cancellationToken = default)
    {
        var parsed =
            ParseCredentialReference(
                reference);

        var value =
            await LookupAsync(
                parsed.TargetId,
                parsed.Kind,
                cancellationToken);

        return value is null
            ? null
            : new SecretValue(
                value);
    }

    public async Task DeleteAsync(
        CredentialReference reference,
        CancellationToken cancellationToken = default)
    {
        var parsed =
            ParseCredentialReference(
                reference);

        await ClearAsync(
            parsed.TargetId,
            parsed.Kind,
            cancellationToken);
    }

    public static (
        string TargetId,
        string Kind)
        ParseCredentialReference(
            CredentialReference reference)
    {
        var parts =
            reference.Value
                .Split(
                    '/',
                    StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 4 ||
            !parts[0].Equals(
                "graveops",
                StringComparison.OrdinalIgnoreCase) ||
            !parts[1].Equals(
                "target",
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(
                parts[2]) ||
            parts[3] is not (
                "password" or
                "passphrase"))
        {
            throw new InvalidOperationException(
                "The credential reference is not a GraveOps target password or passphrase reference.");
        }

        return (
            parts[2],
            parts[3]);
    }

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

    public LinuxControlPlaneCoordinator(
        string? configDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(
                configDirectory))
        {
            var home =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile);
            var configRoot =
                Environment.GetEnvironmentVariable(
                    "XDG_CONFIG_HOME");

            if (string.IsNullOrWhiteSpace(
                    configRoot))
            {
                configRoot =
                    Path.Combine(
                        home,
                        ".config");
            }

            configDirectory =
                Path.Combine(
                    configRoot,
                    "GraveOps");
        }

        ConfigDirectory =
            Path.GetFullPath(
                configDirectory);

        KnownHostsDirectory =
            Path.Combine(
                ConfigDirectory,
                "known-hosts");

        Directory.CreateDirectory(
            ConfigDirectory);
        Directory.CreateDirectory(
            KnownHostsDirectory);

        Profiles =
            new LinuxHostProfileStore(
                ConfigDirectory);
        State =
            new LinuxControlPlaneStateStore(
                ConfigDirectory);
        Credentials =
            new LinuxCredentialStore();

        HostProviders =
            DesktopHostProviderComposition.Create(
                _localProbe,
                Credentials,
                KnownHostsDirectory);

        if (Profiles.Find(
                State.ActiveHostId) is null)
        {
            State.SetActiveHost(
                "local");
        }
    }

    public string ConfigDirectory { get; }

    public string KnownHostsDirectory { get; }

    public LinuxHostProfileStore Profiles { get; }

    public LinuxControlPlaneStateStore State { get; }

    public LinuxCredentialStore Credentials { get; }

    public IHostProviderRegistry HostProviders { get; }

    public LinuxHostProfile ActiveProfile =>
        Profiles.Find(
            State.ActiveHostId) ??
        Profiles.Find(
            "local") ??
        throw new InvalidOperationException(
            "The local GraveOps target profile is missing.");

    public void SetActive(
        string hostId)
    {
        if (Profiles.Find(
                hostId) is null)
        {
            throw new InvalidOperationException(
                "The selected target profile no longer exists.");
        }

        State.SetActiveHost(
            hostId);
    }

    public TargetCapabilities CapabilitiesFor(
        LinuxHostProfile profile)
    {
        ArgumentNullException.ThrowIfNull(
            profile);

        return profile.Kind switch
        {
            LinuxHostKind.Local =>
                LinuxTargetCapabilityCatalog.ForTarget(
                    isLocal: true),
            LinuxHostKind.RemoteLinux =>
                LinuxTargetCapabilityCatalog.ForTarget(
                    isLocal: false),
            LinuxHostKind.LocalWindows =>
                WindowsTargetCapabilityCatalog.ForLocalTarget(),
            LinuxHostKind.RemoteWindows =>
                WindowsTargetCapabilityCatalog.ForRemoteTarget(),
            _ =>
                TargetCapabilities.Empty
        };
    }

    public async Task<HostProviderProbeResult> ProbeAsync(
        LinuxHostProfile profile,
        CancellationToken cancellationToken = default)
    {
        var target =
            profile.ToTargetProfile();
        var provider =
            HostProviders.Resolve(
                target);

        return await provider.ProbeAsync(
            target,
            cancellationToken);
    }

    public async Task<TargetSnapshotEnvelope<HostSnapshot>>
        CaptureAsync(
            LinuxHostProfile profile,
            TargetRefreshLease lease,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            profile);

        var target =
            profile.ToTargetProfile();
        var provider =
            HostProviders.Resolve(
                target);

        var envelope =
            await provider.CaptureAsync(
                target,
                lease,
                cancellationToken);

        EnsureUsableCapture(
            profile,
            envelope.Snapshot);

        return envelope;
    }

    public async Task<HostSnapshot> CaptureAsync(
        LinuxHostProfile profile,
        CancellationToken cancellationToken = default)
    {
        var lease =
            new TargetRefreshLease(
                profile.Id,
                SelectionGeneration:
                    1,
                RefreshGeneration:
                    1,
                RefreshId:
                    Guid.NewGuid());

        var envelope =
            await CaptureAsync(
                profile,
                lease,
                cancellationToken);

        return envelope.Snapshot;
    }

    public Task<HostSnapshot> CaptureActiveAsync(
        CancellationToken cancellationToken = default) =>
        CaptureAsync(
            ActiveProfile,
            cancellationToken);

    public async Task<LinuxHostKeyScanResult>
        ScanFingerprintAsync(
            LinuxHostProfile profile,
            CancellationToken cancellationToken = default)
    {
        if (!profile.IsRemoteLinux)
        {
            throw new InvalidOperationException(
                "Automatic fingerprint scanning is available only for remote Linux SSH targets.");
        }

        return await LinuxSshTransport
            .ScanFingerprintAsync(
                profile,
                cancellationToken);
    }

    public async Task<LinuxConnectionTestResult>
        TestAsync(
            LinuxHostProfile profile,
            string? suppliedSecret = null,
            CancellationToken cancellationToken = default)
    {
        LinuxHostProfileStore.Validate(
            profile);

        if (profile.IsLocalLinux)
        {
            var snapshot =
                await CaptureAsync(
                    profile,
                    cancellationToken);

            return new LinuxConnectionTestResult(
                true,
                "Local Linux provider is available.",
                $"{snapshot.Hostname} · {snapshot.OperatingSystem}",
                "local");
        }

        if (profile.IsRemoteWindows)
        {
            return await TestRemoteWindowsAsync(
                profile,
                suppliedSecret,
                cancellationToken);
        }

        if (!profile.IsRemoteLinux)
        {
            return new LinuxConnectionTestResult(
                false,
                "Local Windows cannot be tested from this Linux client.",
                "Native local Windows capture requires a Windows client runtime.",
                string.Empty);
        }

        var scan =
            await ScanFingerprintAsync(
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
                $"Expected {profile.HostKeyFingerprint}; " +
                $"received {scan.Fingerprint}. Connection was blocked.",
                scan.Fingerprint);
        }

        try
        {
            var result =
                await LinuxSshTransport.RunScriptAsync(
                    profile,
                    Credentials,
                    KnownHostsDirectory,
                    "set -e\n" +
                    "printf '__GRAVEOPS_OK__\\n'\n" +
                    "hostname\n" +
                    ". /etc/os-release 2>/dev/null || true\n" +
                    "printf '%s\\n' \"${PRETTY_NAME:-Linux}\"\n",
                    suppliedSecret,
                    cancellationToken);

            var lines =
                result.StandardOutput
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
        LinuxHostProfileStore.Validate(
            profile);

        if (profile.IsLocalLinux)
        {
            profile.Id =
                "local";
            profile.Host =
                "127.0.0.1";
            profile.Port =
                22;
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
            profile.CredentialReference =
                string.Empty;
        }
        else if (profile.RequiresCredential)
        {
            profile.CredentialReference =
                profile.EffectiveCredentialReference;
        }

        Profiles.Upsert(
            profile);

        if (!profile.IsLocal &&
            saveSecret &&
            !string.IsNullOrEmpty(
                secret))
        {
            await Credentials.SaveAsync(
                profile.Id,
                profile.CredentialKind,
                secret,
                cancellationToken);
        }

        if (profile.IsLocal ||
            profile.Authentication ==
                LinuxHostAuthentication.Agent)
        {
            await Credentials.ClearAsync(
                profile.Id,
                "password",
                cancellationToken);
            await Credentials.ClearAsync(
                profile.Id,
                "passphrase",
                cancellationToken);
        }
        else if (profile.CredentialKind ==
                 "password")
        {
            await Credentials.ClearAsync(
                profile.Id,
                "passphrase",
                cancellationToken);
        }
        else
        {
            await Credentials.ClearAsync(
                profile.Id,
                "password",
                cancellationToken);
        }
    }

    public async Task DeleteProfileAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var profile =
            Profiles.Find(
                id);

        if (profile is null ||
            profile.IsLocal)
        {
            return;
        }

        await Credentials.ClearAsync(
            id,
            "password",
            cancellationToken);
        await Credentials.ClearAsync(
            id,
            "passphrase",
            cancellationToken);

        Profiles.Delete(
            id);

        var knownHostsPath =
            Path.Combine(
                KnownHostsDirectory,
                $"{Regex.Replace(
                    id,
                    @"[^A-Za-z0-9_.-]",
                    "_")}.known_hosts");

        try
        {
            File.Delete(
                knownHostsPath);
        }
        catch
        {
            // Known-host cleanup is best effort.
        }

        if (State.ActiveHostId.Equals(
                id,
                StringComparison.OrdinalIgnoreCase))
        {
            State.SetActiveHost(
                "local");
        }
    }

    public OpsBackupSnapshot
        CreateRemoteBackupSnapshot(
            HostSnapshot snapshot)
    {
        return new OpsBackupSnapshot(
            OpsSeverity.Info,
            "TARGET",
            "Target provider boundary",
            "Backup inventory is unavailable for the selected target provider.",
            new[]
            {
                $"Target · {ActiveProfile.ConnectionSummary}",
                $"Host capture · {snapshot.CapturedAt.ToLocalTime():g}",
                "Backup mutations remain disabled outside the local Linux provider."
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
            using var process =
                new Process
                {
                    StartInfo =
                        new ProcessStartInfo
                        {
                            FileName =
                                "ip",
                            RedirectStandardOutput =
                                true,
                            RedirectStandardError =
                                true,
                            UseShellExecute =
                                false,
                            CreateNoWindow =
                                true
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
            {
                return Array.Empty<
                    LinuxLanCandidate>();
            }

            var rows =
                new List<LinuxLanCandidate>();

            foreach (var line in
                     (await stdout).Split(
                         '\n',
                         StringSplitOptions.RemoveEmptyEntries))
            {
                var match =
                    Regex.Match(
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
                    row =>
                        row.Address,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                    group.First())
                .OrderBy(row =>
                    row.Address)
                .ToArray();
        }
        catch
        {
            return Array.Empty<
                LinuxLanCandidate>();
        }
    }

    private async Task<LinuxConnectionTestResult>
        TestRemoteWindowsAsync(
            LinuxHostProfile profile,
            string? suppliedSecret,
            CancellationToken cancellationToken)
    {
        try
        {
            var target =
                profile.ToTargetProfile();

            IHostProvider provider;

            if (!string.IsNullOrEmpty(
                    suppliedSecret))
            {
                provider =
                    RemoteWindowsHostProviderFactory.Create(
                        new TransientCredentialVault(
                            Credentials,
                            new CredentialReference(
                                profile.EffectiveCredentialReference),
                            suppliedSecret));
            }
            else
            {
                provider =
                    HostProviders.Resolve(
                        target);
            }

            var envelope =
                await provider.CaptureAsync(
                    target,
                    new TargetRefreshLease(
                        profile.Id,
                        SelectionGeneration:
                            1,
                        RefreshGeneration:
                            1,
                        RefreshId:
                            Guid.NewGuid()),
                    cancellationToken);

            EnsureUsableCapture(
                profile,
                envelope.Snapshot);

            return new LinuxConnectionTestResult(
                true,
                "WinRM HTTPS connection and remote Windows provider capture succeeded.",
                $"{envelope.Snapshot.Hostname} · " +
                $"{envelope.Snapshot.OperatingSystem}",
                string.Empty);
        }
        catch (Exception exception)
        {
            return new LinuxConnectionTestResult(
                false,
                "WinRM HTTPS connection failed.",
                exception.Message,
                string.Empty);
        }
    }

    private static void EnsureUsableCapture(
        LinuxHostProfile profile,
        HostSnapshot snapshot)
    {
        if (!snapshot.SystemState.Equals(
                "Unavailable",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var detail =
            snapshot.Warnings
                .FirstOrDefault(value =>
                    !string.IsNullOrWhiteSpace(
                        value)) ??
            $"The {profile.KindLabel} provider did not return a usable snapshot.";

        throw new InvalidOperationException(
            detail);
    }
}

internal sealed class RemoteLinuxHostProbe :
    ILocalHostProbe
{
    private readonly LinuxSnapshotCollector _collector;

    public RemoteLinuxHostProbe(
        LinuxHostProfile profile,
        LinuxCredentialStore credentials,
        string knownHostsDirectory)
        : this(
            new LinuxSnapshotCollector(
                new SshLinuxCommandRunner(
                    new LinuxSshScriptExecutor(
                        profile,
                        credentials,
                        knownHostsDirectory))))
    {
    }

    internal RemoteLinuxHostProbe(
        LinuxSnapshotCollector collector)
    {
        _collector = collector ??
            throw new ArgumentNullException(
                nameof(collector));
    }

    public Task<HostSnapshot> CaptureAsync(
        CancellationToken cancellationToken = default) =>
        _collector.CaptureAsync(
            cancellationToken);
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
        var scan = await ScanFingerprintAsync(
            profile,
            cancellationToken);

        return await RunVerifiedScriptAsync(
            profile,
            credentials,
            knownHostsDirectory,
            script,
            suppliedSecret,
            scan,
            cancellationToken);
    }

    public static async Task<SshCommandResult>
        RunVerifiedScriptAsync(
            LinuxHostProfile profile,
            LinuxCredentialStore credentials,
            string knownHostsDirectory,
            string script,
            string? suppliedSecret,
            LinuxHostKeyScanResult scan,
            CancellationToken cancellationToken)
    {
        LinuxHostProfileStore.Validate(profile);
        ArgumentNullException.ThrowIfNull(scan);

        if (profile.IsLocal)
        {
            throw new InvalidOperationException(
                "The SSH transport cannot run against the local provider.");
        }

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

        if (OperatingSystem.IsLinux())
        {
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

        var runtimeDirectory =
            Environment.GetEnvironmentVariable(
                "XDG_RUNTIME_DIR");
        if (string.IsNullOrWhiteSpace(
                runtimeDirectory))
        {
            runtimeDirectory =
                Path.GetTempPath();
        }

        var controlDirectory =
            Path.Combine(
                runtimeDirectory,
                "go");
        Directory.CreateDirectory(
            controlDirectory);

        if (OperatingSystem.IsLinux())
        {
            try
            {
                File.SetUnixFileMode(
                    controlDirectory,
                    UnixFileMode.UserRead |
                    UnixFileMode.UserWrite |
                    UnixFileMode.UserExecute);
            }
            catch
            {
                // Control-socket permission tightening is best effort.
            }
        }

        var controlPath =
            Path.Combine(
                controlDirectory,
                "%C");

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
            "LogLevel=ERROR",
            "-o",
            "ControlMaster=auto",
            "-o",
            "ControlPersist=30",
            "-o",
            $"ControlPath={controlPath}"
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

                if (OperatingSystem.IsLinux())
                {
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

