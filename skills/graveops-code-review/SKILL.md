---
name: graveops-code-review
description: Review GraveOps changes for correctness, architecture, platform parity, security, lifecycle, tests, release safety, and evidence before accepting or publishing them.
---

# GraveOps Code Review

## Review posture

Review the actual diff against the requested outcome and repository rules. Do not reward volume. Do not invent findings to appear thorough.

## Phase 1 — Establish scope

- identify base and head refs
- confirm working-tree and generated-file state
- read the request, issue, specification, and relevant architecture/parity/security/release docs
- list changed files and classify source, tests, generated output, packaging, docs, and artifacts
- flag unrelated changes before deep review

## Phase 2 — Correctness

Check:

- actual execution path reaches the changed code
- state transitions and error paths are complete
- cancellation, timeout, retry, and disposal behavior
- null/empty/stale/loading/unsupported states
- concurrency and UI-thread ownership
- parser behavior for malformed, locale-sensitive, indentation-sensitive, and partial native output
- no stale artifact is being mistaken for the source result

## Phase 3 — Cross-platform architecture

Check:

- no native APIs leaked into shared Core or UI
- provider contracts are small, typed, and capability-aware
- platform adapters contain mechanics rather than duplicated business policy
- Linux reference behavior did not become an undocumented universal assumption
- Windows migration and legacy maintenance remain isolated where required
- macOS path, process, bundle, and privilege assumptions are not precluded
- parity status is recorded honestly

## Phase 4 — Avalonia and lifecycle

Check:

- AXAML resources resolve
- event handlers exist and signatures match
- named controls exist and lookup types match
- one content owner per single-content control
- commands and enabled state are correct
- selected, hover, focus, disabled, loading, stale, empty, populated, and error states remain coherent
- timers and subscriptions have one owner and deterministic disposal
- long-running work does not block the UI thread

## Phase 5 — Security and destructive safety

Check:

- no shell-string construction from untrusted values
- executable/operation allowlists
- typed argument validation
- least-privilege provider path
- Safe Mode and confirmation enforcement
- backup/rollback for destructive or migration actions
- secret storage and redaction
- diagnostic bundles exclude credentials
- update/package manifests and checksums are verified before application
- failure modes fail closed

## Phase 6 — Tests and proof

Require evidence proportional to risk:

- regression test for fixed bugs when a stable seam exists
- provider contract tests
- headless Avalonia tests for stable UI behavior
- Debug and Release builds where relevant
- target RID publish
- native runtime smoke
- package/installer/bundle validation for release changes

Inspect test quality. A test that cannot fail, asserts implementation trivia, or hides real dependencies behind excessive mocks is not meaningful proof.

## Phase 7 — Diff quality

Check for:

- duplicated policy
- shotgun changes across many modules
- feature envy or provider leakage
- primitive strings where typed models are needed
- repeated OS switches instead of a provider
- speculative abstractions
- hidden generated or binary churn
- comments that explain what the code already says rather than why the constraint exists

Repository-documented conventions override generic style preferences.

## Finding format

For each actionable finding include:

- severity: blocker, high, medium, or low
- exact file and line/range
- observable failure or risk
- why current tests do not protect it
- smallest credible correction

Do not bury blockers in a long list of cosmetic notes.

## Final review result

Return one:

- approve
- approve with non-blocking follow-ups
- request changes
- insufficient evidence

Summarize parity, runtime proof, security impact, and untested platforms.
