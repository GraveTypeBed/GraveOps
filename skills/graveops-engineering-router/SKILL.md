---
name: graveops-engineering-router
description: Route any GraveOps coding task to the smallest sufficient combination of architecture, debugging, test-first, review, CI, security, and release workflows.
---

# GraveOps Engineering Router

## Purpose

Choose the workflow before touching code. Avoid both extremes: improvising without discipline and invoking every available skill for a one-line change.

## Always establish context

1. Identify repository, branch, commit, working-tree state, target platform, target RID, and requested outcome.
2. Read the nearest architecture, parity, security, and release documentation relevant to the touched area.
3. Identify whether the requested behavior is shared, platform-specific, legacy-maintenance-only, or a migration step.
4. Define observable acceptance evidence before implementation.

## Routing table

### A reported defect, crash, hang, regression, incorrect projection, or performance problem

Use, in order:

1. `graveops-systematic-debugging`
2. `graveops-cross-platform-avalonia` when UI, lifecycle, provider, platform, publish, or runtime behavior is involved
3. `graveops-test-first` for the regression test and fix
4. `graveops-code-review` before completion

### A new feature or behavior change

Use, in order:

1. `graveops-cross-platform-avalonia`
2. `graveops-test-first`
3. `graveops-code-review`
4. `graveops-safe-change-release` when privileged actions, destructive operations, configuration migration, packaging, or release artifacts are touched

### A refactor or architecture cleanup

Use, in order:

1. `graveops-cross-platform-avalonia`
2. `graveops-test-first` for contract and characterization coverage
3. `graveops-code-review`

The refactor must preserve behavior unless changed behavior is explicitly in scope.

### A failing GitHub Actions check

Use, in order:

1. `gh-fix-ci`
2. an official .NET/MSBuild or test specialist when logs point to build graph, SDK, package, or runner behavior
3. `graveops-systematic-debugging` if the failure is not isolated by CI logs
4. `graveops-code-review`

### A local build, SDK, restore, test-runner, or package-validation failure

Use, in order:

1. the narrow official .NET/MSBuild or test specialist
2. `graveops-systematic-debugging` if the first diagnostic pass does not isolate the cause
3. `graveops-cross-platform-avalonia` when the failure differs by OS or RID

### A release, installer, bundle, updater, signing, or artifact task

Use, in order:

1. `graveops-safe-change-release`
2. `graveops-cross-platform-avalonia`
3. `graveops-code-review`

### A privileged or destructive operation

Use, in order:

1. `graveops-safe-change-release`
2. `graveops-test-first`
3. `graveops-code-review`

Security and rollback rules are mandatory even when the user asks for speed.

### A pull request or requested review

Use:

1. `graveops-code-review`
2. `graveops-cross-platform-avalonia` for architecture/parity assessment
3. `gh-address-comments` only when the user specifically wants review feedback implemented

## Scope calibration

A tiny documentation or naming correction may need only this router plus review. A source change that affects one platform still requires a parity classification, but it does not require pretending all platforms were tested.

## Required opening statement

Before substantial work, state the selected skills and order in one sentence. Do not provide a ceremonial essay.

## Required completion statement

Report:

- files changed
- evidence run and exact results
- runtime status
- Linux/Windows/macOS parity classification
- release/security impact
- remaining limitations

Never substitute confidence language for evidence.
