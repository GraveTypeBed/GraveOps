---
name: graveops-safe-change-release
description: Protect GraveOps source mutations, privileged commands, configuration migrations, packaging, signing, artifacts, and rollback with exact baselines and auditable release evidence.
---

# GraveOps Safe Change and Release

## Part A — Safe source change

### Preflight

Before an automated patch or migration:

1. confirm repository root and intended branch
2. record commit and dirty-tree state
3. stop on unexpected local changes unless they are explicitly in scope
4. verify exact source baseline by commit, blob hash, or complete structural precondition
5. create a bounded backup or restore point when rollback is not trivial
6. verify required tools and SDK versions

Never apply a plausible historical patch to unknown source.

### Patch behavior

- Prefer AST-aware or structure-aware edits.
- For exact text replacement, require exactly one expected match unless multiple replacements are intentional and counted.
- Handle CRLF/LF deliberately.
- Fail before writing when preconditions do not match.
- Write atomically where practical.
- Produce an explicit changed-file list and diff.
- Do not hide unrelated generated files in the patch.

### Build isolation

Builds and tests must not mutate product source, user configuration, live server state, or release staging. Generated output belongs in known ignored directories.

## Part B — Privileged and destructive command safety

### Command model

Represent operations as typed data:

- executable identity
- validated argument list
- working directory
- environment allowlist
- timeout
- cancellation
- privilege requirement
- destructive classification
- redaction policy

Do not pass interpolated user/server values through a general shell string.

### Enforcement

- centralize command execution
- allowlist executable and operation combinations
- validate paths against approved roots
- require Safe Mode policy check
- require explicit confirmation for destructive operations
- verify backup/rollback prerequisite
- capture bounded stdout/stderr and exit code
- redact secrets before persistence or display
- fail closed on unsupported providers or ambiguous privilege state

## Part C — Release staging

### Freshness

1. begin from a clean tree and approved commit
2. delete and recreate the platform/RID staging directory
3. build, test, and publish from source in the current run
4. verify artifact timestamps and source/version identity
5. stage only approved paths
6. reject unexpected files, secrets, debug symbols, local configuration, caches, backups, and prior installers unless explicitly required

### Artifact inventory

Generate a machine-readable and human-readable inventory containing:

- product version
- commit
- platform and RID
- build configuration
- file paths and sizes
- SHA-256 hashes
- creation timestamp
- signing/notarization status
- test/runtime evidence reference

### Platform gates

#### Linux

Validate executable permissions, dependencies, desktop integration, package/archive layout, native runtime launch, and update/rollback behavior.

#### Windows

Validate apphost/nativehost, installer scope, install/reinstall/uninstall, preserved user configuration, signatures, updater behavior, and launch from the installed location.

#### macOS

Validate `.app` structure, `Info.plist`, resources, architecture, executable permissions, code-sign chain, hardened runtime and entitlements, notarization result, stapled ticket, quarantine/Gatekeeper launch behavior, and DMG/PKG contents when used.

Signing is performed after bundle contents are final. Any content change after signing invalidates the prior signing evidence.

## Part D — Rollback and apply kits

A consequential change or release must include:

- pre-change backup or versioned prior artifact
- exact apply steps
- exact verification steps
- exact rollback trigger
- exact rollback steps
- post-rollback verification

Do not describe rollback as “restore the backup” without naming the artifact and command/path.

## Part E — Release completion

A release is complete only when:

- source preflight passed
- tests/build/publish gates passed
- native runtime smoke passed
- staging inventory is clean
- secrets audit passed
- hashes were generated from final bytes
- signatures/notarization were verified where applicable
- install/update/rollback behavior was exercised where applicable
- parity ledger and release documentation were updated

Keep build completion, runtime completion, package completion, and cross-platform parity as separate statuses.
