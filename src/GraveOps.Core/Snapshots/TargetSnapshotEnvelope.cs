using GraveOps.Core.Targets;

namespace GraveOps.Core.Snapshots;

public readonly record struct TargetSelection(
    string TargetId,
    long SelectionGeneration);

public readonly record struct TargetRefreshLease(
    string TargetId,
    long SelectionGeneration,
    long RefreshGeneration,
    Guid RefreshId)
{
    public static TargetRefreshLease None { get; } =
        new(string.Empty, 0, 0, Guid.Empty);
}

public sealed record TargetSnapshotEnvelope<TSnapshot>(
    TargetRefreshLease Lease,
    string ProviderId,
    DateTimeOffset CapturedAt,
    TargetCapabilities Capabilities,
    TSnapshot Snapshot);

public interface ITargetRefreshCoordinator
{
    TargetSelection CurrentSelection { get; }

    TargetSelection Select(string targetId);

    TargetRefreshLease BeginRefresh();

    bool IsCurrent(TargetRefreshLease lease);
}

public sealed class TargetRefreshCoordinator :
    ITargetRefreshCoordinator
{
    private readonly object _sync = new();
    private TargetSelection _selection =
        new(string.Empty, 0);
    private long _refreshGeneration;
    private Guid _refreshId = Guid.Empty;

    public TargetSelection CurrentSelection
    {
        get
        {
            lock (_sync)
                return _selection;
        }
    }

    public TargetSelection Select(string targetId)
    {
        if (string.IsNullOrWhiteSpace(targetId))
            throw new ArgumentException(
                "Target ID is required.",
                nameof(targetId));

        lock (_sync)
        {
            _selection = new TargetSelection(
                targetId,
                checked(_selection.SelectionGeneration + 1));
            _refreshGeneration = 0;
            _refreshId = Guid.Empty;
            return _selection;
        }
    }

    public TargetRefreshLease BeginRefresh()
    {
        lock (_sync)
        {
            if (string.IsNullOrWhiteSpace(_selection.TargetId))
            {
                throw new InvalidOperationException(
                    "A target must be selected before refresh begins.");
            }

            _refreshGeneration =
                checked(_refreshGeneration + 1);
            _refreshId = Guid.NewGuid();

            return new TargetRefreshLease(
                _selection.TargetId,
                _selection.SelectionGeneration,
                _refreshGeneration,
                _refreshId);
        }
    }

    public bool IsCurrent(TargetRefreshLease lease)
    {
        lock (_sync)
        {
            return lease.TargetId.Equals(
                       _selection.TargetId,
                       StringComparison.Ordinal) &&
                   lease.SelectionGeneration ==
                       _selection.SelectionGeneration &&
                   lease.RefreshGeneration ==
                       _refreshGeneration &&
                   lease.RefreshId == _refreshId;
        }
    }
}
