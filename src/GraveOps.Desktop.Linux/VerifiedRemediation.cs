using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GraveOps.Desktop.Linux;

public enum VerifiedRemediationRisk
{
    Inspect,
    Retry,
    Recover,
    Change,
    Destructive
}

public enum VerifiedRemediationTargetKind
{
    Probe,
    SystemdService,
    DockerContainer,
    Mount,
    PiHole,
    BackupTimer
}

public enum VerifiedRemediationJobState
{
    Queued,
    Running,
    Verifying,
    Succeeded,
    Failed,
    Blocked
}

public sealed class VerifiedRemediationSettings
{
    public bool SafeMode { get; set; } = true;
    public bool RequireTypedConfirmation { get; set; } = true;
    public bool VerifyAfterAction { get; set; } = true;
    public bool BlockOnStorageFault { get; set; } = true;

    public VerifiedRemediationSettings Clone() => new()
    {
        SafeMode = SafeMode,
        RequireTypedConfirmation = RequireTypedConfirmation,
        VerifyAfterAction = VerifyAfterAction,
        BlockOnStorageFault = BlockOnStorageFault
    };
}

public sealed record VerifiedRemediationProduct(
    string Name,
    string Family,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> DefaultUnits,
    IReadOnlyList<string> DefaultContainers,
    string HealthPath = "/",
    bool AllowsExitedPrimary = false);

public sealed record VerifiedRemediationPlan(
    string Id,
    string CardKey,
    string Product,
    string Family,
    VerifiedRemediationTargetKind TargetKind,
    string Target,
    string Problem,
    string LikelyCause,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> InspectionCommands,
    string RecoveryAction,
    string RecoveryCommand,
    string ExpectedResult,
    string VerificationCommand,
    string Rollback,
    string NavigationName,
    string Endpoint,
    bool AllowsExitedPrimary,
    bool StorageSensitive,
    VerifiedRemediationRisk Risk)
{
    public bool CanMutate =>
        Risk >= VerifiedRemediationRisk.Recover &&
        !string.IsNullOrWhiteSpace(RecoveryCommand) &&
        TargetKind != VerifiedRemediationTargetKind.Probe;

    public string ConfirmationText =>
        $"RECOVER {Target}";
}

public sealed class VerifiedRemediationJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string PlanId { get; set; } = string.Empty;
    public string HostId { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public VerifiedRemediationJobState State { get; set; } =
        VerifiedRemediationJobState.Queued;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string Command { get; set; } = string.Empty;
    public string Output { get; set; } = string.Empty;
    public string Verification { get; set; } = string.Empty;
    public bool Verified { get; set; }
}

public sealed record VerifiedRemediationExecutionResult(
    bool Success,
    string Summary,
    string Output);

public sealed class VerifiedRemediationStore
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
    private VerifiedRemediationDocument _document;

    public VerifiedRemediationStore(string? configRoot = null)
    {
        var root = configRoot;
        if (string.IsNullOrWhiteSpace(root))
            root = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config");
        }

        _filePath = Path.Combine(root, "GraveOps", "verified-remediation.json");
        _document = Load();
        NormalizeInterruptedJobs();
    }

    public string FilePath => _filePath;

    public VerifiedRemediationSettings GetSettings() =>
        (_document.Settings ?? new VerifiedRemediationSettings()).Clone();

    public void SetSettings(VerifiedRemediationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _document.Settings = settings.Clone();
        Save();
    }

    public IReadOnlyList<VerifiedRemediationJob> RecentJobs(string hostId) =>
        _document.Jobs
            .Where(item => item.HostId.Equals(
                Normalize(hostId),
                StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.StartedAt)
            .Take(50)
            .Select(Clone)
            .ToArray();

    public bool TryStart(
        VerifiedRemediationPlan plan,
        string hostId,
        out VerifiedRemediationJob job)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var host = Normalize(hostId);
        var duplicate = _document.Jobs.FirstOrDefault(item =>
            item.HostId.Equals(host, StringComparison.OrdinalIgnoreCase) &&
            item.PlanId.Equals(plan.Id, StringComparison.OrdinalIgnoreCase) &&
            item.State is VerifiedRemediationJobState.Queued or
                VerifiedRemediationJobState.Running or
                VerifiedRemediationJobState.Verifying);
        if (duplicate is not null)
        {
            job = Clone(duplicate);
            return false;
        }

        var created = new VerifiedRemediationJob
        {
            PlanId = plan.Id,
            HostId = host,
            Product = plan.Product,
            Target = plan.Target,
            State = VerifiedRemediationJobState.Queued,
            StartedAt = DateTimeOffset.UtcNow,
            Command = plan.RecoveryCommand
        };
        _document.Jobs.Insert(0, created);
        Trim();
        Save();
        job = Clone(created);
        return true;
    }

    public VerifiedRemediationJob Update(
        string jobId,
        VerifiedRemediationJobState state,
        string output = "",
        string verification = "",
        bool verified = false)
    {
        var job = _document.Jobs.First(item =>
            item.Id.Equals(jobId, StringComparison.OrdinalIgnoreCase));
        job.State = state;
        if (!string.IsNullOrWhiteSpace(output))
            job.Output = output;
        if (!string.IsNullOrWhiteSpace(verification))
            job.Verification = verification;
        job.Verified = verified;
        if (state is VerifiedRemediationJobState.Succeeded or
            VerifiedRemediationJobState.Failed or
            VerifiedRemediationJobState.Blocked)
        {
            job.CompletedAt = DateTimeOffset.UtcNow;
        }
        Save();
        return Clone(job);
    }

    private VerifiedRemediationDocument Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new VerifiedRemediationDocument();
            return JsonSerializer.Deserialize<VerifiedRemediationDocument>(
                       File.ReadAllText(_filePath),
                       _json) ??
                   new VerifiedRemediationDocument();
        }
        catch
        {
            return new VerifiedRemediationDocument();
        }
    }

    private void NormalizeInterruptedJobs()
    {
        var changed = false;
        foreach (var job in _document.Jobs.Where(item =>
                     item.State is VerifiedRemediationJobState.Queued or
                         VerifiedRemediationJobState.Running or
                         VerifiedRemediationJobState.Verifying))
        {
            job.State = VerifiedRemediationJobState.Failed;
            job.Output = string.IsNullOrWhiteSpace(job.Output)
                ? "GraveOps closed before remediation completed."
                : job.Output + Environment.NewLine +
                  "GraveOps closed before remediation completed.";
            job.CompletedAt = DateTimeOffset.UtcNow;
            changed = true;
        }
        if (changed)
            Save();
    }

    private void Trim()
    {
        _document.Jobs = _document.Jobs
            .OrderByDescending(item => item.StartedAt)
            .Take(200)
            .ToList();
    }

    private void Save()
    {
        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);
        var temporary = _filePath + ".tmp";
        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(_document, _json));
        File.Move(temporary, _filePath, overwrite: true);
    }

    private static VerifiedRemediationJob Clone(VerifiedRemediationJob source) =>
        new()
        {
            Id = source.Id,
            PlanId = source.PlanId,
            HostId = source.HostId,
            Product = source.Product,
            Target = source.Target,
            State = source.State,
            StartedAt = source.StartedAt,
            CompletedAt = source.CompletedAt,
            Command = source.Command,
            Output = source.Output,
            Verification = source.Verification,
            Verified = source.Verified
        };

    private static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? "local"
            : value.Trim();

    private sealed class VerifiedRemediationDocument
    {
        public VerifiedRemediationSettings Settings { get; set; } = new();
        public List<VerifiedRemediationJob> Jobs { get; set; } = new();
    }
}

public static class VerifiedRemediationPolicy
{
    private static readonly Regex UnitPattern = new(
        @"(?<unit>[A-Za-z0-9_.@:-]+\.(?:service|timer|mount))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ContainerPattern = new(
        @"(?:container|docker)(?:\s+name)?\s*[:=]?\s*(?<name>[A-Za-z0-9_.@:-]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled |
        RegexOptions.CultureInvariant);
    private static readonly Regex MountPattern = new(
        @"(?<mount>/(?:mnt|media|srv|var|opt)(?:/[A-Za-z0-9_.@+ -]+)+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly IReadOnlyList<VerifiedRemediationProduct> Products =
        BuildProducts();

    public static IReadOnlyList<VerifiedRemediationProduct> Catalog => Products;

    public static bool SupportsProduct(string product) =>
        FindProduct(product) is not null;

    public static IReadOnlyList<UnifiedDashboardCard> AttachActions(
        IReadOnlyList<UnifiedDashboardCard> cards,
        IReadOnlyList<OpsIntegration> integrations,
        out IReadOnlyDictionary<string, VerifiedRemediationPlan> plans)
    {
        var map = new Dictionary<string, VerifiedRemediationPlan>(
            StringComparer.OrdinalIgnoreCase);
        var projected = cards.Select(card =>
        {
            if (!ShouldOffer(card))
                return card;
            var plan = BuildPlan(card, integrations);
            if (plan is null)
                return card;
            map[plan.Id] = plan;
            if (card.Actions.Any(action =>
                    action.NavigationName.StartsWith(
                        "@remediate:",
                        StringComparison.OrdinalIgnoreCase)))
            {
                return card;
            }
            return card with
            {
                Actions = card.Actions
                    .Append(new UnifiedDashboardAction(
                        "Remediation",
                        $"@remediate:{plan.Id}",
                        Endpoint: plan.Id))
                    .ToArray()
            };
        }).ToArray();
        plans = map;
        return projected;
    }

    public static VerifiedRemediationPlan? BuildPlan(
        UnifiedDashboardCard card,
        IReadOnlyList<OpsIntegration> integrations)
    {
        ArgumentNullException.ThrowIfNull(card);
        var problemRow = card.Rows
            .OrderByDescending(row => row.Severity)
            .FirstOrDefault();
        var product = ResolveProduct(card);
        var definition = FindProduct(product);
        var planProduct = definition?.Name ?? product;
        var integration = integrations
            .Where(item => ProductMatches(item, product, definition))
            .OrderByDescending(item => item.OwnsHealth)
            .ThenByDescending(item => item.IsVerified)
            .FirstOrDefault();
        var evidence = string.Join(
            " · ",
            new[]
            {
                problemRow?.Detail,
                card.Detail,
                integration?.Evidence,
                integration?.InstanceKey
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var target = ResolveTarget(
            card,
            problemRow,
            integration,
            definition,
            evidence);
        var endpoint = ProductOperationalCatalog.ResolveEndpoint(
            planProduct,
            integration?.Endpoint ?? card.Endpoint ?? string.Empty,
            evidence);
        var problem = problemRow is null
            ? card.Summary
            : $"{problemRow.Label}: {problemRow.Value}";
        var dependencies = definition?.Dependencies ??
            CoreDependencies(card.Key);
        var storageSensitive =
            definition?.Family is "Acquisition" or "Downloads" or
                "Media server" or "Processing" ||
            card.Key is "core:downloads" or "core:acquisition" or
                "core:media" or "core:backups";
        var id = Fingerprint(
            card.Key,
            target.Kind.ToString(),
            target.Value,
            problem);
        var commands = InspectionCommands(
            target.Kind,
            target.Value,
            endpoint,
            definition);
        var recovery = Recovery(
            target.Kind,
            target.Value,
            definition);
        var verification = VerificationCommand(
            target.Kind,
            target.Value,
            endpoint,
            definition);

        return new VerifiedRemediationPlan(
            id,
            card.Key,
            planProduct,
            definition?.Family ?? CoreFamily(card.Key),
            target.Kind,
            target.Value,
            problem,
            LikelyCause(target.Kind, planProduct),
            dependencies,
            commands,
            recovery.Label,
            recovery.Command,
            ExpectedResult(target.Kind, planProduct),
            verification,
            RollbackText(target.Kind),
            ProductOperationalCatalog.ResolveNavigation(
                planProduct,
                card.NavigationName),
            endpoint,
            definition?.AllowsExitedPrimary == true,
            storageSensitive,
            recovery.Risk);
    }

    public static string MutationBlockReason(
        VerifiedRemediationPlan plan,
        VerifiedRemediationSettings settings,
        bool storageFaultActive)
    {
        if (!plan.CanMutate)
            return "This plan is inspection-only because no exact owning target was verified.";
        if (settings.SafeMode)
            return "Safe mode is enabled. Inspection remains available, but mutating actions are blocked.";
        if (settings.BlockOnStorageFault &&
            storageFaultActive &&
            plan.StorageSensitive &&
            plan.TargetKind != VerifiedRemediationTargetKind.Mount)
        {
            return "An active storage fault blocks this recovery because the target reads from or writes to storage.";
        }
        return string.Empty;
    }

    public static bool VerificationSucceeded(
        VerifiedRemediationPlan plan,
        int exitCode,
        string output)
    {
        if (exitCode != 0)
            return false;
        var value = output?.Trim() ?? string.Empty;
        return plan.TargetKind switch
        {
            VerifiedRemediationTargetKind.SystemdService or
            VerifiedRemediationTargetKind.BackupTimer =>
                SignalQualityPolicy.IsHealthySystemdServiceState(
                    value,
                    plan.AllowsExitedPrimary),
            VerifiedRemediationTargetKind.DockerContainer =>
                value.StartsWith("running", StringComparison.OrdinalIgnoreCase) &&
                !value.Contains("unhealthy", StringComparison.OrdinalIgnoreCase),
            VerifiedRemediationTargetKind.Mount =>
                value.Contains("mounted", StringComparison.OrdinalIgnoreCase) &&
                value.Contains("readable", StringComparison.OrdinalIgnoreCase),
            VerifiedRemediationTargetKind.PiHole =>
                value.Contains("enabled", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("listening", StringComparison.OrdinalIgnoreCase),
            _ =>
                ProductOperationalCatalog.EndpointVerificationSucceeded(
                    plan.Product,
                    value)
        };
    }

    public static string ShellQuote(string value) =>
        "'" + (value ?? string.Empty).Replace("'", "'\"'\"'") + "'";

    private static bool ShouldOffer(UnifiedDashboardCard card) =>
        card.Severity >= OpsSeverity.Warning ||
        card.Rows.Any(row => row.Severity >= OpsSeverity.Warning) ||
        card.Status.Equals("STALE", StringComparison.OrdinalIgnoreCase) ||
        card.Status.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase) ||
        card.Status.Equals("LAST GOOD", StringComparison.OrdinalIgnoreCase);

    private static string ResolveProduct(UnifiedDashboardCard card)
    {
        if (card.Key.StartsWith("app:", StringComparison.OrdinalIgnoreCase))
        {
            var key = card.Key[4..];
            var at = key.IndexOf('@');
            return at < 0 ? key : key[..at];
        }
        return card.Title;
    }

    private static VerifiedRemediationProduct? FindProduct(string value)
    {
        var token = Normalize(value);
        return Products.FirstOrDefault(product =>
            Normalize(product.Name).Equals(token, StringComparison.OrdinalIgnoreCase) ||
            product.Aliases.Any(alias =>
                Normalize(alias).Equals(token, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool ProductMatches(
        OpsIntegration integration,
        string product,
        VerifiedRemediationProduct? definition)
    {
        var tokens = new[]
        {
            integration.Name,
            integration.DisplayName,
            integration.InstanceKey,
            integration.Kind,
            integration.Provenance
        }.Select(Normalize).ToArray();
        var expected = new[] { product }
            .Concat(definition?.Aliases ?? Array.Empty<string>())
            .Append(definition?.Name ?? product)
            .Select(Normalize)
            .Where(value => value.Length > 0)
            .ToArray();
        return tokens.Any(actual =>
            expected.Any(item =>
                actual.Equals(item, StringComparison.OrdinalIgnoreCase) ||
                actual.Contains(item, StringComparison.OrdinalIgnoreCase)));
    }

    private static (VerifiedRemediationTargetKind Kind, string Value)
        ResolveTarget(
            UnifiedDashboardCard card,
            UnifiedDashboardRow? row,
            OpsIntegration? integration,
            VerifiedRemediationProduct? definition,
            string evidence)
    {
        if (card.Key.Equals("core:storage", StringComparison.OrdinalIgnoreCase))
        {
            var mount = MountPattern.Match(evidence);
            return (
                VerifiedRemediationTargetKind.Mount,
                mount.Success ? mount.Groups["mount"].Value.Trim() : row?.Label ?? "storage");
        }
        if (card.Key.Equals("core:backups", StringComparison.OrdinalIgnoreCase))
        {
            var timer = UnitPattern.Match(evidence);
            return (
                VerifiedRemediationTargetKind.BackupTimer,
                timer.Success ? timer.Groups["unit"].Value : "backup timers");
        }

        var unit = UnitPattern.Match(evidence);
        if (unit.Success)
        {
            return (
                VerifiedRemediationTargetKind.SystemdService,
                unit.Groups["unit"].Value);
        }
        var container = ContainerPattern.Match(evidence);
        if (container.Success)
        {
            return (
                VerifiedRemediationTargetKind.DockerContainer,
                container.Groups["name"].Value);
        }
        if (definition is not null)
        {
            var defaultUnit = definition.DefaultUnits.FirstOrDefault(unit =>
                evidence.Contains(unit, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(defaultUnit))
            {
                return (
                    VerifiedRemediationTargetKind.SystemdService,
                    defaultUnit);
            }

            var defaultContainer = definition.DefaultContainers.FirstOrDefault(containerName =>
                evidence.Contains(containerName, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(defaultContainer))
            {
                return (
                    VerifiedRemediationTargetKind.DockerContainer,
                    defaultContainer);
            }
        }
        if (definition?.Name.Equals("Pi-hole", StringComparison.OrdinalIgnoreCase) == true)
        {
            return (VerifiedRemediationTargetKind.PiHole, "pihole-FTL");
        }
        return (
            VerifiedRemediationTargetKind.Probe,
            string.IsNullOrWhiteSpace(card.Title) ? "telemetry" : card.Title);
    }

    private static IReadOnlyList<string> InspectionCommands(
        VerifiedRemediationTargetKind kind,
        string target,
        string endpoint,
        VerifiedRemediationProduct? product)
    {
        var quoted = ShellQuote(target);
        return kind switch
        {
            VerifiedRemediationTargetKind.SystemdService or
            VerifiedRemediationTargetKind.BackupTimer => new[]
            {
                $"systemctl status --no-pager -l {quoted}",
                $"journalctl -u {quoted} -n 120 --no-pager"
            },
            VerifiedRemediationTargetKind.DockerContainer => new[]
            {
                $"docker inspect {quoted}",
                $"docker logs --tail 120 {quoted}"
            },
            VerifiedRemediationTargetKind.Mount => new[]
            {
                $"findmnt --target {quoted}",
                "lsblk -f",
                $"test -r {quoted} && echo readable || echo unreadable; " +
                $"test -w {quoted} && echo writable || echo read-only"
            },
            VerifiedRemediationTargetKind.PiHole => new[]
            {
                "pihole status",
                "systemctl status --no-pager -l pihole-FTL.service"
            },
            _ when !string.IsNullOrWhiteSpace(endpoint) =>
                ProductOperationalCatalog.HttpInspectionCommands(
                    product?.Name ?? target,
                    endpoint),
            _ => new[]
            {
                $"echo 'No exact runtime target was verified for {product?.Name ?? target}; open its workspace and logs.'"
            }
        };
    }

    private static (string Label, string Command, VerifiedRemediationRisk Risk)
        Recovery(
            VerifiedRemediationTargetKind kind,
            string target,
            VerifiedRemediationProduct? product)
    {
        var quoted = ShellQuote(target);
        return kind switch
        {
            VerifiedRemediationTargetKind.SystemdService =>
                ("Restart service", $"pkexec systemctl restart {quoted}", VerifiedRemediationRisk.Recover),
            VerifiedRemediationTargetKind.BackupTimer =>
                ("Restart timer", $"pkexec systemctl restart {quoted}", VerifiedRemediationRisk.Recover),
            VerifiedRemediationTargetKind.DockerContainer =>
                ("Restart container", $"docker restart {quoted}", VerifiedRemediationRisk.Recover),
            VerifiedRemediationTargetKind.Mount =>
                ("Mount filesystem", $"pkexec mount {quoted}", VerifiedRemediationRisk.Recover),
            VerifiedRemediationTargetKind.PiHole =>
                ("Restart DNS", "pkexec env PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin pihole restartdns", VerifiedRemediationRisk.Recover),
            _ =>
                ("Inspection only", string.Empty, VerifiedRemediationRisk.Inspect)
        };
    }

    private static string VerificationCommand(
        VerifiedRemediationTargetKind kind,
        string target,
        string endpoint,
        VerifiedRemediationProduct? product)
    {
        var quoted = ShellQuote(target);
        return kind switch
        {
            VerifiedRemediationTargetKind.SystemdService or
            VerifiedRemediationTargetKind.BackupTimer =>
                $"systemctl show {quoted} --no-pager --property=ActiveState --property=SubState | " +
                "awk -F= '/ActiveState/{a=$2}/SubState/{s=$2}END{print a\"/\"s}'",
            VerifiedRemediationTargetKind.DockerContainer =>
                $"docker inspect -f '{{{{.State.Status}}}}|{{{{if .State.Health}}}}{{{{.State.Health.Status}}}}{{{{end}}}}' {quoted}",
            VerifiedRemediationTargetKind.Mount =>
                $"findmnt -n --target {quoted} >/dev/null && test -r {quoted} && echo mounted-readable || echo unavailable",
            VerifiedRemediationTargetKind.PiHole =>
                "pihole status",
            _ when !string.IsNullOrWhiteSpace(endpoint) =>
                ProductOperationalCatalog.HttpVerificationCommand(
                    product?.Name ?? target,
                    endpoint),
            _ =>
                $"echo 'Verification requires the {product?.Name ?? target} workspace.'"
        };
    }

    private static string LikelyCause(
        VerifiedRemediationTargetKind kind,
        string product) =>
        kind switch
        {
            VerifiedRemediationTargetKind.SystemdService =>
                $"The owning {product} systemd service is stopped, failed, or in an invalid substate.",
            VerifiedRemediationTargetKind.DockerContainer =>
                $"The owning {product} container is stopped, unhealthy, or blocked by a dependency.",
            VerifiedRemediationTargetKind.Mount =>
                "The device, mount definition, permissions, or filesystem state requires inspection.",
            VerifiedRemediationTargetKind.BackupTimer =>
                "The backup timer is stopped, disabled, or not producing verifiable artifacts.",
            VerifiedRemediationTargetKind.PiHole =>
                "Pi-hole DNS or FTL is unavailable, degraded, or unable to reach an upstream dependency.",
            _ =>
                $"{product} telemetry is stale, unreachable, or does not identify an exact owning runtime target."
        };

    private static string ExpectedResult(
        VerifiedRemediationTargetKind kind,
        string product) =>
        kind switch
        {
            VerifiedRemediationTargetKind.SystemdService =>
                $"The {product} service reaches active/running, active/listening, or another valid declared substate.",
            VerifiedRemediationTargetKind.DockerContainer =>
                $"The {product} container reports running and its health check is not unhealthy.",
            VerifiedRemediationTargetKind.Mount =>
                "The filesystem is mounted, readable, and its actual write state is reported.",
            VerifiedRemediationTargetKind.BackupTimer =>
                "The timer returns to an active waiting state and remains scheduled.",
            VerifiedRemediationTargetKind.PiHole =>
                "FTL is listening and blocking status can be read successfully.",
            _ =>
                ProductOperationalCatalog.ExpectedResult(product)
        };

    private static string RollbackText(VerifiedRemediationTargetKind kind) =>
        kind switch
        {
            VerifiedRemediationTargetKind.Mount =>
                "No automatic unmount is performed. Review findmnt, fstab and filesystem evidence before reversing a mount.",
            VerifiedRemediationTargetKind.SystemdService or
            VerifiedRemediationTargetKind.BackupTimer =>
                "A restart does not change the unit file. Inspect the journal and restore the previous configuration if verification fails.",
            VerifiedRemediationTargetKind.DockerContainer =>
                "A restart does not recreate the container. Inspect logs and the Compose definition before any configuration rollback.",
            _ =>
                "Inspection is read-only; no rollback is required."
        };

    private static string CoreFamily(string key) => key switch
    {
        "core:storage" => "Storage",
        "core:docker" => "Containers",
        "core:backups" => "Backups",
        "core:downloads" => "Downloads",
        "core:acquisition" => "Acquisition",
        "core:media" => "Media server",
        _ => "Host"
    };

    private static IReadOnlyList<string> CoreDependencies(string key) => key switch
    {
        "core:storage" => new[] { "block device", "filesystem", "mount definition" },
        "core:docker" => new[] { "host", "storage", "container runtime", "network" },
        "core:downloads" => new[] { "storage", "network", "indexers" },
        "core:acquisition" => new[] { "indexers", "download clients", "storage" },
        "core:media" => new[] { "storage", "database", "network" },
        "core:backups" => new[] { "storage", "timer", "backup provider" },
        _ => new[] { "host", "systemd", "storage" }
    };

    private static string Fingerprint(params string[] values)
    {
        var normalized = string.Join(
            "|",
            values.Select(value => Normalize(value)));
        return Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(normalized)))
            .ToLowerInvariant()[..20];
    }

    private static string Normalize(string value) =>
        Regex.Replace(
            value?.Trim().ToLowerInvariant() ?? string.Empty,
            @"[^a-z0-9]+",
            "-")
        .Trim('-');

    private static string SanitizeIdentifier(string value)
    {
        var candidate = Regex.Replace(
            value ?? string.Empty,
            @"[^A-Za-z0-9_.@:-]+",
            "-")
            .Trim('-');
        return string.IsNullOrWhiteSpace(candidate)
            ? "unknown"
            : candidate;
    }

    private static IReadOnlyList<VerifiedRemediationProduct> BuildProducts() =>
        ProductOperationalCatalog.ToVerifiedRemediationProducts();
}
