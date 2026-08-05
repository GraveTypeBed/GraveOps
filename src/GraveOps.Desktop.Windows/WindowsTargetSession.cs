using GraveOps.Core.Hosts;
using GraveOps.Core.Providers;
using GraveOps.Core.Security;
using GraveOps.Core.Snapshots;
using GraveOps.Core.Targets;
using GraveOps.Platform.Windows;

namespace GraveOps.Desktop.Windows;

public static class WindowsTargetPaths
{
    private static string RootDirectory =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "GraveOps");

    public static string DefaultTargetsPath =>
        Path.Combine(
            RootDirectory,
            "targets.json");

    public static string DefaultActiveTargetPath =>
        Path.Combine(
            RootDirectory,
            "active-target.json");
}

public static class WindowsTargetCatalog
{
    public const string LocalTargetId =
        "local-windows";

    public static TargetProfile CreateLocal() =>
        new(
            LocalTargetId,
            string.IsNullOrWhiteSpace(
                Environment.MachineName)
                ? "Local Windows"
                : Environment.MachineName,
            HostProviderIds.LocalWindows,
            TargetPlatform.Windows,
            TargetLocation.Local,
            TargetConnectionProfile.Local,
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["role"] =
                    "Local Windows"
            });

    public static string CredentialReferenceFor(
        string targetId)
    {
        if (string.IsNullOrWhiteSpace(
                targetId))
        {
            throw new ArgumentException(
                "The target ID is required.",
                nameof(targetId));
        }

        return
            $"graveops/target/{targetId.Trim()}/password";
    }

    public static string PlexCredentialReferenceFor(
        string targetId)
    {
        if (string.IsNullOrWhiteSpace(
                targetId))
        {
            throw new ArgumentException(
                "The target ID is required.",
                nameof(targetId));
        }

        return
            $"graveops/target/{targetId.Trim()}/plex-token";
    }

    public static string ApplicationCredentialReferenceFor(
        string targetId,
        string applicationId,
        string secretName)
    {
        var target =
            NormalizeCredentialSegment(
                targetId,
                nameof(targetId));

        var application =
            NormalizeCredentialSegment(
                applicationId,
                nameof(applicationId));

        var secret =
            NormalizeCredentialSegment(
                secretName,
                nameof(secretName));

        return
            $"graveops/target/{target}/application/{application}/{secret}";
    }

    private static string NormalizeCredentialSegment(
        string value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            throw new ArgumentException(
                "Credential reference segments are required.",
                parameterName);
        }

        var normalized =
            value.Trim()
                .ToLowerInvariant();

        if (normalized.Any(character =>
                !char.IsLetterOrDigit(character) &&
                character is not '-' and not '_'))
        {
            throw new ArgumentException(
                "Credential reference segments may contain only letters, numbers, hyphens and underscores.",
                parameterName);
        }

        return normalized;
    }

    public static TargetProfile CreateRemote(
        string targetId,
        string displayName,
        string host,
        int port,
        string username,
        WindowsRemoteAuthentication authentication,
        int operationTimeoutSeconds,
        string? pinnedServerCertificateSha256 = null)
    {
        var normalizedTargetId =
            string.IsNullOrWhiteSpace(
                targetId)
                ? throw new ArgumentException(
                    "The target ID is required.",
                    nameof(targetId))
                : targetId.Trim();

        var target =
            new TargetProfile(
                normalizedTargetId,
                displayName?.Trim() ??
                    string.Empty,
                HostProviderIds.RemoteWindows,
                TargetPlatform.Windows,
                TargetLocation.Remote,
                new TargetConnectionProfile(
                    TransportIds.WinRmHttps,
                    host?.Trim(),
                    port,
                    username?.Trim(),
                    CredentialReferenceFor(
                        normalizedTargetId),
                    pinnedServerCertificateSha256,
                    new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        ["authentication"] =
                            authentication.ToString(),
                        ["operation-timeout-seconds"] =
                            operationTimeoutSeconds.ToString(
                                System.Globalization.CultureInfo.InvariantCulture)
                    }),
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["role"] =
                        "Remote Windows"
                });

        target.Validate();

        _ =
            RemoteWindowsConnectionParser.Parse(
                target);

        return target;
    }
}

public static class WindowsHostProviderComposition
{
    public static IHostProviderRegistry Create(
        ICredentialVault credentialVault)
    {
        ArgumentNullException.ThrowIfNull(
            credentialVault);

        return new HostProviderRegistry(
            new IHostProvider[]
            {
                new LocalWindowsHostProvider(),
                RemoteWindowsHostProviderFactory.Create(
                    credentialVault)
            });
    }
}

public sealed class WindowsTargetSession
{
    private readonly object _sync =
        new();

    private readonly ITargetRegistry _targets;
    private readonly IHostProviderRegistry _providers;
    private readonly IActiveTargetStore
        _activeTargetStore;

    private readonly ICredentialVault
        _credentialVault;

    private readonly TargetRefreshCoordinator
        _refreshCoordinator =
            new();

    private TargetProfile? _selectedTarget;
    private TargetCapabilities _capabilities =
        TargetCapabilities.Empty;

    public WindowsTargetSession(
        ITargetRegistry targets,
        IHostProviderRegistry providers)
        : this(
            targets,
            providers,
            new VolatileActiveTargetStore(),
            new UnavailableCredentialVault())
    {
    }

    public WindowsTargetSession(
        ITargetRegistry targets,
        IHostProviderRegistry providers,
        IActiveTargetStore activeTargetStore)
        : this(
            targets,
            providers,
            activeTargetStore,
            new UnavailableCredentialVault())
    {
    }

    public WindowsTargetSession(
        ITargetRegistry targets,
        IHostProviderRegistry providers,
        IActiveTargetStore activeTargetStore,
        ICredentialVault credentialVault)
    {
        _targets =
            targets ??
            throw new ArgumentNullException(
                nameof(targets));

        _providers =
            providers ??
            throw new ArgumentNullException(
                nameof(providers));

        _activeTargetStore =
            activeTargetStore ??
            throw new ArgumentNullException(
                nameof(activeTargetStore));

        _credentialVault =
            credentialVault ??
            throw new ArgumentNullException(
                nameof(credentialVault));
    }

    public static WindowsTargetSession CreateDefault(
        string? targetsPath = null,
        string? activeTargetPath = null)
    {
        var credentialVault =
            new WindowsCredentialVault();

        return new WindowsTargetSession(
            new JsonTargetRegistry(
                targetsPath ??
                WindowsTargetPaths.DefaultTargetsPath),
            WindowsHostProviderComposition.Create(
                credentialVault),
            new JsonActiveTargetStore(
                activeTargetPath ??
                WindowsTargetPaths.DefaultActiveTargetPath),
            credentialVault);
    }

    public TargetProfile? SelectedTarget
    {
        get
        {
            lock (_sync)
                return _selectedTarget;
        }
    }

    public TargetCapabilities CurrentCapabilities
    {
        get
        {
            lock (_sync)
                return _capabilities;
        }
    }

    public IHostProviderRegistry Providers =>
        _providers;

    public bool CredentialVaultAvailable =>
        _credentialVault.IsAvailable;

    public Task<TargetProfile?> FindAsync(
        string targetId,
        CancellationToken cancellationToken = default) =>
        _targets.FindAsync(
            targetId,
            cancellationToken);

    public async Task StoreCredentialAsync(
        string targetId,
        string secret,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(
                secret))
        {
            throw new ArgumentException(
                "The remote Windows password is required.",
                nameof(secret));
        }

        using var secretValue =
            new SecretValue(
                secret);

        await _credentialVault.StoreAsync(
            new CredentialReference(
                WindowsTargetCatalog.CredentialReferenceFor(
                    targetId)),
            secretValue,
            cancellationToken);
    }

    public Task DeleteCredentialAsync(
        string targetId,
        CancellationToken cancellationToken = default) =>
        _credentialVault.DeleteAsync(
            new CredentialReference(
                WindowsTargetCatalog.CredentialReferenceFor(
                    targetId)),
            cancellationToken);

    public async Task StorePlexTokenAsync(
        string targetId,
        string token,
        CancellationToken cancellationToken = default)
    {
        var normalized =
            token?.Trim() ??
            string.Empty;

        if (normalized.Length is < 8 or > 512 ||
            normalized.Contains('\r') ||
            normalized.Contains('\n'))
        {
            throw new ArgumentException(
                "The Plex token must contain 8 to 512 characters on one line.",
                nameof(token));
        }

        using var secret =
            new SecretValue(
                normalized);

        await _credentialVault.StoreAsync(
            new CredentialReference(
                WindowsTargetCatalog.PlexCredentialReferenceFor(
                    targetId)),
            secret,
            cancellationToken);
    }

    public Task<SecretValue?> RetrievePlexTokenAsync(
        string targetId,
        CancellationToken cancellationToken = default) =>
        _credentialVault.RetrieveAsync(
            new CredentialReference(
                WindowsTargetCatalog.PlexCredentialReferenceFor(
                    targetId)),
            cancellationToken);

    public Task DeletePlexTokenAsync(
        string targetId,
        CancellationToken cancellationToken = default) =>
        _credentialVault.DeleteAsync(
            new CredentialReference(
                WindowsTargetCatalog.PlexCredentialReferenceFor(
                    targetId)),
            cancellationToken);

    public async Task StoreApplicationSecretAsync(
        string targetId,
        string applicationId,
        string secretName,
        string value,
        CancellationToken cancellationToken = default)
    {
        var normalized =
            value?.Trim() ??
            string.Empty;

        if (normalized.Length is < 8 or > 512 ||
            normalized.Contains('\r') ||
            normalized.Contains('\n'))
        {
            throw new ArgumentException(
                "Application secrets must contain 8 to 512 characters on one line.",
                nameof(value));
        }

        using var secret =
            new SecretValue(
                normalized);

        await _credentialVault.StoreAsync(
            new CredentialReference(
                WindowsTargetCatalog.ApplicationCredentialReferenceFor(
                    targetId,
                    applicationId,
                    secretName)),
            secret,
            cancellationToken);
    }

    public Task<SecretValue?> RetrieveApplicationSecretAsync(
        string targetId,
        string applicationId,
        string secretName,
        CancellationToken cancellationToken = default) =>
        _credentialVault.RetrieveAsync(
            new CredentialReference(
                WindowsTargetCatalog.ApplicationCredentialReferenceFor(
                    targetId,
                    applicationId,
                    secretName)),
            cancellationToken);

    public Task DeleteApplicationSecretAsync(
        string targetId,
        string applicationId,
        string secretName,
        CancellationToken cancellationToken = default) =>
        _credentialVault.DeleteAsync(
            new CredentialReference(
                WindowsTargetCatalog.ApplicationCredentialReferenceFor(
                    targetId,
                    applicationId,
                    secretName)),
            cancellationToken);

    public async Task<IReadOnlyList<TargetProfile>>
        InitializeAsync(
            CancellationToken cancellationToken = default)
    {
        var targets =
            await _targets.ListAsync(
                cancellationToken);

        if (!targets.Any(
                target =>
                    target.Id.Equals(
                        WindowsTargetCatalog.LocalTargetId,
                        StringComparison.Ordinal)))
        {
            await _targets.UpsertAsync(
                WindowsTargetCatalog.CreateLocal(),
                cancellationToken);

            targets =
                await _targets.ListAsync(
                    cancellationToken);
        }

        var local =
            targets.First(
                target =>
                    target.Id.Equals(
                        WindowsTargetCatalog.LocalTargetId,
                        StringComparison.Ordinal));

        var preferredTargetId =
            await _activeTargetStore.LoadAsync(
                cancellationToken);

        var selected =
            targets.FirstOrDefault(
                target =>
                    target.Id.Equals(
                        preferredTargetId,
                        StringComparison.Ordinal) &&
                    _providers.TryResolve(
                        target,
                        out _)) ??
            local;

        lock (_sync)
        {
            SetSelectedUnsafe(
                selected);
        }

        await _activeTargetStore.SaveAsync(
            selected.Id,
            cancellationToken);

        return targets;
    }

    public Task<IReadOnlyList<TargetProfile>>
        ListAsync(
            CancellationToken cancellationToken = default) =>
        _targets.ListAsync(
            cancellationToken);

    public async Task<TargetProfile> SelectAsync(
        string targetId,
        CancellationToken cancellationToken = default)
    {
        var target =
            await _targets.FindAsync(
                targetId,
                cancellationToken) ??
            throw new KeyNotFoundException(
                $"Target '{targetId}' was not found.");

        _providers.Resolve(
            target);

        lock (_sync)
        {
            SetSelectedUnsafe(
                target);
        }

        await _activeTargetStore.SaveAsync(
            target.Id,
            cancellationToken);

        return target;
    }

    public async Task CreateAsync(
        TargetProfile target,
        CancellationToken cancellationToken = default)
    {
        ValidateMutationTarget(
            target);

        var existing =
            await _targets.FindAsync(
                target.Id,
                cancellationToken);

        if (existing is not null)
        {
            throw new InvalidOperationException(
                $"Target '{target.Id}' already exists.");
        }

        await _targets.UpsertAsync(
            target,
            cancellationToken);
    }

    public async Task UpsertAsync(
        TargetProfile target,
        CancellationToken cancellationToken = default)
    {
        ValidateMutationTarget(
            target);

        await _targets.UpsertAsync(
            target,
            cancellationToken);

        lock (_sync)
        {
            if (_selectedTarget?.Id.Equals(
                    target.Id,
                    StringComparison.Ordinal) ==
                true)
            {
                SetSelectedUnsafe(
                    target);
            }
        }
    }

    public async Task<bool> RemoveAsync(
        string targetId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                targetId) ||
            targetId.Equals(
                WindowsTargetCatalog.LocalTargetId,
                StringComparison.Ordinal))
        {
            return false;
        }

        var removed =
            await _targets.RemoveAsync(
                targetId,
                cancellationToken);

        if (!removed)
            return false;

        var selected =
            SelectedTarget;

        if (selected?.Id.Equals(
                targetId,
                StringComparison.Ordinal) !=
            true)
        {
            return true;
        }

        var local =
            await _targets.FindAsync(
                WindowsTargetCatalog.LocalTargetId,
                cancellationToken) ??
            throw new InvalidOperationException(
                "The required local Windows target is missing.");

        lock (_sync)
        {
            SetSelectedUnsafe(
                local);
        }

        await _activeTargetStore.SaveAsync(
            local.Id,
            cancellationToken);

        return true;
    }

    public async Task<
        TargetSnapshotEnvelope<HostSnapshot>>
        CaptureAsync(
            CancellationToken cancellationToken = default)
    {
        TargetProfile target;
        TargetRefreshLease lease;

        lock (_sync)
        {
            target =
                _selectedTarget ??
                throw new InvalidOperationException(
                    "A target must be initialized and selected before capture.");

            lease =
                _refreshCoordinator.BeginRefresh();
        }

        var provider =
            _providers.Resolve(
                target);

        var result =
            await provider.CaptureAsync(
                target,
                lease,
                cancellationToken);

        lock (_sync)
        {
            var current =
                _selectedTarget?.Id.Equals(
                    target.Id,
                    StringComparison.Ordinal) ==
                true &&
                _refreshCoordinator.IsCurrent(
                    result.Lease);

            if (!current)
            {
                throw new OperationCanceledException(
                    "The capture belongs to an earlier target selection or refresh generation.");
            }

            _capabilities =
                result.Capabilities;
        }

        return result;
    }

    private void ValidateMutationTarget(
        TargetProfile target)
    {
        ArgumentNullException.ThrowIfNull(
            target);
        target.Validate();

        if (target.Id.Equals(
                WindowsTargetCatalog.LocalTargetId,
                StringComparison.Ordinal) &&
            (
                !target.IsLocal ||
                !target.ProviderId.Equals(
                    HostProviderIds.LocalWindows,
                    StringComparison.Ordinal) ||
                !target.Connection.TransportId.Equals(
                    TransportIds.Local,
                    StringComparison.Ordinal)
            ))
        {
            throw new InvalidOperationException(
                "The required local Windows target cannot be replaced.");
        }

        _providers.Resolve(
            target);
    }

    private void SetSelectedUnsafe(
        TargetProfile target)
    {
        _selectedTarget =
            target;

        _capabilities =
            TargetCapabilities.Empty;

        _refreshCoordinator.Select(
            target.Id);
    }
}

internal sealed class UnavailableCredentialVault :
    ICredentialVault
{
    public string VaultId =>
        "unavailable";

    public bool IsAvailable =>
        false;

    public Task StoreAsync(
        CredentialReference reference,
        SecretValue secret,
        CancellationToken cancellationToken = default) =>
        throw new PlatformNotSupportedException(
            "No credential vault was configured for this target session.");

    public Task<SecretValue?> RetrieveAsync(
        CredentialReference reference,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<SecretValue?>(
            null);

    public Task DeleteAsync(
        CredentialReference reference,
        CancellationToken cancellationToken = default) =>
        throw new PlatformNotSupportedException(
            "No credential vault was configured for this target session.");
}

public static class WindowsTargetNavigationPolicy
{
    public static bool IsSupported(
        string navigationName,
        TargetCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(
            capabilities);

        return navigationName switch
        {
            "ServicesNav" =>
                capabilities.Supports(
                    CapabilityIds.ServicesRead),

            "DockerNav" =>
                capabilities.Supports(
                    CapabilityIds.ContainersRead),

            "StorageNav" =>
                capabilities.Supports(
                    CapabilityIds.StorageRead),

            "LogsNav" =>
                capabilities.Supports(
                    CapabilityIds.EventLogRead) ||
                capabilities.Supports(
                    CapabilityIds.JournalRead),

            "BackupsNav" =>
                capabilities.Supports(
                    CapabilityIds.BackupInventoryRead),

            _ =>
                true
        };
    }

    public static string UnsupportedReason(
        string navigationName) =>
        navigationName switch
        {
            "ServicesNav" =>
                "The selected target does not report service inventory capability.",

            "DockerNav" =>
                "The selected target does not report container inventory capability.",

            "StorageNav" =>
                "The selected target does not report storage inventory capability.",

            "LogsNav" =>
                "The selected target reports neither Windows event-log nor Linux journal capability.",

            "BackupsNav" =>
                "Backup inventory requires an explicitly reported provider capability.",

            _ =>
                "The selected target does not support this workspace."
        };
}