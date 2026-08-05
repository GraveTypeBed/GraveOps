namespace GraveOps.Core.Applications;

public interface IApplicationRegistry
{
    IReadOnlyList<ApplicationInstance> Applications { get; }

    void ReplaceTargetInventory(
        string targetId,
        IEnumerable<ApplicationInstance> applications);

    IReadOnlyList<ApplicationInstance> ForTarget(
        string targetId);

    ApplicationInstance? Find(
        string applicationId);

    string? ResolveOwnerTargetId(
        string applicationId);

    int RemoveTarget(
        string targetId);
}

public sealed class ApplicationRegistry :
    IApplicationRegistry
{
    private readonly object _sync =
        new();

    private Dictionary<string, ApplicationInstance>
        _applications =
            new(
                StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ApplicationInstance>
        Applications
    {
        get
        {
            lock (_sync)
            {
                return _applications.Values
                    .OrderBy(item =>
                        item.OwnerTargetId,
                        StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item =>
                        item.ProductId,
                        StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item =>
                        item.DisplayName,
                        StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item =>
                        item.Id,
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
    }

    public void ReplaceTargetInventory(
        string targetId,
        IEnumerable<ApplicationInstance> applications)
    {
        if (string.IsNullOrWhiteSpace(
                targetId))
        {
            throw new ArgumentException(
                "Target ID is required.",
                nameof(targetId));
        }

        ArgumentNullException.ThrowIfNull(
            applications);

        var normalizedTargetId =
            targetId.Trim();

        var incoming =
            applications.ToArray();

        foreach (var application in incoming)
        {
            application.Validate();

            if (!application.OwnerTargetId.Equals(
                    normalizedTargetId,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Application '{application.Id}' belongs to " +
                    $"'{application.OwnerTargetId}', not " +
                    $"'{normalizedTargetId}'.");
            }
        }

        var duplicate =
            incoming
                .GroupBy(
                    item => item.Id,
                    StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group =>
                    group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Application ID '{duplicate.Key}' appears more than once " +
                $"in target '{normalizedTargetId}'.");
        }

        lock (_sync)
        {
            var next =
                _applications.Values
                    .Where(item =>
                        !item.OwnerTargetId.Equals(
                            normalizedTargetId,
                            StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(
                        item => item.Id,
                        StringComparer.OrdinalIgnoreCase);

            foreach (var application in incoming)
            {
                if (next.TryGetValue(
                        application.Id,
                        out var existing))
                {
                    throw new InvalidOperationException(
                        $"Application ID '{application.Id}' is already owned " +
                        $"by target '{existing.OwnerTargetId}'.");
                }

                next[application.Id] =
                    application;
            }

            _applications =
                next;
        }
    }

    public IReadOnlyList<ApplicationInstance> ForTarget(
        string targetId)
    {
        if (string.IsNullOrWhiteSpace(
                targetId))
        {
            return Array.Empty<ApplicationInstance>();
        }

        lock (_sync)
        {
            return _applications.Values
                .Where(item =>
                    item.OwnerTargetId.Equals(
                        targetId.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                .OrderBy(item =>
                    item.ProductId,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(item =>
                    item.DisplayName,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(item =>
                    item.Id,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public ApplicationInstance? Find(
        string applicationId)
    {
        if (string.IsNullOrWhiteSpace(
                applicationId))
        {
            return null;
        }

        lock (_sync)
        {
            return _applications.TryGetValue(
                    applicationId.Trim(),
                    out var application)
                ? application
                : null;
        }
    }

    public string? ResolveOwnerTargetId(
        string applicationId) =>
        Find(applicationId)?
            .OwnerTargetId;

    public int RemoveTarget(
        string targetId)
    {
        if (string.IsNullOrWhiteSpace(
                targetId))
        {
            return 0;
        }

        lock (_sync)
        {
            var keys =
                _applications
                    .Where(item =>
                        item.Value.OwnerTargetId.Equals(
                            targetId.Trim(),
                            StringComparison.OrdinalIgnoreCase))
                    .Select(item =>
                        item.Key)
                    .ToArray();

            foreach (var key in keys)
            {
                _applications.Remove(
                    key);
            }

            return keys.Length;
        }
    }
}
