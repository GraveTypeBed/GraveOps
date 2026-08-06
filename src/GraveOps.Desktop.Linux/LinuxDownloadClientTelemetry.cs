using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace GraveOps.Desktop.Linux;

internal sealed record LinuxDownloadClientTelemetryContext(
    LinuxControlPlaneCoordinator ControlPlane,
    string ClientKey);

internal sealed class LinuxDownloadClientTelemetryService :
    IApplicationTelemetryAdapter<
        LinuxDownloadClientTelemetryContext,
        DownloadClientTelemetrySnapshot>
{
    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true
        };

    public Task<DownloadClientTelemetrySnapshot>
        CaptureAsync(
            LinuxDownloadClientTelemetryContext context,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            context);

        return CaptureAsync(
            context.ControlPlane,
            context.ClientKey,
            cancellationToken);
    }

    public static bool IsSupported(string? clientKey) =>
        clientKey is not null &&
        (clientKey.Equals(
             "SABnzbd",
             StringComparison.OrdinalIgnoreCase) ||
         clientKey.Equals(
             "qBittorrent",
             StringComparison.OrdinalIgnoreCase));

    public static string NormalizeClientKey(
        string clientKey)
    {
        if (string.IsNullOrWhiteSpace(clientKey))
            return string.Empty;

        if (clientKey.Equals(
                "SABnzbd",
                StringComparison.OrdinalIgnoreCase))
        {
            return "SABnzbd";
        }

        if (clientKey.Equals(
                "qBittorrent",
                StringComparison.OrdinalIgnoreCase))
        {
            return "qBittorrent";
        }

        return clientKey.Trim();
    }

    public async Task<DownloadClientTelemetrySnapshot>
        CaptureAsync(
            LinuxControlPlaneCoordinator controlPlane,
            string clientKey,
            CancellationToken cancellationToken = default)
    {
        var normalized =
            NormalizeClientKey(clientKey);

        var probe =
            normalized switch
            {
                "SABnzbd" => SabProbe,
                "qBittorrent" => QbitProbe,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(clientKey),
                    clientKey,
                    "Unsupported download client.")
            };

        var encoded =
            Convert.ToBase64String(
                Encoding.UTF8.GetBytes(probe));

        var expression =
            "import base64;exec(base64.b64decode('" +
            encoded +
            "'))";

        string standardOutput;
        string standardError;

        if (controlPlane.ActiveProfile.IsLocal)
        {
            var result =
                await RunLocalAsync(
                    expression,
                    cancellationToken);

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(
                        result.StandardError)
                        ? $"{normalized} telemetry probe failed."
                        : result.StandardError.Trim());
            }

            standardOutput =
                result.StandardOutput;
            standardError =
                result.StandardError;
        }
        else
        {
            var command =
                "python3 -c \"" +
                expression +
                "\"";

            var result =
                await LinuxSshTransport.RunScriptAsync(
                    controlPlane.ActiveProfile,
                    controlPlane.Credentials,
                    controlPlane.KnownHostsDirectory,
                    command,
                    suppliedSecret: null,
                    cancellationToken);

            standardOutput =
                result.StandardOutput;
            standardError =
                result.StandardError;
        }

        var payload =
            standardOutput
                .Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault()?
                .Trim();

        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(
                    standardError)
                    ? $"{normalized} telemetry probe returned no data."
                    : standardError.Trim());
        }

        var snapshot =
            JsonSerializer.Deserialize<
                DownloadClientTelemetrySnapshot>(
                payload,
                JsonOptions) ??
            throw new InvalidOperationException(
                $"{normalized} telemetry probe returned invalid JSON.");

        snapshot.ClientKey =
            normalized;
        snapshot.DisplayName =
            normalized;
        snapshot.SampledAt =
            DateTimeOffset.Now;

        return snapshot;
    }

    private static async Task<LocalProcessResult>
        RunLocalAsync(
            string expression,
            CancellationToken cancellationToken)
    {
        using var process =
            new Process
            {
                StartInfo =
                    new ProcessStartInfo
                    {
                        FileName = "python3",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
            };

        process.StartInfo.ArgumentList.Add(
            "-c");
        process.StartInfo.ArgumentList.Add(
            expression);

        if (!process.Start())
        {
            throw new InvalidOperationException(
                "Could not start the local Python telemetry probe.");
        }

        var stdout =
            process.StandardOutput.ReadToEndAsync(
                cancellationToken);

        var stderr =
            process.StandardError.ReadToEndAsync(
                cancellationToken);

        await process.WaitForExitAsync(
            cancellationToken);

        return new LocalProcessResult(
            process.ExitCode,
            await stdout,
            await stderr);
    }

    private sealed record LocalProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);

    private const string SabProbe = """
import glob, json, os, re, time, urllib.error, urllib.parse, urllib.request
from datetime import datetime


def jget(url, timeout=8):
    req=urllib.request.Request(url, method="GET")
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            raw=r.read()
            return r.status, json.loads(raw.decode("utf-8","replace")) if raw else None
    except urllib.error.HTTPError as e:
        try:
            raw=e.read()
            return e.code, json.loads(raw.decode("utf-8","replace")) if raw else None
        except Exception:
            return e.code, None
    except Exception:
        return 0, None


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
            text=open(path,"r",encoding="utf-8-sig",errors="replace").read()
        except Exception:
            continue
        km=re.search(r"(?mi)^\s*api_key\s*=\s*([^\r\n#;]+)",text)
        if not km:
            continue
        pm=re.search(r"(?mi)^\s*port\s*=\s*(\d+)",text)
        port=int(pm.group(1)) if pm else 8080
        return km.group(1).strip(),port
    return "",8080


def text(v, fallback="--"):
    if v is None or v == "":
        return fallback
    return str(v)


def number(v, default=0):
    try:
        return int(float(v))
    except Exception:
        return default


def fmt_epoch(v):
    try:
        n=int(float(v))
        if n <= 0:
            return "--"
        return datetime.fromtimestamp(n).strftime("%Y-%m-%d %H:%M")
    except Exception:
        return "--"


def fmt_kb_rate(v):
    try:
        n=max(0.0,float(v or 0))
        if n>=1024:
            return f"{n/1024.0:.1f} MB/s"
        return f"{n:.0f} KB/s"
    except Exception:
        return text(v)


def fmt_gb(v):
    try:
        return f"{max(0.0,float(v or 0)):.1f} GB"
    except Exception:
        value=text(v)
        return value if any(ch.isalpha() for ch in value) else value + " GB"


def progress_number(v):
    try:
        return max(0.0,min(100.0,float(v)))
    except Exception:
        return 0.0


def normalize_progress(v):
    try:
        return f"{progress_number(v):.1f}%"
    except Exception:
        return text(v)


snapshot={
    "clientKey":"SABnzbd","displayName":"SABnzbd","state":"Unknown","version":"--",
    "security":"API key retained on the Linux host","connection":"Local API","detail":"",
    "downloadSpeed":"--","uploadSpeed":"--","remaining":"--","eta":"--",
    "sessionDownloaded":"--","sessionUploaded":"--","rateLimit":"--","diskFree":"--",
    "dayDownloaded":"--","weekDownloaded":"--","monthDownloaded":"--","totalDownloaded":"--",
    "totalCount":0,"activeCount":0,"downloadingCount":0,"seedingCount":0,"pausedCount":0,
    "stalledCount":0,"completedRecentCount":0,"failedRecentCount":0,"dhtNodes":0,
    "queue":[],"history":[]
}

key,port=sab_config()
if not key:
    snapshot["state"]="Unavailable"
    snapshot["detail"]="No readable SABnzbd configuration containing an API key was found on the Linux host."
    print(json.dumps(snapshot,separators=(",",":")))
    raise SystemExit(0)

base=f"http://127.0.0.1:{port}/api"
qkey=urllib.parse.quote(key)

vcode,vdata=jget(base+"?mode=version&output=json")
if isinstance(vdata,dict):
    snapshot["version"]=text(vdata.get("version"),"--")

qcode,qdata=jget(base+"?mode=queue&start=0&limit=100&output=json&apikey="+qkey)
if qcode != 200 or not isinstance(qdata,dict):
    snapshot["state"]="Degraded" if qcode else "Offline"
    snapshot["detail"]=f"SABnzbd queue API returned HTTP {qcode or 'no response'} on localhost:{port}."
    print(json.dumps(snapshot,separators=(",",":")))
    raise SystemExit(0)

q=qdata.get("queue") or {}
slots=q.get("slots") or []
if not isinstance(slots,list):
    slots=[]

paused=bool(q.get("paused"))
snapshot["state"]="Paused" if paused else "Online"
snapshot["connection"]=f"localhost:{port}"
snapshot["downloadSpeed"]=fmt_kb_rate(q.get("kbpersec")) if q.get("kbpersec") not in (None,"") else text(q.get("speed"))
snapshot["remaining"]=text(q.get("sizeleft"))
snapshot["eta"]=text(q.get("timeleft"))
snapshot["diskFree"]=fmt_gb(q.get("diskspace1") or q.get("diskspace2"))
snapshot["rateLimit"]=text(q.get("speedlimit_abs") or q.get("speedlimit"))
snapshot["totalCount"]=number(q.get("noofslots"),len(slots))

active_states={"Downloading","Fetching","Propagating","Verifying","Repairing","Extracting","Moving","Running"}
down_states={"Downloading","Fetching","Propagating"}
paused_count=0
active_count=0
downloading_count=0

for s in slots[:100]:
    if not isinstance(s,dict):
        continue
    state=text(s.get("status"),"")
    if state in active_states:
        active_count+=1
    if state in down_states:
        downloading_count+=1
    if state.lower()=="paused":
        paused_count+=1

    size=text(s.get("size") or ((text(s.get("mb"),"") + " MB") if s.get("mb") not in (None,"") else None))
    left=text(s.get("sizeleft") or ((text(s.get("mbleft"),"") + " MB") if s.get("mbleft") not in (None,"") else None))
    detail_parts=[]
    if s.get("priority") not in (None,""):
        detail_parts.append("Priority " + str(s.get("priority")))
    if s.get("script") not in (None,"","Default"):
        detail_parts.append("Script " + str(s.get("script")))

    snapshot["queue"].append({
        "name":text(s.get("filename") or s.get("name"),"Download"),
        "category":text(s.get("cat"),"Default"),
        "state":state or "Queued",
        "progress":normalize_progress(s.get("percentage")),
        "progressPercent":progress_number(s.get("percentage")),
        "size":size,
        "downloaded":"--",
        "remaining":left,
        "downloadSpeed":snapshot["downloadSpeed"] if state in down_states else "--",
        "uploadSpeed":"--",
        "eta":text(s.get("timeleft")),
        "ratio":"--","peers":"--",
        "added":fmt_epoch(s.get("time_added")),
        "detail":" | ".join(detail_parts)
    })

snapshot["activeCount"]=active_count
snapshot["downloadingCount"]=downloading_count
snapshot["pausedCount"]=paused_count + (1 if paused and paused_count==0 else 0)

hcode,hdata=jget(base+"?mode=history&start=0&limit=40&output=json&apikey="+qkey)
h=(hdata or {}).get("history",{}) if isinstance(hdata,dict) else {}
hslots=(h.get("slots") or []) if isinstance(h,dict) else []
if not isinstance(hslots,list):
    hslots=[]
if isinstance(h,dict):
    snapshot["dayDownloaded"]=text(h.get("day_size"))
    snapshot["weekDownloaded"]=text(h.get("week_size"))
    snapshot["monthDownloaded"]=text(h.get("month_size"))
    snapshot["totalDownloaded"]=text(h.get("total_size"))


def history_detail(item):
    fail=str(item.get("fail_message") or "").strip()
    if fail:
        return fail
    logs=item.get("stage_log")
    if not isinstance(logs,list):
        return ""
    parts=[]
    for stage in logs[:3]:
        if not isinstance(stage,dict):
            continue
        name=str(stage.get("name") or "Stage")
        actions=stage.get("actions")
        if isinstance(actions,list) and actions:
            action=re.sub(r"<[^>]+>"," ",str(actions[-1]))
            action=re.sub(r"\s+"," ",action).strip()
            if action:
                parts.append(name+": "+action[:120])
        elif name:
            parts.append(name)
    return " | ".join(parts)


completed=0
failed=0
for item in hslots:
    if not isinstance(item,dict):
        continue
    state=text(item.get("status"),"")
    if state.lower()=="completed":
        completed+=1
    if state.lower()=="failed":
        failed+=1

for item in hslots[:20]:
    if not isinstance(item,dict):
        continue
    state=text(item.get("status"),"--")
    detail=history_detail(item)
    snapshot["history"].append({
        "name":text(item.get("name") or item.get("filename"),"History item"),
        "category":text(item.get("category") or item.get("cat"),"Default"),
        "state":state,
        "size":text(item.get("size")),
        "completed":fmt_epoch(item.get("completed") or item.get("completed_time") or item.get("time_completed")),
        "duration":text(item.get("download_time") or item.get("postproc_time")),
        "detail":detail
    })

snapshot["completedRecentCount"]=completed
snapshot["failedRecentCount"]=failed
if hcode not in (0,200):
    snapshot["detail"]=f"Queue telemetry is online; history returned HTTP {hcode}."
else:
    snapshot["detail"]="Read-only queue and history telemetry from the SABnzbd local API."

print(json.dumps(snapshot,separators=(",",":")))
""";

    private const string QbitProbe = """
import json, subprocess
from datetime import datetime


def run(args, timeout=12):
    try:
        p=subprocess.run(args,capture_output=True,text=True,timeout=timeout)
        return p.returncode,p.stdout.strip(),p.stderr.strip()
    except Exception as e:
        return 1,"",type(e).__name__+": "+str(e)


def qget(path):
    rc,out,err=run(["docker","exec","qbittorrent","curl","-fsS","http://127.0.0.1:8081"+path],15)
    return rc,out,err


def fmt_bytes(value):
    try:
        n=max(0.0,float(value or 0))
        units=["B","KB","MB","GB","TB","PB"]
        i=0
        while n>=1024 and i<len(units)-1:
            n/=1024.0
            i+=1
        return f"{n:.1f} {units[i]}" if i else f"{n:.0f} {units[i]}"
    except Exception:
        return "--"


def fmt_rate(value):
    s=fmt_bytes(value)
    return s+"/s" if s!="--" else s


def fmt_eta(value):
    try:
        sec=int(value)
    except Exception:
        return "--"
    if sec <= 0 or sec >= 8640000:
        return "--"
    d,sec=divmod(sec,86400)
    h,sec=divmod(sec,3600)
    m,_=divmod(sec,60)
    if d:
        return f"{d}d {h}h"
    if h:
        return f"{h}h {m}m"
    return f"{m}m"


def fmt_epoch(value):
    try:
        n=int(value)
        if n<=0:
            return "--"
        return datetime.fromtimestamp(n).strftime("%Y-%m-%d %H:%M")
    except Exception:
        return "--"


def lower(v):
    return str(v or "").lower()


snapshot={
    "clientKey":"qBittorrent","displayName":"qBittorrent","state":"Unknown","version":"--",
    "security":"Container-local API; host and LAN WebUI authentication remain enabled",
    "connection":"--","detail":"","downloadSpeed":"--","uploadSpeed":"--","remaining":"--","eta":"--",
    "sessionDownloaded":"--","sessionUploaded":"--","rateLimit":"--","diskFree":"--",
    "dayDownloaded":"--","weekDownloaded":"--","monthDownloaded":"--","totalDownloaded":"--",
    "totalCount":0,"activeCount":0,"downloadingCount":0,"seedingCount":0,"pausedCount":0,
    "stalledCount":0,"completedRecentCount":0,"failedRecentCount":0,"dhtNodes":0,
    "queue":[],"history":[]
}

rc,status,err=run(["docker","inspect","-f","{{.State.Status}}","qbittorrent"],8)
if rc!=0 or status.lower()!="running":
    snapshot["state"]="Offline"
    snapshot["detail"]="The qbittorrent container is not running or could not be inspected."
    print(json.dumps(snapshot,separators=(",",":")))
    raise SystemExit(0)

vrc,version,verr=qget("/api/v2/app/version")
trc,transfer_raw,terr=qget("/api/v2/transfer/info")
qrc,torrents_raw,qerr=qget("/api/v2/torrents/info?filter=all")

if vrc!=0 or trc!=0 or qrc!=0:
    snapshot["state"]="Degraded"
    snapshot["detail"]="Container-local qBittorrent API is unavailable. " + (verr or terr or qerr)
    print(json.dumps(snapshot,separators=(",",":")))
    raise SystemExit(0)

snapshot["version"]=version.strip().lstrip("vV") or "--"
try:
    transfer=json.loads(transfer_raw)
except Exception:
    transfer={}
try:
    torrents=json.loads(torrents_raw)
except Exception:
    torrents=[]
if not isinstance(torrents,list):
    torrents=[]

connection=str(transfer.get("connection_status") or "unknown")
snapshot["connection"]=connection
snapshot["state"]="Online" if connection.lower()=="connected" else "Degraded"
snapshot["downloadSpeed"]=fmt_rate(transfer.get("dl_info_speed"))
snapshot["uploadSpeed"]=fmt_rate(transfer.get("up_info_speed"))
snapshot["sessionDownloaded"]=fmt_bytes(transfer.get("dl_info_data"))
snapshot["sessionUploaded"]=fmt_bytes(transfer.get("up_info_data"))
snapshot["dhtNodes"]=int(transfer.get("dht_nodes") or 0)
dl_limit=int(transfer.get("dl_rate_limit") or 0)
ul_limit=int(transfer.get("up_rate_limit") or 0)
snapshot["rateLimit"]="Unlimited" if not dl_limit and not ul_limit else f"DL {fmt_rate(dl_limit)} | UL {fmt_rate(ul_limit)}"
snapshot["totalCount"]=len(torrents)

remaining_total=0
active=downloading=seeding=paused=stalled=failed=0
eta_candidates=[]

for t in torrents:
    if not isinstance(t,dict):
        continue
    state=lower(t.get("state"))
    remaining_total+=int(t.get("amount_left") or 0)
    is_stalled="stalled" in state
    is_down=("dl" in state or "downloading" in state or "metadl" in state) and "upload" not in state
    is_seed=("up" in state or "seeding" in state) and "down" not in state
    is_paused=("stopped" in state or "paused" in state)
    is_error=("error" in state or "missingfiles" in state)
    if is_stalled:
        stalled+=1
    if is_down:
        downloading+=1
    if is_seed:
        seeding+=1
    if is_paused:
        paused+=1
    if is_error:
        failed+=1
    moving_state=state in {"downloading","forceddl","metadl","uploading","forcedup","checkingdl","checkingup","checkingresumedata","moving","allocating"}
    if (not is_paused) and (moving_state or int(t.get("dlspeed") or 0)>0 or int(t.get("upspeed") or 0)>0):
        active+=1
    try:
        eta_value=int(t.get("eta") or 0)
        if int(t.get("amount_left") or 0)>0 and 0 < eta_value < 8640000:
            eta_candidates.append(eta_value)
    except Exception:
        pass

snapshot["activeCount"]=active
snapshot["downloadingCount"]=downloading
snapshot["seedingCount"]=seeding
snapshot["pausedCount"]=paused
snapshot["stalledCount"]=stalled
snapshot["failedRecentCount"]=failed
snapshot["remaining"]=fmt_bytes(remaining_total)
snapshot["eta"]=fmt_eta(min(eta_candidates)) if eta_candidates else "--"


def queue_rank(t):
    state=lower(t.get("state"))
    if int(t.get("dlspeed") or 0)>0:
        return (0,-int(t.get("dlspeed") or 0))
    if "dl" in state or "downloading" in state or "metadl" in state:
        return (1,-float(t.get("progress") or 0))
    if "stalled" in state:
        return (2,-float(t.get("progress") or 0))
    if "up" in state or "seeding" in state:
        return (3,-int(t.get("completion_on") or 0))
    return (4,-int(t.get("added_on") or 0))

for t in sorted(torrents,key=queue_rank)[:100]:
    state=str(t.get("state") or "unknown")
    progress=float(t.get("progress") or 0)
    peers=int(t.get("num_leechs") or 0)
    seeds=int(t.get("num_seeds") or 0)
    tracker_count=int(t.get("trackers_count") or 0)
    detail=f"Seeds {seeds} | Peers {peers} | Trackers {tracker_count}"
    snapshot["queue"].append({
        "name":str(t.get("name") or "Torrent"),
        "category":str(t.get("category") or "Default"),
        "state":state,
        "progress":f"{max(0,min(100,progress*100)):.1f}%",
        "progressPercent":max(0,min(100,progress*100)),
        "size":fmt_bytes(t.get("total_size") or t.get("size")),
        "downloaded":fmt_bytes(t.get("downloaded")),
        "remaining":fmt_bytes(t.get("amount_left")),
        "downloadSpeed":fmt_rate(t.get("dlspeed")),
        "uploadSpeed":fmt_rate(t.get("upspeed")),
        "eta":fmt_eta(t.get("eta")),
        "ratio":f"{float(t.get('ratio') or 0):.2f}",
        "peers":f"{seeds}/{peers}",
        "added":fmt_epoch(t.get("added_on")),
        "detail":detail
    })

completed=[t for t in torrents if isinstance(t,dict) and int(t.get("completion_on") or 0)>0]
completed.sort(key=lambda t:int(t.get("completion_on") or 0),reverse=True)
now_epoch=int(datetime.now().timestamp())
snapshot["completedRecentCount"]=sum(1 for t in completed if now_epoch-int(t.get("completion_on") or 0) <= 86400)
for t in completed[:20]:
    snapshot["history"].append({
        "name":str(t.get("name") or "Torrent"),
        "category":str(t.get("category") or "Default"),
        "state":str(t.get("state") or "Completed"),
        "size":fmt_bytes(t.get("total_size") or t.get("size")),
        "completed":fmt_epoch(t.get("completion_on")),
        "duration":fmt_eta(t.get("time_active")),
        "detail":f"Ratio {float(t.get('ratio') or 0):.2f} | Uploaded {fmt_bytes(t.get('uploaded'))}"
    })

snapshot["detail"]="Read-only telemetry through docker exec -> 127.0.0.1:8081. No qBittorrent credentials are stored in GraveOps."
print(json.dumps(snapshot,separators=(",",":")))
""";
}
