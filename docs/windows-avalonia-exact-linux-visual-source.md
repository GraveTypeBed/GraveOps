# Windows Avalonia exact Linux visual source

The visual source of truth for the Windows Avalonia shell migration is the
clean Linux workspace reported by the operator on 2026-08-05.

- Commit: `8699e7628196d80f6fee111e77bc4f12fae6e229`
- Branch at capture: `agent/avalonia-windows-target-management`
- Linux `App.axaml` SHA-256: `db11d488514c27f5c74c8a1967856e84486d828e813b19c327c8724295ad22fa`
- Linux `MainWindow.axaml` SHA-256: `4852617aaf9590e2d4303bb67898a538d6ffa2e30f79133b27c8ebf1bc43c6e4`

This source is intentionally pinned. Do not substitute the newer
`linux-client` branch during the migration without a separate reviewed
visual-source update.

The Windows client may retain Windows-specific providers, credential storage,
target management and telemetry services, but its shared presentation must be
derived from this pinned Linux source.