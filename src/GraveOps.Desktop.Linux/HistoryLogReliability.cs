using System.Globalization;

namespace GraveOps.Desktop.Linux;

public sealed class ReliableLogRow
{
    public ReliableLogRow(OpsLogGroup original)
    {
        Original = original;
    }

    public OpsLogGroup Original { get; }
    public OpsSeverity Severity =>
        SignalQualityPolicy.DisplaySeverity(Original);
    public string SeverityLabel =>
        LinuxOpsAnalyzer.SeverityLabel(Severity);
    public string Source => Original.Source;
    public string DisplayTime =>
        Original.LastSeen.ToLocalTime().ToString("g");
    public int Count => Original.Count;
    public string Message => Original.Message;
    public string Key =>
        $"{Original.Source}|{Original.Message}";
}

public sealed record ReliableHistoryProjection(
    IReadOnlyList<InsightHistoryRow> Transitions,
    IReadOnlyList<InsightHistoryRow> Activities,
    IReadOnlyList<InsightHistoryRow> Incidents,
    int RawFilteredCount,
    int VisibleCount,
    int CollapsedCount,
    int HiddenNavigationCount,
    string Summary);

public sealed record ReliableLogProjection(
    IReadOnlyList<ReliableLogRow> Rows,
    int ActiveCount,
    int BackgroundCount,
    int SourceCount,
    string EmptyTitle,
    string EmptyDetail,
    string Summary);

public static class HistoryLogReliabilityPresenter
{
    public static readonly string[] HistoryClassFilters =
    {
        "All meaningful",
        "All events",
        "Incidents",
        "Health transitions",
        "Actions & changes",
        "Notifications",
        "Navigation"
    };

    public static readonly string[] HistorySeverityFilters =
    {
        "All severities",
        "Warnings & errors",
        "Errors only"
    };

    public static readonly string[] HistoryTimeFilters =
    {
        "Last 24 hours",
        "Last 7 days",
        "All retained"
    };

    public static readonly string[] LogSeverityFilters =
    {
        "Warnings & errors",
        "Errors only"
    };

    public static readonly string[] LogTimeFilters =
    {
        "Last hour",
        "Last 24 hours",
        "All retained"
    };

    public static ReliableHistoryProjection BuildHistory(
        IReadOnlyList<InsightHistoryRow> source,
        string classFilter,
        string severityFilter,
        string timeFilter,
        string sourceFilter,
        string textFilter)
    {
        var normalized = source
            .Select(ClassifyHistoryRow)
            .OrderByDescending(item => item.Timestamp)
            .ToArray();

        var hiddenNavigation =
            normalized.Count(item =>
                item.Stream.Equals(
                    "Navigation",
                    StringComparison.Ordinal));

        var since = HistorySince(timeFilter);
        var filtered = normalized
            .Where(item =>
                item.Timestamp >= since)
            .Where(item =>
                MatchesHistoryClass(
                    item,
                    classFilter))
            .Where(item =>
                MatchesSeverity(
                    item.Severity,
                    severityFilter))
            .Where(item =>
                MatchesAny(
                    sourceFilter,
                    item.Target,
                    item.Component,
                    item.Stream))
            .Where(item =>
                MatchesAny(
                    textFilter,
                    item.Transition,
                    item.Detail,
                    item.Target,
                    item.Component,
                    item.Stream))
            .ToArray();

        var collapsed = 0;
        var transitions = CollapseIdentical(
            filtered.Where(IsHealthTransition),
            ref collapsed);
        var activities = CollapseIdentical(
            filtered.Where(item =>
                !IsHealthTransition(item)),
            ref collapsed);

        var incidents = transitions
            .Concat(activities)
            .Where(item =>
                item.Severity >= OpsSeverity.Warning)
            .OrderByDescending(item => item.Timestamp)
            .ToArray();

        var visible =
            transitions.Count +
            activities.Count;

        var summary = visible == 0
            ? source.Count == 0
                ? "No retained history exists yet."
                : "No history row matches the current filters."
            : $"{visible} visible from {filtered.Length} matched raw event(s) · " +
              $"{collapsed} duplicate event(s) collapsed";

        if (classFilter.Equals(
                "All meaningful",
                StringComparison.Ordinal) &&
            hiddenNavigation > 0)
        {
            summary +=
                $" · {hiddenNavigation} navigation event(s) hidden";
        }

        return new ReliableHistoryProjection(
            transitions,
            activities,
            incidents,
            filtered.Length,
            visible,
            collapsed,
            hiddenNavigation,
            summary);
    }

    public static ReliableLogProjection BuildLogs(
        IReadOnlyList<OpsLogGroup> source,
        bool includeInformational,
        string severityFilter,
        string timeFilter,
        string sourceFilter,
        string textFilter,
        IReadOnlyList<string>? providerWarnings)
    {
        var active = source.Count(item =>
            SignalQualityPolicy.DisplaySeverity(item) >=
            OpsSeverity.Warning);
        var background = source.Count(item =>
            SignalQualityPolicy.DisplaySeverity(item) ==
            OpsSeverity.Info);

        var since = LogSince(timeFilter);
        var minimum = severityFilter.Equals(
                "Errors only",
                StringComparison.Ordinal)
            ? OpsSeverity.Error
            : includeInformational
                ? OpsSeverity.Info
                : OpsSeverity.Warning;

        var rows = source
            .Where(item =>
                SignalQualityPolicy.DisplaySeverity(item) >=
                minimum)
            .Where(item =>
                item.LastSeen >= since)
            .Where(item =>
                MatchesAny(
                    sourceFilter,
                    item.Source))
            .Where(item =>
                MatchesAny(
                    textFilter,
                    item.Message,
                    item.Source))
            .OrderByDescending(item =>
                SignalQualityPolicy.DisplaySeverity(item))
            .ThenByDescending(item => item.LastSeen)
            .Select(item =>
                new ReliableLogRow(item))
            .ToArray();

        var sources = rows
            .Select(item => item.Source)
            .Distinct(
                StringComparer.OrdinalIgnoreCase)
            .Count();

        var journalUnavailable =
            source.Count == 0 &&
            providerWarnings is not null &&
            providerWarnings.Any(warning =>
                warning.Contains(
                    "journal",
                    StringComparison.OrdinalIgnoreCase) ||
                warning.Contains(
                    "journalctl",
                    StringComparison.OrdinalIgnoreCase) ||
                warning.Contains(
                    "permission",
                    StringComparison.OrdinalIgnoreCase));

        string emptyTitle;
        string emptyDetail;

        if (rows.Length > 0)
        {
            emptyTitle = string.Empty;
            emptyDetail = string.Empty;
        }
        else if (journalUnavailable)
        {
            emptyTitle = "Journal evidence is unavailable";
            emptyDetail =
                "The active provider reported a journal or permission problem. " +
                "Review provider warnings or validate journalctl access.";
        }
        else if (source.Count == 0)
        {
            emptyTitle = "No journal observations were returned";
            emptyDetail =
                "The current capture contains no grouped warning, error or informational journal evidence.";
        }
        else if (active == 0 &&
                 background > 0 &&
                 !includeInformational)
        {
            emptyTitle = "Only informational observations are hidden";
            emptyDetail =
                $"{background} informational group(s) exist. Enable Include informational to display them.";
        }
        else
        {
            emptyTitle = "Current filters hide all journal groups";
            emptyDetail =
                "Change severity, time, source or message filters to restore matching evidence.";
        }

        var summary =
            $"{rows.Length} shown · {active} warning/error · " +
            $"{background} informational · {sources} source(s)";

        return new ReliableLogProjection(
            rows,
            active,
            background,
            sources,
            emptyTitle,
            emptyDetail,
            summary);
    }

    public static string HistoryKey(
        InsightHistoryRow row) =>
        string.Join(
            "|",
            row.Timestamp.ToUnixTimeMilliseconds()
                .ToString(CultureInfo.InvariantCulture),
            row.Stream,
            row.Target,
            row.Component,
            row.Transition);

    private static InsightHistoryRow ClassifyHistoryRow(
        InsightHistoryRow row)
    {
        if (IsHealthTransition(row))
            return Clone(row, "Health transition");

        var kind = row.Component.Trim();
        var title = row.Transition.Trim();
        var detail = row.Detail.Trim();
        var combined =
            $"{kind} {title} {detail}";

        string stream;

        if (row.Stream.Equals(
                "Notification",
                StringComparison.OrdinalIgnoreCase) ||
            kind.Equals(
                "Notification",
                StringComparison.OrdinalIgnoreCase))
        {
            stream = "Notification";
        }
        else if (kind.Equals(
                     "Navigation",
                     StringComparison.OrdinalIgnoreCase) ||
                 title.StartsWith(
                     "Opened ",
                     StringComparison.OrdinalIgnoreCase))
        {
            stream = "Navigation";
        }
        else if (row.Severity >= OpsSeverity.Warning ||
                 kind.Equals(
                     "Failure",
                     StringComparison.OrdinalIgnoreCase))
        {
            stream = "Incident";
        }
        else if (ContainsAny(
                     combined,
                     "policy",
                     "threshold",
                     "setting",
                     "saved",
                     "defaults restored",
                     "acknowledged",
                     "snoozed",
                     "ignored",
                     "muted"))
        {
            stream = "Policy change";
        }
        else if (ContainsAny(
                     combined,
                     "restart",
                     "started",
                     "stopped",
                     "refreshed",
                     "exported",
                     "changed",
                     "maintenance",
                     "capture",
                     "target"))
        {
            stream = "Operator action";
        }
        else
        {
            stream = "Operational";
        }

        return Clone(row, stream);
    }

    private static IReadOnlyList<InsightHistoryRow>
        CollapseIdentical(
            IEnumerable<InsightHistoryRow> source,
            ref int collapsedCount)
    {
        var ordered = source
            .OrderByDescending(item => item.Timestamp)
            .ToArray();
        var groups =
            new List<List<InsightHistoryRow>>();

        foreach (var item in ordered)
        {
            var window =
                HistoryCollapseWindow(item);
            var group =
                groups.FirstOrDefault(candidate =>
                    candidate.Count > 0 &&
                    SameHistorySignature(
                        candidate[0],
                        item) &&
                    candidate[^1].Timestamp -
                        item.Timestamp <=
                    window);

            if (group is null)
            {
                groups.Add(
                    new List<InsightHistoryRow>
                    {
                        item
                    });
            }
            else
            {
                group.Add(item);
            }
        }

        var result =
            new List<InsightHistoryRow>();

        foreach (var group in groups)
        {
            var first =
                group[0];

            if (group.Count == 1)
            {
                result.Add(first);
                continue;
            }

            collapsedCount += group.Count - 1;
            var oldest =
                group[^1].Timestamp.ToLocalTime();
            var newest =
                group[0].Timestamp.ToLocalTime();
            var detail =
                string.IsNullOrWhiteSpace(first.Detail)
                    ? string.Empty
                    : first.Detail.TrimEnd() +
                      Environment.NewLine +
                      Environment.NewLine;

            detail +=
                $"Repeated {group.Count} times between " +
                $"{oldest:g} and {newest:g}. " +
                "The underlying retained events remain unchanged.";

            result.Add(Clone(
                first,
                first.Stream,
                detail: detail));
        }

        return result
            .OrderByDescending(item => item.Timestamp)
            .ToArray();
    }

    private static TimeSpan HistoryCollapseWindow(
        InsightHistoryRow row)
    {
        var routine =
            row.Stream.Equals(
                "Operator action",
                StringComparison.OrdinalIgnoreCase) &&
            (
                row.Transition.Equals(
                    "Environment refreshed",
                    StringComparison.OrdinalIgnoreCase) ||
                row.Transition.Equals(
                    "GraveOps control plane started",
                    StringComparison.OrdinalIgnoreCase)
            );

        return routine
            ? TimeSpan.FromHours(1)
            : TimeSpan.FromMinutes(15);
    }

    private static bool SameHistorySignature(
        InsightHistoryRow left,
        InsightHistoryRow right) =>
        left.Stream.Equals(
            right.Stream,
            StringComparison.OrdinalIgnoreCase) &&
        left.Target.Equals(
            right.Target,
            StringComparison.OrdinalIgnoreCase) &&
        left.Component.Equals(
            right.Component,
            StringComparison.OrdinalIgnoreCase) &&
        left.Transition.Equals(
            right.Transition,
            StringComparison.OrdinalIgnoreCase) &&
        left.Detail.Equals(
            right.Detail,
            StringComparison.OrdinalIgnoreCase);

    private static bool MatchesHistoryClass(
        InsightHistoryRow row,
        string filter)
    {
        if (filter.Equals(
                "All events",
                StringComparison.Ordinal))
        {
            return true;
        }

        if (filter.Equals(
                "All meaningful",
                StringComparison.Ordinal))
        {
            return !row.Stream.Equals(
                "Navigation",
                StringComparison.Ordinal);
        }

        if (filter.Equals(
                "Incidents",
                StringComparison.Ordinal))
        {
            return row.Severity >= OpsSeverity.Warning;
        }

        if (filter.Equals(
                "Health transitions",
                StringComparison.Ordinal))
        {
            return IsHealthTransition(row);
        }

        if (filter.Equals(
                "Actions & changes",
                StringComparison.Ordinal))
        {
            return row.Stream is
                "Operator action" or
                "Policy change" or
                "Operational";
        }

        if (filter.Equals(
                "Notifications",
                StringComparison.Ordinal))
        {
            return row.Stream.Equals(
                "Notification",
                StringComparison.Ordinal);
        }

        if (filter.Equals(
                "Navigation",
                StringComparison.Ordinal))
        {
            return row.Stream.Equals(
                "Navigation",
                StringComparison.Ordinal);
        }

        return true;
    }

    private static bool MatchesSeverity(
        OpsSeverity severity,
        string filter)
    {
        if (filter.Equals(
                "Errors only",
                StringComparison.Ordinal))
        {
            return severity >= OpsSeverity.Error;
        }

        if (filter.Equals(
                "Warnings & errors",
                StringComparison.Ordinal))
        {
            return severity >= OpsSeverity.Warning;
        }

        return true;
    }

    private static DateTimeOffset HistorySince(
        string filter) =>
        filter switch
        {
            "Last 24 hours" =>
                DateTimeOffset.Now -
                TimeSpan.FromHours(24),
            "Last 7 days" =>
                DateTimeOffset.Now -
                TimeSpan.FromDays(7),
            _ => DateTimeOffset.MinValue
        };

    private static DateTimeOffset LogSince(
        string filter) =>
        filter switch
        {
            "Last hour" =>
                DateTimeOffset.Now -
                TimeSpan.FromHours(1),
            "Last 24 hours" =>
                DateTimeOffset.Now -
                TimeSpan.FromHours(24),
            _ => DateTimeOffset.MinValue
        };

    private static bool IsHealthTransition(
        InsightHistoryRow row) =>
        row.Stream.Equals(
            "Health transition",
            StringComparison.OrdinalIgnoreCase);

    private static bool MatchesAny(
        string filter,
        params string[] values)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        var value = filter.Trim();
        return values.Any(item =>
            item.Contains(
                value,
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsAny(
        string value,
        params string[] needles) =>
        needles.Any(needle =>
            value.Contains(
                needle,
                StringComparison.OrdinalIgnoreCase));

    private static InsightHistoryRow Clone(
        InsightHistoryRow row,
        string stream,
        string? detail = null) =>
        new()
        {
            Timestamp = row.Timestamp,
            Stream = stream,
            Target = row.Target,
            Component = row.Component,
            Transition = row.Transition,
            Detail = detail ?? row.Detail,
            Severity = row.Severity,
            NavigationName = row.NavigationName
        };
}
