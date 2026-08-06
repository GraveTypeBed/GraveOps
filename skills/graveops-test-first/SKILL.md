---
name: graveops-test-first
description: Implement GraveOps behavior with red-green-refactor at stable seams, emphasizing provider contracts, parsers, safety policy, shared state, and Avalonia headless UI behavior.
---

# GraveOps Test-First Implementation

## Goal

Create fast feedback without producing brittle tests that merely mirror implementation details.

## Choose the seam first

Prefer tests around:

- domain policy and state transitions
- platform-provider contracts
- native-output parsers and fixtures
- command allowlists, argument validation, confirmation, Safe Mode, and rollback policy
- configuration migration
- update/release manifest validation
- view-model behavior
- Avalonia headless control tree, bindings, commands, event effects, and interaction states
- integration between shared service and a fake or controlled provider

Avoid making every private helper a public test seam.

## Red

1. Write one test that expresses the externally observable behavior.
2. Confirm it fails for the expected reason.
3. If it passes immediately, prove the test can fail before trusting it.
4. Keep the test focused enough that its failure identifies one behavior.

For a bug, encode the real regression input whenever it is safe and stable.

## Green

1. Implement the smallest production change that satisfies the test.
2. Do not add unrelated abstractions or cleanup.
3. Keep native mechanics behind providers.
4. Preserve cancellation, timeout, redaction, and lifecycle behavior.

## Refactor

After green:

- improve names and module depth
- remove duplication
- simplify interfaces
- centralize ownership
- retain behavior under the tests
- rerun the focused and relevant broader suites

## Test taxonomy for GraveOps

### Unit tests

Use for pure policy, formatting, validation, state transitions, and parsers.

### Contract tests

Run the same behavioral expectations against each platform-provider implementation or controlled adapter. Unsupported capabilities must be explicit typed outcomes, not exceptions hidden as success.

### Integration tests

Use for process execution, filesystem staging, configuration, local HTTP, or service boundaries. Use isolated temporary directories and controlled fixtures. Never mutate the operator’s live server during ordinary tests.

### Avalonia headless tests

Use for:

- view creation and AXAML resource loading
- named controls and expected types
- bindings and command enablement
- navigation and selection state
- loading/empty/error/stale/populated projection
- pointer, keyboard, and focus behavior where stable
- layout invariants that do not require pixel-perfect native rendering

Headless tests complement native smoke tests; they do not replace them.

### Native runtime smoke tests

Use the freshly built/published artifact on the affected OS. Keep a concise checklist for launch, primary navigation, changed action, cancellation/close, and diagnostic log review.

## Safety tests

Every privileged or destructive path should cover:

- Safe Mode blocks mutation
- confirmation is required
- executable and operation are allowlisted
- arguments remain typed and validated
- timeout and cancellation work
- backup/restore or rollback prerequisite is enforced
- secrets are redacted from logs and diagnostics
- unsupported platform behavior fails closed

## Flake prevention

- Use deterministic clocks, schedulers, IDs, and temporary paths where practical.
- Do not sleep when an observable completion signal exists.
- Isolate global Avalonia runtime state according to the chosen test framework.
- Dispose subscriptions, timers, streams, processes, and test windows.
- Keep tests order-independent.

## Completion gate

Do not merge a test-first change until:

- the new test was observed failing for the intended reason
- it passes after the change
- relevant existing suites pass
- native runtime proof is supplied when the change crosses into UI, platform, privilege, or packaging behavior
