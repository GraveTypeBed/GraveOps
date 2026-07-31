# GraveOps 2.0 RC2

GraveOps 2.0 RC2 is the first frozen public release candidate of the GraveOps Windows control center.

## Highlights

- Backups restored as a provider-neutral first-class surface
- ActionRunner-based execution and activity history
- Linux storage filtering
- Recyclarr discovery with preview-only operation
- close-to-tray lifecycle handling
- per-user Windows installation with preserved user configuration

## Validation

- Source/build/UI/lifecycle: **27 PASS, 0 WARN, 0 FAIL**
- Installer/install/reinstall: **13 PASS, 0 WARN, 0 FAIL**
- Uninstall/recovery: **14 PASS, 0 WARN, 0 FAIL**

## Downloads

Use `GraveOps-Setup-2.0-RC2.exe` for normal installation.

The installer is currently **unsigned**, so Windows SmartScreen may warn. Verify its SHA-256 checksum:

``text
03726381B8D9AF078DF27091A398CAA8598C381802BA66D5F38B82A1C8EAFE2A
``

The frozen bootstrap reconstructs the exact RC2 source payload:

``text
Bootstrap SHA-256: 820BCC235C3314D3BE54AF133625555F1999F2C5FDAD3B2C2B7DC39AD6E5A1AB
Payload SHA-256:   9FE6070C1531D0BB6AA4238714F71B2C61B2AEC0B4071A1EFCF693D41C326C27
``

See `SHA256SUMS.txt` for the complete checksum list.