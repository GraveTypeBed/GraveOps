[CmdletBinding()]
param(
    [string]$Root = 'C:\GraveOps\GraveOps-Community',
    [string]$CleanTemplate = '',
    [string]$Output = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($CleanTemplate)) {
    $CleanTemplate = Join-Path $env:USERPROFILE 'Downloads\GraveOps-2.0-RC2-runtime-polish-CLEAN.ps1'
}

if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $env:USERPROFILE 'Downloads\GraveOps-2.0-RC2-FROZEN.ps1'
}

$Root = [IO.Path]::GetFullPath($Root).TrimEnd('\')
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$tempRoot = Join-Path $env:TEMP "GraveOps-RC2-freeze-$stamp"
$payloadZip = Join-Path $tempRoot 'GraveOps-2.0-RC2-payload.zip'
$verifyRoot = Join-Path $tempRoot 'verify'
$manifestPath = [IO.Path]::ChangeExtension($Output, '.payload-manifest.txt')

function Assert-File {
    param([string]$Path, [string]$Label)
    if (-not (Test-Path $Path -PathType Leaf)) {
        throw "Missing ${Label}: $Path"
    }
}

function Assert-Contains {
    param([string]$Path, [string]$Needle, [string]$Label)

    Assert-File $Path $Label
    $text = [IO.File]::ReadAllText($Path)

    if ($text.IndexOf($Needle, [StringComparison]::Ordinal) -lt 0) {
        throw "Freeze gate failed: $Label"
    }
}

function Get-RelativePathForPayload {
    param([string]$FullName)

    $full = [IO.Path]::GetFullPath($FullName)
    if (-not $full.StartsWith($Root + '\', [StringComparison]::OrdinalIgnoreCase) -and
        -not $full.Equals($Root, [StringComparison]::OrdinalIgnoreCase)) {
        throw "File is outside GraveOps root: $full"
    }

    return $full.Substring($Root.Length).TrimStart('\').Replace('\','/')
}

function Get-SourcePayloadFiles {
    $items = New-Object System.Collections.Generic.List[object]

    $build = Join-Path $Root 'build-release.ps1'
    Assert-File $build 'build-release.ps1'

    $items.Add([pscustomobject]@{
        FullName = $build
        Relative = 'build-release.ps1'
    })

    foreach ($top in @('docs','installer','server-helpers','src')) {
        $dir = Join-Path $Root $top
        if (-not (Test-Path $dir -PathType Container)) {
            throw "Missing payload directory: $dir"
        }

        Get-ChildItem $dir -File -Recurse | ForEach-Object {
            $relative = Get-RelativePathForPayload $_.FullName

            if ($relative -notmatch '(^|/)(bin|obj)(/|$)') {
                $items.Add([pscustomobject]@{
                    FullName = $_.FullName
                    Relative = $relative
                })
            }
        }
    }

    return @($items | Sort-Object Relative)
}

function New-DeterministicPayloadZip {
    param(
        [object[]]$Files,
        [string]$Destination
    )

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    if (Test-Path $Destination) {
        Remove-Item $Destination -Force
    }

    $stream = [IO.File]::Open(
        $Destination,
        [IO.FileMode]::CreateNew,
        [IO.FileAccess]::ReadWrite,
        [IO.FileShare]::None
    )

    try {
        $archive = [System.IO.Compression.ZipArchive]::new(
            $stream,
            [IO.Compression.ZipArchiveMode]::Create,
            $true
        )

        try {
            $fixedTime = [DateTimeOffset]::new(
                2000, 1, 1, 0, 0, 0,
                [TimeSpan]::Zero
            )

            foreach ($file in $Files) {
                $entry = $archive.CreateEntry(
                    [string]$file.Relative,
                    [IO.Compression.CompressionLevel]::Optimal
                )
                $entry.LastWriteTime = $fixedTime

                $input = [IO.File]::OpenRead([string]$file.FullName)
                $outputStream = $entry.Open()

                try {
                    $input.CopyTo($outputStream)
                }
                finally {
                    $outputStream.Dispose()
                    $input.Dispose()
                }
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function ConvertTo-Base64Lines {
    param(
        [byte[]]$Bytes,
        [int]$Width = 120
    )

    $base64 = [Convert]::ToBase64String($Bytes)
    $lines = New-Object System.Collections.Generic.List[string]

    for ($i = 0; $i -lt $base64.Length; $i += $Width) {
        $length = [Math]::Min($Width, $base64.Length - $i)
        $lines.Add($base64.Substring($i, $length))
    }

    return ($lines -join "`r`n")
}

function Add-PackagingRegressionGates {
    param([string]$Text)

    $gateMarker = 'PASS: RC2 frozen packaging regression gate'
    if ($Text.Contains($gateMarker)) {
        return $Text
    }

    $anchor = @'
    Assert-Contains $iss '0C406F81-B8B5-4C27-922B-C2B38C9A1E5E' 'stable installer identity'
'@

    if (-not $Text.Contains($anchor)) {
        throw 'Could not locate stable installer identity gate in CLEAN template.'
    }

    $replacement = @'
    Assert-Contains $iss '0C406F81-B8B5-4C27-922B-C2B38C9A1E5E' 'stable installer identity'

    # Frozen RC2 packaging regressions.
    $stagedBuildRelease = Join-Path $Stage 'build-release.ps1'
    Assert-Contains $stagedBuildRelease "LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'" 'per-user Inno Setup discovery'
    Assert-Contains $stagedBuildRelease "Get-Command 'ISCC.exe'" 'PATH Inno Setup discovery fallback'
    Assert-Contains $iss 'UninstallDisplayIcon={app}\{#MyAppExeName}' 'installer uninstall icon metadata'
    Assert-Contains $iss 'VersionInfoVersion=2.0.0.0' 'numeric installer file version'
    Assert-Contains $iss 'VersionInfoProductVersion=2.0.0.0' 'numeric installer product version'
    Assert-Contains $iss 'VersionInfoProductTextVersion={#MyAppVersion}' 'RC2 textual installer version'
    Assert-Contains $iss 'CloseApplications=yes' 'installer close-running-app policy'
    Assert-Contains $iss 'RestartApplications=no' 'installer no-restart-app policy'
    Write-Host 'PASS: RC2 frozen packaging regression gate' -ForegroundColor Green
'@

    return $Text.Replace($anchor, $replacement)
}

function Verify-ExtractedPayload {
    param(
        [object[]]$SourceFiles,
        [string]$ExtractedRoot
    )

    $sourceMap = @{}
    foreach ($item in $SourceFiles) {
        $sourceMap[[string]$item.Relative] = (
            Get-FileHash -Path ([string]$item.FullName) -Algorithm SHA256
        ).Hash
    }

    $extractedMap = @{}
    $extractBase = [IO.Path]::GetFullPath($ExtractedRoot).TrimEnd('\')

    Get-ChildItem $ExtractedRoot -File -Recurse | ForEach-Object {
        $relative = $_.FullName.Substring($extractBase.Length).TrimStart('\').Replace('\','/')
        $extractedMap[$relative] = (
            Get-FileHash -Path $_.FullName -Algorithm SHA256
        ).Hash
    }

    $missing = @($sourceMap.Keys | Where-Object { -not $extractedMap.ContainsKey($_) })
    $extra = @($extractedMap.Keys | Where-Object { -not $sourceMap.ContainsKey($_) })
    $different = @(
        $sourceMap.Keys | Where-Object {
            $extractedMap.ContainsKey($_) -and
            $sourceMap[$_] -ne $extractedMap[$_]
        }
    )

    if ($missing.Count -gt 0 -or $extra.Count -gt 0 -or $different.Count -gt 0) {
        throw @"
Payload verification failed.
Missing: $($missing -join ', ')
Extra: $($extra -join ', ')
Different: $($different -join ', ')
"@
    }

    return $sourceMap
}

Write-Host '============================================================' -ForegroundColor Magenta
Write-Host ' GRAVEOPS 2.0 RC2 - FREEZE CLEAN PAYLOAD' -ForegroundColor Magenta
Write-Host ' Current validated tree -> deterministic embedded release' -ForegroundColor Magenta
Write-Host '============================================================' -ForegroundColor Magenta
Write-Host

try {
    Assert-File $CleanTemplate 'CLEAN RC2 bootstrap template'

    $project = Join-Path $Root 'src\GraveOps.App\GraveOps.App.csproj'
    $actionRunner = Join-Path $Root 'src\GraveOps.App\Services\ActionRunnerService.cs'
    $buildRelease = Join-Path $Root 'build-release.ps1'
    $iss = Join-Path $Root 'installer\GraveOps.iss'

    Assert-File $project 'GraveOps project'
    Assert-File $actionRunner 'ActionRunnerService.cs'
    Assert-File $buildRelease 'build-release.ps1'
    Assert-File $iss 'GraveOps.iss'

    Write-Host '[1/7] Verifying current RC2 release gates...' -ForegroundColor Cyan

    $actionText = [IO.File]::ReadAllText($actionRunner)
    if ($actionText -notmatch 'System\.Diagnostics\.Stopwatch\.StartNew\(\)') {
        throw 'ActionRunner qualified Stopwatch fix is missing.'
    }
    if ($actionText -match '(?<!System\.Diagnostics\.)Stopwatch\.StartNew\(\)') {
        throw 'Unqualified Stopwatch.StartNew() regression is present.'
    }

    Assert-Contains $buildRelease "LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'" 'per-user Inno Setup discovery'
    Assert-Contains $buildRelease "Get-Command 'ISCC.exe'" 'PATH Inno Setup discovery fallback'

    Assert-Contains $iss 'AppId={{0C406F81-B8B5-4C27-922B-C2B38C9A1E5E}' 'stable installer AppId'
    Assert-Contains $iss 'AppVersion={#MyAppVersion}' 'installer AppVersion'
    Assert-Contains $iss 'UninstallDisplayIcon={app}\{#MyAppExeName}' 'installer uninstall icon'
    Assert-Contains $iss 'VersionInfoVersion=2.0.0.0' 'installer file version'
    Assert-Contains $iss 'VersionInfoProductVersion=2.0.0.0' 'installer numeric product version'
    Assert-Contains $iss 'VersionInfoProductTextVersion={#MyAppVersion}' 'installer text product version'
    Assert-Contains $iss 'CloseApplications=yes' 'installer close applications policy'
    Assert-Contains $iss 'RestartApplications=no' 'installer restart applications policy'

    $templateText = [IO.File]::ReadAllText($CleanTemplate)
    if ($templateText.IndexOf(
        'PASS: ActionRunner Stopwatch regression gate',
        [StringComparison]::Ordinal
    ) -lt 0) {
        throw 'The selected bootstrap is not the CLEAN template with the permanent ActionRunner regression gate.'
    }

    Write-Host 'PASS: current RC2 source + packaging gates' -ForegroundColor Green

    Write-Host '[2/7] Running release build before freeze...' -ForegroundColor Cyan
    Get-Process GraveOps -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue

    & $buildRelease
    if ($LASTEXITCODE -ne 0) {
        throw "build-release.ps1 failed with exit code $LASTEXITCODE"
    }

    $installer = Join-Path $Root 'dist\GraveOps-Setup-2.0-RC2.exe'
    Assert-File $installer 'compiled RC2 installer'
    Write-Host 'PASS: release build + installer compile' -ForegroundColor Green

    Write-Host '[3/7] Collecting source payload...' -ForegroundColor Cyan
    $payloadFiles = @(Get-SourcePayloadFiles)
    if ($payloadFiles.Count -lt 140) {
        throw "Payload file count looks incomplete: $($payloadFiles.Count)"
    }

    $xamlCount = @(
        $payloadFiles | Where-Object { $_.Relative -like '*.xaml' }
    ).Count

    Write-Host "Payload files: $($payloadFiles.Count)"
    Write-Host "XAML files:   $xamlCount"

    New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null

    Write-Host '[4/7] Building deterministic embedded ZIP...' -ForegroundColor Cyan
    New-DeterministicPayloadZip -Files $payloadFiles -Destination $payloadZip

    $payloadHash = (Get-FileHash $payloadZip -Algorithm SHA256).Hash
    $payloadBytes = [IO.File]::ReadAllBytes($payloadZip)
    $payloadBase64 = ConvertTo-Base64Lines -Bytes $payloadBytes

    Write-Host "Payload ZIP:    $([Math]::Round($payloadBytes.Length / 1KB, 1)) KB"
    Write-Host "Payload SHA256: $payloadHash"
    Write-Host 'PASS: deterministic payload built' -ForegroundColor Green

    Write-Host '[5/7] Rebuilding CLEAN bootstrap with frozen payload...' -ForegroundColor Cyan

    $templateText = Add-PackagingRegressionGates $templateText

    $payloadRegex = [regex]::new(
        '(?s)\$Payload\s*=\s*@''\r?\n.*?\r?\n''@'
    )

    if ($payloadRegex.Matches($templateText).Count -ne 1) {
        throw 'Expected exactly one embedded $Payload here-string in CLEAN template.'
    }

    $payloadBlock = '$Payload = @''' + "`r`n" + $payloadBase64 + "`r`n'@"

    $templateText = $payloadRegex.Replace(
        $templateText,
        [Text.RegularExpressions.MatchEvaluator]{
            param($match)
            return $payloadBlock
        },
        1
    )

    $hashRegex = [regex]::new(
        '(?m)^\$ExpectedPayloadSha256\s*=\s*''[A-Fa-f0-9]{64}''\s*$'
    )

    if ($hashRegex.Matches($templateText).Count -ne 1) {
        throw 'Expected exactly one $ExpectedPayloadSha256 assignment in CLEAN template.'
    }

    $hashLine = '$ExpectedPayloadSha256 = ''' + $payloadHash + ''''

    $templateText = $hashRegex.Replace(
        $templateText,
        [Text.RegularExpressions.MatchEvaluator]{
            param($match)
            return $hashLine
        },
        1
    )

    $outputDir = Split-Path $Output -Parent
    if (-not [string]::IsNullOrWhiteSpace($outputDir)) {
        New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
    }

    if (Test-Path $Output) {
        Copy-Item $Output "$Output.pre-freeze-$stamp.bak" -Force
    }

    [IO.File]::WriteAllText(
        $Output,
        $templateText,
        [Text.UTF8Encoding]::new($true)
    )

    Write-Host "Frozen bootstrap: $Output" -ForegroundColor Green

    $tokens = $null
    $parseErrors = $null
    [Management.Automation.Language.Parser]::ParseFile(
        $Output,
        [ref]$tokens,
        [ref]$parseErrors
    ) | Out-Null

    if ($parseErrors.Count -gt 0) {
        $messages = @($parseErrors | ForEach-Object { $_.Message })
        throw "Frozen bootstrap PowerShell syntax failed: $($messages -join ' | ')"
    }

    Write-Host 'PASS: frozen bootstrap PowerShell syntax verified' -ForegroundColor Green

    Write-Host '[6/7] Verifying frozen bootstrap payload byte-for-byte...' -ForegroundColor Cyan

    $frozenText = [IO.File]::ReadAllText($Output)
    $payloadMatch = $payloadRegex.Match($frozenText)

    if (-not $payloadMatch.Success) {
        throw 'Frozen bootstrap payload could not be re-read.'
    }

    $payloadBodyMatch = [regex]::Match(
        $payloadMatch.Value,
        "(?s)@'\r?\n(.*?)\r?\n'@"
    )

    if (-not $payloadBodyMatch.Success) {
        throw 'Frozen bootstrap payload body could not be parsed.'
    }

    $decoded = [Convert]::FromBase64String(
        ($payloadBodyMatch.Groups[1].Value -replace '\s','')
    )

    $verifyZip = Join-Path $tempRoot 'verify-payload.zip'
    [IO.File]::WriteAllBytes($verifyZip, $decoded)

    $decodedHash = (Get-FileHash $verifyZip -Algorithm SHA256).Hash
    if ($decodedHash -ne $payloadHash) {
        throw "Frozen payload hash mismatch. Expected $payloadHash; got $decodedHash"
    }

    New-Item -ItemType Directory -Force -Path $verifyRoot | Out-Null
    Expand-Archive -Path $verifyZip -DestinationPath $verifyRoot -Force

    $sourceMap = Verify-ExtractedPayload -SourceFiles $payloadFiles -ExtractedRoot $verifyRoot

    Assert-Contains (Join-Path $verifyRoot 'build-release.ps1') "LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'" 'frozen Inno per-user discovery'
    Assert-Contains (Join-Path $verifyRoot 'installer\GraveOps.iss') 'VersionInfoProductTextVersion={#MyAppVersion}' 'frozen installer RC2 text version'
    Assert-Contains (Join-Path $verifyRoot 'installer\GraveOps.iss') 'CloseApplications=yes' 'frozen installer close applications policy'

    Write-Host "PASS: frozen payload matches all $($sourceMap.Count) current source files" -ForegroundColor Green

    Write-Host '[7/7] Writing frozen payload manifest...' -ForegroundColor Cyan

    $installerHash = (Get-FileHash $installer -Algorithm SHA256).Hash
    $bootstrapHash = (Get-FileHash $Output -Algorithm SHA256).Hash

    $manifest = @(
        'GRAVEOPS 2.0 RC2 FROZEN PAYLOAD MANIFEST'
        '========================================='
        "Frozen:             $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss K')"
        "Source root:        $Root"
        "Payload files:      $($payloadFiles.Count)"
        "Payload ZIP bytes:  $($payloadBytes.Length)"
        "Payload SHA256:     $payloadHash"
        "Bootstrap SHA256:   $bootstrapHash"
        "Installer SHA256:   $installerHash"
        ''
        'Validated pre-freeze release gates:'
        '- ActionRunner qualified Stopwatch regression fix'
        '- per-user + PATH Inno Setup discovery'
        '- stable installer AppId'
        '- numeric + textual RC2 version metadata'
        '- CloseApplications=yes'
        '- RestartApplications=no'
        ''
        'Payload contents:'
    )

    foreach ($relative in ($sourceMap.Keys | Sort-Object)) {
        $manifest += "$($sourceMap[$relative])  $relative"
    }

    [IO.File]::WriteAllLines(
        $manifestPath,
        $manifest,
        [Text.UTF8Encoding]::new($false)
    )

    Write-Host
    Write-Host '============================================================' -ForegroundColor Green
    Write-Host ' GRAVEOPS 2.0 RC2 PAYLOAD FROZEN SUCCESSFULLY' -ForegroundColor Green
    Write-Host '============================================================' -ForegroundColor Green
    Write-Host "Frozen bootstrap: $Output"
    Write-Host "Payload manifest: $manifestPath"
    Write-Host "Payload SHA256:   $payloadHash"
    Write-Host "Bootstrap SHA256: $bootstrapHash"
    Write-Host "Installer SHA256: $installerHash"
    Write-Host
    Write-Host 'NEXT: run the frozen bootstrap itself, then rerun the release validators.' -ForegroundColor Yellow
}
finally {
    Remove-Item $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
