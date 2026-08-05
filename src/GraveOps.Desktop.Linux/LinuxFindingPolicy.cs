using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GraveOps.Core.Hosts;

namespace GraveOps.Desktop.Linux;

public sealed record OpsPolicyFinding(
    OpsFinding Finding,
    string Key,
    string Resource,
    bool CanAcknowledge,
    bool CanIgnore)
{
    public OpsSeverity Severity => Finding.Severity;
    public string Component => Finding.Component;
    public string Problem => Finding.Problem;
    public string Evidence => Finding.Evidence;
    public string Impact => Finding.Impact;
    public string NextStep => Finding.NextStep;
    public int Rank => Finding.Rank;
}

public sealed record OpsMutedFinding(
    string Key,
    OpsSeverity Severity,
    string Component,
    string Resource,
    string Problem,
    string CurrentState,
    string Reason,
    string UntilText);

public sealed record OpsPolicyEvaluation(
    OpsAnalysis Analysis,
    IReadOnlyList<OpsPolicyFinding> Active,
    IReadOnlyList<OpsMutedFinding> Muted);

public sealed class StorageThresholdPolicy
{
    public int WarningPercent { get; set; } = 85;
    public int ErrorPercent { get; set; } = 90;
    public int CriticalPercent { get; set; } = 95;
    public double WarningFreeGiB { get; set; }
    public double ErrorFreeGiB { get; set; }
    public double CriticalFreeGiB { get; set; }

    public StorageThresholdPolicy Clone() => new()
    {
        WarningPercent = WarningPercent,
        ErrorPercent = ErrorPercent,
        CriticalPercent = CriticalPercent,
        WarningFreeGiB = WarningFreeGiB,
        ErrorFreeGiB = ErrorFreeGiB,
        CriticalFreeGiB = CriticalFreeGiB
    };

    public static StorageThresholdPolicy Defaults() => new();

    public static StorageThresholdPolicy LargeMediaPreset() => new()
    {
        WarningPercent = 95,
        ErrorPercent = 97,
        CriticalPercent = 99,
        WarningFreeGiB = 1536,
        ErrorFreeGiB = 750,
        CriticalFreeGiB = 250
    };
}


public enum StorageCapacityAlertMode
{
    Normal,
    DashboardOnly,
    Muted,
    Disabled
}

public sealed class StorageCapacityAlertPolicy
{
    public bool MonitoringEnabled { get; set; } = true;
    public StorageCapacityAlertMode Mode { get; set; } =
        StorageCapacityAlertMode.Normal;
    public DateTimeOffset? MutedUntil { get; set; }
    public bool IgnoreMount { get; set; }

    public StorageCapacityAlertPolicy Clone() => new()
    {
        MonitoringEnabled = MonitoringEnabled,
        Mode = Mode,
        MutedUntil = MutedUntil,
        IgnoreMount = IgnoreMount
    };

    public static StorageCapacityAlertPolicy Defaults() => new();
}

public sealed record StorageCapacityEvaluation(
    OpsSeverity Severity,
    OpsSeverity ThresholdSeverity,
    string StatusLabel,
    string PolicyLabel,
    StorageCapacityAlertMode Mode,
    bool MonitoringEnabled,
    bool IsMuted,
    bool RaisesFinding,
    bool IsIgnored);

public sealed class LinuxFindingPolicyStore
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private FindingPolicyDocument _document;

    public LinuxFindingPolicyStore()
    {
        var root = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config");
        }

        _filePath = Path.Combine(root, "GraveOps", "finding-policies.json");
        _document = Load();
    }

    public string PolicyPath => _filePath;

    public OpsPolicyEvaluation Evaluate(
        HostSnapshot snapshot,
        OpsAnalysis rawAnalysis)
    {
        var changed = RemoveExpiredRules();
        changed |= RemoveExpiredStorageCapacityMutes();
        var findings = BuildPolicyAwareFindings(snapshot, rawAnalysis.Findings)
            .OrderByDescending(item => item.Severity)
            .ThenBy(item => item.Rank)
            .ThenBy(item => item.Component)
            .ToArray();

        var active = new List<OpsPolicyFinding>();
        var muted = new List<OpsMutedFinding>();

        foreach (var finding in findings)
        {
            var row = CreateRow(finding);
            var rule = _document.Rules.FirstOrDefault(item =>
                item.Key.Equals(row.Key, StringComparison.OrdinalIgnoreCase));

            if (rule is null || finding.Severity >= OpsSeverity.Critical)
            {
                active.Add(row);
                continue;
            }

            if (rule.Mode.Equals("acknowledged", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    rule.Signature,
                    ObservationSignature(row),
                    StringComparison.Ordinal))
            {
                _document.Rules.Remove(rule);
                changed = true;
                active.Add(row);
                continue;
            }

            if (!RuleApplies(rule))
            {
                _document.Rules.Remove(rule);
                changed = true;
                active.Add(row);
                continue;
            }

            muted.Add(new OpsMutedFinding(
                row.Key,
                row.Severity,
                row.Component,
                row.Resource,
                row.Problem,
                row.Evidence,
                RuleReason(rule),
                RuleUntilText(rule)));
        }

        if (changed)
            Save();

        return new OpsPolicyEvaluation(
            RebuildAnalysis(active.Select(item => item.Finding).ToArray()),
            active,
            muted
                .OrderByDescending(item => item.Severity)
                .ThenBy(item => item.Component)
                .ThenBy(item => item.Resource)
                .ToArray());
    }

    public IReadOnlyList<OpsLifecycleStage> ApplyLifecycle(
        HostSnapshot snapshot,
        IReadOnlyList<OpsLifecycleStage> rawLifecycle,
        OpsPolicyEvaluation evaluation)
    {
        var activeStorage = evaluation.Active
            .Where(item => IsStorageCapacityKey(item.Key))
            .ToArray();
        var mutedStorage = evaluation.Muted
            .Where(item => IsStorageCapacityKey(item.Key))
            .ToArray();

        var storage = LinuxOpsAnalyzer.OperationalStorage(snapshot)
            .OrderByDescending(item =>
                LinuxOpsAnalyzer.UsePercent(item.PercentUsed))
            .Select(volume =>
            {
                var capacity = EvaluateStorageCapacity(volume);
                var ruleMuted = mutedStorage.Any(item =>
                    item.Resource.Equals(
                        volume.MountPoint,
                        StringComparison.OrdinalIgnoreCase));
                return new
                {
                    Volume = volume,
                    Capacity = capacity,
                    Severity = ruleMuted
                        ? OpsSeverity.Info
                        : capacity.Severity,
                    Muted = ruleMuted || capacity.IsMuted
                };
            })
            .ToArray();

        OpsLifecycleStage storageStage;
        if (storage.Length == 0)
        {
            storageStage = new OpsLifecycleStage(
                2,
                "Storage",
                "UNKNOWN",
                OpsSeverity.Warning,
                "No operational filesystems were returned.",
                "Downloads, imports, libraries and backups require visible storage.",
                "Open Storage and verify mounts before continuing.");
        }
        else
        {
            var severity = storage
                .Select(item => item.Severity)
                .Append(
                    activeStorage.Length == 0
                        ? OpsSeverity.Healthy
                        : activeStorage.Max(item => item.Severity))
                .Max();
            var allIgnored = storage.All(item =>
                item.Capacity.IsIgnored);
            var allUnmonitored = storage.All(item =>
                !item.Capacity.MonitoringEnabled);
            var mutedCount = storage.Count(item => item.Muted);
            var dashboardOnlyCount = storage.Count(item =>
                item.Capacity.Mode ==
                StorageCapacityAlertMode.DashboardOnly);

            var state = allIgnored
                ? "IGNORED"
                : allUnmonitored
                    ? "UNMONITORED"
                    : mutedCount > 0 && severity < OpsSeverity.Warning
                        ? "MUTED"
                        : severity >= OpsSeverity.Error
                            ? "BLOCKED"
                            : severity == OpsSeverity.Warning
                                ? "ATTENTION"
                                : "READY";

            var fullest = storage[0];
            var evidence =
                $"{fullest.Volume.MountPoint} is the fullest mount at " +
                $"{fullest.Volume.PercentUsed} " +
                $"({fullest.Volume.Available} free).";

            if (allUnmonitored)
                evidence += " Capacity monitoring is disabled by operator policy.";
            else if (mutedCount > 0)
                evidence += $" {mutedCount} capacity alert(s) are muted.";

            if (dashboardOnlyCount > 0)
            {
                evidence +=
                    $" {dashboardOnlyCount} mount(s) report capacity on the Dashboard only.";
            }

            storageStage = new OpsLifecycleStage(
                2,
                "Storage",
                state,
                severity,
                evidence,
                "Every downstream stage reads from or writes to storage.",
                allUnmonitored || allIgnored
                    ? "Capacity remains visible, while mount, filesystem, permission and I/O failures remain protected."
                    : severity >= OpsSeverity.Warning
                        ? "Review the active capacity state before queue growth creates an outage."
                        : mutedCount > 0
                            ? "Capacity remains visible without contributing an active warning."
                            : "No active storage-capacity policy is triggered.");
        }

        return rawLifecycle
            .Select(item => item.Stage.Equals(
                    "Storage",
                    StringComparison.OrdinalIgnoreCase)
                ? storageStage
                : item)
            .ToArray();
    }

    public OpsPolicyFinding CreateRow(OpsFinding finding)
    {
        var key = FindingKey(finding);
        return new OpsPolicyFinding(
            finding,
            key,
            FindingResource(finding),
            CanTemporarilyMute(finding),
            CanIgnoreFinding(finding));
    }

    public void Acknowledge(OpsPolicyFinding finding)
    {
        if (!finding.CanAcknowledge)
            throw new InvalidOperationException(
                "This finding is protected and cannot be acknowledged.");

        UpsertRule(new FindingPolicyRule
        {
            Key = finding.Key,
            Mode = "acknowledged",
            Signature = ObservationSignature(finding),
            CreatedAt = DateTimeOffset.Now,
            Reason = "Acknowledged until the observed state meaningfully changes"
        });
    }

    public void Snooze(OpsPolicyFinding finding, TimeSpan duration)
    {
        if (!finding.CanAcknowledge)
            throw new InvalidOperationException(
                "This finding is protected and cannot be snoozed.");
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration));

        UpsertRule(new FindingPolicyRule
        {
            Key = finding.Key,
            Mode = "snoozed",
            Until = DateTimeOffset.Now.Add(duration),
            CreatedAt = DateTimeOffset.Now,
            Reason = $"Snoozed for {FormatDuration(duration)}"
        });
    }

    public void Ignore(OpsPolicyFinding finding)
    {
        if (!finding.CanIgnore)
        {
            throw new InvalidOperationException(
                "Permanent ignore is unavailable for this safety-relevant finding.");
        }

        UpsertRule(new FindingPolicyRule
        {
            Key = finding.Key,
            Mode = "ignored",
            CreatedAt = DateTimeOffset.Now,
            Reason = "Ignored for this exact finding and resource"
        });
    }

    public bool Restore(string key)
    {
        var removed = _document.Rules.RemoveAll(item =>
            item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
            return false;

        Save();
        return true;
    }

    public StorageCapacityAlertPolicy
        GetGlobalStorageCapacityAlertPolicy() =>
        NormalizeStorageCapacityAlertPolicy(
            _document.GlobalStorageCapacityPolicy);

    public void SetGlobalStorageCapacityAlertPolicy(
        StorageCapacityAlertPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _document.GlobalStorageCapacityPolicy =
            NormalizeStorageCapacityAlertPolicy(policy);
        Save();
    }

    public void SetStorageCapacityAlertPolicies(
        StorageCapacityAlertPolicy globalPolicy,
        string? mountPoint,
        StorageCapacityAlertPolicy? mountPolicy,
        bool useGlobalForMount)
    {
        ArgumentNullException.ThrowIfNull(globalPolicy);

        var previousGlobal =
            _document.GlobalStorageCapacityPolicy.Clone();
        var previousOverrides =
            _document.StorageCapacityOverrides.ToDictionary(
                item => item.Key,
                item => item.Value.Clone(),
                StringComparer.OrdinalIgnoreCase);

        try
        {
            _document.GlobalStorageCapacityPolicy =
                NormalizeStorageCapacityAlertPolicy(globalPolicy);

            if (!string.IsNullOrWhiteSpace(mountPoint))
            {
                if (useGlobalForMount)
                {
                    _document.StorageCapacityOverrides.Remove(mountPoint);
                }
                else
                {
                    ArgumentNullException.ThrowIfNull(mountPolicy);
                    _document.StorageCapacityOverrides[mountPoint] =
                        NormalizeStorageCapacityAlertPolicy(mountPolicy);
                }
            }

            Save();
        }
        catch
        {
            _document.GlobalStorageCapacityPolicy = previousGlobal;
            _document.StorageCapacityOverrides = previousOverrides;
            throw;
        }
    }

    public StorageCapacityAlertPolicy
        GetStorageCapacityAlertPolicy(string mountPoint)
    {
        if (!string.IsNullOrWhiteSpace(mountPoint) &&
            _document.StorageCapacityOverrides.TryGetValue(
                mountPoint,
                out var policy))
        {
            return NormalizeStorageCapacityAlertPolicy(policy);
        }

        return GetGlobalStorageCapacityAlertPolicy();
    }

    public bool HasStorageCapacityAlertOverride(string mountPoint) =>
        !string.IsNullOrWhiteSpace(mountPoint) &&
        _document.StorageCapacityOverrides.ContainsKey(mountPoint);

    public int StorageCapacityAlertOverrideCount =>
        _document.StorageCapacityOverrides.Count;

    public void SetStorageCapacityAlertOverride(
        string mountPoint,
        StorageCapacityAlertPolicy policy)
    {
        if (string.IsNullOrWhiteSpace(mountPoint))
        {
            throw new ArgumentException(
                "A mount point is required.",
                nameof(mountPoint));
        }

        ArgumentNullException.ThrowIfNull(policy);
        _document.StorageCapacityOverrides[mountPoint] =
            NormalizeStorageCapacityAlertPolicy(policy);
        Save();
    }

    public bool ResetStorageCapacityAlertOverride(string mountPoint)
    {
        if (string.IsNullOrWhiteSpace(mountPoint) ||
            !_document.StorageCapacityOverrides.Remove(mountPoint))
        {
            return false;
        }

        Save();
        return true;
    }

    public StorageCapacityEvaluation EvaluateStorageCapacity(
        StorageVolumeSnapshot volume) =>
        EvaluateStorageCapacity(
            volume.MountPoint,
            LinuxOpsAnalyzer.UsePercent(volume.PercentUsed),
            ParseSizeToGiB(volume.Available));

    public StorageCapacityEvaluation EvaluateStorageCapacity(
        string mountPoint,
        int percentUsed,
        double availableGiB)
    {
        var threshold = GetStorageThreshold(mountPoint);
        var thresholdSeverity = EvaluateStorageThresholdSeverity(
            threshold,
            percentUsed,
            availableGiB);
        var policy = GetStorageCapacityAlertPolicy(mountPoint);
        var hasOverride = HasStorageCapacityAlertOverride(mountPoint);
        var ignored = policy.IgnoreMount;
        var monitoringEnabled =
            policy.MonitoringEnabled &&
            policy.Mode != StorageCapacityAlertMode.Disabled &&
            !ignored;
        var thresholdTriggered =
            thresholdSeverity >= OpsSeverity.Warning;
        var muteWindowActive =
            policy.Mode == StorageCapacityAlertMode.Muted &&
            (policy.MutedUntil is null ||
             policy.MutedUntil > DateTimeOffset.Now);

        // Temporary muting does not suppress a critical capacity event.
        // Explicit Disabled/Ignore modes are the operator's deliberate opt-out.
        var isMuted =
            monitoringEnabled &&
            thresholdTriggered &&
            muteWindowActive &&
            thresholdSeverity < OpsSeverity.Critical;

        var raisesFinding =
            monitoringEnabled &&
            thresholdTriggered &&
            policy.Mode != StorageCapacityAlertMode.DashboardOnly &&
            !isMuted;

        var severity =
            !monitoringEnabled || isMuted
                ? OpsSeverity.Info
                : thresholdSeverity;

        var status = ignored
            ? "IGNORED"
            : !monitoringEnabled
                ? "UNMONITORED"
                : isMuted
                    ? "MUTED"
                    : policy.Mode == StorageCapacityAlertMode.DashboardOnly
                        ? thresholdTriggered
                            ? $"{LinuxOpsAnalyzer.SeverityLabel(thresholdSeverity)} · DASHBOARD"
                            : "DASHBOARD ONLY"
                        : policy.Mode == StorageCapacityAlertMode.Muted &&
                          !thresholdTriggered
                            ? "MUTE ARMED"
                            : LinuxOpsAnalyzer.SeverityLabel(thresholdSeverity);

        var scope = hasOverride
            ? "mount override"
            : "global policy";
        var mode = StorageCapacityAlertModeLabel(policy.Mode);
        var thresholdScope = HasCustomStorageThreshold(mountPoint)
            ? "custom thresholds"
            : "default thresholds";
        var policyLabel = $"{scope} · {mode} · {thresholdScope}";

        if (policy.Mode == StorageCapacityAlertMode.Muted &&
            policy.MutedUntil is not null &&
            policy.MutedUntil > DateTimeOffset.Now)
        {
            policyLabel +=
                $" · until {policy.MutedUntil.Value.ToLocalTime():g}";
        }

        return new StorageCapacityEvaluation(
            severity,
            thresholdSeverity,
            status,
            policyLabel,
            policy.Mode,
            monitoringEnabled,
            isMuted,
            raisesFinding,
            ignored);
    }

    public static string StorageCapacityAlertModeLabel(
        StorageCapacityAlertMode mode) =>
        mode switch
        {
            StorageCapacityAlertMode.DashboardOnly => "Dashboard only",
            StorageCapacityAlertMode.Muted => "Muted",
            StorageCapacityAlertMode.Disabled => "Disabled",
            _ => "Normal"
        };

    public StorageThresholdPolicy GetStorageThreshold(string mountPoint)
    {
        return _document.StorageThresholds.TryGetValue(
            mountPoint,
            out var policy)
            ? policy.Clone()
            : StorageThresholdPolicy.Defaults();
    }

    public bool HasCustomStorageThreshold(string mountPoint) =>
        _document.StorageThresholds.ContainsKey(mountPoint);

    public void SetStorageThreshold(
        string mountPoint,
        StorageThresholdPolicy policy)
    {
        ValidateStorageThreshold(policy);
        _document.StorageThresholds[mountPoint] = policy.Clone();
        Save();
    }

    public bool ResetStorageThreshold(string mountPoint)
    {
        if (!_document.StorageThresholds.Remove(mountPoint))
            return false;

        Save();
        return true;
    }

    public OpsSeverity EvaluateStorageSeverity(StorageVolumeSnapshot volume) =>
        EvaluateStorageCapacity(volume).Severity;

    public static bool IsStorageCapacityKey(string key) =>
        key.StartsWith(
            "storage.capacity:",
            StringComparison.OrdinalIgnoreCase);

    public static string MountPointFromStorageKey(string key) =>
        IsStorageCapacityKey(key)
            ? key["storage.capacity:".Length..]
            : string.Empty;

    private IReadOnlyList<OpsFinding> BuildPolicyAwareFindings(
        HostSnapshot snapshot,
        IReadOnlyList<OpsFinding> rawFindings)
    {
        // Only capacity observations are rebuilt here. Mount loss, read-only
        // filesystems, permissions and I/O failures remain untouched and can
        // never be hidden by a capacity-alert preference.
        var findings = rawFindings
            .Where(item => !IsStorageCapacityFinding(item))
            .ToList();

        foreach (var volume in LinuxOpsAnalyzer.OperationalStorage(snapshot))
        {
            var capacity = EvaluateStorageCapacity(volume);
            if (!capacity.RaisesFinding)
                continue;

            var severity = capacity.ThresholdSeverity;
            var percent = LinuxOpsAnalyzer.UsePercent(volume.PercentUsed);
            var threshold = GetStorageThreshold(volume.MountPoint);
            var thresholdSummary =
                $"{capacity.PolicyLabel} · " +
                $"{threshold.WarningPercent}/{threshold.ErrorPercent}/{threshold.CriticalPercent}%";

            findings.Add(new OpsFinding(
                severity,
                "Storage",
                $"{volume.MountPoint} is {percent}% full.",
                $"{volume.Source} · {volume.Used} used · {volume.Available} free · " +
                $"{volume.FileSystem} · {thresholdSummary}",
                "Low free space can block downloads, imports, databases, transcodes and backups.",
                severity >= OpsSeverity.Critical
                    ? "Free space or move data before continuing write-heavy operations."
                    : "Inspect growth or change the exact mount's capacity-alert policy when this usage is intentional.",
                1));
        }

        return findings;
    }

    private static OpsAnalysis RebuildAnalysis(
        IReadOnlyList<OpsFinding> activeFindings)
    {
        var ordered = activeFindings
            .OrderByDescending(item => item.Severity)
            .ThenBy(item => item.Rank)
            .ThenBy(item => item.Component)
            .ToArray();

        if (ordered.Length == 0)
        {
            return new OpsAnalysis(
                OpsSeverity.Healthy,
                "HEALTHY",
                "No active fault detected",
                "No active operational finding requires attention.",
                ordered);
        }

        var top = ordered[0];
        var severity = ordered.Max(item => item.Severity);
        return new OpsAnalysis(
            severity,
            LinuxOpsAnalyzer.SeverityLabel(severity),
            top.Component,
            $"Highest-priority active finding: {top.Component} — {top.Problem}",
            ordered);
    }

    private static string FindingKey(OpsFinding finding)
    {
        if (IsStorageCapacityFinding(finding))
        {
            return "storage.capacity:" +
                   FindingResource(finding);
        }

        if (finding.Component.Equals(
                "Backups",
                StringComparison.OrdinalIgnoreCase))
        {
            return "backups.readiness";
        }

        if (finding.Component.Equals(
                "systemd",
                StringComparison.OrdinalIgnoreCase) &&
            finding.Problem.StartsWith(
                "Failed unit:",
                StringComparison.OrdinalIgnoreCase))
        {
            return "systemd.failed:" +
                   finding.Problem["Failed unit:".Length..].Trim();
        }

        var normalized = Regex.Replace(
            finding.Problem.ToLowerInvariant(),
            @"\b\d+\s+occurrences?\b",
            "occurrences");
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

        return $"finding:{finding.Component.ToLowerInvariant()}:{ShortHash(normalized)}";
    }

    private static string FindingResource(OpsFinding finding)
    {
        if (IsStorageCapacityFinding(finding))
        {
            var marker = finding.Problem.IndexOf(
                " is ",
                StringComparison.OrdinalIgnoreCase);
            if (marker > 0)
                return finding.Problem[..marker].Trim();
        }

        return finding.Component;
    }

    private static bool IsStorageCapacityFinding(OpsFinding finding) =>
        finding.Component.Equals(
            "Storage",
            StringComparison.OrdinalIgnoreCase) &&
        finding.Problem.Contains("% full", StringComparison.OrdinalIgnoreCase);

    private static bool CanTemporarilyMute(OpsFinding finding)
    {
        if (finding.Severity >= OpsSeverity.Critical)
            return false;

        var problem = finding.Problem.ToLowerInvariant();
        if (problem.Contains("read-only") ||
            problem.Contains("input/output") ||
            problem.Contains("i/o error") ||
            problem.Contains("mount missing"))
        {
            return false;
        }

        return true;
    }

    private static bool CanIgnoreFinding(OpsFinding finding)
    {
        if (!CanTemporarilyMute(finding))
            return false;

        if (IsStorageCapacityFinding(finding))
            return true;

        if (finding.Component.Equals(
                "Provider",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return finding.Rank >= 8 &&
               !finding.Component.Equals(
                   "Backups",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string ObservationSignature(OpsPolicyFinding finding)
    {
        if (IsStorageCapacityKey(finding.Key))
            return $"{finding.Key}|{finding.Severity}";

        var normalized = Regex.Replace(
            finding.Problem.ToLowerInvariant(),
            @"\b\d+\s+occurrences?\b",
            "occurrences");
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
        return $"{finding.Key}|{finding.Severity}|{normalized}";
    }

    private static string RuleReason(FindingPolicyRule rule) =>
        string.IsNullOrWhiteSpace(rule.Reason)
            ? rule.Mode
            : rule.Reason;

    private static string RuleUntilText(FindingPolicyRule rule)
    {
        if (rule.Mode.Equals(
                "ignored",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Permanent";
        }

        if (rule.Mode.Equals(
                "acknowledged",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Until observed state changes";
        }

        return rule.Until is null
            ? "--"
            : rule.Until.Value.ToLocalTime().ToString("g");
    }

    private static bool RuleApplies(FindingPolicyRule rule)
    {
        if (rule.Mode.Equals(
                "ignored",
                StringComparison.OrdinalIgnoreCase) ||
            rule.Mode.Equals(
                "acknowledged",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return rule.Mode.Equals(
                   "snoozed",
                   StringComparison.OrdinalIgnoreCase) &&
               rule.Until is not null &&
               rule.Until > DateTimeOffset.Now;
    }

    private bool RemoveExpiredRules()
    {
        var removed = _document.Rules.RemoveAll(item =>
            item.Mode.Equals(
                "snoozed",
                StringComparison.OrdinalIgnoreCase) &&
            (item.Until is null || item.Until <= DateTimeOffset.Now));
        return removed > 0;
    }

    private void UpsertRule(FindingPolicyRule rule)
    {
        _document.Rules.RemoveAll(item =>
            item.Key.Equals(rule.Key, StringComparison.OrdinalIgnoreCase));
        _document.Rules.Add(rule);
        Save();
    }

    private static void ValidateStorageThreshold(StorageThresholdPolicy policy)
    {
        if (policy.WarningPercent is < 1 or > 100 ||
            policy.ErrorPercent is < 1 or > 100 ||
            policy.CriticalPercent is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(policy),
                "Percentage thresholds must be between 1 and 100.");
        }

        if (!(policy.WarningPercent < policy.ErrorPercent &&
              policy.ErrorPercent < policy.CriticalPercent))
        {
            throw new ArgumentException(
                "Percentage thresholds must increase from warning to error to critical.",
                nameof(policy));
        }

        if (policy.WarningFreeGiB < 0 ||
            policy.ErrorFreeGiB < 0 ||
            policy.CriticalFreeGiB < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(policy),
                "Free-space thresholds cannot be negative.");
        }
    }

    private static OpsSeverity EvaluateStorageThresholdSeverity(
        StorageThresholdPolicy threshold,
        int percentUsed,
        double availableGiB)
    {
        if (percentUsed >= threshold.CriticalPercent ||
            FreeThresholdTriggered(
                availableGiB,
                threshold.CriticalFreeGiB))
        {
            return OpsSeverity.Critical;
        }

        if (percentUsed >= threshold.ErrorPercent ||
            FreeThresholdTriggered(
                availableGiB,
                threshold.ErrorFreeGiB))
        {
            return OpsSeverity.Error;
        }

        if (percentUsed >= threshold.WarningPercent ||
            FreeThresholdTriggered(
                availableGiB,
                threshold.WarningFreeGiB))
        {
            return OpsSeverity.Warning;
        }

        return OpsSeverity.Healthy;
    }

    private static StorageCapacityAlertPolicy
        NormalizeStorageCapacityAlertPolicy(
            StorageCapacityAlertPolicy? policy)
    {
        var normalized =
            policy?.Clone() ??
            StorageCapacityAlertPolicy.Defaults();

        if (normalized.Mode != StorageCapacityAlertMode.Muted)
            normalized.MutedUntil = null;

        if (normalized.Mode == StorageCapacityAlertMode.Disabled)
            normalized.MonitoringEnabled = false;

        if (normalized.Mode == StorageCapacityAlertMode.Muted &&
            normalized.MutedUntil is not null &&
            normalized.MutedUntil <= DateTimeOffset.Now)
        {
            normalized.Mode = StorageCapacityAlertMode.Normal;
            normalized.MutedUntil = null;
        }

        return normalized;
    }

    private bool RemoveExpiredStorageCapacityMutes()
    {
        var changed = false;

        if (_document.GlobalStorageCapacityPolicy.Mode ==
                StorageCapacityAlertMode.Muted &&
            _document.GlobalStorageCapacityPolicy.MutedUntil is not null &&
            _document.GlobalStorageCapacityPolicy.MutedUntil <=
                DateTimeOffset.Now)
        {
            _document.GlobalStorageCapacityPolicy.Mode =
                StorageCapacityAlertMode.Normal;
            _document.GlobalStorageCapacityPolicy.MutedUntil = null;
            changed = true;
        }

        foreach (var policy in
                 _document.StorageCapacityOverrides.Values)
        {
            if (policy.Mode != StorageCapacityAlertMode.Muted ||
                policy.MutedUntil is null ||
                policy.MutedUntil > DateTimeOffset.Now)
            {
                continue;
            }

            policy.Mode = StorageCapacityAlertMode.Normal;
            policy.MutedUntil = null;
            changed = true;
        }

        return changed;
    }

    private static bool FreeThresholdTriggered(
        double availableGiB,
        double thresholdGiB) =>
        thresholdGiB > 0 &&
        availableGiB >= 0 &&
        availableGiB <= thresholdGiB;

    private static double ParseSizeToGiB(string value)
    {
        var match = Regex.Match(
            value.Trim(),
            @"^(?<number>[0-9]+(?:\.[0-9]+)?)\s*(?<unit>[KMGTPE]?)",
            RegexOptions.IgnoreCase);

        if (!match.Success ||
            !double.TryParse(
                match.Groups["number"].Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var number))
        {
            return -1;
        }

        return match.Groups["unit"].Value.ToUpperInvariant() switch
        {
            "K" => number / (1024d * 1024d),
            "M" => number / 1024d,
            "G" => number,
            "T" => number * 1024d,
            "P" => number * 1024d * 1024d,
            "E" => number * 1024d * 1024d * 1024d,
            _ => number / (1024d * 1024d * 1024d)
        };
    }

    private static string ShortHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..12].ToLowerInvariant();
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1 &&
            Math.Abs(duration.TotalDays - Math.Round(duration.TotalDays)) < 0.001)
        {
            return $"{Math.Round(duration.TotalDays):0} day(s)";
        }

        if (duration.TotalHours >= 1)
            return $"{duration.TotalHours:0.#} hour(s)";

        return $"{duration.TotalMinutes:0} minute(s)";
    }

    private FindingPolicyDocument Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new FindingPolicyDocument();

            var document = JsonSerializer.Deserialize<FindingPolicyDocument>(
                               File.ReadAllText(_filePath),
                               _json) ??
                           new FindingPolicyDocument();
            document.Rules ??= new List<FindingPolicyRule>();
            document.StorageThresholds = new Dictionary<string, StorageThresholdPolicy>(
                document.StorageThresholds ??
                new Dictionary<string, StorageThresholdPolicy>(),
                StringComparer.OrdinalIgnoreCase);
            document.GlobalStorageCapacityPolicy =
                NormalizeStorageCapacityAlertPolicy(
                    document.GlobalStorageCapacityPolicy);
            document.StorageCapacityOverrides =
                new Dictionary<string, StorageCapacityAlertPolicy>(
                    document.StorageCapacityOverrides ??
                    new Dictionary<string, StorageCapacityAlertPolicy>(),
                    StringComparer.OrdinalIgnoreCase);
            return document;
        }
        catch
        {
            return new FindingPolicyDocument();
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var temp = _filePath + ".tmp";
        File.WriteAllText(
            temp,
            JsonSerializer.Serialize(_document, _json),
            new UTF8Encoding(false));
        File.Move(temp, _filePath, true);
    }

    public sealed class FindingPolicyDocument
    {
        public List<FindingPolicyRule> Rules { get; set; } = new();
        public Dictionary<string, StorageThresholdPolicy> StorageThresholds { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
        public StorageCapacityAlertPolicy GlobalStorageCapacityPolicy { get; set; } =
            StorageCapacityAlertPolicy.Defaults();
        public Dictionary<string, StorageCapacityAlertPolicy> StorageCapacityOverrides { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class FindingPolicyRule
    {
        public string Key { get; set; } = string.Empty;
        public string Mode { get; set; } = string.Empty;
        public DateTimeOffset? Until { get; set; }
        public string Signature { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
        public string Reason { get; set; } = string.Empty;
    }
}
