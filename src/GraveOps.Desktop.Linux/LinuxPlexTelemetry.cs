using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace GraveOps.Desktop.Linux;

public sealed class LinuxPlexSessionRow
{
    public string Title { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string Player { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Progress { get; set; } = "--";
    public string VideoDecision { get; set; } = "--";
    public string AudioDecision { get; set; } = "--";
    public string Bandwidth { get; set; } = "--";
    public string Detail { get; set; } = string.Empty;
}

public sealed class LinuxPlexSnapshot
{
    public string State { get; set; } = "Unknown";
    public string Service { get; set; } = "--";
    public string ServiceDetail { get; set; } = "--";
    public string Version { get; set; } = "--";
    public string Endpoint { get; set; } =
        "http://127.0.0.1:32400/web";
    public string Connection { get; set; } = "--";
    public string Security { get; set; } = "--";
    public string Dependency { get; set; } = "--";
    public string Detail { get; set; } = string.Empty;
    public int ActiveSessions { get; set; }
    public int DirectPlayCount { get; set; }
    public int DirectStreamCount { get; set; }
    public int TranscodeCount { get; set; }
    public int LibraryCount { get; set; }
    public string TotalBandwidth { get; set; } = "0 Kbps";
    public DateTimeOffset SampledAt { get; set; } =
        DateTimeOffset.Now;
    public List<LinuxPlexSessionRow> Sessions { get; set; } =
        new();
}

internal sealed class LinuxPlexTelemetryService
{
    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true
        };

    public async Task<LinuxPlexSnapshot> CaptureAsync(
        LinuxControlPlaneCoordinator controlPlane,
        CancellationToken cancellationToken = default)
    {
        var encoded =
            Convert.ToBase64String(
                Encoding.UTF8.GetBytes(PlexProbe));

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
                        ? "Plex telemetry probe failed."
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
                    ? "Plex telemetry probe returned no data."
                    : standardError.Trim());
        }

        var snapshot =
            JsonSerializer.Deserialize<
                LinuxPlexSnapshot>(
                payload,
                JsonOptions) ??
            throw new InvalidOperationException(
                "Plex telemetry probe returned invalid JSON.");

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
                        FileName =
                            "python3",
                        RedirectStandardOutput =
                            true,
                        RedirectStandardError =
                            true,
                        UseShellExecute =
                            false,
                        CreateNoWindow =
                            true
                    }
            };

        process.StartInfo.ArgumentList.Add(
            "-c");

        process.StartInfo.ArgumentList.Add(
            expression);

        if (!process.Start())
        {
            throw new InvalidOperationException(
                "Could not start the local Plex telemetry probe.");
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

    // The token is discovered and consumed only inside the target process.
    // It is never included in the JSON returned to GraveOps.
    private const string PlexProbe = """
import json
import os
import re
import subprocess
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET
from pathlib import Path

BASE = "http://127.0.0.1:32400"

def run(args, timeout=8):
    try:
        completed = subprocess.run(
            args,
            capture_output=True,
            text=True,
            timeout=timeout,
            check=False,
        )
        return completed.returncode, completed.stdout.strip(), completed.stderr.strip()
    except Exception as exc:
        return 1, "", str(exc)

def xml_token(text):
    if not text:
        return ""
    try:
        root = ET.fromstring(text)
        return root.attrib.get("PlexOnlineToken", "").strip()
    except Exception:
        match = re.search(r'PlexOnlineToken="([^"]+)"', text)
        return match.group(1).strip() if match else ""

def read_text(path):
    try:
        return Path(path).read_text(errors="replace")
    except Exception:
        return ""

def graveops_secret_path():
    config_home = (
        os.environ.get("XDG_CONFIG_HOME")
        or str(Path.home() / ".config")
    )

    return (
        Path(config_home)
        / "GraveOps"
        / "secrets"
        / "plex-token"
    )

def read_secret_token(path):
    try:
        candidate = Path(path)

        if (
            not candidate.is_file()
            or candidate.is_symlink()
        ):
            return ""

        metadata = candidate.stat()

        if metadata.st_uid != os.geteuid():
            return ""

        if metadata.st_mode & 0o077:
            return ""

        value = candidate.read_text(
            encoding="utf-8",
            errors="strict",
        )

        if "\n" in value or "\r" in value:
            return ""

        value = value.strip()

        return (
            value
            if 8 <= len(value) <= 512
            else ""
        )
    except Exception:
        return ""

def docker_names():
    code, output, _ = run(
        ["docker", "ps", "--format", "{{.Names}}"]
    )
    if code != 0:
        return []
    return [
        value.strip()
        for value in output.splitlines()
        if value.strip()
    ]

def find_plex_container():
    for name in docker_names():
        if "plex" in name.lower():
            return name
    return ""

def discover_token(container):
    environment_token = (
        os.environ.get("PLEX_TOKEN")
        or ""
    ).strip()
    if environment_token:
        return environment_token, "environment"

    secret_token = read_secret_token(
        graveops_secret_path()
    )

    if secret_token:
        return secret_token, "GraveOps secret file"

    candidates = [
        "/var/lib/plexmediaserver/Library/Application Support/Plex Media Server/Preferences.xml",
        str(Path.home() / ".config/plex/Library/Application Support/Plex Media Server/Preferences.xml"),
        "/config/Library/Application Support/Plex Media Server/Preferences.xml",
    ]

    for path in candidates:
        token = xml_token(read_text(path))
        if token:
            return token, "Preferences.xml"

    if container:
        code, output, _ = run(
            [
                "docker",
                "exec",
                container,
                "cat",
                "/config/Library/Application Support/Plex Media Server/Preferences.xml",
            ]
        )
        if code == 0:
            token = xml_token(output)
            if token:
                return token, "container Preferences.xml"

        code, output, _ = run(
            [
                "docker",
                "inspect",
                "--format",
                "{{range .Config.Env}}{{println .}}{{end}}",
                container,
            ]
        )
        if code == 0:
            for line in output.splitlines():
                if line.startswith("PLEX_TOKEN="):
                    token = line.split("=", 1)[1].strip()
                    if token:
                        return token, "container environment"

    return "", ""

def fetch_xml(path, token=""):
    request = urllib.request.Request(
        BASE + path,
        headers={
            "Accept": "application/xml",
            "X-Plex-Product": "GraveOps",
            "X-Plex-Client-Identifier": "graveops-linux-control-center",
        },
    )
    if token:
        request.add_header("X-Plex-Token", token)

    with urllib.request.urlopen(request, timeout=7) as response:
        return ET.fromstring(response.read())

def service_context(container):
    code, output, _ = run(
        ["systemctl", "is-active", "plexmediaserver.service"]
    )
    if code == 0 and output.strip() == "active":
        return "systemd", "active", "plexmediaserver.service"

    if container:
        code, output, _ = run(
            [
                "docker",
                "inspect",
                "--format",
                "{{.State.Status}}|{{.State.Health.Status}}",
                container,
            ]
        )
        if code == 0:
            parts = output.split("|", 1)
            state = parts[0].strip() if parts else "unknown"
            health = parts[1].strip() if len(parts) > 1 else ""
            detail = container
            if health:
                detail += " · health " + health
            return "Docker", state, detail

    return "Not detected", "inactive", "No systemd unit or running Plex container"

def attr(node, name, default=""):
    value = node.attrib.get(name)
    return value if value not in (None, "") else default

def human_bandwidth(value):
    try:
        number = int(float(value))
    except Exception:
        return "0 Kbps"
    if number >= 1000:
        return f"{number / 1000:.1f} Mbps"
    return f"{number} Kbps"

def progress_text(node):
    try:
        duration = int(attr(node, "duration", "0"))
        offset = int(attr(node, "viewOffset", "0"))
        if duration <= 0:
            return "--"
        percent = max(0, min(100, round(offset * 100 / duration)))
        return f"{percent}%"
    except Exception:
        return "--"

def session_title(node):
    title = attr(node, "title", "Unknown media")
    grandparent = attr(node, "grandparentTitle")
    parent = attr(node, "parentTitle")
    index = attr(node, "index")
    parent_index = attr(node, "parentIndex")

    if grandparent:
        parts = [grandparent]
        if parent_index and index:
            parts.append(f"S{int(parent_index):02d}E{int(index):02d}")
        parts.append(title)
        return " · ".join(parts)

    if parent and parent != title:
        return f"{parent} · {title}"

    return title

def decision(node, kind):
    transcode = node.find("TranscodeSession")
    media = node.find("Media")

    key = kind + "Decision"
    if transcode is not None and attr(transcode, key):
        return attr(transcode, key).replace("copy", "direct stream").title()

    if media is not None and attr(media, key):
        return attr(media, key).replace("copy", "direct stream").title()

    if transcode is not None:
        return "Transcode"

    return "Direct Play"

container = find_plex_container()
service_kind, service_state, service_detail = service_context(container)
token, token_source = discover_token(container)

identity = None
identity_error = ""
for path in ("/identity", "/:/identity"):
    try:
        identity = fetch_xml(path)
        break
    except Exception as exc:
        identity_error = str(exc)

version = "--"
machine_identifier = ""
if identity is not None:
    version = attr(identity, "version", "--")
    machine_identifier = attr(identity, "machineIdentifier")

sessions = []
library_count = 0
direct_play = 0
direct_stream = 0
transcode_count = 0
total_bandwidth = 0
protected_errors = []

if token:
    try:
        session_root = fetch_xml("/status/sessions", token)
        for node in list(session_root):
            if node.tag not in ("Video", "Track", "Photo"):
                continue

            user_node = node.find("User")
            player_node = node.find("Player")
            transcode_node = node.find("TranscodeSession")

            video_decision = decision(node, "video")
            audio_decision = decision(node, "audio")
            decision_text = (
                video_decision + " " + audio_decision
            ).lower()

            if "transcode" in decision_text:
                transcode_count += 1
            elif "direct stream" in decision_text:
                direct_stream += 1
            else:
                direct_play += 1

            session_node = node.find("Session")

            bandwidth = 0
            for candidate in (
                attr(session_node, "bandwidth", "")
                if session_node is not None
                else "",
                attr(transcode_node, "bandwidth", "")
                if transcode_node is not None
                else "",
            ):
                try:
                    parsed = int(float(candidate))
                except Exception:
                    continue

                if parsed > 0:
                    bandwidth = parsed
                    break

            total_bandwidth += bandwidth

            player = "Unknown player"
            state = attr(node, "viewOffset", "Playing")
            if player_node is not None:
                player = (
                    attr(player_node, "title")
                    or attr(player_node, "product")
                    or attr(player_node, "platform")
                    or "Unknown player"
                )
                state = attr(player_node, "state", "playing").title()

            detail_parts = [
                value
                for value in (
                    attr(node, "type"),
                    attr(node, "year"),
                    attr(node, "container"),
                )
                if value
            ]

            sessions.append(
                {
                    "Title": session_title(node),
                    "User": (
                        attr(user_node, "title", "Unknown user")
                        if user_node is not None
                        else "Unknown user"
                    ),
                    "Player": player,
                    "State": state,
                    "Progress": progress_text(node),
                    "VideoDecision": video_decision,
                    "AudioDecision": audio_decision,
                    "Bandwidth": human_bandwidth(bandwidth),
                    "Detail": " · ".join(detail_parts),
                }
            )
    except Exception as exc:
        protected_errors.append("sessions unavailable: " + str(exc))

    try:
        library_root = fetch_xml("/library/sections", token)
        library_count = len(
            [
                node
                for node in list(library_root)
                if node.tag == "Directory"
            ]
        )
    except Exception as exc:
        protected_errors.append("libraries unavailable: " + str(exc))

identity_online = identity is not None
service_online = service_state.lower() in (
    "active",
    "running",
    "healthy",
)

state = "Online" if identity_online or service_online else "Unavailable"
connection = (
    "Direct local API"
    if identity_online
    else "Endpoint did not answer"
)
security = (
    "Protected session telemetry · token used only inside the target host"
    if token
    else "Identity-only telemetry · Plex token was not found"
)
dependency = (
    "Plex Media Server · " + service_kind
    if service_kind != "Not detected"
    else "Plex Media Server dependency not detected"
)

details = []
if machine_identifier:
    details.append("server identity verified")
if token_source:
    details.append("protected token source: " + token_source)
if identity_error and not identity_online:
    details.append("identity unavailable")
details.extend(protected_errors)

payload = {
    "State": state,
    "Service": service_kind,
    "ServiceDetail": service_detail,
    "Version": version,
    "Endpoint": BASE + "/web",
    "Connection": connection,
    "Security": security,
    "Dependency": dependency,
    "Detail": " · ".join(details),
    "ActiveSessions": len(sessions),
    "DirectPlayCount": direct_play,
    "DirectStreamCount": direct_stream,
    "TranscodeCount": transcode_count,
    "LibraryCount": library_count,
    "TotalBandwidth": human_bandwidth(total_bandwidth),
    "Sessions": sessions,
}

print(json.dumps(payload, separators=(",", ":")))
""";
}
