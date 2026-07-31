using System.Diagnostics;
using System.Net.Http;
using System.Text.RegularExpressions;
using GraveOps.App.Models;

namespace GraveOps.App.Services;

/// <summary>
/// Host-owned telemetry and safe operations for integrations that do not
/// participate in the Arr/download telemetry pipeline. This service owns no timer;
/// pages and fleet services decide when a sample is appropriate.
/// </summary>
public sealed class IntegrationRuntimeService
{
    private readonly AppServices _services;
    private static readonly HttpClient LocalClient = new(
        new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(2),
            AllowAutoRedirect = false
        })
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    private sealed record HttpProbeResult(int StatusCode, long LatencyMs);

    private sealed record RuntimeProbeResult(
        bool Found,
        bool? Running,
        string State,
        string Build,
        string Cpu,
        string Memory,
        string Uptime,
        string Detail)
    {
        public static RuntimeProbeResult Unknown(string detail) =>
            new(false, null, "Not identified", "--", "--", "--", "--", detail);
    }

    public IntegrationRuntimeService(AppServices services) => _services = services;

    public async Task<IntegrationRuntimeStatus> ProbeAsync(
        string integrationName,
        ServerProfile server,
        ManagedApp? app,
        CancellationToken cancellationToken = default)
    {
        if (integrationName.Equals("Recyclarr", StringComparison.OrdinalIgnoreCase))
            return await ProbeRecyclarrAsync(server, app, cancellationToken);

        if (integrationName.Equals("Kometa", StringComparison.OrdinalIgnoreCase) ||
            integrationName.Equals("Unpackerr", StringComparison.OrdinalIgnoreCase))
            return await ProbeRuntimeIntegrationAsync(integrationName, server, app, cancellationToken);

        if (app is null)
        {
            return new IntegrationRuntimeStatus
            {
                Name = integrationName,
                Health = AppHealthState.Stale,
                StateText = "Not configured",
                Owner = server.Name,
                Detail = "No verified application record is assigned to this host."
            };
        }

        var runtimeProbe = await ProbeRuntimeAsync(integrationName, server, cancellationToken);
        var endpoint = ResolveEndpoint(app, server);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return new IntegrationRuntimeStatus
            {
                Name = integrationName,
                Health = runtimeProbe.Running == false ? AppHealthState.Degraded : AppHealthState.Healthy,
                StateText = runtimeProbe.Running == false ? "Attention" : "Detected",
                Owner = server.Name,
                Endpoint = "Port not identified",
                Runtime = RuntimeText(server),
                DiscoveryEvidence = app.DiscoveryEvidence,
                Detail = "The integration was verified, but GraveOps does not yet know its web endpoint.",
                RuntimeStateText = runtimeProbe.State,
                BuildText = runtimeProbe.Build,
                CpuText = runtimeProbe.Cpu,
                MemoryText = runtimeProbe.Memory,
                UptimeText = runtimeProbe.Uptime,
                ReadinessText = runtimeProbe.Running == false ? "Runtime stopped" : "Endpoint unknown",
                RuntimeDetail = runtimeProbe.Detail
            };
        }

        try
        {
            var httpProbe = server.ConnectionKind switch
            {
                HostConnectionKind.LocalWindows => await ProbeLocalHttpAsync(endpoint, cancellationToken),
                HostConnectionKind.RemoteLinux => await ProbeRemoteLinuxHttpAsync(server, endpoint, cancellationToken),
                HostConnectionKind.RemoteWindows => await ProbeDirectHttpAsync(endpoint, cancellationToken),
                _ => null
            };

            if (httpProbe is null)
            {
                var supportedProbe = server.ConnectionKind is HostConnectionKind.LocalWindows or HostConnectionKind.RemoteLinux or HostConnectionKind.RemoteWindows;
                return new IntegrationRuntimeStatus
                {
                    Name = integrationName,
                    Health = supportedProbe ? AppHealthState.Offline : AppHealthState.Degraded,
                    StateText = supportedProbe ? "Offline" : "Detected",
                    Owner = server.Name,
                    Endpoint = endpoint,
                    Runtime = RuntimeText(server),
                    DiscoveryEvidence = app.DiscoveryEvidence,
                    CanOpen = true,
                    Detail = supportedProbe
                        ? "The verified endpoint did not return an HTTP status during the probe."
                        : "Verified integration. Runtime probing for this host type is not implemented yet.",
                    HttpText = "No response",
                    RuntimeStateText = runtimeProbe.State,
                    BuildText = runtimeProbe.Build,
                    CpuText = runtimeProbe.Cpu,
                    MemoryText = runtimeProbe.Memory,
                    UptimeText = runtimeProbe.Uptime,
                    ReadinessText = "Unavailable",
                    RuntimeDetail = runtimeProbe.Detail
                };
            }

            var code = httpProbe.StatusCode;
            var healthy = code is >= 200 and < 500;
            var readiness = await ProbeReadinessAsync(integrationName, server, endpoint, code, cancellationToken);
            return new IntegrationRuntimeStatus
            {
                Name = integrationName,
                Health = healthy ? AppHealthState.Healthy : AppHealthState.Degraded,
                StateText = healthy ? "Online" : "Attention",
                Owner = server.Name,
                Endpoint = endpoint,
                Runtime = RuntimeText(server),
                DiscoveryEvidence = app.DiscoveryEvidence,
                CanOpen = true,
                Detail = healthy
                    ? code is 401 or 403
                        ? "Endpoint is reachable and authentication is protecting the application."
                        : "Verified application endpoint is reachable."
                    : "The endpoint responded, but the server returned an error status.",
                HttpText = $"HTTP {code}",
                LatencyText = $"{httpProbe.LatencyMs} ms",
                RuntimeStateText = runtimeProbe.State,
                BuildText = runtimeProbe.Build,
                CpuText = runtimeProbe.Cpu,
                MemoryText = runtimeProbe.Memory,
                UptimeText = runtimeProbe.Uptime,
                ReadinessText = readiness,
                RuntimeDetail = runtimeProbe.Detail
            };
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return new IntegrationRuntimeStatus
            {
                Name = integrationName,
                Health = AppHealthState.Offline,
                StateText = "Offline",
                Owner = server.Name,
                Endpoint = endpoint,
                Runtime = RuntimeText(server),
                DiscoveryEvidence = app.DiscoveryEvidence,
                CanOpen = true,
                Detail = string.IsNullOrWhiteSpace(ex.Message) ? "Endpoint probe failed." : ex.Message,
                HttpText = "Probe failed",
                RuntimeStateText = runtimeProbe.State,
                BuildText = runtimeProbe.Build,
                CpuText = runtimeProbe.Cpu,
                MemoryText = runtimeProbe.Memory,
                UptimeText = runtimeProbe.Uptime,
                ReadinessText = "Unavailable",
                RuntimeDetail = runtimeProbe.Detail
            };
        }
    }

    public async Task<IReadOnlyList<RecyclarrInstanceInfo>> DiscoverRecyclarrInstancesAsync(
        ServerProfile server,
        CancellationToken cancellationToken = default)
    {
        if (server.ConnectionKind == HostConnectionKind.LocalWindows)
            return DiscoverLocalRecyclarrInstances();

        if (server.ConnectionKind == HostConnectionKind.RemoteWindows)
        {
            const string remoteParser = """
$roots=@(
  (Join-Path $env:APPDATA 'recyclarr'),
  (Join-Path $env:LOCALAPPDATA 'recyclarr'),
  (Join-Path $HOME '.config\recyclarr'),
  'C:\ProgramData\recyclarr'
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -Unique
foreach($root in $roots) {
  Get-ChildItem -Path $root -File -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -in '.yml','.yaml' } | ForEach-Object {
      $svc=''
      foreach($line in (Get-Content -LiteralPath $_.FullName -ErrorAction SilentlyContinue)) {
        if($line -match '^(sonarr|radarr):\s*$') { $svc=$Matches[1].ToLowerInvariant(); continue }
        if($svc -and $line -match '^\s{2}([A-Za-z0-9_]+):\s*$') { Write-Output ($svc+'|'+$Matches[1]); continue }
        if($line -match '^\S' -and $line -notmatch '^(sonarr|radarr):') { $svc='' }
      }
    }
}
""";
            var output = await _services.PowerShellRemote.ExecuteAsync(server, remoteParser, 35, cancellationToken);
            return ParseRecyclarrInstanceLines(output);
        }

        if (server.ConnectionKind != HostConnectionKind.RemoteLinux)
            return Array.Empty<RecyclarrInstanceInfo>();

        const string parser =
            "parse_file(){ awk '" +
            "/^(sonarr|radarr):[[:space:]]*$/ {svc=$1; sub(\":\",\"\",svc); next} " +
            "svc != \"\" && /^[[:space:]][[:space:]][A-Za-z0-9_]+:[[:space:]]*$/ {name=$1; sub(\":\",\"\",name); print svc \"|\" name; next} " +
            "/^[^[:space:]#]/ {if ($0 !~ /^(sonarr|radarr):/) svc=\"\"}' \"$1\"; }; " +
            "for f in /config/recyclarr.yml /config/recyclarr.yaml /config/*.yml /config/*.yaml /config/configs/*.yml /config/configs/*.yaml /opt/recyclarr/recyclarr.yml /opt/recyclarr/recyclarr.yaml /opt/recyclarr/*.yml /opt/recyclarr/*.yaml /opt/recyclarr/configs/*.yml /opt/recyclarr/configs/*.yaml ~/.config/recyclarr/*.yml ~/.config/recyclarr/*.yaml; do [ -f \"$f\" ] && parse_file \"$f\"; done";

        var command =
            "if c=$(docker ps --format '{{.Names}}|{{.Image}}' 2>/dev/null | grep -i -m1 recyclarr | cut -d'|' -f1) && [ -n \"$c\" ]; then " +
            $"docker exec \"$c\" sh -lc {ShellQuote(parser)}; " +
            "else " + parser + "; fi";

        var execution = await _services.Ssh.ExecuteAsync(server, command, 35, cancellationToken);
        return ParseRecyclarrInstanceLines(execution.StdOut);
    }

    private static IReadOnlyList<RecyclarrInstanceInfo> DiscoverLocalRecyclarrInstances()
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "recyclarr"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "recyclarr"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "recyclarr"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "recyclarr")
        };
        var output = new List<string>();
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                             .Where(x => x.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)))
                {
                    string service = "";
                    foreach (var line in File.ReadLines(file))
                    {
                        var top = Regex.Match(line, "^(sonarr|radarr):\\s*$", RegexOptions.IgnoreCase);
                        if (top.Success) { service = top.Groups[1].Value.ToLowerInvariant(); continue; }
                        var item = Regex.Match(line, "^\\s{2}([A-Za-z0-9_]+):\\s*$");
                        if (service.Length > 0 && item.Success) { output.Add(service + "|" + item.Groups[1].Value); continue; }
                        if (Regex.IsMatch(line, "^\\S") && !Regex.IsMatch(line, "^(sonarr|radarr):", RegexOptions.IgnoreCase)) service = "";
                    }
                }
            }
            catch { }
        }
        return ParseRecyclarrInstanceLines(string.Join(Environment.NewLine, output));
    }

    private static IReadOnlyList<RecyclarrInstanceInfo> ParseRecyclarrInstanceLines(string output)
    {
        var instances = new List<RecyclarrInstanceInfo>();
        foreach (var rawLine in (output ?? "").Replace("\r", "", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = rawLine.Trim().Split(new[] { '|' }, 2, StringSplitOptions.None);
            if (parts.Length != 2) continue;
            var service = parts[0].Trim().ToLowerInvariant();
            var name = parts[1].Trim();
            if ((service != "sonarr" && service != "radarr") || !Regex.IsMatch(name, "^[A-Za-z0-9_]+$")) continue;
            instances.Add(new RecyclarrInstanceInfo { Service = service, Name = name });
        }
        return instances
            .GroupBy(x => x.Service + "|" + x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.Service)
            .ThenBy(x => x.Name)
            .ToArray();
    }

    public async Task<RecyclarrPreviewResult> RunRecyclarrPreviewAsync(
        ServerProfile server,
        string service,
        string? instance = null,
        CancellationToken cancellationToken = default)
    {
        service = service.Equals("radarr", StringComparison.OrdinalIgnoreCase) ? "radarr" : "sonarr";
        if (!string.IsNullOrWhiteSpace(instance) && !Regex.IsMatch(instance, "^[A-Za-z0-9_]+$"))
        {
            return new RecyclarrPreviewResult
            {
                Success = false,
                Output = "The Recyclarr instance name is not valid for safe CLI execution."
            };
        }

        if (server.ConnectionKind == HostConnectionKind.LocalWindows)
        {
            var path = FindExecutableOnPath("recyclarr.exe") ?? FindExecutableOnPath("recyclarr");
            if (path is null)
                return new RecyclarrPreviewResult { Success = false, Output = "Recyclarr is not available on the local PATH." };
            try
            {
                var psi = new ProcessStartInfo(path) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                psi.ArgumentList.Add("sync"); psi.ArgumentList.Add(service); psi.ArgumentList.Add("--preview");
                if (!string.IsNullOrWhiteSpace(instance)) { psi.ArgumentList.Add("--instance"); psi.ArgumentList.Add(instance); }
                using var process = Process.Start(psi) ?? throw new InvalidOperationException("Unable to start Recyclarr.");
                var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);
                var localPreviewOutput = string.Join(Environment.NewLine, new[] { await stdoutTask, await stderrTask }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();
                return new RecyclarrPreviewResult { Success = process.ExitCode == 0, Output = localPreviewOutput.Length == 0 ? "Preview completed with no console output." : localPreviewOutput };
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                return new RecyclarrPreviewResult { Success = false, Output = ex.Message };
            }
        }

        if (server.ConnectionKind == HostConnectionKind.RemoteWindows)
        {
            var inst = string.IsNullOrWhiteSpace(instance) ? "" : " --instance " + instance;
            var script = "$ErrorActionPreference='Stop'; if(Get-Command recyclarr -ErrorAction SilentlyContinue){ & recyclarr sync " + service + " --preview" + inst + "; exit $LASTEXITCODE }; " +
                         "$c=(docker ps --format '{{.Names}}' 2>$null | Select-String -Pattern 'recyclarr' | Select-Object -First 1).Line; if($c){ docker exec $c recyclarr sync " + service + " --preview" + inst + "; exit $LASTEXITCODE }; throw 'Recyclarr CLI or running container was not found.'";
            try
            {
                var remotePreviewOutput = await _services.PowerShellRemote.ExecuteAsync(server, script, 180, cancellationToken);
                return new RecyclarrPreviewResult { Success = true, Output = string.IsNullOrWhiteSpace(remotePreviewOutput) ? "Preview completed with no console output." : remotePreviewOutput };
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                return new RecyclarrPreviewResult { Success = false, Output = ex.Message };
            }
        }

        if (server.ConnectionKind != HostConnectionKind.RemoteLinux)
            return new RecyclarrPreviewResult { Success = false, Output = "Recyclarr preview is not available for this host provider." };

        var instanceArg = string.IsNullOrWhiteSpace(instance) ? "" : $" --instance {instance}";
        var command =
            "if command -v recyclarr >/dev/null 2>&1; then " +
            $"recyclarr sync {service} --preview{instanceArg}; " +
            "elif c=$(docker ps --format '{{.Names}}' 2>/dev/null | grep -i -m1 recyclarr) && [ -n \"$c\" ]; then " +
            $"docker exec \"$c\" recyclarr sync {service} --preview{instanceArg}; " +
            "else printf '__GRAVEOPS_UNAVAILABLE__\\nRecyclarr is detected, but no direct CLI or running container is available for safe preview execution.\\n'; exit 3; fi";

        var execution = await _services.Ssh.ExecuteAsync(server, command, 180, cancellationToken);
        var output = string.Join(
            Environment.NewLine,
            new[] { execution.StdOut.Trim(), execution.StdErr.Trim() }.Where(x => x.Length > 0));

        if (output.Contains("__GRAVEOPS_UNAVAILABLE__", StringComparison.Ordinal))
            output = output.Replace("__GRAVEOPS_UNAVAILABLE__", "", StringComparison.Ordinal).Trim();

        return new RecyclarrPreviewResult
        {
            Success = execution.Success,
            Output = string.IsNullOrWhiteSpace(output)
                ? (execution.Success ? "Preview completed with no console output." : "Preview command failed without console output.")
                : output
        };
    }

    private async Task<IntegrationRuntimeStatus> ProbeRecyclarrAsync(
        ServerProfile server,
        ManagedApp? app,
        CancellationToken cancellationToken)
    {
        if (server.ConnectionKind == HostConnectionKind.LocalWindows)
        {
            var path = FindExecutableOnPath("recyclarr.exe") ?? FindExecutableOnPath("recyclarr");
            if (path is null)
            {
                return new IntegrationRuntimeStatus
                {
                    Name = "Recyclarr",
                    Health = app?.DiscoveryVerified == true ? AppHealthState.Degraded : AppHealthState.Offline,
                    StateText = app?.DiscoveryVerified == true ? "Detected" : "Not found",
                    Owner = server.Name,
                    Runtime = "Local Windows",
                    DiscoveryEvidence = app?.DiscoveryEvidence ?? "",
                    Detail = app?.DiscoveryVerified == true
                        ? "Recyclarr was previously verified, but the executable is no longer on PATH."
                        : "Recyclarr executable was not found on PATH.",
                    RuntimeStateText = "Not running",
                    ReadinessText = "Unavailable"
                };
            }

            string version;
            try
            {
                var psi = new ProcessStartInfo(path, "--version")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                if (process is null) throw new InvalidOperationException("Unable to start recyclarr.");
                await process.WaitForExitAsync(cancellationToken);
                version = (await process.StandardOutput.ReadToEndAsync(cancellationToken)).Trim();
                if (version.Length == 0)
                    version = (await process.StandardError.ReadToEndAsync(cancellationToken)).Trim();
            }
            catch
            {
                version = "Executable detected";
            }

            return new IntegrationRuntimeStatus
            {
                Name = "Recyclarr",
                Health = AppHealthState.Healthy,
                StateText = "Available",
                Owner = server.Name,
                Runtime = "Local CLI",
                DiscoveryEvidence = app?.DiscoveryEvidence ?? "recyclarr executable on PATH",
                Detail = "Recyclarr is available locally. GraveOps can run preview-only plans without exposing sync writes.",
                BuildText = version.Length == 0 ? "Executable detected" : version,
                RuntimeStateText = "Available",
                ReadinessText = "Preview ready",
                RuntimeDetail = path
            };
        }

        if (server.ConnectionKind == HostConnectionKind.RemoteWindows)
        {
            var remoteRuntimeProbe = await ProbeRuntimeAsync("Recyclarr", server, cancellationToken);
            return new IntegrationRuntimeStatus
            {
                Name = "Recyclarr",
                Health = remoteRuntimeProbe.Found ? AppHealthState.Healthy : AppHealthState.Degraded,
                StateText = remoteRuntimeProbe.Found ? "Available" : "Detected",
                Owner = server.Name,
                Runtime = "Remote Windows",
                DiscoveryEvidence = app?.DiscoveryEvidence ?? "",
                Detail = remoteRuntimeProbe.Found
                    ? "Recyclarr runtime is available through the remote Windows provider. Preview-only execution is enabled; sync writes remain unavailable."
                    : "Recyclarr was verified during discovery, but the current runtime could not be identified.",
                CanPreviewRecyclarr = remoteRuntimeProbe.Found,
                RuntimeStateText = remoteRuntimeProbe.State,
                BuildText = remoteRuntimeProbe.Build,
                CpuText = remoteRuntimeProbe.Cpu,
                MemoryText = remoteRuntimeProbe.Memory,
                UptimeText = remoteRuntimeProbe.Uptime,
                ReadinessText = remoteRuntimeProbe.Found ? "Preview ready" : "Runtime unavailable",
                RuntimeDetail = remoteRuntimeProbe.Detail
            };
        }

        if (server.ConnectionKind != HostConnectionKind.RemoteLinux)
        {
            return new IntegrationRuntimeStatus
            {
                Name = "Recyclarr",
                Health = AppHealthState.Degraded,
                StateText = "Detected",
                Owner = server.Name,
                DiscoveryEvidence = app?.DiscoveryEvidence ?? "",
                Detail = "Recyclarr probing is not implemented for this host provider yet."
            };
        }

        const string versionCommand =
            "if command -v recyclarr >/dev/null 2>&1; then printf 'CLI|'; recyclarr --version 2>&1 | head -n 1; " +
            "elif c=$(docker ps --format '{{.Names}}' 2>/dev/null | grep -i -m1 recyclarr) && [ -n \"$c\" ]; then printf 'CONTAINER|'; docker exec \"$c\" recyclarr --version 2>&1 | head -n 1; " +
            "elif docker images --format '{{.Repository}}:{{.Tag}}' 2>/dev/null | grep -qi -m1 recyclarr; then printf 'IMAGE|docker image'; " +
            "else exit 3; fi";

        var execution = await _services.Ssh.ExecuteAsync(server, versionCommand, 30, cancellationToken);
        var runtimeVersion = execution.StdOut.Trim();
        if (!execution.Success && runtimeVersion.Length == 0)
        {
            return new IntegrationRuntimeStatus
            {
                Name = "Recyclarr",
                Health = AppHealthState.Offline,
                StateText = "Not found",
                Owner = server.Name,
                Runtime = "Remote Linux",
                DiscoveryEvidence = app?.DiscoveryEvidence ?? "",
                Detail = "Recyclarr runtime evidence is no longer present on the owning host.",
                RuntimeStateText = "Not found",
                ReadinessText = "Unavailable"
            };
        }

        var runtimeProbe = await ProbeRuntimeAsync("Recyclarr", server, cancellationToken);
        var canPreview = runtimeVersion.StartsWith("CLI|", StringComparison.Ordinal) ||
                         runtimeVersion.StartsWith("CONTAINER|", StringComparison.Ordinal);
        var build = runtimeVersion.Contains('|')
            ? runtimeVersion[(runtimeVersion.IndexOf('|') + 1)..].Trim()
            : runtimeVersion;
        var runtimeLabel = runtimeVersion.StartsWith("CLI|", StringComparison.Ordinal)
            ? "Remote Linux CLI"
            : runtimeVersion.StartsWith("CONTAINER|", StringComparison.Ordinal)
                ? "Docker container"
                : "Docker image";

        return new IntegrationRuntimeStatus
        {
            Name = "Recyclarr",
            Health = AppHealthState.Healthy,
            StateText = "Available",
            Owner = server.Name,
            Runtime = runtimeLabel,
            DiscoveryEvidence = runtimeVersion.StartsWith("CLI|", StringComparison.Ordinal)
                ? "recyclarr executable on remote PATH"
                : runtimeVersion.StartsWith("CONTAINER|", StringComparison.Ordinal)
                    ? "running Recyclarr Docker container"
                    : "Recyclarr Docker image installed",
            Detail = canPreview
                ? "Safe preview execution is available. GraveOps can target discovered Sonarr/Radarr instances individually; sync writes remain intentionally unavailable."
                : "Recyclarr image is installed. GraveOps will not guess a compose working directory or start an ad-hoc container.",
            CanPreviewRecyclarr = canPreview,
            RuntimeStateText = runtimeProbe.Found ? runtimeProbe.State : "Available",
            BuildText = string.IsNullOrWhiteSpace(build) ? runtimeProbe.Build : build,
            CpuText = runtimeProbe.Cpu,
            MemoryText = runtimeProbe.Memory,
            UptimeText = runtimeProbe.Uptime,
            ReadinessText = canPreview ? "Preview ready" : "Installed only",
            RuntimeDetail = runtimeProbe.Found ? runtimeProbe.Detail : runtimeLabel
        };
    }

    private async Task<IntegrationRuntimeStatus> ProbeRuntimeIntegrationAsync(
        string integrationName,
        ServerProfile server,
        ManagedApp? app,
        CancellationToken cancellationToken)
    {
        if (app is null)
        {
            return new IntegrationRuntimeStatus
            {
                Name = integrationName,
                Health = AppHealthState.Stale,
                StateText = "Not configured",
                Owner = server.Name,
                Detail = "No verified application record is assigned to this host."
            };
        }

        var runtimeProbe = await ProbeRuntimeAsync(integrationName, server, cancellationToken);
        var isKometa = integrationName.Equals("Kometa", StringComparison.OrdinalIgnoreCase);
        var healthy = runtimeProbe.Running == true || (isKometa && runtimeProbe.Found);
        var state = runtimeProbe.Running == true ? "Running" : isKometa && runtimeProbe.Found ? "Available" : "Attention";

        return new IntegrationRuntimeStatus
        {
            Name = integrationName,
            Health = healthy ? AppHealthState.Healthy : AppHealthState.Degraded,
            StateText = state,
            Owner = server.Name,
            Runtime = RuntimeText(server),
            DiscoveryEvidence = app.DiscoveryEvidence,
            Detail = runtimeProbe.Running == true
                ? $"{integrationName} runtime is active on the owning host."
                : isKometa && runtimeProbe.Found
                    ? "Kometa is installed but not currently running; this is normal for scheduled or one-shot execution."
                    : $"{integrationName} is installed but not running; its workflow may be unavailable.",
            RuntimeStateText = runtimeProbe.State,
            BuildText = runtimeProbe.Build,
            CpuText = runtimeProbe.Cpu,
            MemoryText = runtimeProbe.Memory,
            UptimeText = runtimeProbe.Uptime,
            ReadinessText = runtimeProbe.Running == true ? "Runtime ready" : isKometa && runtimeProbe.Found ? "Scheduled / idle" : "Runtime stopped",
            RuntimeDetail = runtimeProbe.Detail
        };
    }

    private async Task<RuntimeProbeResult> ProbeRuntimeAsync(
        string integrationName,
        ServerProfile server,
        CancellationToken cancellationToken)
    {
        if (server.ConnectionKind == HostConnectionKind.LocalWindows)
            return ProbeLocalWindowsRuntime(integrationName);
        if (server.ConnectionKind == HostConnectionKind.RemoteLinux)
            return await ProbeRemoteLinuxRuntimeAsync(integrationName, server, cancellationToken);
        if (server.ConnectionKind == HostConnectionKind.RemoteWindows)
            return await ProbeRemoteWindowsRuntimeAsync(integrationName, server, cancellationToken);
        return RuntimeProbeResult.Unknown("Runtime telemetry is not implemented for this host provider yet.");
    }

    private static RuntimeProbeResult ProbeLocalWindowsRuntime(string integrationName)
    {
        var tokens = RuntimeTokens(integrationName);
        if (tokens.Length == 0)
            return RuntimeProbeResult.Unknown("No process identity is defined for this integration.");

        var processes = Process.GetProcesses();
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    var processName = process.ProcessName;
                    if (!tokens.Any(token => processName.Contains(token, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    var build = processName;
                    try
                    {
                        var productVersion = process.MainModule?.FileVersionInfo.ProductVersion;
                        if (!string.IsNullOrWhiteSpace(productVersion))
                            build = productVersion;
                    }
                    catch { }

                    var uptime = "Running";
                    try
                    {
                        uptime = FormatDuration(DateTime.Now - process.StartTime);
                    }
                    catch { }

                    var memory = "--";
                    try
                    {
                        memory = FormatBytes(process.WorkingSet64);
                    }
                    catch { }

                    return new RuntimeProbeResult(
                        true,
                        true,
                        "Running",
                        build,
                        "--",
                        memory,
                        uptime,
                        $"Windows process {processName} (PID {process.Id})");
                }
                catch { }
            }
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }

        return RuntimeProbeResult.Unknown("No matching local process is currently visible.");
    }

    private async Task<RuntimeProbeResult> ProbeRemoteLinuxRuntimeAsync(
        string integrationName,
        ServerProfile server,
        CancellationToken cancellationToken)
    {
        var pattern = RuntimePattern(integrationName);
        if (string.IsNullOrWhiteSpace(pattern))
            return RuntimeProbeResult.Unknown("No Linux runtime identity is defined for this integration.");

        var containerFormat = ShellQuote("{{.Names}}|{{.Image}}|{{.Status}}");
        var containerNameFormat = ShellQuote("{{.Names}}");
        var statsFormat = ShellQuote("{{.CPUPerc}}|{{.MemUsage}}");
        var restartFormat = ShellQuote("{{.HostConfig.RestartPolicy.Name}}");
        var command =
            $"pattern={ShellQuote(pattern)}; " +
            $"line=$(docker ps -a --format {containerFormat} 2>/dev/null | grep -Ei -m1 \"$pattern\" || true); " +
            "if [ -n \"$line\" ]; then " +
            "name=$(printf '%s' \"$line\" | cut -d'|' -f1); image=$(printf '%s' \"$line\" | cut -d'|' -f2); status=$(printf '%s' \"$line\" | cut -d'|' -f3-); " +
            $"if docker ps --format {containerNameFormat} 2>/dev/null | grep -Fxq \"$name\"; then stats=$(docker stats --no-stream --format {statsFormat} \"$name\" 2>/dev/null | head -n1); [ -n \"$stats\" ] || stats='--|--'; else stats='--|--'; fi; " +
            $"restart=$(docker inspect -f {restartFormat} \"$name\" 2>/dev/null || true); " +
            "printf 'CONTAINER|%s|%s|%s|%s|%s\\n' \"$name\" \"$image\" \"$status\" \"$stats\" \"$restart\"; " +
            "else line=$(ps -eo pid=,etimes=,comm=,args= 2>/dev/null | grep -Ei \"$pattern\" | grep -Eiv 'grep|graveops' | head -n1 || true); " +
            "if [ -n \"$line\" ]; then set -- $line; printf 'PROCESS|%s|%s|%s\\n' \"$1\" \"$2\" \"$3\"; else printf 'NONE\\n'; fi; fi";

        var execution = await _services.Ssh.ExecuteAsync(server, command, 30, cancellationToken);
        var line = execution.StdOut.Replace("\r", "", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.Trim() ?? "";

        if (line.StartsWith("CONTAINER|", StringComparison.Ordinal))
        {
            var parts = line.Split('|');
            if (parts.Length >= 7)
            {
                var name = parts[1];
                var image = parts[2];
                var status = parts[3];
                var cpu = parts[4];
                var memory = parts[5];
                var restart = parts[6];
                var running = status.StartsWith("Up ", StringComparison.OrdinalIgnoreCase) || status.Equals("Up", StringComparison.OrdinalIgnoreCase);
                return new RuntimeProbeResult(
                    true,
                    running,
                    running ? "Running" : "Stopped",
                    string.IsNullOrWhiteSpace(image) ? "--" : image,
                    string.IsNullOrWhiteSpace(cpu) ? "--" : cpu,
                    string.IsNullOrWhiteSpace(memory) ? "--" : memory,
                    string.IsNullOrWhiteSpace(status) ? "--" : status,
                    $"Docker container {name} | restart policy {(string.IsNullOrWhiteSpace(restart) ? "unknown" : restart)}");
            }
        }

        if (line.StartsWith("PROCESS|", StringComparison.Ordinal))
        {
            var parts = line.Split('|');
            if (parts.Length >= 4 && long.TryParse(parts[2], out var seconds))
            {
                return new RuntimeProbeResult(
                    true,
                    true,
                    "Running",
                    parts[3],
                    "--",
                    "--",
                    FormatDuration(TimeSpan.FromSeconds(seconds)),
                    $"Linux process {parts[3]} (PID {parts[1]})");
            }
        }

        return RuntimeProbeResult.Unknown("No matching running process or Docker container was identified in the current sample.");
    }


    private async Task<RuntimeProbeResult> ProbeRemoteWindowsRuntimeAsync(
        string integrationName,
        ServerProfile server,
        CancellationToken cancellationToken)
    {
        var tokens = RuntimeTokens(integrationName);
        if (tokens.Length == 0)
            return RuntimeProbeResult.Unknown("No Windows runtime identity is defined for this integration.");

        var tokenLiteral = string.Join(",", tokens.Select(x => "'" + x.Replace("'", "''", StringComparison.Ordinal) + "'"));
        var script = $@"
$tokens = @({tokenLiteral})
$container = $null
if (Get-Command docker -ErrorAction SilentlyContinue) {{
  $container = docker ps -a --format '{{{{.Names}}}}|{{{{.Image}}}}|{{{{.Status}}}}' 2>$null |
    Where-Object {{
      $line = $_.ToLowerInvariant()
      @($tokens | Where-Object {{ $line.Contains($_.ToLowerInvariant()) }}).Count -gt 0
    }} | Select-Object -First 1
}}
if ($container) {{
  $parts = $container -split '\|',3
  $name = $parts[0]; $image = $parts[1]; $status = $parts[2]
  $running = docker ps --format '{{{{.Names}}}}' 2>$null | Where-Object {{ $_ -eq $name }}
  $stats = '--|--'
  if ($running) {{
    $sample = docker stats --no-stream --format '{{{{.CPUPerc}}}}|{{{{.MemUsage}}}}' $name 2>$null | Select-Object -First 1
    if ($sample) {{ $stats = $sample }}
  }}
  $restart = docker inspect -f '{{{{.HostConfig.RestartPolicy.Name}}}}' $name 2>$null
  Write-Output ('CONTAINER|' + $name + '|' + $image + '|' + $status + '|' + $stats + '|' + $restart)
  return
}}
$p = Get-Process -ErrorAction SilentlyContinue | Where-Object {{
  $n = $_.ProcessName.ToLowerInvariant()
  @($tokens | Where-Object {{ $n.Contains($_.ToLowerInvariant()) }}).Count -gt 0
}} | Select-Object -First 1
if ($p) {{
  $age = 0
  try {{ $age = [int64]((Get-Date) - $p.StartTime).TotalSeconds }} catch {{}}
  $mem = 0
  try {{ $mem = [int64]$p.WorkingSet64 }} catch {{}}
  Write-Output ('PROCESS|' + $p.Id + '|' + $age + '|' + $p.ProcessName + '|' + $mem)
}} else {{
  Write-Output 'NONE'
}}
";
        string output;
        try
        {
            output = await _services.PowerShellRemote.ExecuteAsync(server, script, 35, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return RuntimeProbeResult.Unknown("Remote Windows runtime probe failed: " + ex.Message);
        }

        var line = output.Replace("\r", "", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.Trim() ?? "";

        if (line.StartsWith("CONTAINER|", StringComparison.Ordinal))
        {
            var parts = line.Split('|');
            if (parts.Length >= 7)
            {
                var name = parts[1];
                var image = parts[2];
                var status = parts[3];
                var cpu = parts[4];
                var memory = parts[5];
                var restart = parts[6];
                var running = status.StartsWith("Up ", StringComparison.OrdinalIgnoreCase) || status.Equals("Up", StringComparison.OrdinalIgnoreCase);
                return new RuntimeProbeResult(
                    true,
                    running,
                    running ? "Running" : "Stopped",
                    string.IsNullOrWhiteSpace(image) ? "--" : image,
                    string.IsNullOrWhiteSpace(cpu) ? "--" : cpu,
                    string.IsNullOrWhiteSpace(memory) ? "--" : memory,
                    string.IsNullOrWhiteSpace(status) ? "--" : status,
                    $"Remote Docker container {name} | restart policy {(string.IsNullOrWhiteSpace(restart) ? "unknown" : restart)}");
            }
        }

        if (line.StartsWith("PROCESS|", StringComparison.Ordinal))
        {
            var parts = line.Split('|');
            if (parts.Length >= 5 && long.TryParse(parts[2], out var seconds))
            {
                var memory = long.TryParse(parts[4], out var bytes) ? FormatBytes(bytes) : "--";
                return new RuntimeProbeResult(
                    true,
                    true,
                    "Running",
                    parts[3],
                    "--",
                    memory,
                    FormatDuration(TimeSpan.FromSeconds(seconds)),
                    $"Remote Windows process {parts[3]} (PID {parts[1]})");
            }
        }

        return RuntimeProbeResult.Unknown("No matching remote Windows process or Docker container was identified.");
    }

    private async Task<string> ProbeReadinessAsync(
        string integrationName,
        ServerProfile server,
        string endpoint,
        int rootStatus,
        CancellationToken cancellationToken)
    {
        if (rootStatus is 401 or 403)
            return "Protected";

        string? path = integrationName.ToLowerInvariant() switch
        {
            "autobrr" => "/api/healthz/readiness",
            "seerr" => "/api/v1/settings/public",
            "maintainerr" => "/api/health/ready",
            _ => null
        };

        if (path is null)
            return rootStatus is >= 200 and < 400 ? "Ready" : rootStatus < 500 ? "Reachable" : "Attention";

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var baseUri))
            return "Unknown";

        var builder = new UriBuilder(baseUri) { Path = path, Query = "" };
        var readinessEndpoint = builder.Uri.ToString();
        var readinessProbe = server.ConnectionKind switch
        {
            HostConnectionKind.LocalWindows => await ProbeLocalHttpAsync(readinessEndpoint, cancellationToken),
            HostConnectionKind.RemoteLinux => await ProbeRemoteLinuxHttpAsync(server, readinessEndpoint, cancellationToken),
            HostConnectionKind.RemoteWindows => await ProbeDirectHttpAsync(readinessEndpoint, cancellationToken),
            _ => null
        };

        return readinessProbe?.StatusCode switch
        {
            200 => "Ready",
            401 or 403 => "Protected",
            >= 500 => "Dependency issue",
            > 0 => $"HTTP {readinessProbe.StatusCode}",
            _ => "Unknown"
        };
    }

    private async Task<HttpProbeResult?> ProbeRemoteLinuxHttpAsync(
        ServerProfile server,
        string endpoint,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            return null;

        var localUri = new UriBuilder(uri) { Host = "127.0.0.1" }.Uri.ToString();
        var command =
            "if ! command -v curl >/dev/null 2>&1; then exit 127; fi; " +
            $"curl -k -sS -o /dev/null --connect-timeout 1 --max-time 5 -w '%{{http_code}}|%{{time_total}}' {ShellQuote(localUri)}";
        var execution = await _services.Ssh.ExecuteAsync(server, command, 14, cancellationToken);
        var text = execution.StdOut.Trim();
        var parts = text.Split(new[] { '|' }, 2, StringSplitOptions.None);
        if (parts.Length != 2 || !int.TryParse(parts[0], out var code) || code <= 0)
            return null;

        var latencyMs = 0L;
        if (double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds))
            latencyMs = Math.Max(0, (long)Math.Round(seconds * 1000));
        return new HttpProbeResult(code, latencyMs);
    }

    private static async Task<HttpProbeResult?> ProbeLocalHttpAsync(string endpoint, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            return null;

        var localUri = new UriBuilder(uri) { Host = "127.0.0.1" }.Uri;
        using var request = new HttpRequestMessage(HttpMethod.Get, localUri);
        var stopwatch = Stopwatch.StartNew();
        using var response = await LocalClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        stopwatch.Stop();
        return new HttpProbeResult((int)response.StatusCode, stopwatch.ElapsedMilliseconds);
    }

    private static async Task<HttpProbeResult?> ProbeDirectHttpAsync(string endpoint, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            return null;

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        var stopwatch = Stopwatch.StartNew();
        using var response = await LocalClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        stopwatch.Stop();
        return new HttpProbeResult((int)response.StatusCode, stopwatch.ElapsedMilliseconds);
    }

    public static string ResolveEndpoint(ManagedApp app, ServerProfile server)
    {
        if (string.IsNullOrWhiteSpace(app.Url))
            return "";
        var host = server.ConnectionKind == HostConnectionKind.LocalWindows
            ? "127.0.0.1"
            : server.Host;
        return app.Url.Replace("{host}", host, StringComparison.OrdinalIgnoreCase);
    }

    private static string RuntimeText(ServerProfile server) => server.ConnectionKind switch
    {
        HostConnectionKind.LocalWindows => "native Windows",
        HostConnectionKind.RemoteLinux => "Linux host-local probe over SSH",
        HostConnectionKind.LocalLinux => "native Linux",
        HostConnectionKind.RemoteWindows => "remote Windows",
        _ => "host provider"
    };

    private static string[] RuntimeTokens(string integrationName) => integrationName.ToLowerInvariant() switch
    {
        "tautulli" => ["tautulli"],
        "kometa" => ["kometa", "plex-meta-manager"],
        "bazarr" => ["bazarr"],
        "seerr" => ["seerr", "overseerr", "jellyseerr"],
        "recyclarr" => ["recyclarr"],
        "profilarr" => ["profilarr"],
        "autobrr" => ["autobrr"],
        "unpackerr" => ["unpackerr"],
        "cleanuparr" => ["cleanuparr"],
        "tdarr" => ["tdarr"],
        "maintainerr" => ["maintainerr"],
        _ => [integrationName]
    };

    private static string RuntimePattern(string integrationName) =>
        string.Join("|", RuntimeTokens(integrationName).Select(Regex.Escape));

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024L * 1024) return $"{bytes / 1024d:0.0} KiB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024d / 1024d:0.0} MiB";
        return $"{bytes / 1024d / 1024d / 1024d:0.0} GiB";
    }

    private static string FormatDuration(TimeSpan value)
    {
        if (value.TotalDays >= 1) return $"{(int)value.TotalDays}d {value.Hours}h";
        if (value.TotalHours >= 1) return $"{(int)value.TotalHours}h {value.Minutes}m";
        if (value.TotalMinutes >= 1) return $"{(int)value.TotalMinutes}m {value.Seconds}s";
        return $"{Math.Max(0, (int)value.TotalSeconds)}s";
    }

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static string? FindExecutableOnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var segment in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(segment.Trim(), name);
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }
        return null;
    }
}
