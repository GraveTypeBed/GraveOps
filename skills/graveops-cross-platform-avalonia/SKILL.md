---
name: graveops-cross-platform-avalonia
description: Design and validate GraveOps changes across shared Avalonia UI, Linux, Windows, and planned macOS providers without platform leakage or false parity claims.
---

# GraveOps Cross-Platform Avalonia

## Architectural objective

One shared application and Avalonia UI layer, with native behavior behind explicit platform contracts. Linux may be the reference implementation without becoming the accidental universal implementation.

## Layer rules

### Shared Core

May contain:

- domain models
- immutable snapshots and state transitions
- provider interfaces
- validation and policy
- command/result abstractions
- platform-neutral serialization and configuration contracts

Must not import Linux, Windows, macOS, shell, registry, systemd, launchd, WMI, PowerShell, polkit, UAC, AppKit, or platform process assumptions.

### Shared Avalonia application/UI

May contain:

- views, view models, styles, converters, navigation, layout, and shared interaction behavior
- dependency injection against interfaces
- UI-thread marshaling and lifecycle coordination

Must not directly invoke native commands, read native secrets, own multiple independent pollers for the same state, or branch repeatedly on operating-system checks where a provider belongs.

### Platform providers

Own native implementation details behind small typed contracts, such as:

- platform information
- command execution
- service and process control
- storage and mount discovery
- update/install behavior
- privilege elevation
- secure secret storage
- notification and tray integration
- packaging/runtime support

Keep adapters thin. Put policy in shared layers and mechanics in providers.

## Change classification

Classify every changed behavior as exactly one:

- `shared/completed`
- `pending port`
- `intentional platform difference`
- `legacy maintenance only`

Record the reason. A Linux-complete feature is not cross-platform complete merely because the shared solution compiles on Windows.

## Platform matrix

For non-trivial changes, define the applicable matrix before implementation:

| Platform | Architecture | Build | Tests | Publish | Runtime | Packaging |
|---|---|---|---|---|---|---|
| Linux | x64/arm64 as supported | required where affected | required | affected RIDs | native smoke | archive/package checks |
| Windows | x64/arm64 as supported | required where affected | required | affected RIDs | native smoke | installer/update checks |
| macOS | x64/arm64 as supported | required once target exists | required | affected RIDs | native smoke | app bundle/sign/notarize |

Mark unexecuted cells as untested. Do not convert them into implied passes.

## Avalonia UI gates

For every XAML/AXAML change:

1. Parse/load the affected markup.
2. Verify every declared event handler exists and has a compatible signature.
3. Verify every code-behind named-control lookup exists.
4. Verify lookup type matches the control supplied by markup.
5. Verify resource keys resolve in the affected view/application scope.
6. Verify single-content controls do not receive multiple content owners.
7. Test pointer-over, selected, focus-visible, disabled, expanded, loading, empty, error, stale, and live-refresh states as applicable.
8. Run a headless UI test for stable behavior that can be automated.
9. Launch the affected window/page on the native platform.

A successful compiler pass is only one gate.

## State and lifecycle rules

- One writer owns each mutable application state.
- Many views may read shared snapshots.
- Centralize polling and refresh cadence; do not create page-specific duplicate pollers.
- Dispose timers, subscriptions, process streams, and cancellation sources deterministically.
- Marshal UI updates through the proper dispatcher.
- Keep long-running or blocking platform work off the UI thread.
- Preserve raw evidence even when a known benign signal is excluded from aggregate health scoring.

## Provider-contract tests

For each provider contract, test:

- supported behavior
- unsupported capability reporting
- cancellation
- timeouts
- malformed native output
- permission denied
- missing executable/service/resource
- redaction of secrets
- deterministic typed result mapping

Use fixtures for platform output parsers. Indentation-sensitive and locale-sensitive inputs require executable regression fixtures.

## Platform-specific release gates

### Linux

Validate native service/process/storage behavior, permissions/elevation, desktop integration, and the actual published executable on a supported Linux host.

### Windows

Validate Avalonia runtime behavior, apphost/nativehost, UAC or secure elevation path, installer/update behavior, tray/lifecycle behavior, and the published executable on Windows.

### macOS

Treat the `.app` bundle as a platform artifact, not a renamed publish folder. Validate bundle structure, `Info.plist`, icons/resources, executable permissions, x64/arm64 selection, code signing, hardened runtime/entitlements, notarization, stapling, and launch on macOS. Signing/notarization evidence requires a macOS-capable environment and valid Apple credentials.

## Completion evidence

A cross-platform change report must include:

- shared contracts touched
- provider implementations touched
- platform matrix with pass/fail/untested
- XAML/headless/runtime evidence
- lifecycle and polling impact
- parity classification
- explicit deferred ports
