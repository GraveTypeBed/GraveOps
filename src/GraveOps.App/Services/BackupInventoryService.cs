using GraveOps.App.Models;

namespace GraveOps.App.Services;

public sealed class BackupInventoryService
{
    private readonly AppServices _services;

    public BackupInventoryService(AppServices services) => _services = services;

    public async Task<BackupInventorySnapshot> InspectAsync(
        ServerProfile server,
        CancellationToken cancellationToken = default)
    {
        var actions = _services.Config.Current.Actions
            .Where(x => x.ServerId is null || x.ServerId == server.Id)
            .Where(IsBackupAction)
            .OrderBy(x => x.Name)
            .ToArray();

        return server.ConnectionKind switch
        {
            HostConnectionKind.RemoteLinux => await InspectRemoteLinuxAsync(server, actions, cancellationToken),
            HostConnectionKind.RemoteWindows => await InspectRemoteWindowsAsync(server, actions, cancellationToken),
            HostConnectionKind.LocalWindows => await InspectLocalWindowsAsync(actions, cancellationToken),
            _ => new BackupInventorySnapshot { Actions = actions }
        };
    }

    private async Task<BackupInventorySnapshot> InspectRemoteLinuxAsync(
        ServerProfile server,
        QuickAction[] actions,
        CancellationToken cancellationToken)
    {
        const string command =
            "echo '__TOOLS__'; " +
            "for t in restic borg rclone duplicity rsnapshot; do command -v $t >/dev/null 2>&1 && echo $t; done; " +
            "echo '__TIMERS__'; " +
            "systemctl list-timers --all --no-legend --no-pager 2>/dev/null | grep -Ei 'backup|restic|borg|snapshot|rsync|rclone' | head -n 60 || true; " +
            "echo '__CRON__'; " +
            "(crontab -l 2>/dev/null; cat /etc/crontab 2>/dev/null) | grep -Ei 'backup|restic|borg|snapshot|rsync|rclone' | head -n 40 || true";

        var result = await _services.Ssh.ExecuteAsync(server, command, 35, cancellationToken);
        var text = result.StdOut.Replace("\r", "", StringComparison.Ordinal);

        var tools = Section(text, "__TOOLS__", "__TIMERS__")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var timers = Section(text, "__TIMERS__", "__CRON__")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var cron = Tail(text, "__CRON__")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var schedules = timers
            .Concat(cron)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var evidence = tools
            .Select(x => $"Tool detected: {x}")
            .ToArray();

        return BuildSnapshot(
            tools.Length == 0 ? "Not configured" : string.Join(" / ", tools.Select(x => x.ToUpperInvariant())),
            evidence,
            schedules,
            actions,
            result.ExitCode == 0);
    }

    private async Task<BackupInventorySnapshot> InspectRemoteWindowsAsync(
        ServerProfile server,
        QuickAction[] actions,
        CancellationToken cancellationToken)
    {
        var output = await _services.PowerShellRemote.ExecuteAsync(
            server,
            ScheduledTaskScript,
            35,
            cancellationToken);

        var schedules = ParseTaskOutput(output);
        return BuildSnapshot(
            schedules.Count > 0 ? "Windows Task Scheduler" : "Not configured",
            schedules.Count > 0 ? new[] { "Backup-related scheduled task(s) detected" } : Array.Empty<string>(),
            schedules,
            actions,
            true);
    }

    private async Task<BackupInventorySnapshot> InspectLocalWindowsAsync(
        QuickAction[] actions,
        CancellationToken cancellationToken)
    {
        var result = await _services.LocalPowerShell.ExecuteAsync(
            ScheduledTaskScript,
            35,
            cancellationToken);

        var schedules = ParseTaskOutput(result.StdOut);
        return BuildSnapshot(
            schedules.Count > 0 ? "Windows Task Scheduler" : "Not configured",
            schedules.Count > 0 ? new[] { "Backup-related scheduled task(s) detected" } : Array.Empty<string>(),
            schedules,
            actions,
            result.ExitCode == 0);
    }

    private static BackupInventorySnapshot BuildSnapshot(
        string provider,
        IReadOnlyList<string> evidence,
        IReadOnlyList<string> schedules,
        IReadOnlyList<QuickAction> actions,
        bool probeSucceeded)
    {
        var configured = schedules.Count > 0 || actions.Count > 0;
        var available = evidence.Count > 0 || !provider.Equals("Not configured", StringComparison.OrdinalIgnoreCase);

        var readiness = !probeSucceeded
            ? BackupReadiness.Attention
            : configured
                ? BackupReadiness.Configured
                : available
                    ? BackupReadiness.Available
                    : BackupReadiness.NotConfigured;

        return new BackupInventorySnapshot
        {
            Readiness = readiness,
            ProviderText = provider,
            Evidence = evidence,
            Schedules = schedules,
            Actions = actions
        };
    }

    private static bool IsBackupAction(QuickAction action) =>
        action.Category.Contains("backup", StringComparison.OrdinalIgnoreCase) ||
        action.Name.Contains("backup", StringComparison.OrdinalIgnoreCase) ||
        action.Name.Contains("restore", StringComparison.OrdinalIgnoreCase) ||
        action.Name.Contains("snapshot", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> ParseTaskOutput(string output) =>
        output.Replace("\r", "", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !x.Equals("NONE", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(60)
            .ToArray();

    private static string Section(string text, string start, string end)
    {
        var a = text.IndexOf(start, StringComparison.Ordinal);
        if (a < 0)
            return "";
        a += start.Length;
        var b = text.IndexOf(end, a, StringComparison.Ordinal);
        return b < 0 ? text[a..] : text[a..b];
    }

    private static string Tail(string text, string marker)
    {
        var index = text.IndexOf(marker, StringComparison.Ordinal);
        return index < 0 ? "" : text[(index + marker.Length)..];
    }

    private const string ScheduledTaskScript = @"
$tasks = Get-ScheduledTask -ErrorAction SilentlyContinue |
    Where-Object { $_.TaskName -match 'backup|restore|snapshot|file history' }
if ($tasks) {
    $tasks | Select-Object -First 60 TaskName,State |
        ForEach-Object { Write-Output ($_.TaskName + ' | ' + $_.State) }
} else {
    Write-Output 'NONE'
}
";
}
