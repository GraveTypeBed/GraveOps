# GraveOps Coding-Agent Instructions

## Mandatory entry point

Before changing GraveOps code, read:

1. `skills/graveops-engineering-router/SKILL.md`
2. Every specialist skill selected by that router

Use the smallest sufficient combination. State the selected skills and their order in one sentence before substantial work.

## Project posture

- GraveOps is a cross-platform .NET 10 desktop application using Avalonia for the shared application and UI direction.
- Linux is the most mature Avalonia implementation and current reference behavior.
- Windows Avalonia is an active migration target; the legacy Windows line may still require isolated maintenance.
- macOS is a planned platform-provider and release target.
- Shared code must not silently become Linux-specific, Windows-specific, or macOS-specific.

## Non-negotiable rules

- Do not call a change fixed because it builds.
- Do not edit before reproducing or characterizing a reported bug, unless the request is a purely mechanical change with deterministic acceptance checks.
- Do not place platform APIs in shared Core or shared Avalonia UI code.
- Do not concatenate untrusted values into shell command strings.
- Do not bypass Safe Mode, confirmation, backup, allowlist, or privilege boundaries.
- Do not apply a historical patch to an unknown source baseline.
- Do not reuse stale publish or packaging directories.
- Do not claim Linux/Windows/macOS parity without evidence from each claimed platform.
- Do not leave a fixed regression without an automated test when a stable test seam exists.
- Do not broaden the diff beyond the task without explicitly documenting the reason.

## Required working style

- Inspect the repository and current branch before planning.
- Prefer small, reviewable changes over one enormous rewrite.
- Use typed contracts and thin platform adapters.
- Keep one owner for polling, state mutation, command execution, and release staging.
- Preserve raw diagnostic evidence even when health scoring suppresses a known benign signal.
- Report exact files changed, commands run, test/build/publish results, runtime evidence, remaining risk, and parity state.

## Evidence template

Use `templates/change-evidence.md` for non-trivial changes and releases.
