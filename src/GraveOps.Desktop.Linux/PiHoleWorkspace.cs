using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GraveOps.Desktop.Linux;

public enum PiHoleControlAction
{
    EnableBlocking,
    DisableBlockingFiveMinutes,
    ReloadDns
}

public sealed record PiHoleControlResult(
    bool Success,
    string Summary,
    string Detail);

internal sealed record LinuxPiHoleTelemetryContext(
    LinuxControlPlaneCoordinator ControlPlane,
    LinuxHostProfile Profile,
    string? VerifiedEndpoint);

internal sealed class LinuxPiHoleTelemetryAdapter :
    IApplicationTelemetryAdapter<
        LinuxPiHoleTelemetryContext,
        PiHoleTelemetrySnapshot>
{
    public Task<PiHoleTelemetrySnapshot> CaptureAsync(
        LinuxPiHoleTelemetryContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            context);

        return PiHoleWorkspaceService.CaptureAsync(
            context.ControlPlane,
            context.Profile,
            context.VerifiedEndpoint,
            cancellationToken);
    }
}

public static class PiHoleWorkspaceService
{
    private const string CaptureScript =
        """
        set +e
        export LC_ALL=C
        printf '__STATUS__\n'
        pihole status 2>&1 || true
        printf '__VERSION__\n'
        pihole -v 2>&1 || true
        printf '__HOST__\n'
        hostname 2>/dev/null || true
        uptime -p 2>/dev/null || true
        awk '{print $1}' /proc/loadavg 2>/dev/null || true
        if [ -r /sys/class/thermal/thermal_zone0/temp ]; then
          awk '{printf "%.1f\\n", $1/1000}' /sys/class/thermal/thermal_zone0/temp
        else
          printf '%s\n' '--'
        fi
        printf '__STATS__\n'
        if command -v timeout >/dev/null 2>&1; then
          timeout 6 pihole api stats/summary 2>/dev/null || true
        else
          pihole api stats/summary 2>/dev/null || true
        fi
        printf '\n__END__\n'
        true
        """;

    public static async Task<PiHoleTelemetrySnapshot> CaptureAsync(
        LinuxControlPlaneCoordinator controlPlane,
        LinuxHostProfile profile,
        string? verifiedEndpoint,
        CancellationToken cancellationToken = default)
    {
        var output =
            profile.IsLocal
                ? await RunLocalScriptAsync(
                    CaptureScript,
                    cancellationToken)
                : (await LinuxSshTransport.RunScriptAsync(
                    profile,
                    controlPlane.Credentials,
                    controlPlane.KnownHostsDirectory,
                    CaptureScript,
                    suppliedSecret: null,
                    cancellationToken)).StandardOutput;

        return Parse(
            output,
            profile,
            verifiedEndpoint);
    }

    public static async Task<PiHoleControlResult> RunActionAsync(
        LinuxControlPlaneCoordinator controlPlane,
        LinuxHostProfile profile,
        PiHoleControlAction action,
        CancellationToken cancellationToken = default)
    {
        var command = action switch
        {
            PiHoleControlAction.EnableBlocking =>
                "sudo -n pihole enable",
            PiHoleControlAction.DisableBlockingFiveMinutes =>
                "sudo -n pihole disable 5m",
            PiHoleControlAction.ReloadDns =>
                "sudo -n pihole reloaddns",
            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(action))
        };

        var script =
            $"""
            set +e
            {command} 2>&1
            code=$?
            printf '\n__EXIT__%s\n' "$code"
            exit 0
            """;

        var output =
            profile.IsLocal
                ? await RunLocalScriptAsync(
                    script,
                    cancellationToken)
                : (await LinuxSshTransport.RunScriptAsync(
                    profile,
                    controlPlane.Credentials,
                    controlPlane.KnownHostsDirectory,
                    script,
                    suppliedSecret: null,
                    cancellationToken)).StandardOutput;

        var match = Regex.Match(
            output,
            @"__EXIT__(?<code>\d+)",
            RegexOptions.CultureInvariant);
        var success =
            match.Success &&
            match.Groups["code"].Value == "0";
        var detail = Regex.Replace(
                output,
                @"\s*__EXIT__\d+\s*$",
                string.Empty,
                RegexOptions.CultureInvariant)
            .Trim();

        return new PiHoleControlResult(
            success,
            success
                ? "Pi-hole action completed."
                : "Pi-hole action failed.",
            string.IsNullOrWhiteSpace(detail)
                ? success
                    ? "Command completed without output."
                    : "The target returned no diagnostic output."
                : detail);
    }

    public static string NormalizeWebUrl(
        LinuxHostProfile profile,
        string? verifiedEndpoint)
    {
        var candidate =
            verifiedEndpoint?.Trim();

        if (!Uri.TryCreate(
                candidate,
                UriKind.Absolute,
                out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp &&
             uri.Scheme != Uri.UriSchemeHttps))
        {
            uri = new UriBuilder(
                    Uri.UriSchemeHttp,
                    profile.IsLocal
                        ? "127.0.0.1"
                        : profile.Host)
                .Uri;
        }

        var builder =
            new UriBuilder(uri)
            {
                Query = string.Empty,
                Fragment = string.Empty
            };
        var path =
            builder.Path.TrimEnd('/');

        if (string.IsNullOrWhiteSpace(path) ||
            path == "/")
        {
            builder.Path = "/admin";
        }

        return builder.Uri
            .ToString()
            .TrimEnd('/');
    }

    private static PiHoleTelemetrySnapshot Parse(
        string output,
        LinuxHostProfile profile,
        string? verifiedEndpoint)
    {
        var status = Slice(
            output,
            "__STATUS__",
            "__VERSION__");
        var versions = Slice(
            output,
            "__VERSION__",
            "__HOST__");
        var hostRows = Slice(
                output,
                "__HOST__",
                "__STATS__")
            .Replace("\r", string.Empty)
            .Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);
        var stats = Slice(
            output,
            "__STATS__",
            "__END__");

        var dnsOnline =
            status.Contains(
                "FTL is listening on port 53",
                StringComparison.OrdinalIgnoreCase) ||
            status.Contains(
                "DNS service is running",
                StringComparison.OrdinalIgnoreCase);
        var blockingEnabled =
            status.Contains(
                "blocking is enabled",
                StringComparison.OrdinalIgnoreCase);

        long? queries = null;
        long? blocked = null;
        double? percentBlocked = null;
        long? activeClients = null;
        long? totalClients = null;
        double? queryRate = null;
        long? gravityDomains = null;
        DateTimeOffset? gravityUpdated = null;

        try
        {
            var jsonStart = stats.IndexOf('{');

            if (jsonStart >= 0)
            {
                using var document =
                    JsonDocument.Parse(
                        stats[jsonStart..]);
                var root = document.RootElement;

                if (root.TryGetProperty(
                        "queries",
                        out var queryObject))
                {
                    queries = ReadInt64(
                        queryObject,
                        "total");
                    blocked = ReadInt64(
                        queryObject,
                        "blocked");
                    percentBlocked = ReadDouble(
                        queryObject,
                        "percent_blocked");
                    queryRate = ReadDouble(
                        queryObject,
                        "frequency");
                }
                else
                {
                    queries = ReadInt64(
                        root,
                        "total_queries");
                    blocked = ReadInt64(
                        root,
                        "blocked_queries");
                    percentBlocked = ReadDouble(
                        root,
                        "percent_blocked");
                }

                if (root.TryGetProperty(
                        "clients",
                        out var clients))
                {
                    activeClients = ReadInt64(
                        clients,
                        "active");
                    totalClients = ReadInt64(
                        clients,
                        "total");
                }

                if (root.TryGetProperty(
                        "gravity",
                        out var gravity))
                {
                    gravityDomains = ReadInt64(
                        gravity,
                        "domains_being_blocked");
                    var epoch = ReadInt64(
                        gravity,
                        "last_update");

                    if (epoch is > 0)
                    {
                        gravityUpdated =
                            DateTimeOffset
                                .FromUnixTimeSeconds(
                                    epoch.Value)
                                .ToLocalTime();
                    }
                }
            }
        }
        catch
        {
            // Status remains useful when optional JSON statistics are unavailable.
        }

        var severity =
            !dnsOnline
                ? ApplicationTelemetryHealth.Error
                : !blockingEnabled
                    ? ApplicationTelemetryHealth.Warning
                    : ApplicationTelemetryHealth.Healthy;
        var state =
            !dnsOnline
                ? "DNS OFFLINE"
                : !blockingEnabled
                    ? "BLOCKING DISABLED"
                    : "HEALTHY";

        return new PiHoleTelemetrySnapshot(
            DateTimeOffset.Now,
            severity,
            state,
            dnsOnline,
            blockingEnabled,
            Version(
                versions,
                "Core version is",
                "Pi-hole version is"),
            Version(
                versions,
                "Web version is"),
            Version(
                versions,
                "FTL version is"),
            hostRows.ElementAtOrDefault(0) ??
            profile.DisplayName,
            (hostRows.ElementAtOrDefault(1) ?? "--")
                .Replace(
                    "up ",
                    string.Empty,
                    StringComparison.OrdinalIgnoreCase),
            hostRows.ElementAtOrDefault(2) ?? "--",
            double.TryParse(
                hostRows.ElementAtOrDefault(3),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var temperature)
                ? $"{temperature:0.0} °C"
                : "--",
            queries,
            blocked,
            percentBlocked,
            activeClients,
            totalClients,
            queryRate,
            gravityDomains,
            gravityUpdated,
            NormalizeWebUrl(
                profile,
                verifiedEndpoint),
            LimitEvidence(output));
    }

    private static long? ReadInt64(
        JsonElement element,
        string property) =>
        element.TryGetProperty(
                property,
                out var value) &&
            value.TryGetInt64(
                out var parsed)
            ? parsed
            : null;

    private static double? ReadDouble(
        JsonElement element,
        string property) =>
        element.TryGetProperty(
                property,
                out var value) &&
            value.TryGetDouble(
                out var parsed)
            ? parsed
            : null;

    private static string Slice(
        string text,
        string start,
        string end)
    {
        var first = text.IndexOf(
            start,
            StringComparison.Ordinal);

        if (first < 0)
            return string.Empty;

        first += start.Length;
        var last = text.IndexOf(
            end,
            first,
            StringComparison.Ordinal);

        return last < 0
            ? text[first..]
            : text[first..last];
    }

    private static string Version(
        string text,
        params string[] prefixes)
    {
        foreach (var prefix in prefixes)
        {
            var match = Regex.Match(
                text,
                Regex.Escape(prefix) +
                @"\s+(?<version>[^\s]+)",
                RegexOptions.IgnoreCase |
                RegexOptions.CultureInvariant);

            if (match.Success)
                return match.Groups["version"].Value;
        }

        return "--";
    }

    private static string LimitEvidence(
        string value)
    {
        var normalized = value
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Trim();

        return normalized.Length <= 24000
            ? normalized
            : normalized[..24000] +
              "\n\n[Evidence truncated by GraveOps]";
    }

    private static async Task<string> RunLocalScriptAsync(
        string script,
        CancellationToken cancellationToken)
    {
        using var process =
            new Process
            {
                StartInfo =
                    new ProcessStartInfo
                    {
                        FileName = "bash",
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
            };

        process.StartInfo.ArgumentList.Add("-s");
        process.Start();
        await process.StandardInput.WriteAsync(
            script.AsMemory(),
            cancellationToken);
        process.StandardInput.Close();

        var stdout =
            process.StandardOutput.ReadToEndAsync(
                cancellationToken);
        var stderr =
            process.StandardError.ReadToEndAsync(
                cancellationToken);

        await process.WaitForExitAsync(
            cancellationToken);

        return (await stdout) +
               (string.IsNullOrWhiteSpace(
                    await stderr)
                    ? string.Empty
                    : Environment.NewLine +
                      await stderr);
    }
}
