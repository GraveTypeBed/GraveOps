namespace GraveOps.Presentation.Avalonia.Fleet;

public enum UnifiedFleetFocus
{
    Hosts = 0,
    Applications = 1
}

public sealed record UnifiedFleetHostRow(
    string TargetId,
    string DisplayName,
    string Platform,
    string Connection,
    string State,
    string CapabilitySummary,
    int ApplicationCount,
    DateTimeOffset? CapturedAt,
    bool IsActive,
    bool IsStale,
    bool CanActivate)
{
    public string CaptureLabel =>
        CapturedAt is null
            ? "No accepted capture"
            : CapturedAt.Value.ToLocalTime().ToString("g");

    public string StatusLabel =>
        IsActive
            ? "ACTIVE"
            : IsStale
                ? "STALE"
                : State.ToUpperInvariant();
}

public sealed record UnifiedFleetApplicationRow(
    string ApplicationKey,
    string Product,
    string DisplayName,
    string Category,
    string Role,
    string Runtime,
    string OwnerTargetId,
    string OwnerTargetName,
    string State,
    string Summary,
    bool IsVerified,
    bool IsStale,
    bool CanOpen,
    bool CanEditIdentity,
    string NavigationKey)
{
    public string VerificationLabel =>
        IsStale
            ? "CACHED"
            : IsVerified
                ? "VERIFIED"
                : "CANDIDATE";

    public string OwnerLabel =>
        string.IsNullOrWhiteSpace(OwnerTargetName)
            ? OwnerTargetId
            : OwnerTargetName;
}

public sealed record UnifiedFleetState(
    IReadOnlyList<UnifiedFleetHostRow> Hosts,
    IReadOnlyList<UnifiedFleetApplicationRow> Applications,
    string Status,
    string InventoryDetail)
{
    public static UnifiedFleetState Empty { get; } =
        new(
            Array.Empty<UnifiedFleetHostRow>(),
            Array.Empty<UnifiedFleetApplicationRow>(),
            "Waiting for fleet inventory.",
            "No platform adapter has projected inventory yet.");
}

public sealed class UnifiedFleetHostRequestedEventArgs(
    string targetId)
    : EventArgs
{
    public string TargetId { get; } =
        targetId;
}

public sealed class UnifiedFleetApplicationRequestedEventArgs(
    string applicationKey,
    string ownerTargetId,
    bool editIdentity)
    : EventArgs
{
    public string ApplicationKey { get; } =
        applicationKey;

    public string OwnerTargetId { get; } =
        ownerTargetId;

    public bool EditIdentity { get; } =
        editIdentity;
}