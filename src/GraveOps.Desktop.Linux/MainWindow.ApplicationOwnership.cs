using Avalonia.Controls;
using GraveOps.Core.Applications;
using GraveOps.Core.Targets;

namespace GraveOps.Desktop.Linux;

public partial class MainWindow
{
    private readonly ApplicationRegistry
        _applicationRegistry =
            new();

    private readonly Dictionary<
        string,
        TargetApplicationInventory>
        _targetApplicationInventories =
            new(
                StringComparer.OrdinalIgnoreCase);

    private sealed record TargetApplicationInventory(
        LinuxHostProfile Profile,
        DateTimeOffset CapturedAt,
        TargetCapabilities Capabilities,
        ApplicationIdentityResolution Resolution,
        bool IsStale);

    private sealed record OwnedApplicationProjection(
        LinuxHostProfile Profile,
        DateTimeOffset CapturedAt,
        TargetCapabilities Capabilities,
        ApplicationIdentityRecord? Identity,
        OpsIntegration Integration,
        bool IsStale);

    private void RememberApplicationInventory(
        LinuxHostProfile profile,
        DateTimeOffset capturedAt,
        TargetCapabilities capabilities,
        ApplicationIdentityResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(
            profile);
        ArgumentNullException.ThrowIfNull(
            capabilities);
        ArgumentNullException.ThrowIfNull(
            resolution);

        var profileSnapshot =
            SnapshotTargetProfile(
                profile);

        var applications =
            resolution.Records
                .Select(record =>
                {
                    var integration =
                        resolution.Integrations
                            .FirstOrDefault(item =>
                                item.InstanceKey.Equals(
                                    record.SourceKey,
                                    StringComparison.OrdinalIgnoreCase));

                    return CreateApplicationInstance(
                        profileSnapshot,
                        capabilities,
                        record,
                        integration);
                })
                .ToArray();

        _applicationRegistry.ReplaceTargetInventory(
            profileSnapshot.Id,
            applications);

        _targetApplicationInventories[
                profileSnapshot.Id] =
            new TargetApplicationInventory(
                profileSnapshot,
                capturedAt,
                capabilities,
                resolution,
                IsStale: false);

        PersistApplicationInventories();
    }

    private void ForgetApplicationInventory(
        string targetId)
    {
        if (string.IsNullOrWhiteSpace(
                targetId))
        {
            return;
        }

        _targetApplicationInventories.Remove(
            targetId);
        _applicationRegistry.RemoveTarget(
            targetId);

        PersistApplicationInventories();
    }

    private IReadOnlyList<OwnedApplicationProjection>
        OwnedApplicationProjections()
    {
        var activeTargetId =
            _controlPlane.ActiveProfile.Id;

        return _targetApplicationInventories.Values
            .Select(inventory =>
            {
                var currentProfile =
                    _controlPlane.Profiles.Find(
                        inventory.Profile.Id);

                return currentProfile is null
                    ? null
                    : inventory with
                    {
                        Profile =
                            SnapshotTargetProfile(
                                currentProfile)
                    };
            })
            .Where(inventory =>
                inventory is not null)
            .Cast<TargetApplicationInventory>()
            .OrderByDescending(inventory =>
                inventory.Profile.Id.Equals(
                    activeTargetId,
                    StringComparison.OrdinalIgnoreCase))
            .ThenBy(inventory =>
                inventory.IsStale)
            .ThenBy(inventory =>
                inventory.Profile.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .SelectMany(inventory =>
                inventory.Resolution.Integrations
                    .Select(integration =>
                        new OwnedApplicationProjection(
                            inventory.Profile,
                            inventory.CapturedAt,
                            inventory.Capabilities,
                            inventory.Resolution.Records
                                .FirstOrDefault(record =>
                                    record.SourceKey.Equals(
                                        integration.InstanceKey,
                                        StringComparison.OrdinalIgnoreCase)),
                            integration,
                            inventory.IsStale)))
            .ToArray();
    }

    private DateTimeOffset?
        MostRecentApplicationInventoryCapture()
    {
        if (_targetApplicationInventories.Count == 0)
            return null;

        return _targetApplicationInventories.Values
            .Max(item =>
                item.CapturedAt);
    }

    private LinuxMediaApplicationRow?
        FindOwnedMediaRow(
            string sourceKey)
    {
        if (string.IsNullOrWhiteSpace(
                sourceKey))
        {
            return null;
        }

        return _mediaRows.FirstOrDefault(item =>
            item.SourceKey.Equals(
                sourceKey,
                StringComparison.OrdinalIgnoreCase));
    }

    private async Task ActivateOwnedApplicationAsync(
        LinuxMediaApplicationRow row,
        bool openIdentityEditor)
    {
        ArgumentNullException.ThrowIfNull(
            row);

        var profile =
            _controlPlane.Profiles.Find(
                row.OwnerTargetId);

        if (profile is null)
        {
            Get<TextBlock>(
                    "IntegrationActionStatusText")
                .Text =
                $"Owner target '{row.OwnerTargetName}' is no longer saved.";
            return;
        }

        if (!profile.Id.Equals(
                _controlPlane.ActiveProfile.Id,
                StringComparison.OrdinalIgnoreCase))
        {
            Get<TextBlock>(
                    "IntegrationActionStatusText")
                .Text =
                $"Activating {profile.DisplayName}...";

            await SwitchActiveTargetAsync(
                profile);
        }
        else if (row.IsStale)
        {
            Get<TextBlock>(
                    "IntegrationActionStatusText")
                .Text =
                $"Refreshing {profile.DisplayName} before opening cached inventory...";

            await RefreshAsync();
        }

        if (!_acceptedTargetId.Equals(
                profile.Id,
                StringComparison.OrdinalIgnoreCase) ||
            !_targetApplicationInventories.TryGetValue(
                profile.Id,
                out var refreshedInventory) ||
            refreshedInventory.IsStale)
        {
            Get<TextBlock>(
                    "IntegrationActionStatusText")
                .Text =
                $"Could not refresh owner target {profile.DisplayName}.";
            return;
        }

        PopulateMediaHub();

        var activeRow =
            _mediaRows.FirstOrDefault(item =>
                item.OwnerTargetId.Equals(
                    profile.Id,
                    StringComparison.OrdinalIgnoreCase) &&
                item.SourceKey.Equals(
                    row.SourceKey,
                    StringComparison.OrdinalIgnoreCase)) ??
            _mediaRows.FirstOrDefault(item =>
                item.OwnerTargetId.Equals(
                    profile.Id,
                    StringComparison.OrdinalIgnoreCase) &&
                item.IntegrationName.Equals(
                    row.IntegrationName,
                    StringComparison.OrdinalIgnoreCase));

        if (openIdentityEditor)
        {
            Navigate(
                "MediaHubNav");
            _selectedIdentitySourceKey =
                row.SourceKey;
            ShowMediaLauncherSettings();
            SelectIdentityRegistrySource(
                row.SourceKey);
            return;
        }

        var navigationName =
            NavigationForIntegration(
                row.IntegrationName);

        if (!string.IsNullOrWhiteSpace(
                navigationName))
        {
            Navigate(
                navigationName);
            return;
        }

        var integration =
            activeRow?.Integration ??
            _integrations.FirstOrDefault(item =>
                item.InstanceKey.Equals(
                    row.SourceKey,
                    StringComparison.OrdinalIgnoreCase)) ??
            _integrations.FirstOrDefault(item =>
                item.Name.Equals(
                    row.IntegrationName,
                    StringComparison.OrdinalIgnoreCase));

        if (integration is null)
        {
            Get<TextBlock>(
                    "IntegrationActionStatusText")
                .Text =
                $"The application was not returned by {profile.DisplayName}'s latest capture.";
            return;
        }

        await OpenMediaIntegrationAsync(
            integration,
            "IntegrationActionStatusText");
    }

    private static ApplicationInstance
        CreateApplicationInstance(
            LinuxHostProfile profile,
            TargetCapabilities capabilities,
            ApplicationIdentityRecord record,
            OpsIntegration? integration)
    {
        var endpoint =
            ResolveApplicationEndpoint(
                record,
                integration);

        var classification =
            ApplicationIdentityClassifier.Classify(
                new ApplicationIdentityEvidence(
                    record.Product,
                    record.Role,
                    string.Join(
                        " ",
                        new[]
                        {
                            record.Kind,
                            integration?.Kind
                        }.Where(value =>
                            !string.IsNullOrWhiteSpace(
                                value))),
                    record.Protocol,
                    record.DisplayName,
                    string.Join(
                        " ",
                        new[]
                        {
                            record.Evidence,
                            integration?.Evidence
                        }.Where(value =>
                            !string.IsNullOrWhiteSpace(
                                value))),
                    endpoint is not null,
                    record.IsVerified));

        var metadata =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["category"] =
                    record.Category,
                ["identity-role"] =
                    record.Role,
                ["protocol"] =
                    record.Protocol,
                ["kind"] =
                    record.Kind,
                ["verification-state"] =
                    record.VerificationState,
                ["verified"] =
                    record.IsVerified.ToString(),
                ["owns-health"] =
                    record.OwnsHealth.ToString(),
                ["visible"] =
                    record.IsVisible.ToString(),
                ["show-in-navigation"] =
                    record.ShowInNavigation.ToString()
            };

        var application =
            new ApplicationInstance(
                record.SourceKey,
                classification.ProductId,
                record.DisplayName,
                profile.Id,
                classification.Role,
                classification.Runtime,
                endpoint,
                capabilities,
                metadata);

        application.Validate();
        return application;
    }

    private static Uri?
        ResolveApplicationEndpoint(
            ApplicationIdentityRecord record,
            OpsIntegration? integration)
    {
        foreach (var candidate in new[]
                 {
                     record.LaunchUrl,
                     integration?.Endpoint,
                     record.Endpoint
                 })
        {
            if (string.IsNullOrWhiteSpace(
                    candidate) ||
                !Uri.TryCreate(
                    candidate.Trim(),
                    UriKind.Absolute,
                    out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp &&
                 uri.Scheme != Uri.UriSchemeHttps))
            {
                continue;
            }

            return uri;
        }

        return null;
    }
}
