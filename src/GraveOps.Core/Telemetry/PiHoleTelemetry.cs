using System.Globalization;

namespace GraveOps.Core.Telemetry;

public sealed record PiHoleTelemetrySnapshot(
    DateTimeOffset CapturedAt,
    ApplicationTelemetryHealth Severity,
    string State,
    bool DnsOnline,
    bool BlockingEnabled,
    string CoreVersion,
    string WebVersion,
    string FtlVersion,
    string Host,
    string Uptime,
    string Load,
    string Temperature,
    long? Queries,
    long? Blocked,
    double? PercentBlocked,
    long? ActiveClients,
    long? TotalClients,
    double? QueryRate,
    long? GravityDomains,
    DateTimeOffset? GravityUpdatedAt,
    string WebUrl,
    string RawEvidence) :
    IApplicationTelemetrySnapshot
{
    public string QueriesText =>
        Queries is { } value
            ? value.ToString(
                "N0",
                CultureInfo.CurrentCulture)
            : "--";

    public string BlockedText =>
        Blocked is { } value
            ? value.ToString(
                "N0",
                CultureInfo.CurrentCulture)
            : "--";

    public string PercentBlockedText =>
        PercentBlocked is { } value
            ? $"{value:0.0}%"
            : "--";

    public string ClientText =>
        ActiveClients is { } active
            ? TotalClients is { } total
                ? $"{active:N0} active · {total:N0} known"
                : $"{active:N0} active"
            : "--";

    public string QueryRateText =>
        QueryRate is { } value &&
        value > 0
            ? $"{value:0.0} q/s"
            : "--";

    public string GravityText =>
        GravityDomains is { } value
            ? value.ToString(
                "N0",
                CultureInfo.CurrentCulture)
            : "--";

    public string GravityAgeText
    {
        get
        {
            if (GravityUpdatedAt is not { } updated)
                return "Update time unavailable";

            var age =
                DateTimeOffset.Now -
                updated;

            return age.TotalDays >= 1
                ? $"Updated {age.TotalDays:0.#}d ago"
                : age.TotalHours >= 1
                    ? $"Updated {age.TotalHours:0.#}h ago"
                    : $"Updated {Math.Max(
                        0,
                        age.TotalMinutes):0}m ago";
        }
    }
}
