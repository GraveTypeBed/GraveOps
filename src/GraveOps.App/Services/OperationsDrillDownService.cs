using System.Diagnostics;
using GraveOps.App.Models;

namespace GraveOps.App.Services;

public sealed class OperationsDrillDownService
{
    private readonly AppServices _services;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OperationsDrillDownService(AppServices services) => _services = services;

    public Task<List<DockerDrillRow>> GetDockerAsync(
        ServerProfile server,
        CancellationToken token = default)
        => server.ConnectionKind switch
        {
            HostConnectionKind.RemoteWindows => RunWindowsJsonAsync<List<DockerDrillRow>>(server, WindowsDockerProbe, 60, token),
            HostConnectionKind.LocalWindows => RunLocalWindowsJsonAsync<List<DockerDrillRow>>(WindowsDockerProbe, 60, token),
            _ => RunPythonJsonAsync<List<DockerDrillRow>>(server, DockerProbe, 60, token)
        };

    public Task<List<StorageDrillRow>> GetStorageAsync(
        ServerProfile server,
        CancellationToken token = default)
        => server.ConnectionKind switch
        {
            HostConnectionKind.RemoteWindows => RunWindowsJsonAsync<List<StorageDrillRow>>(server, WindowsStorageProbe, 60, token),
            HostConnectionKind.LocalWindows => RunLocalWindowsJsonAsync<List<StorageDrillRow>>(WindowsStorageProbe, 60, token),
            _ => RunPythonJsonAsync<List<StorageDrillRow>>(server, StorageProbe, 60, token)
        };

    public Task<List<QueueDrillRow>> GetQueuesAsync(
        ServerProfile server,
        CancellationToken token = default)
        => GetQueuesAsync(server, Array.Empty<string>(), token);

    public Task<List<QueueDrillRow>> GetQueuesAsync(
        ServerProfile server,
        IReadOnlyCollection<string> serviceNames,
        CancellationToken token = default)
    {
        if (server.ConnectionKind is HostConnectionKind.RemoteWindows or HostConnectionKind.LocalWindows)
            return Task.FromResult(new List<QueueDrillRow>());

        var scope = serviceNames
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var scopeJson = JsonSerializer.Serialize(scope);
        var scope64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(scopeJson));
        var script = QueueProbe.Replace(
            "__SCOPESPEC__",
            scope64,
            StringComparison.Ordinal);

        return RunPythonJsonAsync<List<QueueDrillRow>>(server, script, 90, token);
    }

    public async Task<string> GetDockerDetailAsync(
        ServerProfile server,
        string container,
        CancellationToken token = default)
    {
        if (server.ConnectionKind is HostConnectionKind.RemoteWindows or HostConnectionKind.LocalWindows)
        {
            var qps = PowerShellLiteral(container);
            var script =
                "$name=" + qps + "; " +
                "'CONTAINER'; docker inspect $name 2>&1; " +
                "''; 'PORTS'; docker port $name 2>&1; " +
                "''; 'TOP'; docker top $name 2>&1; " +
                "''; 'RECENT LOGS'; docker logs --tail 120 --timestamps $name 2>&1";
            return server.ConnectionKind == HostConnectionKind.RemoteWindows
                ? await _services.PowerShellRemote.ExecuteAsync(server, script, 90, token)
                : await RunLocalPowerShellTextAsync(script, 90, token);
        }

        var q = ShellQuote(container);
        var command =
            "echo 'CONTAINER'; " +
            "docker inspect --format 'Name={{.Name}}\nImage={{.Config.Image}}\nStatus={{.State.Status}}\n" +
            "Health={{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}\n" +
            "RestartCount={{.RestartCount}}\nStartedAt={{.State.StartedAt}}\nFinishedAt={{.State.FinishedAt}}\n" +
            "ExitCode={{.State.ExitCode}}\nError={{.State.Error}}' " + q + " 2>&1; " +
            "echo; echo 'PORTS'; docker port " + q + " 2>&1 || true; " +
            "echo; echo 'MOUNTS'; docker inspect --format '{{range .Mounts}}{{.Source}} -> {{.Destination}} ({{.Mode}}){{printf \"\\n\"}}{{end}}' " + q + " 2>&1; " +
            "echo; echo 'TOP'; docker top " + q + " 2>&1 || true; " +
            "echo; echo 'RECENT LOGS'; docker logs --tail 120 --timestamps " + q + " 2>&1 || true";

        var result = await _services.Ssh.ExecuteAsync(server, command, 90, token);
        return result.Combined.Trim();
    }

    public async Task<string> GetStorageDetailAsync(
        ServerProfile server,
        string target,
        CancellationToken token = default)
    {
        if (server.ConnectionKind is HostConnectionKind.RemoteWindows or HostConnectionKind.LocalWindows)
        {
            var drive = PowerShellLiteral(target.TrimEnd('\\'));
            var script = "$drive=" + drive + "; " +
                "$d=Get-CimInstance Win32_LogicalDisk -Filter (\"DeviceID='\"+$drive+\"'\") -ErrorAction SilentlyContinue; " +
                "'VOLUME'; $d | Select-Object DeviceID,VolumeName,FileSystem,Size,FreeSpace | Format-List | Out-String; " +
                "'PHYSICAL DISKS'; Get-PhysicalDisk -ErrorAction SilentlyContinue | Select-Object FriendlyName,SerialNumber,MediaType,HealthStatus,OperationalStatus,Size | Format-Table -AutoSize | Out-String";
            return server.ConnectionKind == HostConnectionKind.RemoteWindows
                ? await _services.PowerShellRemote.ExecuteAsync(server, script, 60, token)
                : await RunLocalPowerShellTextAsync(script, 60, token);
        }
        var q = ShellQuote(target);
        var command =
            "echo 'MOUNT'; findmnt -T " + q + " -o TARGET,SOURCE,FSTYPE,OPTIONS,PROPAGATION 2>&1; " +
            "echo; echo 'USAGE'; df -hT " + q + " 2>&1; " +
            "echo; echo 'BLOCK DEVICE'; " +
            "src=$(findmnt -T " + q + " -n -o SOURCE 2>/dev/null | sed 's/\\[.*//'); " +
            "real=$(readlink -f \"$src\" 2>/dev/null || true); " +
            "if test -n \"$real\"; then lsblk -o NAME,PATH,PKNAME,TYPE,SIZE,FSTYPE,MOUNTPOINTS,MODEL,SERIAL \"$real\" 2>&1 || true; fi; " +
            "echo; echo 'SMART'; " +
            "if test -n \"$real\"; then parent=$(lsblk -no PKNAME \"$real\" 2>/dev/null | head -1); " +
            "dev=\"$real\"; if test -n \"$parent\"; then dev=\"/dev/$parent\"; fi; " +
            "if sudo -n smartctl -H \"$dev\" >/dev/null 2>&1; then sudo -n smartctl -H -A \"$dev\" 2>&1 | head -n 140; " +
            "else echo 'Direct SMART elevation is unavailable for this device.'; fi; fi";

        var result = await _services.Ssh.ExecuteAsync(server, command, 90, token);
        return result.Combined.Trim();
    }

    public async Task<string> VerifyStorageAsync(
        ServerProfile server,
        string target,
        CancellationToken token = default)
    {
        if (server.ConnectionKind is HostConnectionKind.RemoteWindows or HostConnectionKind.LocalWindows)
        {
            var drive = PowerShellLiteral(target.TrimEnd('\\'));
            var script = "$drive=" + drive + "; if (Test-Path ($drive+'\\')) { 'MOUNT=PASS'; try { Get-ChildItem ($drive+'\\') -ErrorAction Stop | Select-Object -First 1 | Out-Null; 'READ=PASS' } catch { 'READ=FAIL' } } else { 'MOUNT=FAIL'; 'READ=FAIL' }";
            var text = server.ConnectionKind == HostConnectionKind.RemoteWindows
                ? await _services.PowerShellRemote.ExecuteAsync(server, script, 30, token)
                : await RunLocalPowerShellTextAsync(script, 30, token);
            _services.Activity.Record("Storage target verified", $"{target}\n{text.Trim()}", ActivityLevel.Info, serverId: server.Id, deepLink: "page:Storage");
            return text.Trim();
        }
        var q = ShellQuote(target);
        var command =
            "target=" + q + "; " +
            "if findmnt -T \"$target\" >/dev/null 2>&1; then echo 'MOUNT=PASS'; else echo 'MOUNT=FAIL'; fi; " +
            "if test -r \"$target\"; then echo 'READ=PASS'; else echo 'READ=FAIL'; fi; " +
            "if test -w \"$target\"; then echo 'WRITE=PASS'; else echo 'WRITE=FAIL'; fi; " +
            "findmnt -T \"$target\" -n -o TARGET,SOURCE,FSTYPE,OPTIONS 2>/dev/null || true; " +
            "df -hT \"$target\" 2>/dev/null | tail -1 || true";

        var result = await _services.Ssh.ExecuteAsync(server, command, 30, token);
        _services.Activity.Record(
            "Storage target verified",
            $"{target}\n{result.Combined.Trim()}",
            result.ExitCode == 0 ? ActivityLevel.Info : ActivityLevel.Error,
            serverId: server.Id,
            deepLink: "page:Storage");
        return result.Combined.Trim();
    }

    public async Task<ActionRunResult> RunDockerOperationAsync(
        ServerProfile server,
        string container,
        string operation,
        CancellationToken token = default)
    {
        operation = operation.Trim().ToLowerInvariant();
        if (operation is not ("start" or "restart" or "stop"))
            throw new ArgumentOutOfRangeException(nameof(operation));

        if (server.ConnectionKind is HostConnectionKind.RemoteWindows or HostConnectionKind.LocalWindows)
            return await RunWindowsDockerOperationAsync(server, container, operation, token);

        if (_services.Config.Current.Settings.SafeMode)
        {
            const string blocked = "Safe Mode blocks Docker container mutations.";
            _services.Activity.Record(
                "Safe Mode blocked Docker operation",
                $"{operation} {container}: {blocked}",
                ActivityLevel.Warning,
                serverId: server.Id,
                deepLink: "page:Docker");
            return new ActionRunResult(false, -3, "", blocked, blocked, TimeSpan.Zero);
        }

        var watch = Stopwatch.StartNew();
        var title = $"{char.ToUpperInvariant(operation[0])}{operation[1..]} container: {container}";
        var job = _services.Jobs.Begin(title, server.Id, "page:Docker");
        var q = ShellQuote(container);

        try
        {
            _services.Jobs.Update(job, GraveJobState.Running, "Capturing current container state...");
            var before = await _services.Ssh.ExecuteAsync(
                server,
                "docker inspect -f '{{.State.Status}}|{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}|{{.RestartCount}}' " + q + " 2>&1",
                20,
                token);

            var mutation = operation switch
            {
                "start" => "docker start " + q,
                "restart" => "docker restart --time 20 " + q,
                "stop" => "docker stop --time 20 " + q,
                _ => throw new InvalidOperationException()
            };

            _services.Jobs.Update(job, GraveJobState.Running, $"Running Docker {operation}...");
            var command = await _services.Ssh.ExecuteAsync(server, mutation + " 2>&1", 90, token);
            if (command.ExitCode != 0)
            {
                watch.Stop();
                var failure = string.IsNullOrWhiteSpace(command.StdErr) ? command.Combined : command.StdErr;
                _services.Jobs.Update(job, GraveJobState.Failed, failure);
                _services.Activity.Record(
                    "Docker operation failed",
                    $"{operation} {container}\n{failure}",
                    ActivityLevel.Error,
                    watch.Elapsed.TotalSeconds,
                    server.Id,
                    "page:Docker");
                return new ActionRunResult(false, command.ExitCode, command.StdOut, command.StdErr, failure, watch.Elapsed);
            }

            _services.Jobs.Update(job, GraveJobState.Running, "Command completed; verifying container state...");

            string after = "";
            var verified = false;
            for (var i = 0; i < 30; i++)
            {
                token.ThrowIfCancellationRequested();
                var check = await _services.Ssh.ExecuteAsync(
                    server,
                    "docker inspect -f '{{.State.Status}}|{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}|{{.RestartCount}}' " + q + " 2>&1",
                    20,
                    token);

                after = check.StdOut.Trim();
                var parts = after.Split('|');
                var state = parts.ElementAtOrDefault(0) ?? "";
                var health = parts.ElementAtOrDefault(1) ?? "none";

                if (operation == "stop")
                {
                    verified = state.Equals("exited", StringComparison.OrdinalIgnoreCase) ||
                               state.Equals("dead", StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    verified = state.Equals("running", StringComparison.OrdinalIgnoreCase) &&
                               !health.Equals("starting", StringComparison.OrdinalIgnoreCase) &&
                               !health.Equals("unhealthy", StringComparison.OrdinalIgnoreCase);
                }

                if (verified) break;
                if (health.Equals("unhealthy", StringComparison.OrdinalIgnoreCase)) break;
                await Task.Delay(1000, token);
            }

            watch.Stop();
            var verification =
                $"Before: {before.StdOut.Trim()}\nAfter: {after}\n" +
                (verified ? "Verified intended Docker state." : "Docker command completed, but intended state was not verified.");

            _services.Jobs.Update(
                job,
                verified ? GraveJobState.Success : GraveJobState.Failed,
                verification,
                verified ? 100 : null);

            _services.Activity.Record(
                verified ? "Docker operation verified" : "Docker verification failed",
                $"{operation} {container}\n{verification}",
                verified ? ActivityLevel.Success : ActivityLevel.Error,
                watch.Elapsed.TotalSeconds,
                server.Id,
                "page:Docker");

            return new ActionRunResult(
                verified,
                verified ? 0 : -4,
                command.StdOut,
                command.StdErr,
                verification,
                watch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            watch.Stop();
            _services.Jobs.Update(job, GraveJobState.Cancelled, "Cancelled");
            throw;
        }
        catch (Exception ex)
        {
            watch.Stop();
            _services.Jobs.Update(job, GraveJobState.Failed, ex.Message);
            _services.Activity.Record(
                "Docker operation failed",
                $"{operation} {container}\n{ex.Message}",
                ActivityLevel.Error,
                watch.Elapsed.TotalSeconds,
                server.Id,
                "page:Docker");
            return new ActionRunResult(false, -1, "", ex.Message, "", watch.Elapsed);
        }
    }

    private async Task<ActionRunResult> RunWindowsDockerOperationAsync(
        ServerProfile server,
        string container,
        string operation,
        CancellationToken token)
    {
        if (_services.Config.Current.Settings.SafeMode)
            return new ActionRunResult(false, -3, "", "Safe Mode blocks Docker container mutations.", "Safe Mode blocks Docker container mutations.", TimeSpan.Zero);

        var watch = Stopwatch.StartNew();
        var title = $"{char.ToUpperInvariant(operation[0])}{operation[1..]} container: {container}";
        var job = _services.Jobs.Begin(title, server.Id, "page:Docker");
        try
        {
            var q = PowerShellLiteral(container);
            var beforeScript = "$name=" + q + "; docker inspect -f '{{.State.Status}}|{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}|{{.RestartCount}}' $name";
            var before = server.ConnectionKind == HostConnectionKind.RemoteWindows
                ? await _services.PowerShellRemote.ExecuteAsync(server, beforeScript, 20, token)
                : await RunLocalPowerShellTextAsync(beforeScript, 20, token);

            var mutation = operation switch
            {
                "start" => "$name=" + q + "; docker start $name",
                "restart" => "$name=" + q + "; docker restart --time 20 $name",
                "stop" => "$name=" + q + "; docker stop --time 20 $name",
                _ => throw new InvalidOperationException()
            };
            _services.Jobs.Update(job, GraveJobState.Running, $"Running Docker {operation}...");
            var output = server.ConnectionKind == HostConnectionKind.RemoteWindows
                ? await _services.PowerShellRemote.ExecuteAsync(server, mutation, 90, token)
                : await RunLocalPowerShellTextAsync(mutation, 90, token);

            string after = "";
            var verified = false;
            for (var i = 0; i < 30; i++)
            {
                token.ThrowIfCancellationRequested();
                after = server.ConnectionKind == HostConnectionKind.RemoteWindows
                    ? await _services.PowerShellRemote.ExecuteAsync(server, beforeScript, 20, token)
                    : await RunLocalPowerShellTextAsync(beforeScript, 20, token);
                var parts = after.Trim().Split('|');
                var state = parts.ElementAtOrDefault(0) ?? "";
                var health = parts.ElementAtOrDefault(1) ?? "none";
                verified = operation == "stop"
                    ? state.Equals("exited", StringComparison.OrdinalIgnoreCase) || state.Equals("dead", StringComparison.OrdinalIgnoreCase)
                    : state.Equals("running", StringComparison.OrdinalIgnoreCase) && !health.Equals("starting", StringComparison.OrdinalIgnoreCase) && !health.Equals("unhealthy", StringComparison.OrdinalIgnoreCase);
                if (verified || health.Equals("unhealthy", StringComparison.OrdinalIgnoreCase)) break;
                await Task.Delay(1000, token);
            }

            watch.Stop();
            var verification = $"Before: {before.Trim()}\nAfter: {after.Trim()}\n" + (verified ? "Verified intended Docker state." : "Docker command completed, but intended state was not verified.");
            _services.Jobs.Update(job, verified ? GraveJobState.Success : GraveJobState.Failed, verification, verified ? 100 : null);
            _services.Activity.Record(verified ? "Docker operation verified" : "Docker verification failed", $"{operation} {container}\n{verification}", verified ? ActivityLevel.Success : ActivityLevel.Error, watch.Elapsed.TotalSeconds, server.Id, "page:Docker");
            return new ActionRunResult(verified, verified ? 0 : -4, output, "", verification, watch.Elapsed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            watch.Stop();
            _services.Jobs.Update(job, GraveJobState.Failed, ex.Message);
            return new ActionRunResult(false, -1, "", ex.Message, ex.Message, watch.Elapsed);
        }
    }

    private async Task<T> RunWindowsJsonAsync<T>(ServerProfile server, string script, int timeout, CancellationToken token) where T : class
    {
        var text = await _services.PowerShellRemote.ExecuteAsync(server, script, timeout, token);
        return DeserializeJson<T>(text);
    }

    private async Task<T> RunLocalWindowsJsonAsync<T>(string script, int timeout, CancellationToken token) where T : class
    {
        var text = await RunLocalPowerShellTextAsync(script, timeout, token);
        return DeserializeJson<T>(text);
    }

    private static T DeserializeJson<T>(string text) where T : class
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Provider probe returned no JSON.");
        var value = JsonSerializer.Deserialize<T>(text.Trim(), JsonOptions);
        return value ?? throw new InvalidOperationException("Provider probe returned invalid JSON.");
    }

    private static async Task<string> RunLocalPowerShellTextAsync(string script, int timeoutSeconds, CancellationToken token)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command -",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var process = new Process { StartInfo = psi };
        process.Start();
        await process.StandardInput.WriteAsync(script);
        process.StandardInput.Close();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 5, 180)));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        var outText = await stdout;
        var errText = await stderr;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(errText) ? outText : errText);
        return outText.Trim();
    }

    private static string PowerShellLiteral(string value) => "'" + (value ?? "").Replace("'", "''") + "'";

    private async Task<T> RunPythonJsonAsync<T>(
        ServerProfile server,
        string script,
        int timeout,
        CancellationToken token)
        where T : class
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(script));
        var command = $"python3 -c \"import base64;exec(base64.b64decode('{encoded}'))\"";
        var result = await _services.Ssh.ExecuteAsync(server, command, timeout, token);

        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(result.StdErr) ? result.Combined : result.StdErr);

        var value = JsonSerializer.Deserialize<T>(result.StdOut.Trim(), JsonOptions);
        return value ?? throw new InvalidOperationException("Drill-down probe returned invalid JSON.");
    }

    private static string ShellQuote(string value)
        => "'" + (value ?? "").Replace("'", "'\"'\"'") + "'";

    private const string WindowsDockerProbe = """
$ErrorActionPreference='SilentlyContinue'
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { '[]'; return }
$stats=@{}
docker stats --no-stream --format '{{json .}}' 2>$null | ForEach-Object {
    try { $s=$_ | ConvertFrom-Json; $stats[$s.Name]=$s } catch {}
}
$rows=@()
docker ps -aq 2>$null | ForEach-Object {
    try {
        $c=(docker inspect $_ 2>$null | ConvertFrom-Json)[0]
        if ($null -eq $c) { return }
        $name=([string]$c.Name).TrimStart('/')
        $st=$stats[$name]
        $health='none'; if ($null -ne $c.State.Health) { $health=[string]$c.State.Health.Status }
        $rows += [pscustomobject]@{
            name=$name; image=[string]$c.Config.Image; state=[string]$c.State.Status; health=$health;
            restarts=[int]$c.RestartCount; cpu=$(if($st){[string]$st.CPUPerc}else{'--'});
            memory=$(if($st){[string]$st.MemUsage}else{'--'}); pids=$(if($st){[string]$st.PIDs}else{'--'});
            started=[string]$c.State.StartedAt
        }
    } catch {}
}
@($rows | Sort-Object name) | ConvertTo-Json -Compress -Depth 5
""";

    private const string WindowsStorageProbe = """
$rows=@()
Get-CimInstance Win32_LogicalDisk -Filter "DriveType=3" -ErrorAction SilentlyContinue | ForEach-Object {
    $size=[int64]($_.Size); $free=[int64]($_.FreeSpace); $used=[math]::Max(0,$size-$free)
    $rows += [pscustomobject]@{
        target=[string]$_.DeviceID; source=[string]$_.VolumeName; fileSystem=[string]$_.FileSystem;
        options='Local fixed disk'; size=$size; used=[int64]$used; available=$free; writable=$true
    }
}
@($rows | Sort-Object target) | ConvertTo-Json -Compress -Depth 4
""";

    private const string DockerProbe = """
import json, subprocess

def run(args):
    try:
        p=subprocess.run(args, capture_output=True, text=True, timeout=30)
        return p.returncode, p.stdout, p.stderr
    except Exception as e:
        return 1, "", type(e).__name__ + ": " + str(e)

rc,out,err=run(["docker","ps","-aq"])
ids=[x.strip() for x in out.splitlines() if x.strip()]
if not ids:
    print("[]")
    raise SystemExit(0)

rc,inspect_out,err=run(["docker","inspect"] + ids)
items=json.loads(inspect_out) if rc==0 and inspect_out.strip() else []

stats={}
rc,stats_out,err=run(["docker","stats","--no-stream","--format","{{json .}}"])
if rc==0:
    for line in stats_out.splitlines():
        try:
            row=json.loads(line)
            stats[row.get("Name","")]=row
        except Exception:
            pass

rows=[]
for c in items:
    name=(c.get("Name") or "").lstrip("/")
    state=c.get("State") or {}
    health=(state.get("Health") or {}).get("Status","none")
    st=stats.get(name,{})
    rows.append({
        "name":name,
        "image":(c.get("Config") or {}).get("Image",""),
        "state":state.get("Status","unknown"),
        "health":health,
        "restarts":int(c.get("RestartCount") or 0),
        "cpu":str(st.get("CPUPerc","--")),
        "memory":str(st.get("MemUsage","--")),
        "pids":str(st.get("PIDs","--")),
        "started":str(state.get("StartedAt",""))
    })

rows.sort(key=lambda x:x["name"].lower())
print(json.dumps(rows))
""";

    private const string StorageProbe = """
import json, os, shutil, subprocess

try:
    p=subprocess.run(
        ["findmnt","-J","-o","TARGET,SOURCE,FSTYPE,OPTIONS"],
        capture_output=True, text=True, timeout=20)
    data=json.loads(p.stdout) if p.returncode==0 and p.stdout.strip() else {}
except Exception:
    data={}

def walk(nodes):
    for fs in nodes or []:
        if not isinstance(fs,dict):
            continue
        yield fs
        for child in walk(fs.get("children") or []):
            yield child

rows=[]
seen=set()

for fs in walk(data.get("filesystems") or []):
    target=str(fs.get("target") or "")
    if not (target=="/" or target.startswith("/mnt/")):
        continue
    if target in seen:
        continue
    seen.add(target)

    try:
        usage=shutil.disk_usage(target)
        size=int(usage.total)
        used=int(usage.used)
        avail=int(usage.free)
    except Exception:
        size=used=avail=0

    rows.append({
        "target":target,
        "source":str(fs.get("source") or ""),
        "fileSystem":str(fs.get("fstype") or ""),
        "options":str(fs.get("options") or ""),
        "size":size,
        "used":used,
        "available":avail,
        "writable":bool(os.access(target, os.W_OK))
    })

rows.sort(key=lambda x:(x["target"]!="/", x["target"].lower()))
print(json.dumps(rows))
""";

    private const string QueueProbe = """
import base64, glob, json, os, re, subprocess, urllib.error, urllib.parse, urllib.request
import xml.etree.ElementTree as ET

SCOPE=set(x.lower() for x in json.loads(base64.b64decode("__SCOPESPEC__").decode("utf-8")))
rows=[]

def want(name):
    return not SCOPE or str(name or "").lower() in SCOPE

def add(service, kind, title, state, progress="", remaining="", detail=""):
    title=str(title or "")
    detail=str(detail or "")
    if detail.strip()==title.strip():
        detail=""
    rows.append({
        "service":str(service or ""),
        "kind":str(kind or ""),
        "title":title,
        "state":str(state or ""),
        "progress":str(progress or ""),
        "remaining":str(remaining or ""),
        "detail":detail
    })

def api_key(path):
    try:
        root=ET.parse(path).getroot()
        node=root.find("ApiKey")
        return (node.text or "").strip() if node is not None else ""
    except Exception:
        return ""

def arr_identity(xml_text):
    try:
        root=ET.fromstring(xml_text) if isinstance(xml_text,str) else ET.parse(xml_text).getroot()
        key=(root.findtext("ApiKey") or "").strip()
        port=int((root.findtext("Port") or "0").strip() or 0)
        return port,key
    except Exception:
        return 0,""

def discover_arr_keys():
    # Provider-neutral discovery: native installs, arbitrary /opt layouts and
    # Docker containers. Keys never leave this host-side probe.
    found={}
    candidates=[]
    for pattern in (
        os.path.expanduser("~/.config/*/config.xml"),
        "/var/lib/*/config.xml",
        "/var/lib/*/*/config.xml",
        "/config/config.xml"
    ):
        candidates.extend(glob.glob(pattern))
    try:
        scan=subprocess.run(
            ["find","/opt","-maxdepth","8","-type","f","-name","config.xml","-print"],
            capture_output=True,text=True,timeout=4)
        candidates.extend(scan.stdout.splitlines())
    except Exception:
        pass
    seen=set()
    for path in candidates:
        if not path or path in seen or not os.path.isfile(path):
            continue
        seen.add(path)
        try:
            port,key=arr_identity(path)
            if port and key:
                found.setdefault(port,key)
        except Exception:
            pass
    try:
        ps=subprocess.run(["docker","ps","--format","{{.Names}}"],capture_output=True,text=True,timeout=3)
        for container in ps.stdout.splitlines():
            if not any(x in container.lower() for x in ("sonarr","radarr","lidarr","prowlarr")):
                continue
            cat=subprocess.run(["docker","exec",container,"sh","-lc","cat /config/config.xml 2>/dev/null || true"],capture_output=True,text=True,timeout=3)
            if cat.stdout.strip():
                port,key=arr_identity(cat.stdout)
                if port and key:
                    found.setdefault(port,key)
    except Exception:
        pass
    return found

ARR_KEYS=discover_arr_keys()

def jget(url, headers=None, timeout=5):
    req=urllib.request.Request(url, headers=headers or {})
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            raw=r.read()
            if not raw:
                return r.status, None
            try:
                return r.status, json.loads(raw.decode("utf-8","replace"))
            except Exception:
                return r.status, None
    except urllib.error.HTTPError as e:
        try:
            raw=e.read()
            return e.code, json.loads(raw.decode("utf-8","replace")) if raw else None
        except Exception:
            return e.code, None
    except Exception:
        return 0, None

def title_for(r):
    for key in ("title","sourceTitle","downloadTitle"):
        v=r.get(key)
        if v:
            return str(v)
    for key,field in (("series","title"),("movie","title"),("artist","artistName"),("album","title")):
        obj=r.get(key)
        if isinstance(obj,dict) and obj.get(field):
            return str(obj.get(field))
    return "Queue item " + str(r.get("id",""))

def detail_for(r):
    parts=[]
    if r.get("errorMessage"):
        parts.append(str(r.get("errorMessage")))
    msgs=r.get("statusMessages")
    if isinstance(msgs,list):
        for m in msgs[:3]:
            if isinstance(m,dict):
                text=m.get("title") or m.get("message")
                if text:
                    parts.append(str(text))
    return " | ".join(parts)

arr={
 "Sonarr":(8989,"v3",True),
 "Sonarr Debrid":(8990,"v3",True),
 "Radarr":(7878,"v3",True),
 "Radarr Debrid":(7879,"v3",True),
 "Prowlarr":(9696,"v1",False),
 "Lidarr":(8686,"v1",True)
}

for name,(port,ver,has_queue) in arr.items():
    if not want(name):
        continue
    key=ARR_KEYS.get(port,"")
    if not key:
        add(name,"Access","API config not discovered","Unavailable","","","GraveOps could not discover the local Arr API key for port %s."%port)
        continue

    hdr={"X-Api-Key":key}

    code,health=jget("http://127.0.0.1:%d/api/%s/health"%(port,ver),hdr)
    if isinstance(health,list):
        for issue in health[:25]:
            if not isinstance(issue,dict):
                continue
            add(
                name,
                "Health",
                issue.get("source") or issue.get("type") or "Health issue",
                issue.get("type") or "Warning",
                "",
                "",
                issue.get("message") or ""
            )

    if not has_queue:
        continue

    code,queue=jget(
        "http://127.0.0.1:%d/api/%s/queue?page=1&pageSize=50&sortDirection=descending&includeUnknownSeriesItems=true&includeUnknownMovieItems=true"%(port,ver),
        hdr,
        8
    )
    if not isinstance(queue,dict):
        add(name,"Queue","Queue unavailable","Unavailable","","","HTTP %s"%code)
        continue

    records=queue.get("records") or []
    if not records:
        add(name,"Queue","Queue empty","Idle","","","No queued items.")
        continue

    for r in records[:50]:
        if not isinstance(r,dict):
            continue
        size=r.get("size")
        left=r.get("sizeleft")
        progress=""
        if isinstance(size,(int,float)) and size>0 and isinstance(left,(int,float)):
            progress="%.1f%%"%max(0,min(100,(size-left)*100.0/size))
        state=r.get("trackedDownloadStatus") or r.get("status") or r.get("protocol") or ""
        remaining=r.get("timeleft") or r.get("estimatedCompletionTime") or ""
        add(name,"Queue",title_for(r),state,progress,remaining,detail_for(r))

def sab_config():
    candidates=[]
    candidates.extend(glob.glob(os.path.expanduser("~/.sabnzbd/sabnzbd.ini")))
    candidates.extend(glob.glob("/home/*/.sabnzbd/sabnzbd.ini"))
    candidates.extend(glob.glob("/var/lib/sabnzbd*/sabnzbd.ini"))
    candidates.extend(glob.glob("/etc/sabnzbd*/sabnzbd.ini"))

    seen=set()
    for path in candidates:
        if not path or path in seen or not os.path.isfile(path):
            continue
        seen.add(path)
        try:
            text=open(path,"r",encoding="utf-8",errors="replace").read()
        except Exception:
            continue

        km=re.search(r"(?mi)^\s*api_key\s*=\s*([^\r\n#;]+)",text)
        if not km:
            continue

        pm=re.search(r"(?mi)^\s*port\s*=\s*(\d+)",text)
        port=int(pm.group(1)) if pm else 8080
        return km.group(1).strip(),port

    return "",8080

if want("SABnzbd"):
    try:
        key,sab_port=sab_config()
        if key:
            url="http://127.0.0.1:%d/api?mode=queue&output=json&apikey="%sab_port + urllib.parse.quote(key)
            code,data=jget(url,timeout=8)
            q=(data or {}).get("queue",{}) if isinstance(data,dict) else {}
            slots=q.get("slots",[]) if isinstance(q,dict) else []
            if isinstance(slots,list) and slots:
                for s in slots[:50]:
                    if not isinstance(s,dict):
                        continue
                    add(
                        "SABnzbd",
                        "Queue",
                        s.get("filename") or s.get("name") or "Download",
                        s.get("status") or "",
                        str(s.get("percentage") or ""),
                        s.get("timeleft") or s.get("sizeleft") or "",
                        s.get("cat") or ""
                    )
            elif code==200:
                add("SABnzbd","Queue","Queue empty","Idle","","","No queued items.")
            else:
                add("SABnzbd","Access","Queue API unavailable","Unavailable","","","HTTP %s on localhost:%s"%(code,sab_port))
        else:
            add("SABnzbd","Access","API key not discovered","Unavailable","","","No readable SABnzbd config containing api_key was found.")
    except Exception as e:
        add("SABnzbd","Access","Queue probe failed","Unavailable","","",type(e).__name__ + ": " + str(e))

def qbit_json(path):
    try:
        p=subprocess.run(
            ["docker","exec","qbittorrent","curl","-fsS","http://127.0.0.1:8081"+path],
            capture_output=True,text=True,timeout=15)
        if p.returncode!=0 or not p.stdout.strip():
            return None,p.stderr.strip()
        return json.loads(p.stdout),""
    except Exception as e:
        return None,type(e).__name__ + ": " + str(e)

if want("qBittorrent"):
    try:
        data,error=qbit_json("/api/v2/torrents/info?filter=all")
        if isinstance(data,list):
            if not data:
                add("qBittorrent","Queue","Queue empty","Idle","","","No torrents returned.")
            for t in data[:75]:
                if not isinstance(t,dict):
                    continue
                progress=t.get("progress")
                pct=("%.1f%%"%(float(progress)*100.0)) if isinstance(progress,(int,float)) else ""
                eta=t.get("eta")
                remaining=("" if eta in (None,0,8640000) else str(eta)+"s")
                detail="DL %s B/s | UL %s B/s | category %s"%(t.get("dlspeed",0),t.get("upspeed",0),t.get("category") or "Default")
                add("qBittorrent","Queue",t.get("name") or "Torrent",t.get("state") or "",pct,remaining,detail)
        else:
            add("qBittorrent","Access","Container-local API unavailable","Unavailable","","",error or "qBittorrent API did not return a torrent list.")
    except Exception as e:
        add("qBittorrent","Access","Queue probe failed","Unavailable","","",type(e).__name__ + ": " + str(e))

print(json.dumps(rows))
""";
}