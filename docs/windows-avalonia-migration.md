# Windows Avalonia Migration

Status: started 2026-08-04.

## Decision

GraveOps will move toward one Avalonia desktop architecture for Windows, Linux, and eventually macOS. The existing Windows WPF application remains the legacy/rollback line until the Avalonia Windows build passes feature, safety, packaging, upgrade, and runtime validation.

## Isolation rules

- Do not rename, delete, or convert `src/GraveOps.App` in place.
- Do not overwrite `publish/win-x64`.
- Use `src/GraveOps.Platform.Windows` for Windows-native host behavior.
- Use `src/GraveOps.Desktop.Windows` as the migration-stage Avalonia Windows host.
- Publish the preview to `publish/win-x64-avalonia` with a distinct executable identity.
- Keep configuration and credentials side-by-side or explicitly migrated.

## First deliverable

The first scaffold is intentionally read-only and proves:

- Avalonia 12 / .NET 10 starts on Windows.
- The desktop consumes `GraveOps.Core`.
- `GraveOps.Platform.Windows` implements the shared `ILocalHostProbe`.
- Local storage, selected services, Docker containers, and detected integrations render in the preview.
- The WPF project and release output remain untouched.

## Migration order

1. Runnable Windows preview and native provider.
2. Shared Avalonia theme/control/view-model extraction.
3. Windows platform services for actions, privilege, startup, credentials, notifications, and updates.
4. Dashboard and canonical health parity.
5. Media, infrastructure, and tool parity.
6. Side-by-side Preview/RC packaging.
7. Explicit WPF cutover decision only after acceptance.

## Non-negotiable invariants

- One recurring owner per telemetry stream.
- Full Health remains canonical and explicit.
- Safe Mode blocks mutations.
- Privileged actions use on-demand elevation or narrow helpers.
- Recyclarr preview remains read-only.
- Secrets do not enter ordinary configuration, diagnostics, or documentation.
- Build success alone is insufficient; runtime validation is required.
