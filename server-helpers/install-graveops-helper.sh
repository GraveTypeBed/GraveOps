#!/usr/bin/env bash
set -euo pipefail

# Optional restricted helper for GraveOps Remote Linux hosts.
# GraveOps 2.0 does not require this helper for discovery, telemetry or monitoring.
# Install it only when you want a narrowly scoped privileged SMART-health read.

TARGET_USER="${1:-${SUDO_USER:-$USER}}"
if ! id "$TARGET_USER" >/dev/null 2>&1; then
  echo "User not found: $TARGET_USER" >&2
  exit 2
fi

sudo install -d -m 0755 /usr/local/sbin
TMP="$(mktemp)"
trap 'rm -f "$TMP"' EXIT
cat > "$TMP" <<'SCRIPT'
#!/usr/bin/env bash
set -euo pipefail
cmd="${1:-}"
case "$cmd" in
  host-status)
    hostname
    uptime -p 2>/dev/null || uptime
    df -hT
    ;;
  docker-status)
    command -v docker >/dev/null 2>&1 || { echo "Docker CLI not found" >&2; exit 127; }
    docker ps -a --format 'table {{.Names}}\t{{.Image}}\t{{.Status}}\t{{.Ports}}'
    ;;
  smart-health)
    dev="${2:-}"
    case "$dev" in
      /dev/*) ;;
      *) echo "A /dev/... device path is required" >&2; exit 2 ;;
    esac
    command -v smartctl >/dev/null 2>&1 || { echo "smartctl not found" >&2; exit 127; }
    exec smartctl -H "$dev"
    ;;
  *)
    echo "graveopsctl: supported operations: host-status, docker-status, smart-health /dev/..." >&2
    exit 2
    ;;
esac
SCRIPT
sudo install -o root -g root -m 0755 "$TMP" /usr/local/sbin/graveopsctl
SUDOERS="/etc/sudoers.d/graveops-${TARGET_USER}"
printf '%s ALL=(root) NOPASSWD: /usr/local/sbin/graveopsctl smart-health *\n' "$TARGET_USER" | sudo tee "$SUDOERS" >/dev/null
sudo chmod 0440 "$SUDOERS"
if command -v visudo >/dev/null 2>&1; then sudo visudo -cf "$SUDOERS" >/dev/null; fi

echo "Installed optional GraveOps helper for $TARGET_USER."
echo "Core GraveOps discovery and telemetry do not depend on this helper."
