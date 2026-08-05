namespace GraveOps.Core.Targets;

public interface ITargetRegistry
{
    Task<IReadOnlyList<TargetProfile>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<TargetProfile?> FindAsync(
        string targetId,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(
        TargetProfile target,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(
        string targetId,
        CancellationToken cancellationToken = default);
}
