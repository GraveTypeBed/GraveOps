using GraveOps.App.Models;

namespace GraveOps.App.Services;

public sealed class IntegrationAssignmentService
{
    private readonly ConfigService _config;
    private readonly IntegrationCatalog _catalog;
    private readonly HashSet<string> _applicationKeys;

    public IntegrationAssignmentService(ConfigService config, IntegrationCatalog catalog)
    {
        _config = config;
        _catalog = catalog;
        _applicationKeys = catalog.All
            .Where(x => x.Category != IntegrationCategory.Infrastructure)
            .Select(x => x.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public void ApplyVerified(
        ServerProfile profile,
        IEnumerable<DetectedIntegrationOption> detected,
        string source)
    {
        var now = DateTimeOffset.UtcNow;
        var options = detected
            .Where(x => x.Enabled)
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.Category)
            .ThenBy(x => x.DisplayName)
            .ToArray();

        // Remove only entries created by a previous verified discovery pass. Legacy
        // or manually configured entries are preserved until they are verified or the
        // user explicitly edits/deletes them.
        _config.Current.Applications.RemoveAll(x =>
            x.ServerId == profile.Id &&
            _applicationKeys.Contains(x.Name) &&
            x.DiscoveryVerified);

        profile.EnabledModules.RemoveAll(x => _applicationKeys.Contains(x));

        foreach (var integration in options)
        {
            AddModule(profile, integration.Key);

            // Verified ownership is useful even for non-web integrations such as
            // Recyclarr. Keep a ManagedApp record with an empty URL so fleet
            // navigation and environment health can still route to the owning host.
            var app = _config.Current.Applications.FirstOrDefault(x =>
                x.ServerId == profile.Id &&
                x.Name.Equals(integration.DisplayName, StringComparison.OrdinalIgnoreCase));

            if (app is null)
            {
                app = new ManagedApp
                {
                    Name = integration.DisplayName,
                    ServerId = profile.Id
                };
                _config.Current.Applications.Add(app);
            }

            app.Category = ToAppCategory(integration.Category);
            app.Url = integration.Url ?? "";
            app.OpenEmbedded = true;
            app.DiscoveryVerified = true;
            app.DiscoveryEvidence = integration.Evidence;
            app.DiscoveredUtc = now;
        }

        profile.LastIntegrationDiscoveryUtc = now;
        profile.IntegrationDiscoverySummary = options.Length == 0
            ? $"{source}: no supported integrations verified."
            : $"{source}: verified {options.Length} integration(s): {string.Join(", ", options.Select(x => x.DisplayName))}.";

        _config.Save();
    }

    private static string ToAppCategory(IntegrationCategory category) => category switch
    {
        IntegrationCategory.Library => "Media",
        IntegrationCategory.Acquisition => "Arr",
        IntegrationCategory.Downloads => "Downloads",
        IntegrationCategory.QualityAutomation => "Automation",
        IntegrationCategory.Processing => "Processing",
        IntegrationCategory.Lifecycle => "Lifecycle",
        IntegrationCategory.Network => "Network",
        IntegrationCategory.Infrastructure => "Infrastructure",
        _ => "Other"
    };

    private static void AddModule(ServerProfile profile, string module)
    {
        if (!profile.EnabledModules.Any(x => x.Equals(module, StringComparison.OrdinalIgnoreCase)))
            profile.EnabledModules.Add(module);
    }
}
