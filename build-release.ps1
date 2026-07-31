$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $Root 'src\GraveOps.App\GraveOps.App.csproj'
$Out = Join-Path $Root 'publish\win-x64'
$Dist = Join-Path $Root 'dist'

Write-Host '=== GraveOps 2.0 RC2 Build ===' -ForegroundColor DarkMagenta
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK is not installed. Install the .NET 10 SDK, then rerun this script.'
}

$info = & dotnet --version
if ($LASTEXITCODE -ne 0) { throw 'Unable to query the .NET SDK.' }
Write-Host "dotnet SDK: $info"

if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
New-Item -ItemType Directory -Force -Path $Out | Out-Null

& dotnet restore $Project
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE" }

& dotnet publish $Project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $Out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

$Exe = Join-Path $Out 'GraveOps.exe'
if (-not (Test-Path $Exe)) { throw "Publish completed but $Exe was not created." }

Write-Host ''
Write-Host "Build succeeded: $Exe" -ForegroundColor Green

# An installer is produced when Inno Setup 6 is installed, but the self-contained
# publish remains a valid RC build when ISCC is not available on a development PC.
$isccCandidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
)

$pathIscc = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
if ($pathIscc) {
    $isccCandidates += $pathIscc.Source
}

$isccCandidates = @(
    $isccCandidates |
        Where-Object { $_ -and (Test-Path $_) } |
        Select-Object -Unique
)

if ($isccCandidates.Count -gt 0) {
    $iscc = $isccCandidates[0]
    Write-Host "Inno Setup compiler: $iscc" -ForegroundColor Cyan
    if (Test-Path $Dist) { Remove-Item $Dist -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $Dist | Out-Null
    $iss = Join-Path $Root 'installer\GraveOps.iss'
    & $iscc "/O$Dist" $iss
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE" }
    Write-Host "Installer output: $Dist" -ForegroundColor Green
} else {
    Write-Host 'Inno Setup 6 not found; self-contained GraveOps.exe publish succeeded.' -ForegroundColor DarkGray
}
