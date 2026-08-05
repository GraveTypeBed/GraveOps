#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

required=(
  "AGENTS.md"
  "docs/agent-skills/README.md"
  "docs/agent-skills/INSTALL.md"
  "docs/agent-skills/SOURCES.md"
  "templates/change-evidence.md"
  "skills/graveops-engineering-router/SKILL.md"
  "skills/graveops-cross-platform-avalonia/SKILL.md"
  "skills/graveops-systematic-debugging/SKILL.md"
  "skills/graveops-test-first/SKILL.md"
  "skills/graveops-code-review/SKILL.md"
  "skills/graveops-safe-change-release/SKILL.md"
)

for path in "${required[@]}"; do
  [[ -f "$ROOT/$path" ]] || { echo "FAIL: missing $path" >&2; exit 1; }
done

names=()
while IFS= read -r -d '' skill; do
  first="$(sed -n '1p' "$skill")"
  [[ "$first" == "---" ]] || { echo "FAIL: no YAML opener: $skill" >&2; exit 1; }
  name="$(sed -n 's/^name:[[:space:]]*//p' "$skill" | head -n1)"
  description="$(sed -n 's/^description:[[:space:]]*//p' "$skill" | head -n1)"
  [[ -n "$name" ]] || { echo "FAIL: missing name: $skill" >&2; exit 1; }
  [[ -n "$description" ]] || { echo "FAIL: missing description: $skill" >&2; exit 1; }
  grep -q '^# ' "$skill" || { echo "FAIL: missing H1: $skill" >&2; exit 1; }
  names+=("$name")
done < <(find "$ROOT/skills" -name SKILL.md -print0 | sort -z)

if [[ "$(printf '%s\n' "${names[@]}" | sort | uniq -d | wc -l)" -ne 0 ]]; then
  echo "FAIL: duplicate skill names" >&2
  printf '%s\n' "${names[@]}" | sort | uniq -d >&2
  exit 1
fi

if grep -RInE '(^|[^[:alpha:]])(TODO|TBD|FIXME|PLACEHOLDER)([^[:alpha:]]|$)|\.\.\.' \
  "$ROOT/AGENTS.md" "$ROOT/skills" "$ROOT/templates"; then
  echo "FAIL: unfinished placeholder marker found" >&2
  exit 1
fi

echo "PASS: GraveOps agent skill pack is structurally valid (${#names[@]} skills)."
