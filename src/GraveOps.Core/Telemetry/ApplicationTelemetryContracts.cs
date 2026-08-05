namespace GraveOps.Core.Telemetry;

public enum ApplicationTelemetryHealth
{
    Unknown = 0,
    Healthy = 1,
    Informational = 2,
    Warning = 3,
    Error = 4
}

public enum ApplicationTelemetrySourceKind
{
    Unknown = 0,
    HostProbe = 1,
    ApplicationApi = 2,
    Hybrid = 3
}

public interface IApplicationTelemetrySnapshot
{
    DateTimeOffset CapturedAt { get; }
}

public interface IApplicationTelemetryAdapter<TContext, TSnapshot>
    where TSnapshot : IApplicationTelemetrySnapshot
{
    Task<TSnapshot> CaptureAsync(
        TContext context,
        CancellationToken cancellationToken = default);
}

public sealed record ApplicationTelemetryEnvelope<TSnapshot>(
    string TargetId,
    string ApplicationId,
    string ProductId,
    ApplicationTelemetrySourceKind SourceKind,
    DateTimeOffset ReceivedAt,
    TSnapshot Snapshot,
    IReadOnlyList<string> Warnings)
    where TSnapshot : IApplicationTelemetrySnapshot
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(TargetId))
        {
            throw new InvalidOperationException(
                "Telemetry target ID is required.");
        }

        if (string.IsNullOrWhiteSpace(ApplicationId))
        {
            throw new InvalidOperationException(
                "Telemetry application ID is required.");
        }

        if (string.IsNullOrWhiteSpace(ProductId))
        {
            throw new InvalidOperationException(
                "Telemetry product ID is required.");
        }

        ArgumentNullException.ThrowIfNull(Snapshot);
        ArgumentNullException.ThrowIfNull(Warnings);
    }

    public bool IsStale(
        TimeSpan maximumAge,
        DateTimeOffset? now = null)
    {
        if (maximumAge < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumAge));
        }

        var reference =
            now ?? DateTimeOffset.UtcNow;

        return reference -
            Snapshot.CapturedAt.ToUniversalTime() >
            maximumAge;
    }
}

public static class ApplicationTelemetryHealthClassifier
{
    public static ApplicationTelemetryHealth FromState(
        string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
            return ApplicationTelemetryHealth.Unknown;

        var normalized =
            state.Trim();

        if (ContainsAny(
                normalized,
                "offline",
                "unavailable",
                "failed",
                "error",
                "unhealthy",
                "dns offline"))
        {
            return ApplicationTelemetryHealth.Error;
        }

        if (ContainsAny(
                normalized,
                "warning",
                "attention",
                "degraded",
                "paused",
                "disabled",
                "stalled"))
        {
            return ApplicationTelemetryHealth.Warning;
        }

        if (ContainsAny(
                normalized,
                "online",
                "healthy",
                "running",
                "active",
                "enabled",
                "connected"))
        {
            return ApplicationTelemetryHealth.Healthy;
        }

        if (ContainsAny(
                normalized,
                "info",
                "idle",
                "unknown",
                "checking"))
        {
            return ApplicationTelemetryHealth.Informational;
        }

        return ApplicationTelemetryHealth.Unknown;
    }

    private static bool ContainsAny(
        string state,
        params string[] values) =>
        values.Any(value =>
            state.Contains(
                value,
                StringComparison.OrdinalIgnoreCase));
}
