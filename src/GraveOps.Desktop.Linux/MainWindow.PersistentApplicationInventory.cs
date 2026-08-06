using System.Diagnostics;
using GraveOps.Core.Applications;
using GraveOps.Core.Targets;

namespace GraveOps.Desktop.Linux;

public partial class MainWindow
{
    private ApplicationInventoryCacheStore?
        _applicationInventoryCacheStore;

    private ApplicationInventoryCacheStore
        ApplicationInventoryCacheStore =>
        _applicationInventoryCacheStore ??=
            new ApplicationInventoryCacheStore(
                _operatorSettingsStore.InventoryCachePath);

    private void InitializePersistentApplicationInventory()
    {
        var result =
            ApplicationInventoryCacheStore.Load();

        foreach (var warning in result.Warnings)
            Debug.WriteLine(warning);

        foreach (var cachedTarget in result.Document.Targets)
        {
            var profile =
                _controlPlane.Profiles.Find(
                    cachedTarget.TargetId);

            if (profile is null)
                continue;

            try
            {
                var capabilities =
                    new TargetCapabilities(
                        cachedTarget.CapabilityIds);

                var applications =
                    cachedTarget.Applications
                        .Select(application =>
                            ApplicationInventoryCacheStore
                                .ToApplicationInstance(
                                    application,
                                    capabilities))
                        .ToArray();

                _applicationRegistry.ReplaceTargetInventory(
                    profile.Id,
                    applications);

                var integrations =
                    cachedTarget.Applications
                        .Select(CreateCachedIntegration)
                        .ToArray();

                _targetApplicationInventories[
                        profile.Id] =
                    new TargetApplicationInventory(
                        SnapshotTargetProfile(profile),
                        cachedTarget.CapturedAt,
                        capabilities,
                        new ApplicationIdentityResolution(
                            Array.Empty<
                                ApplicationIdentityRecord>(),
                            integrations),
                        IsStale: true);
            }
            catch (Exception exception)
            {
                Debug.WriteLine(
                    $"Ignored cached inventory for " +
                    $"{cachedTarget.TargetId}: " +
                    $"{exception.Message}");
            }
        }
    }

    private void PersistApplicationInventories()
    {
        try
        {
            var targets =
                _targetApplicationInventories.Values
                    .Select(inventory =>
                    {
                        var integrationIds =
                            inventory.Resolution.Integrations
                                .Select(item => item.InstanceKey)
                                .ToHashSet(
                                    StringComparer.OrdinalIgnoreCase);

                        var applications =
                            _applicationRegistry
                                .ForTarget(
                                    inventory.Profile.Id)
                                .Where(application =>
                                    integrationIds.Contains(
                                        application.Id))
                                .ToArray();

                        return ApplicationInventoryCacheStore
                            .CreateTarget(
                                inventory.Profile.Id,
                                inventory.Profile.DisplayName,
                                inventory.CapturedAt,
                                inventory.Capabilities,
                                applications);
                    })
                    .ToArray();

            ApplicationInventoryCacheStore.Save(targets);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"Could not persist redacted fleet inventory: " +
                $"{exception.Message}");
        }
    }

    private int StaleApplicationInventoryTargetCount() =>
        _targetApplicationInventories.Values
            .Count(item => item.IsStale);

    private static OpsIntegration CreateCachedIntegration(
        CachedApplicationInventory cached)
    {
        var category =
            CachedMetadata(cached, "category");
        var identityRole =
            CachedMetadata(cached, "identity-role");
        var protocol =
            CachedMetadata(cached, "protocol");
        var verified =
            CachedBoolean(
                cached,
                "verified",
                fallback: true);
        var visible =
            CachedBoolean(
                cached,
                "visible",
                fallback: true);
        var showInNavigation =
            CachedBoolean(
                cached,
                ApplicationNavigationResolver
                    .ShowInNavigationMetadataKey,
                fallback: false);

        return new OpsIntegration(
            cached.ProductId,
            cached.Runtime.ToString(),
            "Cached inventory",
            "Persisted redacted fleet inventory. Refresh the owner target for current state.",
            string.Empty,
            OpsSeverity.Info)
        {
            InstanceKey =
                cached.Id,
            DisplayName =
                cached.DisplayName,
            Category =
                string.IsNullOrWhiteSpace(category)
                    ? ApplicationIdentityCatalog
                        .DefaultCategory(cached.ProductId)
                    : category,
            Role =
                string.IsNullOrWhiteSpace(identityRole)
                    ? CachedApplicationRoleLabel(cached.Role)
                    : identityRole,
            Protocol =
                protocol,
            OwnsHealth =
                false,
            IsVerified =
                verified,
            IsVisible =
                visible,
            ShowInNavigation =
                showInNavigation,
            Provenance =
                "Persistent redacted cache"
        };
    }

    private static string CachedMetadata(
        CachedApplicationInventory cached,
        string key) =>
        cached.Metadata.TryGetValue(
            key,
            out var value)
            ? value
            : string.Empty;

    private static bool CachedBoolean(
        CachedApplicationInventory cached,
        string key,
        bool fallback) =>
        cached.Metadata.TryGetValue(
            key,
            out var value) &&
        bool.TryParse(
            value,
            out var parsed)
            ? parsed
            : fallback;

    private static string CachedApplicationRoleLabel(
        ApplicationRole role) =>
        role switch
        {
            ApplicationRole.Server =>
                ApplicationIdentityRoles.NativeApplication,
            ApplicationRole.DesktopClient =>
                ApplicationIdentityRoles.CompatibilityInterface,
            ApplicationRole.Service =>
                ApplicationIdentityRoles.SupportingService,
            ApplicationRole.WebApplication =>
                ApplicationIdentityRoles.NativeApplication,
            ApplicationRole.Agent =>
                ApplicationIdentityRoles.SupportingService,
            _ =>
                ApplicationIdentityRoles.DiscoveryCandidate
        };
}
