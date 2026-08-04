[CmdletBinding()]
param(
    [string]$Runtime = "win-x64",
    [string]$LegacyRoot = "C:\GraveOps\GraveOps-Control-Center"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $Root "src\GraveOps.Desktop.Windows\GraveOps.Desktop.Windows.csproj"
$Output = Join-Path $Root "publish\win-x64-avalonia"
$LegacyProject = Join-Path $LegacyRoot "src\GraveOps.App\GraveOps.App.csproj"
$LegacyBuild = Join-Path $LegacyRoot "build-release.ps1"
$LegacyExecutable = Join-Path $LegacyRoot "publish\win-x64\GraveOps.exe"

function Hash-OrMissing([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return "<missing>"
    }

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

if (-not (Test-Path -LiteralPath $Project)) {
    throw "Windows Avalonia project not found: $Project"
}

$legacyProjectBefore = Hash-OrMissing $LegacyProject
$legacyBuildBefore = Hash-OrMissing $LegacyBuild
$legacyExecutableBefore = Hash-OrMissing $LegacyExecutable

Write-Host "Building GraveOps Windows Avalonia Preview..." -ForegroundColor DarkYellow
Write-Host "Legacy output remains reserved: publish\win-x64" -ForegroundColor Cyan
Write-Host "Preview output: $Output" -ForegroundColor Cyan

& dotnet restore $Project
if ($LASTEXITCODE -ne 0) { throw "Restore failed." }

& dotnet build $Project -c Debug --no-restore -warnaserror
if ($LASTEXITCODE -ne 0) { throw "Debug build failed." }

& dotnet build $Project -c Release --no-restore -warnaserror
if ($LASTEXITCODE -ne 0) { throw "Release build failed." }

Remove-Item -LiteralPath $Output -Recurse -Force -ErrorAction SilentlyContinue

& dotnet publish $Project `
    -c Release `
    -r $Runtime `
    --self-contained false `
    -warnaserror `
    -o $Output

if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

$exe = Join-Path $Output "GraveOps.Avalonia.Windows.exe"
if (-not (Test-Path -LiteralPath $exe)) {
    throw "Publish completed but the preview executable was not created: $exe"
}

if ((Hash-OrMissing $LegacyProject) -ne $legacyProjectBefore) {
    throw "Legacy WPF project changed during the Avalonia build."
}
if ((Hash-OrMissing $LegacyBuild) -ne $legacyBuildBefore) {
    throw "Legacy WPF build script changed during the Avalonia build."
}
if ((Hash-OrMissing $LegacyExecutable) -ne $legacyExecutableBefore) {
    throw "Legacy WPF executable changed during the Avalonia build."
}

Write-Host ""
Write-Host "============================================================" -ForegroundColor Green
Write-Host " GRAVEOPS WINDOWS AVALONIA PREVIEW BUILD SUCCEEDED" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green
Write-Host "Run: $exe"
Write-Host "The WPF legacy project and publish\win-x64 output were not replaced." -ForegroundColor Cyan
