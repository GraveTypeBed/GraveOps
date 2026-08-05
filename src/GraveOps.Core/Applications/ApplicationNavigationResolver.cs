namespace GraveOps.Core.Applications;

public static class ApplicationNavigationResolver
{
    public const string ShowInNavigationMetadataKey =
        "show-in-navigation";

    public static bool IsNavigationVisible(
        ApplicationInstance application)
    {
        ArgumentNullException.ThrowIfNull(
            application);

        return application.Metadata is not null &&
            application.Metadata.TryGetValue(
                ShowInNavigationMetadataKey,
                out var value) &&
            bool.TryParse(
                value,
                out var visible) &&
            visible;
    }

    public static IReadOnlyList<ApplicationInstance>
        FindCandidates(
            IApplicationRegistry registry,
            string productId)
    {
        ArgumentNullException.ThrowIfNull(
            registry);

        if (string.IsNullOrWhiteSpace(
                productId))
        {
            return Array.Empty<ApplicationInstance>();
        }

        return registry
            .ForProduct(
                productId)
            .Where(
                IsNavigationVisible)
            .OrderBy(application =>
                application.OwnerTargetId,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(application =>
                application.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(application =>
                application.Id,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static ApplicationInstance?
        ResolvePreferredOwner(
            IApplicationRegistry registry,
            string productId,
            string? activeTargetId,
            Func<string, bool>? targetIsStale = null)
    {
        var normalizedActiveTargetId =
            activeTargetId?.Trim() ??
            string.Empty;

        return FindCandidates(
                registry,
                productId)
            .OrderByDescending(application =>
                application.OwnerTargetId.Equals(
                    normalizedActiveTargetId,
                    StringComparison.OrdinalIgnoreCase))
            .ThenBy(application =>
                targetIsStale?.Invoke(
                    application.OwnerTargetId) ==
                true)
            .ThenBy(application =>
                application.OwnerTargetId,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(application =>
                application.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(application =>
                application.Id,
                StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }
}
