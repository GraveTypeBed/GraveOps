# Installing the GraveOps Skill Pack

## Repository-local installation

Place the contents of this directory at the GraveOps repository root:

```text
GraveOps/
├── AGENTS.md
├── skills/
├── templates/
├── scripts/
├── src/
├── tests/
└── ...
```

The root `AGENTS.md` is the canonical router. Keep one authoritative copy rather than maintaining divergent instructions per agent.

## Agent adapters

Different coding agents discover project instructions differently. Keep `AGENTS.md` canonical, then add a small adapter only when needed:

### GitHub Copilot

Copy `adapters/copilot-instructions.md` to:

```text
.github/copilot-instructions.md
```

### Claude Code

Copy `adapters/claude-project-instructions.md` to the project instruction location used by your Claude Code setup, or reference the repository `AGENTS.md` from the existing project instructions.

### Codex or AGENTS-aware agents

Use the root `AGENTS.md` directly.

## External supporting skills

Keep the existing `gh-fix-ci` skill available for GitHub Actions failures. Install only the specific official .NET skills that solve a recurring need, such as MSBuild diagnosis, structured build logging, project analysis, package validation, or test execution. They are supporting specialists, not the project router.

## Validation

Linux/macOS shell:

```bash
bash scripts/validate-skill-pack.sh
```

Windows PowerShell:

```powershell
pwsh -File scripts/Validate-SkillPack.ps1
```

Both validators confirm required files, YAML frontmatter, unique skill names, required sections, and the absence of unfinished placeholder markers.

## Updating the pack

When GraveOps architecture changes:

1. Update the Notion architecture, parity, security, and release pages first.
2. Update the relevant specialist skill.
3. Run both validation scripts when possible.
4. Review the instruction diff as production code.
5. Record why the rule changed in the commit or pull request.
