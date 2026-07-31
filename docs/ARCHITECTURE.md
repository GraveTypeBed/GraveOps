# GraveOps Architecture

## GraveOps 2.0 RC2 — Windows feature-complete candidate

GraveOps 2.0 RC2 is the Windows reference implementation of the environment-aware media/homelab control plane. The visible product name is **GraveOps**. The historical `%APPDATA%\GraveOps Community` data directory and `GraveOpsCommunity` Credential Manager namespace are intentionally retained for seamless upgrades from earlier preview builds; they are implementation details, not product branding.

The frozen Personal V1.2.2 tree remains a separate product lineage and is never modified by GraveOps 2.0 RC builds.

## Product boundary

GraveOps answers four questions: **what is running, what is wrong, what depends on it, and what should I do next?** It does not attempt to replace the full native Sonarr, Radarr, Plex, qBittorrent, or other application UIs.

The Windows client can manage:

- the local Windows PC natively;
- remote Windows hosts through PowerShell remoting with credentials kept in Windows Credential Manager;
- remote Linux hosts through pinned-host-key SSH;
- mixed native/Docker media environments.

A Linux Mint desktop client is the next platform phase and should reuse the provider/core model rather than fork behavior.

## Environment / host / application model

- **Environment**: fleet health, ownership, topology, impact, lifecycle and history.
- **Host**: one selected machine, its storage/runtime/services/logs and provider-specific operations.
- **Application**: one verified capability routed to the host that owns it.

Application navigation is fleet-aware. Selecting an app can activate its owning host automatically. Target-scoped telemetry is invalidated on host switches and stale samples from the previous owner are rejected.

## Host providers

- `LocalWindowsHostProvider` — native local Windows process/storage/service/Docker capability probe.
- `RemoteWindowsHostProvider` — PowerShell remoting provider. GraveOps does not enable WinRM or modify TrustedHosts automatically.
- `RemoteLinuxHostProvider` — SSH provider using pinned host keys and credentials from Windows Credential Manager.
- `HostProviderRegistry` — resolves a profile to the correct transport/capability provider.

Privileged operations remain explicit. GraveOps is not designed to run permanently elevated.

## Discovery and ownership

Discovery is evidence-based rather than port-only. Providers correlate process identity, Docker/container identity, listener state, executable identity and bounded HTTP fingerprints. Verified ownership is stored per host and drives navigation, topology and health propagation.

Current integration catalog includes Plex/Jellyfin/Emby, Tautulli, Kometa, Sonarr, Radarr, Lidarr, Prowlarr, Bazarr, Seerr, SABnzbd, qBittorrent, Recyclarr, Profilarr, autobrr, Unpackerr, Cleanuparr, Tdarr, Maintainerr, Pi-hole and Docker. Integrations not verified in the environment remain hidden.

## Telemetry ownership

`LiveAnalyticsService` remains the single owner for adaptive media/download telemetry. Deep health, SMART, mutations and heavyweight diagnostics stay on demand. Integration companion pages reuse provider/on-demand probes and do not create duplicate background polling loops.

## Dashboard and modular UI

The Dashboard uses a graphite/mauve modular design:

- fleet summary;
- interactive Environment Map;
- compact actionable attention;
- one selected-host operational strip;
- configurable Quick Modules for Intelligence, Servers, Media Hub, Lifecycle, Recyclarr, Docker, Storage, Backups and Activity.

Quick Modules reuse existing snapshots rather than polling independently. Their visibility/order are profile settings so the same component system can support future optional providers.

## RC2 runtime polish

RC2 removes the obsolete hidden full-health compatibility surface and its unused parser/snapshot types. Backup readiness is provider-neutral and first-class in navigation, Dashboard entry points and Quick Modules. Fleet history/activity collection mutations are marshalled through the WPF dispatcher, the Lifecycle page uses the shared status-strip resource, and normal Linux storage views suppress pseudo/system filesystems while raw diagnostic views remain available.

The cleanup also removes dead model properties, an unused Intelligence window, obsolete internal Community integration type names and duplicate local PowerShell execution code.

## Media lifecycle and Intelligence 2.0

`MediaLifecycleService` correlates current Arr and downloader work into an end-to-end operational view and identifies likely blocking layers. `ControlPlaneIntelligenceService` combines environment impact, dependency state and lifecycle context to recommend an inspection order rather than blindly restarting services.

The current lifecycle model is intentionally evidence-driven: it does not claim request/subtitle/transcode stages when a provider cannot supply authenticated item-level data safely.

## History and incident replay

`FleetHistoryService` records bounded meaningful fleet transitions to `fleet-history.json` when enabled. It has no independent polling loop. History can be replayed around an incident window together with GraveOps activity so operators can see what changed immediately before a failure.

## Recyclarr safety

Recyclarr is preview-only in GraveOps. The provider can discover standard Sonarr/Radarr instance names on supported Windows/Linux hosts and execute `sync <service> --preview` optionally scoped by `--instance`. GraveOps 2.0 RC2 exposes **no Recyclarr sync/write action**.

## Security and diagnostics

- secrets are kept in Windows Credential Manager and are excluded from profile exports;
- SSH host keys remain pinned;
- PowerShell-remoting passwords are passed to the child process through its environment, never the command line;
- diagnostic bundles are sanitized and provider-neutral;
- application API keys are not copied into GraveOps merely to populate generic integration pages;
- destructive actions remain distinct, risk-classified and confirmed;
- Safe Mode can make operational controls read-only.

## Shareability

The RC source contains no personal usernames, Plerver paths, `/opt/dumb` assumptions, fixed backup repositories or mandatory helper dependencies. The optional Linux helper is restricted to generic host/Docker/SMART primitives; core GraveOps operation does not require it.

## Packaging

The Windows publish is self-contained `win-x64` and produces `GraveOps.exe`. `installer/GraveOps.iss` defines the upgrade-safe Windows installer identity. `build-release.ps1` also invokes Inno Setup 6 when `ISCC.exe` is installed; absence of Inno Setup does not invalidate the self-contained RC publish.

The stable installer AppId is preserved across upgrades.
