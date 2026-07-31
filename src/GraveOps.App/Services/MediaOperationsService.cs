using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Xml.Linq;
using GraveOps.App.Models;

namespace GraveOps.App.Services;

public sealed class MediaOperationsService
{
    private readonly AppServices _services;
    private readonly ConcurrentDictionary<Guid, MediaOperationsSnapshot> _cache = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(12);
    private static readonly HttpClient LocalClient = new(
        new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(2)
        })
    {
        Timeout = TimeSpan.FromSeconds(4)
    };

    public MediaOperationsService(AppServices services) => _services = services;

    public async Task<MediaOperationsSnapshot> GetSnapshotAsync(ServerProfile server, bool force = false, CancellationToken cancellationToken = default)
    {
        if (!force && _cache.TryGetValue(server.Id, out var cached) && DateTimeOffset.Now - cached.SampledAt < CacheLifetime)
            return cached;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!force && _cache.TryGetValue(server.Id, out cached) && DateTimeOffset.Now - cached.SampledAt < CacheLifetime)
                return cached;

            if (server.ConnectionKind == HostConnectionKind.LocalWindows)
            {
                var localSnapshot = await GetLocalWindowsSnapshotAsync(server, cancellationToken);
                _cache[server.Id] = localSnapshot;
                return localSnapshot;
            }

            if (server.ConnectionKind == HostConnectionKind.RemoteWindows)
            {
                var remoteWindowsSnapshot = await GetRemoteWindowsSnapshotAsync(server, cancellationToken);
                _cache[server.Id] = remoteWindowsSnapshot;
                return remoteWindowsSnapshot;
            }

            var apps = _services.Config.Current.Applications
                .Where(a => a.ServerId == server.Id || (a.ServerId is null && _services.Context.Current?.Id == server.Id))
                // Media summary is an HTTP/API probe. Runtime-only integrations such
                // as Recyclarr, Kometa and Unpackerr may intentionally have an empty
                // URL and are health-checked by IntegrationRuntimeService instead.
                // Never let one non-web capability abort the entire Arr/media sample.
                .Where(a => IsHttpProbeTarget(a.Url))
                .Select(a => new
                {
                    id = a.Id,
                    name = a.Name,
                    category = a.Category,
                    url = a.Url.Replace("{host}", "127.0.0.1", StringComparison.OrdinalIgnoreCase)
                })
                .ToList();

            var specJson = JsonSerializer.Serialize(apps);
            var spec64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(specJson));
            var script = BuildProbeScript(spec64);
            var script64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(script));
            var command = $"python3 -c \"import base64;exec(base64.b64decode('{script64}'))\"";
            var result = await _services.Ssh.ExecuteAsync(server, command, 45, cancellationToken);

            if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StdOut))
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.StdErr) ? "Media telemetry probe returned no data." : result.StdErr);

            var snapshot = Parse(result.StdOut, server);
            _cache[server.Id] = snapshot;
            return snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate(Guid serverId) => _cache.TryRemove(serverId, out _);

    private async Task<MediaOperationsSnapshot> GetLocalWindowsSnapshotAsync(
        ServerProfile server,
        CancellationToken cancellationToken)
    {
        var snapshot = new MediaOperationsSnapshot
        {
            ServerId = server.Id,
            SampledAt = DateTimeOffset.Now
        };

        var apps = _services.Config.Current.Applications
            .Where(a => a.ServerId == server.Id)
            .Where(a => IsHttpProbeTarget(a.Url))
            .ToArray();

        foreach (var app in apps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolved = app.Url.Replace("{host}", "127.0.0.1", StringComparison.OrdinalIgnoreCase);
            var probe = await ProbeLocalAppAsync(app.Name, resolved, cancellationToken);
            snapshot.Apps.Add(new AppHealthCard
            {
                AppId = app.Id,
                Name = app.Name,
                Category = app.Category,
                Health = probe.Reachable ? AppHealthState.Healthy : AppHealthState.Offline,
                HttpCode = probe.StatusCode,
                LatencyMs = probe.LatencyMs,
                Version = probe.Version,
                Detail = probe.Detail,
                ResolvedUrl = resolved,
                SampledAt = snapshot.SampledAt
            });
        }

        var plexCard = snapshot.Apps.FirstOrDefault(x => x.Name.Equals("Plex", StringComparison.OrdinalIgnoreCase));
        snapshot.Plex = new PlexTelemetry
        {
            ServiceState = IsProcessRunning("plex") ? "Running" : plexCard is { Health: AppHealthState.Healthy } ? "Reachable" : "Not detected",
            Version = string.IsNullOrWhiteSpace(plexCard?.Version) ? "--" : plexCard.Version,
            EndpointState = plexCard is { Health: AppHealthState.Healthy } ? "Online" : "Offline",
            HttpCode = plexCard?.HttpCode ?? 0,
            LatencyMs = plexCard?.LatencyMs ?? 0,
            DockerDependency = "Native Windows host"
        };

        var sabCard = snapshot.Apps.FirstOrDefault(x => x.Name.Equals("SABnzbd", StringComparison.OrdinalIgnoreCase));
        snapshot.Sab = new SabTelemetry
        {
            State = sabCard is null ? "Not configured" : sabCard.Health == AppHealthState.Healthy ? "Online" : "Offline",
            Speed = "--",
            Remaining = "--",
            QueueCount = 0,
            Detail = sabCard?.Detail ?? ""
        };

        var qbitCard = snapshot.Apps.FirstOrDefault(x => x.Name.Equals("qBittorrent", StringComparison.OrdinalIgnoreCase));
        snapshot.Qbit = new QbitTelemetry
        {
            State = qbitCard is null ? "Not configured" : qbitCard.Health == AppHealthState.Healthy ? "Online" : "Offline",
            DownloadSpeed = "--",
            UploadSpeed = "--",
            Detail = qbitCard?.Detail ?? ""
        };

        return snapshot;
    }

    private async Task<MediaOperationsSnapshot> GetRemoteWindowsSnapshotAsync(
        ServerProfile server,
        CancellationToken cancellationToken)
    {
        var snapshot = new MediaOperationsSnapshot
        {
            ServerId = server.Id,
            SampledAt = DateTimeOffset.Now
        };

        var apps = _services.Config.Current.Applications
            .Where(a => a.ServerId == server.Id)
            .Where(a => IsHttpProbeTarget(a.Url))
            .ToArray();

        foreach (var app in apps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolved = app.Url.Replace("{host}", server.Host, StringComparison.OrdinalIgnoreCase);
            var probe = await ProbeLocalAppAsync(app.Name, resolved, cancellationToken);
            snapshot.Apps.Add(new AppHealthCard
            {
                AppId = app.Id,
                Name = app.Name,
                Category = app.Category,
                Health = probe.Reachable ? AppHealthState.Healthy : AppHealthState.Offline,
                HttpCode = probe.StatusCode,
                LatencyMs = probe.LatencyMs,
                Version = probe.Version,
                Detail = probe.Detail.Replace("Native localhost", "Remote Windows", StringComparison.OrdinalIgnoreCase),
                ResolvedUrl = resolved,
                SampledAt = snapshot.SampledAt
            });
        }

        var plexCard = snapshot.Apps.FirstOrDefault(x => x.Name.Equals("Plex", StringComparison.OrdinalIgnoreCase));
        snapshot.Plex = new PlexTelemetry
        {
            ServiceState = plexCard is { Health: AppHealthState.Healthy } ? "Reachable" : "Not detected",
            Version = string.IsNullOrWhiteSpace(plexCard?.Version) ? "--" : plexCard.Version,
            EndpointState = plexCard is { Health: AppHealthState.Healthy } ? "Online" : "Offline",
            HttpCode = plexCard?.HttpCode ?? 0,
            LatencyMs = plexCard?.LatencyMs ?? 0,
            DockerDependency = "Remote Windows host"
        };

        var sabCard = snapshot.Apps.FirstOrDefault(x => x.Name.Equals("SABnzbd", StringComparison.OrdinalIgnoreCase));
        snapshot.Sab = new SabTelemetry
        {
            State = sabCard is null ? "Not configured" : sabCard.Health == AppHealthState.Healthy ? "Online" : "Offline",
            Detail = sabCard?.Detail ?? ""
        };

        var qbitCard = snapshot.Apps.FirstOrDefault(x => x.Name.Equals("qBittorrent", StringComparison.OrdinalIgnoreCase));
        snapshot.Qbit = new QbitTelemetry
        {
            State = qbitCard is null ? "Not configured" : qbitCard.Health == AppHealthState.Healthy ? "Online" : "Offline",
            Detail = qbitCard?.Detail ?? ""
        };

        return snapshot;
    }

    private static bool IsHttpProbeTarget(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        var normalized = url.Replace("{host}", "127.0.0.1", StringComparison.OrdinalIgnoreCase);
        return Uri.TryCreate(normalized, UriKind.Absolute, out var uri) &&
               (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<(bool Reachable, int StatusCode, long LatencyMs, string Version, string Detail)> ProbeLocalAppAsync(
        string name,
        string resolvedUrl,
        CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var uri = new Uri(resolvedUrl, UriKind.Absolute);
            var target = name.Equals("Plex", StringComparison.OrdinalIgnoreCase)
                ? new Uri($"{uri.Scheme}://{uri.Host}:{uri.Port}/identity")
                : name.Equals("qBittorrent", StringComparison.OrdinalIgnoreCase)
                    ? new Uri($"{uri.Scheme}://{uri.Host}:{uri.Port}/api/v2/app/version")
                    : uri;

            using var request = new HttpRequestMessage(HttpMethod.Get, target);
            using var response = await LocalClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            watch.Stop();

            var status = (int)response.StatusCode;
            var reachable = status > 0 && status < 500;
            var version = "";
            var detail = status is 401 or 403
                ? "Endpoint reachable; authentication is protected."
                : "Native localhost endpoint";

            if (name.Equals("Plex", StringComparison.OrdinalIgnoreCase) && response.IsSuccessStatusCode)
            {
                try
                {
                    var xml = XDocument.Parse(body);
                    version = xml.Root?.Attribute("version")?.Value ?? "";
                }
                catch { }
            }
            else if (name.Equals("qBittorrent", StringComparison.OrdinalIgnoreCase) && response.IsSuccessStatusCode)
            {
                version = body.Trim().TrimStart('v', 'V');
                detail = "Native localhost Web API";
            }

            return (reachable, status, watch.ElapsedMilliseconds, version, detail);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            watch.Stop();
            return (false, 0, watch.ElapsedMilliseconds, "", "Local endpoint unavailable");
        }
    }

    private static bool IsProcessRunning(string contains)
    {
        try
        {
            return Process.GetProcesses().Any(process =>
            {
                try { return process.ProcessName.Contains(contains, StringComparison.OrdinalIgnoreCase); }
                catch { return false; }
                finally { process.Dispose(); }
            });
        }
        catch
        {
            return false;
        }
    }

    private static MediaOperationsSnapshot Parse(string json, ServerProfile server)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var snapshot = new MediaOperationsSnapshot
        {
            ServerId = server.Id,
            SampledAt = DateTimeOffset.Now
        };

        if (root.TryGetProperty("apps", out var apps))
        {
            foreach (var item in apps.EnumerateArray())
            {
                var code = item.GetProperty("code").GetInt32();
                var latency = item.GetProperty("latency_ms").GetInt64();
                var stateText = item.TryGetProperty("state", out var se) ? se.GetString() ?? "" : "";
                var queueValue = item.TryGetProperty("queue", out var q0) && q0.ValueKind == JsonValueKind.Number ? q0.GetInt32() : (int?)null;
                var issueValue = item.TryGetProperty("issues", out var h0) && h0.ValueKind == JsonValueKind.Number ? h0.GetInt32() : (int?)null;
                var health = code == 0 ? AppHealthState.Offline
                    : issueValue is > 0 ? AppHealthState.Degraded
                    : queueValue is > 0 ? AppHealthState.Busy
                    : stateText.Equals("busy", StringComparison.OrdinalIgnoreCase) ? AppHealthState.Busy
                    : code is >= 200 and < 400 ? AppHealthState.Healthy
                    : AppHealthState.Degraded;

                var card = new AppHealthCard
                {
                    AppId = Guid.TryParse(item.GetProperty("id").GetString(), out var id) ? id : Guid.Empty,
                    Name = item.GetProperty("name").GetString() ?? "Application",
                    Category = item.GetProperty("category").GetString() ?? "Other",
                    HttpCode = code,
                    LatencyMs = latency,
                    Health = health,
                    Version = item.TryGetProperty("version", out var version) ? version.GetString() ?? "" : "",
                    QueueCount = queueValue,
                    HealthIssueCount = issueValue,
                    Detail = item.TryGetProperty("detail", out var detail) ? detail.GetString() ?? "" : "",
                    ResolvedUrl = item.TryGetProperty("url", out var url) ? url.GetString() ?? "" : "",
                    SampledAt = snapshot.SampledAt
                };
                snapshot.Apps.Add(card);
            }
        }

        if (root.TryGetProperty("plex", out var plex))
        {
            snapshot.Plex = new PlexTelemetry
            {
                ServiceState = GetString(plex, "service", "Unknown"),
                Version = GetString(plex, "version", "--"),
                EndpointState = GetString(plex, "endpoint", "Unknown"),
                HttpCode = GetInt(plex, "code"),
                LatencyMs = GetLong(plex, "latency_ms"),
                DockerDependency = GetString(plex, "dependency", "Host/runtime")
            };
        }

        if (root.TryGetProperty("sab", out var sab))
        {
            snapshot.Sab = new SabTelemetry
            {
                State = GetString(sab, "state", "Unknown"),
                Speed = GetString(sab, "speed", "--"),
                Remaining = GetString(sab, "remaining", "--"),
                QueueCount = GetInt(sab, "queue"),
                Detail = GetString(sab, "detail", "")
            };
        }

        if (root.TryGetProperty("qbit", out var qbit))
        {
            snapshot.Qbit = new QbitTelemetry
            {
                State = GetString(qbit, "state", "Unknown"),
                DownloadSpeed = GetString(qbit, "down", "--"),
                UploadSpeed = GetString(qbit, "up", "--"),
                Detail = GetString(qbit, "detail", "")
            };
        }

        return snapshot;
    }

    private static string GetString(JsonElement e, string name, string fallback)
        => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? fallback : fallback;
    private static int GetInt(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : 0;
    private static long GetLong(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt64() : 0;

    private static string BuildProbeScript(string appSpec64) => """
import base64, configparser, glob, json, os, subprocess, time, urllib.error, urllib.parse, urllib.request, xml.etree.ElementTree as ET
APPS = json.loads(base64.b64decode("__APPSPEC__").decode("utf-8"))

def get(url, headers=None, timeout=3):
    start=time.time()
    try:
        req=urllib.request.Request(url, headers=headers or {}, method="GET")
        with urllib.request.urlopen(req, timeout=timeout) as r:
            body=r.read()
            return r.getcode(), body, int((time.time()-start)*1000)
    except urllib.error.HTTPError as e:
        try: body=e.read()
        except Exception: body=b""
        return e.code, body, int((time.time()-start)*1000)
    except Exception:
        return 0, b"", int((time.time()-start)*1000)

def jget(url, headers=None, timeout=3):
    code, body, ms=get(url, headers, timeout)
    if code and body:
        try: return code, json.loads(body.decode("utf-8", "replace")), ms
        except Exception: pass
    return code, None, ms

def api_key(path):
    try:
        root=ET.parse(path).getroot()
        node=root.find("ApiKey")
        return (node.text or "").strip() if node is not None else ""
    except Exception: return ""

def fmt_rate(value):
    try:
        n=float(value)
        if n >= 1024**2: return f"{n/1024**2:.1f} MB/s"
        if n >= 1024: return f"{n/1024:.1f} KB/s"
        return f"{n:.0f} B/s"
    except Exception: return str(value or "--")

rows=[]
for a in APPS:
    code, body, ms=get(a["url"], timeout=3)
    rows.append({"id":a["id"],"name":a["name"],"category":a["category"],"url":a["url"],"code":code,"latency_ms":ms,"version":"","queue":None,"issues":None,"detail":"","state":""})
byname={r["name"].lower():r for r in rows}

# Docker state enriches container-backed integrations without requiring web credentials.
def docker_names():
    try:
        p=subprocess.run(["docker","ps","--format","{{.Names}}"],capture_output=True,text=True,timeout=3)
        return [x.strip() for x in p.stdout.splitlines() if x.strip()]
    except Exception: return []

def docker_match(fragment):
    names=docker_names()
    f=fragment.lower()
    return next((n for n in names if n.lower()==f), next((n for n in names if f in n.lower()), ""))

def docker_state(name):
    if not name: return "missing"
    try:
        p=subprocess.run(["docker","inspect","-f","{{.State.Status}}|{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}",name],capture_output=True,text=True,timeout=3)
        return p.stdout.strip() if p.returncode==0 else "missing"
    except Exception: return "unknown"

qbit_container=docker_match("qbittorrent") or docker_match("qbit")
qbdocker=docker_state(qbit_container)
if "qbittorrent" in byname: byname["qbittorrent"]["detail"]="Docker "+qbdocker if qbit_container else "Host endpoint"

# Plex identity endpoint does not expose the token and gives us server version.
plex={"service":"Not present","version":"--","endpoint":"Offline","code":0,"latency_ms":0,"dependency":"Host service / endpoint"}
if "plex" in byname:
    try:
        p=subprocess.run(["systemctl","is-active","plexmediaserver"],capture_output=True,text=True,timeout=3)
        plex["service"]=p.stdout.strip() or "unknown"
    except Exception: plex["service"]="unknown"
    code, body, ms=get("http://127.0.0.1:32400/identity", timeout=3)
    plex["latency_ms"]=ms
    plex["code"]=code
    plex["endpoint"]="Online" if code else "Offline"
    if body:
        try:
            x=ET.fromstring(body.decode("utf-8","replace"))
            plex["version"]=x.attrib.get("version","--")
            byname["plex"]["version"]=plex["version"]
        except Exception: pass

# Arr API telemetry. API keys are discovered and consumed only on the host and are never returned.
def arr_identity(xml_text):
    try:
        root=ET.fromstring(xml_text) if isinstance(xml_text,str) else ET.parse(xml_text).getroot()
        key=(root.findtext("ApiKey") or "").strip()
        port=int((root.findtext("Port") or "0").strip() or 0)
        return port,key
    except Exception: return 0,""

def discover_arr_keys():
    found={}
    candidates=[]
    for pattern in (os.path.expanduser("~/.config/*/config.xml"),"/var/lib/*/config.xml","/var/lib/*/*/config.xml","/config/config.xml"):
        candidates.extend(glob.glob(pattern))
    try:
        scan=subprocess.run(["find","/opt","-maxdepth","8","-type","f","-name","config.xml","-print"],capture_output=True,text=True,timeout=4)
        candidates.extend(scan.stdout.splitlines())
    except Exception: pass
    seen=set()
    for path in candidates:
        if not path or path in seen or not os.path.isfile(path): continue
        seen.add(path)
        port,key=arr_identity(path)
        if port and key: found.setdefault(port,key)
    for container in docker_names():
        if not any(x in container.lower() for x in ("sonarr","radarr","lidarr","prowlarr")): continue
        try:
            cat=subprocess.run(["docker","exec",container,"sh","-lc","cat /config/config.xml 2>/dev/null || true"],capture_output=True,text=True,timeout=3)
            if cat.stdout.strip():
                port,key=arr_identity(cat.stdout)
                if port and key: found.setdefault(port,key)
        except Exception: pass
    return found

arr_keys=discover_arr_keys()
arr={
 "Sonarr":(8989,"v3"),
 "Sonarr Debrid":(8990,"v3"),
 "Radarr":(7878,"v3"),
 "Radarr Debrid":(7879,"v3"),
 "Prowlarr":(9696,"v1"),
 "Lidarr":(8686,"v1")
}
for name,(port,ver) in arr.items():
    r=byname.get(name.lower())
    if not r: continue
    key=arr_keys.get(port,"")
    if not key:
        r["detail"]=(r["detail"]+" API config not discovered").strip()
        continue
    hdr={"X-Api-Key":key}
    code,status,_=jget(f"http://127.0.0.1:{port}/api/{ver}/system/status",hdr)
    if isinstance(status,dict): r["version"]=str(status.get("version", ""))
    code,health,_=jget(f"http://127.0.0.1:{port}/api/{ver}/health",hdr)
    if isinstance(health,list): r["issues"]=len(health)
    if name != "Prowlarr":
        code,queue,_=jget(f"http://127.0.0.1:{port}/api/{ver}/queue?page=1&pageSize=1",hdr)
        if isinstance(queue,dict):
            q=queue.get("totalRecords",queue.get("totalCount"))
            if isinstance(q,(int,float)): r["queue"]=int(q)

# SABnzbd lightweight summary. The API key is discovered and consumed only on the Linux host.
def sab_config():
    import glob, re
    candidates=[]
    candidates.extend(glob.glob(os.path.expanduser("~/.sabnzbd/sabnzbd.ini")))
    candidates.extend(glob.glob("/home/*/.sabnzbd/sabnzbd.ini"))
    candidates.extend(glob.glob("/var/lib/sabnzbd*/sabnzbd.ini"))
    candidates.extend(glob.glob("/etc/sabnzbd*/sabnzbd.ini"))
    for path in candidates:
        if not os.path.isfile(path):
            continue
        try:
            raw=open(path,"r",encoding="utf-8-sig",errors="replace").read()
        except Exception:
            continue
        m=re.search(r"(?mi)^\s*api_key\s*=\s*([^\r\n#;]+)",raw)
        if not m:
            continue
        pm=re.search(r"(?mi)^\s*port\s*=\s*(\d+)",raw)
        return m.group(1).strip(),int(pm.group(1)) if pm else 8080
    return "",8080

sab={"state":"Not configured","speed":"--","remaining":"--","queue":0,"detail":""}
if "sabnzbd" in byname:
    try:
        key,sab_port=sab_config()
        if key:
            url=f"http://127.0.0.1:{sab_port}/api?mode=queue&output=json&apikey="+urllib.parse.quote(key)
            code,data,_=jget(url,timeout=4)
            q=(data or {}).get("queue",{}) if isinstance(data,dict) else {}
            sab["state"]="Paused" if code==200 and bool(q.get("paused")) else ("Online" if code==200 else "Degraded")
            sab["speed"]=str(q.get("speed","--"))
            sab["remaining"]=str(q.get("sizeleft","--"))
            slots=q.get("slots",[])
            sab["queue"]=int(q.get("noofslots")) if str(q.get("noofslots","")).isdigit() else (len(slots) if isinstance(slots,list) else 0)
            byname["sabnzbd"]["queue"]=sab["queue"]
            byname["sabnzbd"]["detail"]="Linux-host local API"
        else:
            sab["detail"]="API key not readable on Linux host"
    except Exception as e:
        sab["detail"]=type(e).__name__

# qBittorrent lightweight summary through a discovered container-local API. Host/LAN authentication stays enabled.
def qbit_get(path):
    if not qbit_container: return 1,""
    try:
        shell='port="${WEBUI_PORT:-}"; if [ -z "$port" ]; then port=$(tr "\000" " " </proc/1/cmdline 2>/dev/null | sed -n "s/.*--webui-port[= ]\([0-9][0-9]*\).*/\1/p"); fi; port=${port:-8080}; curl -fsS "http://127.0.0.1:${port}'+path+'"'
        p=subprocess.run(["docker","exec",qbit_container,"sh","-lc",shell],capture_output=True,text=True,timeout=5)
        return p.returncode,p.stdout.strip()
    except Exception:
        return 1,""

qbit={"state":"Not configured","down":"--","up":"--","detail":"Docker "+qbdocker}
if "qbittorrent" in byname:
    vrc,vtext=qbit_get("/api/v2/app/version")
    trc,ttext=qbit_get("/api/v2/transfer/info")
    try:
        data=json.loads(ttext) if trc==0 and ttext else None
    except Exception:
        data=None
    if vrc==0 and trc==0 and isinstance(data,dict):
        connected=str(data.get("connection_status") or "").lower()=="connected"
        dl_speed=int(data.get("dl_info_speed") or 0)
        ul_speed=int(data.get("up_info_speed") or 0)
        qbit["state"]=("Busy" if (dl_speed>0 or ul_speed>0) else "Online") if connected else "Degraded"
        qbit["down"]=fmt_rate(dl_speed)
        qbit["up"]=fmt_rate(ul_speed)
        qbit["detail"]="Container-local API; host WebUI protected; Docker "+qbdocker
        byname["qbittorrent"]["code"]=200
        byname["qbittorrent"]["version"]=vtext.lstrip("vV")
        byname["qbittorrent"]["state"]="busy" if connected and (dl_speed>0 or ul_speed>0) else ""
        byname["qbittorrent"]["detail"]="Container-local API; host WebUI protected; Docker "+qbdocker
    elif qbdocker.startswith("running"):
        qbit["state"]="Degraded"
        qbit["detail"]="Container running; container-local API unavailable"
    else:
        qbit["state"]="Offline"

print(json.dumps({"apps":rows,"plex":plex,"sab":sab,"qbit":qbit},separators=(",",":")))
""".Replace("__APPSPEC__", appSpec64, StringComparison.Ordinal);
}