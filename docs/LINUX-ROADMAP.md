# GraveOps Linux Roadmap

## Product direction

The Linux Mint desktop client is a native local GraveOps control center. It
must reuse the provider/core model and product behavior from the Windows
reference implementation instead of becoming a separate fork.

The Linux client manages its own machine without an SSH hop. Remote Linux,
Windows and NAS targets remain part of the same environment/host/application
model.

## Phase 1 — foundation

Status: started on `linux-client`.

- Create an isolated Linux development worktree and branch.
- Add `GraveOps.Core` for platform-neutral contracts and models.
- Add `GraveOps.Platform.Linux` for native Linux host providers.
- Add `GraveOps.Desktop.Linux` using Avalonia.
- Implement the first local Linux snapshot:
  - hostname
  - distribution
  - kernel
  - uptime
  - systemd state
  - Docker availability/version
  - operational storage view with pseudo filesystems filtered
- Add Linux CI and a self-contained `linux-x64` smoke publish.

## Phase 2 — extract reusable Windows logic

- Move provider-neutral models, fleet ownership, lifecycle correlation,
  intelligence, history and action contracts into `GraveOps.Core`.
- Keep WPF-only dispatcher, controls and Windows Credential Manager code in the
  Windows project.
- Keep systemd, `/proc`, `/sys`, `journalctl`, `findmnt`, `lsblk`, Docker and
  Linux privilege adapters in `GraveOps.Platform.Linux`.
- Add contract tests proving Windows and Linux providers produce equivalent
  semantic snapshots.

## Phase 3 — Linux feature parity

- Dashboard and Environment Map.
- Servers, Media Hub, lifecycle and intelligence.
- Native systemd Services & Actions.
- Docker containers, logs and compose ownership.
- Storage, SMART and mount health.
- Backups and activity/history.
- Terminal and SFTP.
- Recyclarr preview-only.
- Safe Mode and destructive-action confirmation.

## Phase 4 — Linux Mint packaging

- Self-contained `linux-x64` publish.
- `.desktop` launcher and icon installation.
- `.deb` package for Linux Mint/Ubuntu-family systems.
- Per-user configuration under XDG-compatible paths.
- Keyring-backed credential storage.
- Upgrade and uninstall validation preserving user data.

## Non-negotiable boundaries

- Do not modify the frozen Personal V1.2.2 lineage.
- Do not destabilize the validated Windows RC2 release.
- Do not duplicate polling loops already owned by shared analytics services.
- Do not run permanently elevated.
- Keep destructive actions explicit, risk-classified and confirmed.
- Keep Recyclarr preview-only until a later release explicitly changes that
  safety boundary.