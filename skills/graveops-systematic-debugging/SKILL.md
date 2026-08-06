---
name: graveops-systematic-debugging
description: Diagnose GraveOps defects by reproducing, tracing the real execution path, testing competing hypotheses, applying the smallest fix, and proving the regression is closed.
---

# GraveOps Systematic Debugging

## Rule zero

Do not start with a patch. Start with a reproducible observation or a bounded characterization of what cannot yet be reproduced.

## Phase 1 — Capture the failure

Record:

- exact user-visible symptom
- expected behavior
- platform, architecture, distro/OS version, desktop/session details, and target RID
- repository branch, commit, dirty-tree state, and build configuration
- exact executable or artifact launched
- artifact timestamp/hash when stale-output risk exists
- logs, exception, stack trace, exit code, screenshot, and triggering inputs
- whether the defect occurs in Debug, Release, published output, or packaged output

For intermittent failures, record frequency and the smallest reliable trigger window.

## Phase 2 — Reproduce and minimize

1. Reproduce using the same artifact and environment.
2. Remove irrelevant actions and data until the smallest failing scenario remains.
3. Compare a known-good baseline when available.
4. Distinguish source defect from stale artifact, configuration, permission, environment, provider, or packaging defect.
5. Preserve the original failing evidence.

## Phase 3 — Trace the actual path

Follow execution end to end. For an Avalonia action this commonly means:

`AXAML control → event/command binding → view model or code-behind → shared service → provider interface → platform provider → process/file/network/native API → typed result → shared state → UI projection`

Inspect every transition. Do not infer that a method is called merely because it exists.

## Phase 4 — Competing hypotheses

Write at least three plausible hypotheses for a non-trivial bug. Rank them by evidence, not intuition.

Examples:

- wrong control type or missing resource at runtime
- handler never wired or command cannot execute
- stale published artifact launched
- duplicate poller overwrites fresh state
- provider emits data but projection attributes it to the wrong owner
- native output parser assumes a format, locale, or indentation level
- permission/elevation path differs from development shell
- cancellation or disposal race
- platform-specific path, quoting, environment, or executable discovery

For each hypothesis, choose the cheapest observation that would discriminate it from the others.

## Phase 5 — Instrument, do not thrash

Add temporary structured evidence at boundaries:

- inputs with secrets redacted
- selected provider and capability
- command identity and typed arguments, never raw credential-bearing command strings
- start/end timestamps and cancellation state
- native exit code and bounded output
- state-version or snapshot identity
- UI lifecycle transitions

Remove or convert temporary instrumentation before completion.

## Phase 6 — Fix the root cause

- Make the smallest change that explains all observed evidence.
- Preserve platform boundaries.
- Avoid broad rewrites while the failure mechanism is uncertain.
- Add a regression test at the lowest stable seam that reproduces the defect.
- Add a higher-level test when the bug crossed layers and the lower test alone would not prevent recurrence.

## Stop rule

After three materially different fixes fail to change the evidence, stop editing. Revert speculative changes, return to reproduction and tracing, and reassess the model. Repeated patching without new evidence is not progress.

## Proof ladder

Run the narrowest proof first, then expand:

1. regression test fails before fix
2. regression test passes after fix
3. affected test project
4. full relevant test suite
5. Debug build
6. Release build
7. affected RID publish
8. XAML/static UI gates
9. headless UI scenario
10. native runtime smoke using the fresh artifact
11. packaged artifact smoke when packaging was implicated

## Completion report

State:

- root cause
- disproven alternatives
- exact fix
- regression coverage
- commands and results
- runtime artifact tested
- parity impact
- residual uncertainty

Do not use “should be fixed” when runtime proof is required but absent.
