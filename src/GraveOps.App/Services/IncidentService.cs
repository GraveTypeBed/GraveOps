using GraveOps.App.Models;

namespace GraveOps.App.Services;

/// <summary>
/// Provider-native incident analysis. No personal mount paths, container names or
/// helper scripts are assumed. The selected host, verified applications and shared
/// GraveOps telemetry are the source of truth.
/// </summary>
public sealed class IncidentService
{
    private readonly AppServices _services;
    public IncidentService(AppServices services) => _services = services;

    public async Task<IncidentReport> AnalyzeAsync(ServerProfile server, CancellationToken token = default)
    {
        var report = new IncidentReport();
        HostProbeResult? host = null;
        EnvironmentOverviewSnapshot? environment = null;

        try
        {
            host = await _services.Hosts.Resolve(server).ProbeAsync(server, token);
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            report.Severity = "CRITICAL";
            report.RootCause = "Host reachability";
            report.Headline = $"{server.Name} is not reachable through its configured GraveOps provider.";
            report.Findings.Add(ex.Message);
            report.Recommendations.Add("Open Servers and verify the host address, credentials and transport before restarting child applications.");
            report.Raw = BuildRaw(server, null, null);
            return report;
        }

        try { environment = await _services.Environment.GetSnapshotAsync(false, token); }
        catch { /* host-native evidence remains useful */ }

        var hostSnapshot = environment?.Hosts.FirstOrDefault(x => x.ServerId == server.Id);
        var impacts = environment?.Impacts.Where(x => x.ServerId == server.Id).ToList() ?? new();

        report.Findings.Add($"Host: {host.HostName} | {host.OperatingSystem} | {host.Architecture}");
        report.Findings.Add($"Storage roots: {host.StorageRoots.Count}");
        report.Findings.Add(host.Capabilities.HasFlag(HostCapability.Docker)
            ? "Docker capability is available."
            : "Docker capability is not detected on this host.");

        if (host.Uptime is { } uptime)
            report.Findings.Add($"Uptime: {FriendlyAge(uptime)}.");

        if (hostSnapshot is not null)
            report.Findings.Add($"Verified applications: {hostSnapshot.Apps.Count}.");

        if (impacts.Count == 0)
        {
            report.Severity = "HEALTHY";
            report.RootCause = "No critical fault detected";
            report.Headline = "The selected host is reachable and no active verified-application impacts are present.";
        }
        else
        {
            var blocker = impacts.FirstOrDefault(x => x.State == EnvironmentHealthState.Offline);
            var lead = blocker ?? impacts[0];
            report.Severity = blocker is null ? "WARNING" : "ERROR";
            report.RootCause = lead.Category == "Host" ? "Host dependency" : lead.Component;
            report.Headline = blocker is null
                ? $"{impacts.Count} component(s) on {server.Name} need attention."
                : $"{lead.Component} is unavailable and may block dependent workflows.";

            foreach (var impact in impacts.Take(12))
                report.Findings.Add($"{impact.Component}: {impact.Detail} — {impact.Impact}");

            MediaLifecycleSnapshot? lifecycle = null;
            try { lifecycle = await _services.Lifecycle.GetSnapshotAsync(server, false, token); } catch { }
            if (environment is not null)
            {
                var remediation = await _services.Lifecycle.BuildRemediationAsync(server, environment, lifecycle, token);
                foreach (var step in remediation.Take(8))
                    report.Recommendations.Add($"{step.Order}. {step.Component}: {step.NextAction}");
            }
        }

        if (report.Recommendations.Count == 0)
            report.Recommendations.Add("No repair action is recommended. Continue monitoring or open Intelligence for dependency detail.");

        report.Raw = BuildRaw(server, host, hostSnapshot);
        return report;
    }

    public async Task<SystemStateSnapshot> CaptureStateAsync(ServerProfile server, CancellationToken token = default)
    {
        HostProbeResult host;
        try
        {
            host = await _services.Hosts.Resolve(server).ProbeAsync(server, token);
        }
        catch (Exception ex)
        {
            return new SystemStateSnapshot
            {
                Health = "Offline",
                Plex = "Unavailable with host",
                Infrastructure = "Provider unavailable",
                FailedUnits = ex.Message,
                Mounts = "--",
                Backup = "Not configured",
                Uptime = "--"
            };
        }

        EnvironmentOverviewSnapshot? env = null;
        try { env = await _services.Environment.GetSnapshotAsync(false, token); } catch { }
        var selected = env?.Hosts.FirstOrDefault(x => x.ServerId == server.Id);
        var plex = selected?.Apps.FirstOrDefault(x => x.Name.Equals("Plex", StringComparison.OrdinalIgnoreCase));

        return new SystemStateSnapshot
        {
            Health = selected?.State.ToString() ?? "Reachable",
            Plex = plex is null ? "Not configured" : $"{plex.State}: {plex.Detail}",
            Infrastructure = host.Capabilities.HasFlag(HostCapability.Docker) ? "Docker available" : "Docker not detected",
            FailedUnits = "Provider-native checks",
            Mounts = $"{host.StorageRoots.Count} root(s)",
            Backup = "Provider-specific / not configured",
            Uptime = host.Uptime is { } uptime ? FriendlyAge(uptime) : "--"
        };
    }

    public static string Compare(SystemStateSnapshot before, SystemStateSnapshot after)
    {
        var pairs = new (string Name, string A, string B)[]
        {
            ("Health", before.Health, after.Health),
            ("Plex", before.Plex, after.Plex),
            ("Infrastructure", before.Infrastructure, after.Infrastructure),
            ("Provider checks", before.FailedUnits, after.FailedUnits),
            ("Storage roots", before.Mounts, after.Mounts),
            ("Backup", before.Backup, after.Backup),
            ("Uptime", before.Uptime, after.Uptime)
        };

        var sb = new StringBuilder();
        foreach (var pair in pairs)
        {
            if (pair.A == pair.B)
                sb.AppendLine($"= {pair.Name}: {pair.B}");
            else
            {
                sb.AppendLine($"- {pair.Name}: {pair.A}");
                sb.AppendLine($"+ {pair.Name}: {pair.B}");
            }
        }
        return sb.ToString().TrimEnd();
    }

    public async Task<string> BuildDiagnosticBundleAsync(ServerProfile server, CancellationToken token = default)
    {
        var report = await AnalyzeAsync(server, token);
        var diagnostics = new DiagnosticsBundleService(_services);
        var raw = await diagnostics.CollectAsync(server, token);

        var sb = new StringBuilder();
        sb.AppendLine("GRAVEOPS INCIDENT ANALYSIS");
        sb.AppendLine($"Generated: {report.Timestamp:O}");
        sb.AppendLine($"Severity: {report.Severity}");
        sb.AppendLine($"Root cause: {report.RootCause}");
        sb.AppendLine(report.Headline);
        sb.AppendLine();
        sb.AppendLine("FINDINGS");
        foreach (var item in report.Findings) sb.AppendLine("- " + item);
        if (report.Recommendations.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("RECOMMENDATIONS");
            foreach (var item in report.Recommendations) sb.AppendLine("- " + item);
        }
        sb.AppendLine();
        sb.AppendLine(raw);
        return sb.ToString();
    }

    private static string BuildRaw(ServerProfile profile, HostProbeResult? host, EnvironmentHostSnapshot? environment)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"PROFILE: {profile.Name} ({profile.ConnectionKind})");
        if (host is not null)
        {
            sb.AppendLine($"HOST: {host.HostName}");
            sb.AppendLine($"OS: {host.OperatingSystem}");
            sb.AppendLine($"CAPABILITIES: {host.Capabilities}");
            sb.AppendLine($"STORAGE: {string.Join(", ", host.StorageRoots)}");
            sb.AppendLine($"EVIDENCE: {string.Join(" | ", host.Evidence)}");
        }
        if (environment is not null)
        {
            sb.AppendLine($"STATE: {environment.State}");
            foreach (var app in environment.Apps)
                sb.AppendLine($"APP: {app.Name} | {app.State} | {app.Detail}");
        }
        return sb.ToString().TrimEnd();
    }

    private static string FriendlyAge(TimeSpan age) =>
        age.TotalDays >= 1 ? $"{age.TotalDays:0.#} days" :
        age.TotalHours >= 1 ? $"{age.TotalHours:0.#} hours" :
        $"{Math.Max(0, age.TotalMinutes):0} minutes";
}
