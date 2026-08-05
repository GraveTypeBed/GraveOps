# GraveOps Cross-Platform Target Architecture

Status: approved architecture foundation; implementation must remain additive and read-only until provider parity tests pass.

## 1. Current Linux local-host provider

The canonical Avalonia client is `src/GraveOps.Desktop.Linux`. Its local host collection is implemented by `src/GraveOps.Platform.Linux/LocalLinuxHostProbe.cs` and returns the shared `GraveOps.Core.Hosts.HostSnapshot` contract.

The local Linux probe currently collects:

- hostname, OS, kernel, uptime, system state, CPU, load, memory, and IP addresses;
- mounted storage through `df`;
- systemd service inventory and failed units;
- Docker containers;
- warning-or-higher journal entries;
- application/integration evidence derived from systemd units and Docker containers.

The current implementation is operationally important and is the compatibility baseline. The first migration phases must wrap it, not rewrite its output or alter the canonical Avalonia UI.

## 2. Existing remote Linux and SSH implementation

`src/GraveOps.Desktop.Linux/LinuxControlPlane.cs` already contains reusable remote-Linux foundations:

- `LinuxHostProfile` and `LinuxHostProfileStore`;
- `LinuxCredentialStore`, backed by Secret Service through `secret-tool`;
- `LinuxControlPlaneCoordinator` for active-profile selection;
- `RemoteLinuxHostProbe` and its remote snapshot parser;
- `LinuxSshTransport` with host-key scanning, explicit SHA-256 fingerprint pinning, per-target known-host files, SSH agent/private-key/password modes, cancellation, and temporary askpass cleanup.

The remote probe currently duplicates much of the local Linux collection logic in a shell capture script. The safe reuse path is to retain the proven SSH trust and credential behavior while moving collection rules into one `LinuxSnapshotCollector` that depends on an injected runner.

Target structure:

```text
LinuxSnapshotCollector
├── LocalLinuxCommandRunner
└── SshLinuxCommandRunner
```

The collector owns Linux knowledge and parsing. The runner owns where and how commands execute.

## 3. Current models

### Canonical Linux Avalonia

The Linux desktop currently owns platform-specific models that are actually product-wide concepts:

- `LinuxHostProfile`, `LinuxHostKind`, and `LinuxHostAuthentication`;
- profile persistence and active-host state;
- Secret Service credential storage;
- local-versus-remote dispatch in `LinuxControlPlaneCoordinator`.

### Shared Core

`GraveOps.Core` currently contains `HostSnapshot`, `StorageVolumeSnapshot`, `ServiceSnapshot`, `DockerContainerSnapshot`, `IntegrationSnapshot`, and `ILocalHostProbe`. This is a useful shared data contract but is not yet target-scoped and does not carry provider identity, target identity, capabilities, or refresh generation.

### Legacy Windows/WPF tree

The legacy `src/GraveOps.App` tree contains additional cross-platform concepts under WPF-oriented namespaces:

- `Models/ServerProfile.cs`;
- `Models/HostModels.cs` (`HostConnectionKind`, `HostPlatform`, `HostCapability`, and `HostProbeResult`);
- `Models/ManagedApp.cs`;
- `Services/Hosts/IHostProvider.cs`;
- `Services/Hosts/HostProviderRegistry.cs`;
- Windows credential, SSH, PowerShell remoting, discovery, and integration-assignment services.

Those types are useful design evidence, but the permanent Avalonia product must not depend on `GraveOps.App.*` or restore the old WPF interface.

## 4. Contracts that move to Core

The following concepts belong in `GraveOps.Core`:

- target identity and target profiles;
- target platform and local/remote location;
- provider and transport identifiers;
- capability identifiers and capability sets;
- target registry and active-target/refresh coordination;
- host-provider and provider-registry contracts;
- target-scoped snapshot envelopes;
- application instance identity and target ownership;
- credential references and vault contracts;
- transport-neutral query-runner contracts.

The following remain platform-specific:

- Linux process execution, `/proc`, systemd, journal, mount, and Docker command details;
- SSH process invocation and host-key file handling;
- Windows service/process/registry/performance-counter querying;
- secure remote Windows transport;
- Secret Service and Windows Credential Manager vault implementations;
- desktop notifications and OS integration.

## 5. Collection versus transport

Collectors answer platform questions. Runners answer execution-location questions.

```text
LinuxSnapshotCollector
  input: TargetProfile + ILinuxCommandRunner
  output: HostSnapshot + capability evidence

LocalLinuxCommandRunner
  executes local read-only Linux commands

SshLinuxCommandRunner
  executes the same collector requests over pinned-host-key SSH

WindowsSnapshotCollector
  input: TargetProfile + IWindowsQueryRunner
  output: HostSnapshot + capability evidence

LocalWindowsQueryRunner
  uses local Windows APIs and read-only PowerShell/CIM queries

RemoteWindowsQueryRunner
  uses a secure authenticated remote transport, initially WinRM/PowerShell over HTTPS or an equivalently authenticated implementation
```

The collector must not know whether GraveOps itself runs on Windows or Linux. A Windows client may host an SSH runner, and a Linux client may host a remote-Windows runner, provided the required transport implementation is available.

Host telemetry and application API telemetry remain separate:

- host providers collect OS, service, process, storage, container, log, and discovery evidence;
- application telemetry providers call Plex, Arr, SABnzbd, qBittorrent, Pi-hole, or other application APIs;
- application API credentials are referenced through the vault and never copied into host snapshots.

## 6. Proposed shared interfaces

The initial additive contracts are introduced under:

- `GraveOps.Core.Targets`;
- `GraveOps.Core.Providers`;
- `GraveOps.Core.Snapshots`;
- `GraveOps.Core.Applications`;
- `GraveOps.Core.Security`;
- `GraveOps.Core.Execution`.

Key interfaces:

- `ITargetRegistry`: list, find, upsert, and remove target profiles;
- `ITargetRefreshCoordinator`: select targets, begin refresh leases, and validate snapshot currency;
- `IHostProvider`: probe and capture one target using shared contracts;
- `IHostProviderRegistry`: resolve the provider that can handle a target;
- `IQueryRunner<TRequest,TResponse>`: transport-neutral execution seam used by platform collectors;
- `ICredentialVault`: platform-specific secret persistence behind opaque references.

`TargetSnapshotEnvelope<T>` carries the target ID, provider ID, selection generation, refresh generation, refresh ID, capture timestamp, reported capabilities, and payload. The UI accepts an envelope only when its refresh lease is still current.

Application instances carry a mandatory `OwnerTargetId`, product identity, role, runtime kind, optional management endpoint, and their own capabilities. This prevents Plex Media Server from being confused with Plex Desktop and distinguishes a qBittorrent desktop process from a remotely managed qBittorrent API instance.

## 7. Preserving Local Linux behavior

The migration order deliberately keeps `LocalLinuxHostProbe` working throughout:

1. Add Core contracts without changing existing call sites.
2. Add adapters around the current local probe.
3. Extract command execution behind `LocalLinuxCommandRunner` while preserving command arguments and parsing.
4. Move parsing and collection rules into `LinuxSnapshotCollector` with golden snapshot tests.
5. Keep an adapter implementing the current `ILocalHostProbe` until all Linux UI call sites use the target/provider coordinator.
6. Remove the compatibility adapter only after local Linux parity tests pass.

No canonical Linux Avalonia controls, page layouts, or navigation design are replaced during these steps.

## 8. Windows Avalonia consumption

`windows-avalonia` already references `GraveOps.Core.Hosts` and has `GraveOps.Platform.Windows/LocalWindowsHostProbe.cs`. It can consume the new Core contracts without importing Linux desktop namespaces.

The Windows branch should:

1. reference the same target, provider, snapshot, ownership, and vault contracts;
2. wrap `LocalWindowsHostProbe` behind `IHostProvider`;
3. move Windows collection knowledge into `WindowsSnapshotCollector`;
4. add `LocalWindowsQueryRunner` first;
5. add `RemoteWindowsQueryRunner` after the local collector is stable;
6. register remote Linux SSH support through a transport package shared by both desktop clients;
7. replace the single `_hostProbe` field with target/provider/refresh coordination;
8. show or hide pages from reported capabilities and owned applications, not `OperatingSystem.IsWindows()`.

## 9. Safest file-by-file sequence

### Phase A — additive foundation

1. Add this architecture document.
2. Add Core target, capability, provider, snapshot-envelope, ownership, vault, and query-runner contracts.
3. Add contract tests for target switching, stale refresh rejection, provider resolution, ownership, and secret redaction.
4. Do not change either desktop UI.

### Phase B — Linux adapters

1. Add `GraveOps.Platform.Linux/LocalLinuxCommandRunner.cs`.
2. Add `GraveOps.Platform.Linux/LinuxSnapshotCollector.cs`.
3. Convert `LocalLinuxHostProbe` into a compatibility adapter over the collector.
4. Move SSH transport out of `LinuxControlPlane.cs` into a reusable transport project or platform-neutral SSH package.
5. Add `SshLinuxCommandRunner.cs`.
6. Convert `RemoteLinuxHostProbe` into an adapter over the same collector.
7. Add Linux local/remote parity fixtures.

### Phase C — target orchestration in Linux Avalonia

1. Add a JSON target-registry implementation using XDG config paths.
2. Adapt existing `hosts.json` data into the new schema without losing profiles.
3. Add Secret Service implementation of `ICredentialVault`.
4. Replace active-host dispatch with provider-registry dispatch.
5. add refresh leases and stale-envelope rejection before UI assignment;
6. attach `OwnerTargetId` to discovered applications;
7. leave all operations read-only.

### Phase D — Windows provider adoption

1. Cherry-pick or merge the Core-only commits into `windows-avalonia`.
2. Add `WindowsSnapshotCollector` and `LocalWindowsQueryRunner` around current behavior.
3. Add Windows Credential Manager implementation of `ICredentialVault`.
4. Add the shared target registry using the Windows configuration location.
5. add local Windows as the default target without making it the only target;
6. add shared remote Linux SSH transport;
7. add secure remote Windows transport and parity tests.

### Phase E — shared application telemetry

1. Move application product identities and API snapshot contracts into Core.
2. Convert Plex, Arr, SABnzbd, qBittorrent, and Pi-hole telemetry to target-owned application providers.
3. Route application selection through `OwnerTargetId`.
4. Reject telemetry snapshots whose target/refresh lease is stale.

## 10. Expected merge conflicts

Highest-risk conflicts:

- `src/GraveOps.Core/Hosts/HostSnapshot.cs` if both branches expand it independently;
- desktop `.csproj` references;
- `MainWindow.axaml.cs` refresh and navigation logic;
- `MainWindow.axaml` target selector/navigation visibility;
- application identity/discovery code;
- build scripts and CI workflows.

Lower-risk files:

- new Core namespace folders;
- new platform collector/runner files;
- new tests;
- architecture documentation.

Avoid editing the giant Linux and Windows `MainWindow` files in the same foundational PR. Land Core contracts first, then adapt one client at a time.

## 11. Branch and integration strategy

- Base the architecture foundation on `linux-client`, because it is the canonical Avalonia UI and Linux implementation.
- Use `agent/cross-platform-target-foundation` for the additive Core contracts and report.
- Merge that small foundation into `linux-client` after review.
- Cherry-pick the Core-only commit(s) into `windows-avalonia` immediately.
- Continue Linux provider extraction on `agent/linux-target-provider`.
- Continue Windows provider extraction on `agent/windows-target-provider`.
- Keep UI changes in separate, short-lived branches after provider contracts stabilize.
- Do not merge the entire `linux-client` branch into `windows-avalonia` or vice versa; merge shared commits and intentionally port platform adapters.

## 12. Required tests

### Target switching

- local Linux → remote Linux → local Linux;
- local Windows → remote Linux → remote Windows;
- selecting an application activates its owner target;
- a deleted target cannot remain selected.

### Refresh cancellation

- starting a new refresh cancels the prior provider request;
- cancellation is propagated through local and remote runners;
- canceled work does not update UI state or activity history as a successful capture.

### Stale-snapshot rejection

- a prior refresh on the same target is rejected;
- a snapshot from a previously selected target is rejected;
- switching away and back still rejects envelopes from the earlier selection generation.

### Capability-driven navigation

- storage pages require storage-read capability;
- Docker pages require container-read capability;
- log pages distinguish Windows event-log and Linux journal capability;
- unsupported pages are hidden or disabled with an explicit reason;
- client OS does not determine page visibility.

### Application ownership

- Plex Media Server and Plex Desktop have distinct product/role identities;
- qBittorrent desktop and qBittorrent API instances remain distinct;
- duplicate products on different targets remain separate instances;
- application telemetry is applied only to the owner target.

### Secret redaction

- secret values render as `[REDACTED]` through `ToString()`;
- profiles and exports contain only opaque credential references;
- provider exceptions, logs, diagnostics, and snapshots do not include passwords, passphrases, API keys, tokens, or temporary askpass environment values;
- vault values are disposed after use.

### Local and remote parity

- local and remote Linux collectors produce equivalent normalized snapshots from the same fixture;
- local and remote Windows collectors produce equivalent normalized snapshots from the same fixture;
- capability differences reflect actual target support, not transport location;
- missing commands or permissions degrade to warnings rather than inventing data.

## Immediate implementation boundary

The foundation phase is complete when the new Core contracts and contract tests compile while both existing desktop clients remain behaviorally unchanged. Provider extraction begins only after that boundary is reviewed.
