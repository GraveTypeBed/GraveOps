namespace GraveOps.Core.Execution;

public interface IQueryRunner<in TRequest, TResponse>
{
    string RunnerId { get; }

    Task<TResponse> ExecuteAsync(
        TRequest request,
        CancellationToken cancellationToken = default);
}
