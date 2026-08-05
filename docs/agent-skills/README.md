# GraveOps Agent Engineering Skills

A project-specific coding-agent workflow for GraveOps Control Center.

This pack is designed for the actual GraveOps development model:

- .NET 10 and C#
- Avalonia shared desktop UI
- Linux as the most mature Avalonia implementation and current reference behavior
- Windows Avalonia as the active migration target
- macOS as a planned provider and release target
- platform-specific providers behind shared contracts
- privileged operations, destructive-action safeguards, backups, diagnostics, packaging, and release evidence

It intentionally does **not** make a Microsoft-only skill pack the project methodology. Official .NET/MSBuild skills are retained as narrow specialists for build, test, package, and diagnostic problems. The primary workflow is cross-platform and GraveOps-specific.

## Included skills

1. **graveops-engineering-router** — selects the smallest workflow for each task.
2. **graveops-cross-platform-avalonia** — enforces shared Avalonia architecture, provider boundaries, parity, UI/runtime checks, and platform matrices.
3. **graveops-systematic-debugging** — reproduces, traces, hypothesizes, instruments, fixes, and regression-tests defects.
4. **graveops-test-first** — applies red/green/refactor at useful seams, including provider contracts and headless Avalonia UI tests.
5. **graveops-code-review** — reviews correctness, architecture, security, lifecycle, parity, and proof.
6. **graveops-safe-change-release** — governs exact-baseline patching, clean staging, privileged commands, packaging, checksums, signing, notarization, and rollback.

The root `AGENTS.md` is the entry point. It tells a compatible coding agent to load the router and then the smallest set of specialist skills.

## Recommended supporting skills

Use these only when their narrower trigger applies:

- `gh-fix-ci` for failing GitHub Actions checks and job logs.
- Official `dotnet/skills` MSBuild and test skills for build graph, binary-log, test-runner, package, or SDK-specific diagnosis.
- A repository-specific security scanner or dependency auditor when available.

Do not install a giant overlapping skill collection and invoke everything on every task. More prompts are not automatically more discipline.

## Immediate installation

Copy this pack into the GraveOps repository root so that `AGENTS.md`, `skills/`, `templates/`, and `scripts/` sit beside the solution and source directories. See `INSTALL.md` for adapter-specific options.

Then run one validator:

```bash
bash scripts/validate-skill-pack.sh
```

```powershell
pwsh -File scripts/Validate-SkillPack.ps1
```

## Definition of done

A GraveOps change is not complete merely because it compiles. Completion requires evidence appropriate to the scope:

- exact intended diff
- applicable unit, contract, integration, and headless UI tests
- Debug and Release build where relevant
- target RID publish where relevant
- XAML parse, event-handler, named-control, and control-type validation for UI changes
- runtime smoke validation on the affected platform
- documented parity status for Linux, Windows, and macOS
- security and destructive-action checks for privileged work
- fresh artifact staging, inventory, checksums, and rollback material for releases

## What this pack does not do

These files are agent instructions, not executable product features. They do not modify GraveOps source code by themselves. They become effective when installed in the repository and followed by the coding agent performing the work.
