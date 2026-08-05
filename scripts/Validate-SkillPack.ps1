[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot

$Required = @(
    'AGENTS.md',
    'docs/agent-skills/README.md',
    'docs/agent-skills/INSTALL.md',
    'docs/agent-skills/SOURCES.md',
    'templates/change-evidence.md',
    'skills/graveops-engineering-router/SKILL.md',
    'skills/graveops-cross-platform-avalonia/SKILL.md',
    'skills/graveops-systematic-debugging/SKILL.md',
    'skills/graveops-test-first/SKILL.md',
    'skills/graveops-code-review/SKILL.md',
    'skills/graveops-safe-change-release/SKILL.md'
)

foreach ($RelativePath in $Required) {
    $Path = Join-Path $Root $RelativePath
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Missing required file: $RelativePath"
    }
}

$Names = [System.Collections.Generic.List[string]]::new()
$SkillFiles = Get-ChildItem -LiteralPath (Join-Path $Root 'skills') -Filter SKILL.md -File -Recurse | Sort-Object FullName
foreach ($Skill in $SkillFiles) {
    $Content = Get-Content -LiteralPath $Skill.FullName -Raw
    $Lines = $Content -split "`r?`n"
    if ($Lines[0] -ne '---') {
        throw "Missing YAML opener: $($Skill.FullName)"
    }

    $NameMatch = [regex]::Match($Content, '(?m)^name:\s*(.+)$')
    $DescriptionMatch = [regex]::Match($Content, '(?m)^description:\s*(.+)$')
    if (-not $NameMatch.Success) { throw "Missing name: $($Skill.FullName)" }
    if (-not $DescriptionMatch.Success) { throw "Missing description: $($Skill.FullName)" }
    if ($Content -notmatch '(?m)^#\s+\S') { throw "Missing H1: $($Skill.FullName)" }
    $Names.Add($NameMatch.Groups[1].Value.Trim())
}

$Duplicates = $Names | Group-Object | Where-Object Count -gt 1
if ($Duplicates) {
    throw "Duplicate skill names: $($Duplicates.Name -join ', ')"
}

$ScanRoots = @(
    (Join-Path $Root 'AGENTS.md'),
    (Join-Path $Root 'skills'),
    (Join-Path $Root 'templates')
)
$FilesToScan = foreach ($ScanRoot in $ScanRoots) {
    if (Test-Path -LiteralPath $ScanRoot -PathType Leaf) {
        Get-Item -LiteralPath $ScanRoot
    }
    else {
        Get-ChildItem -LiteralPath $ScanRoot -File -Recurse
    }
}

foreach ($File in $FilesToScan) {
    $Content = Get-Content -LiteralPath $File.FullName -Raw
    if ($Content -match '(?i)(^|[^a-z])(TODO|TBD|FIXME|PLACEHOLDER)([^a-z]|$)|\.\.\.') {
        throw "Unfinished placeholder marker found: $($File.FullName)"
    }
}

Write-Host "PASS: GraveOps agent skill pack is structurally valid ($($Names.Count) skills)."
