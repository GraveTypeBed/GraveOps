[CmdletBinding()]
param(
    [string]$Root = 'C:\GraveOps\GraveOps-Community',
    [string]$InstallerPath = '',
    [string]$FunctionalValidator = '',
    [switch]$SkipFunctionalValidation,
    [switch]$SkipReinstallTest
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($InstallerPath)) {
    $InstallerPath = Join-Path $Root 'dist\GraveOps-Setup-2.0-RC2.exe'
}

if ([string]::IsNullOrWhiteSpace($FunctionalValidator)) {
    $FunctionalValidator = Join-Path $env:USERPROFILE 'Downloads\GraveOps-2.0-validation-v1.7.ps1'
}

$ResultsRoot = Join-Path $Root 'test-results'
New-Item -ItemType Directory -Force -Path $ResultsRoot | Out-Null

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$logPath = Join-Path $ResultsRoot "GraveOps-installer-validation-$stamp.log"
$setupLog = Join-Path $ResultsRoot "GraveOps-setup-$stamp.log"
$reinstallLog = Join-Path $ResultsRoot "GraveOps-reinstall-$stamp.log"
$backupRoot = Join-Path $ResultsRoot "installer-backups\$stamp"

$results = New-Object System.Collections.Generic.List[object]

# Script-scoped state populated as validation progresses.
$script:entry = $null
$script:installDir = $null
$script:installedExe = $null

function Write-Step {
    param([string]$Text)
    $line = "[$(Get-Date -Format 'HH:mm:ss')] $Text"
    Write-Host $line -ForegroundColor Cyan
    Add-Content -Path $logPath -Value $line -Encoding UTF8
}

function Add-Result {
    param(
        [ValidateSet('PASS','WARN','FAIL')]
        [string]$Status,
        [string]$Name,
        [string]$Detail
    )

    $obj = [pscustomobject]@{
        Status = $Status
        Name = $Name
        Detail = $Detail
    }
    $results.Add($obj)

    $color = switch ($Status) {
        'PASS' { 'Green' }
        'WARN' { 'Yellow' }
        'FAIL' { 'Red' }
    }

    $line = "[$Status] $Name - $Detail"
    Write-Host $line -ForegroundColor $color
    Add-Content -Path $logPath -Value $line -Encoding UTF8
}

function Invoke-Check {
    param(
        [string]$Name,
        [scriptblock]$Action
    )

    try {
        $detail = & $Action
        if ($detail -is [array]) { $detail = $detail -join '; ' }
        if ([string]::IsNullOrWhiteSpace([string]$detail)) { $detail = 'OK' }
        Add-Result PASS $Name ([string]$detail)
        return $true
    }
    catch {
        Add-Result FAIL $Name $_.Exception.Message
        return $false
    }
}

function Stop-GraveOps {
    $running = @(Get-Process GraveOps -ErrorAction SilentlyContinue)
    foreach ($p in $running) {
        try { $p.Kill() } catch {}
        try { [void]$p.WaitForExit(5000) } catch {}
    }
}

function Get-GraveOpsUninstallEntry {
    $roots = @(
        'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
    )

    $appIdNeedle = '0C406F81-B8B5-4C27-922B-C2B38C9A1E5E'

    $entries = @(
        Get-ItemProperty $roots -ErrorAction SilentlyContinue
    )

    if ($entries.Count -eq 0) {
        return $null
    }

    # Most reliable: Inno Setup uninstall key derived from AppId.
    $byAppId = @(
        $entries | Where-Object {
            $keyName = [string]$_.PSChildName
            -not [string]::IsNullOrWhiteSpace($keyName) -and
            $keyName.IndexOf($appIdNeedle, [StringComparison]::OrdinalIgnoreCase) -ge 0
        }
    ) | Select-Object -First 1

    if ($null -ne $byAppId) {
        return $byAppId
    }

    # Fallback: Inno may default DisplayName to "GraveOps 2.0.0-rc2"
    # rather than exactly "GraveOps".
    $byName = @(
        $entries | Where-Object {
            $props = $_.PSObject.Properties
            if ($null -eq $props['DisplayName']) { return $false }

            $displayName = [string]$props['DisplayName'].Value
            -not [string]::IsNullOrWhiteSpace($displayName) -and
            $displayName.StartsWith('GraveOps', [StringComparison]::OrdinalIgnoreCase)
        }
    ) | Select-Object -First 1

    if ($null -ne $byName) {
        return $byName
    }

    # Last registry fallback: identify the app by its installed executable icon.
    return @(
        $entries | Where-Object {
            $props = $_.PSObject.Properties
            if ($null -eq $props['DisplayIcon']) { return $false }

            $displayIcon = [string]$props['DisplayIcon'].Value
            -not [string]::IsNullOrWhiteSpace($displayIcon) -and
            $displayIcon -match '(?i)GraveOps\.exe'
        }
    ) | Select-Object -First 1
}

function Get-FileHashValue {
    param([string]$Path)
    if (-not (Test-Path $Path -PathType Leaf)) { return $null }
    return (Get-FileHash $Path -Algorithm SHA256).Hash
}

function Resolve-InstallDir {
    param($Entry)

    if ($null -ne $Entry) {
        $props = $Entry.PSObject.Properties

        if ($null -ne $props['InstallLocation']) {
            $installLocation = [string]$props['InstallLocation'].Value
            if (-not [string]::IsNullOrWhiteSpace($installLocation)) {
                return $installLocation.Trim('"').TrimEnd('\')
            }
        }

        if ($null -ne $props['DisplayIcon']) {
            $icon = [string]$props['DisplayIcon'].Value
            if (-not [string]::IsNullOrWhiteSpace($icon)) {
                $icon = $icon.Trim('"')
                if ($icon -match ',\d+$') {
                    $icon = $icon -replace ',\d+$',''
                }
                if (Test-Path $icon -PathType Leaf) {
                    return Split-Path $icon -Parent
                }
            }
        }

        if ($null -ne $props['UninstallString']) {
            $uninstall = [string]$props['UninstallString'].Value
            if (-not [string]::IsNullOrWhiteSpace($uninstall)) {
                if ($uninstall -match '^\s*"([^"]+)"') {
                    $uninstaller = $matches[1]
                }
                else {
                    $uninstaller = ($uninstall -split '\s+')[0]
                }

                if (Test-Path $uninstaller -PathType Leaf) {
                    return Split-Path $uninstaller -Parent
                }
            }
        }
    }

    # Registry-independent fallback: resolve the Start Menu shortcut target.
    $startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
    $shortcut = @(
        Get-ChildItem $startMenu -Filter 'GraveOps.lnk' -File -Recurse -ErrorAction SilentlyContinue
    ) | Select-Object -First 1

    if ($null -ne $shortcut) {
        $shell = New-Object -ComObject WScript.Shell
        $target = $shell.CreateShortcut($shortcut.FullName).TargetPath

        if (-not [string]::IsNullOrWhiteSpace($target) -and
            (Test-Path $target -PathType Leaf)) {
            return Split-Path $target -Parent
        }
    }

    throw 'Unable to resolve GraveOps install directory from registry or Start Menu shortcut.'
}

function Invoke-Setup {
    param(
        [string]$LogFile
    )

    $args = @(
        '/VERYSILENT',
        '/SUPPRESSMSGBOXES',
        '/NORESTART',
        '/NORESTARTAPPLICATIONS',
        "/LOG=$LogFile"
    )

    $p = Start-Process -FilePath $InstallerPath -ArgumentList $args -PassThru -Wait
    if ($p.ExitCode -ne 0) {
        throw "Setup exited with code $($p.ExitCode). See $LogFile"
    }

    return "Setup exit code 0; log $LogFile"
}

Write-Host '============================================================'
Write-Host ' GRAVEOPS 2.0 INSTALLER VALIDATION'
Write-Host ' Silent install + preservation + installed-copy validation'
Write-Host '============================================================'
Write-Host

$canContinue = Invoke-Check 'Installer exists' {
    if (-not (Test-Path $InstallerPath -PathType Leaf)) {
        throw "Missing installer: $InstallerPath"
    }
    $hash = Get-FileHashValue $InstallerPath
    "$InstallerPath | SHA256 $hash"
}

if (-not $canContinue) { exit 1 }

Invoke-Check 'No existing GraveOps process' {
    Stop-GraveOps
    if (Get-Process GraveOps -ErrorAction SilentlyContinue) {
        throw 'GraveOps is still running.'
    }
    'No GraveOps processes running'
} | Out-Null

$appDataDir = Join-Path $env:APPDATA 'GraveOps'
$configPath = Join-Path $appDataDir 'config.json'
$configHashBefore = Get-FileHashValue $configPath

Invoke-Check 'Back up GraveOps user data' {
    New-Item -ItemType Directory -Force -Path $backupRoot | Out-Null

    if (Test-Path $appDataDir -PathType Container) {
        Copy-Item $appDataDir (Join-Path $backupRoot 'GraveOps-AppData') -Recurse -Force
        "Backed up $appDataDir to $backupRoot"
    }
    else {
        'No existing %APPDATA%\GraveOps directory; backup directory created'
    }
} | Out-Null

$installed = Invoke-Check 'Silent install' {
    Invoke-Setup $setupLog
}

if (-not $installed) { exit 1 }

$entry = Get-GraveOpsUninstallEntry
$installDir = $null
$installedExe = $null

Invoke-Check 'Uninstall registration exists' {
    $script:entry = Get-GraveOpsUninstallEntry
    if ($null -eq $script:entry) {
        throw 'No GraveOps uninstall registration could be identified by AppId, display name, or display icon.'
    }

    $props = $script:entry.PSObject.Properties
    $displayName = if ($null -ne $props['DisplayName']) { [string]$props['DisplayName'].Value } else { '<none>' }
    $displayVersion = if ($null -ne $props['DisplayVersion']) { [string]$props['DisplayVersion'].Value } else { '<none>' }

    "Key=$($script:entry.PSChildName) | DisplayName=$displayName | DisplayVersion=$displayVersion"
} | Out-Null

Invoke-Check 'Installed executable exists' {
    $script:installDir = Resolve-InstallDir $script:entry
    $script:installedExe = Join-Path $script:installDir 'GraveOps.exe'

    if (-not (Test-Path $script:installedExe -PathType Leaf)) {
        throw "Installed GraveOps.exe missing: $script:installedExe"
    }

    $script:installedExe
} | Out-Null

Invoke-Check 'Installed binary matches published binary' {
    if ([string]::IsNullOrWhiteSpace([string]$script:installedExe)) {
        throw 'Installed executable path was not resolved.'
    }

    $publishedExe = Join-Path $Root 'publish\win-x64\GraveOps.exe'
    if (-not (Test-Path $publishedExe -PathType Leaf)) {
        throw "Published executable missing: $publishedExe"
    }

    $publishedHash = Get-FileHashValue $publishedExe
    $installedHash = Get-FileHashValue $script:installedExe

    if ($publishedHash -ne $installedHash) {
        throw "Hash mismatch. Published=$publishedHash Installed=$installedHash"
    }

    "SHA256 $installedHash"
} | Out-Null

Invoke-Check 'Install preserved existing config before first launch' {
    $after = Get-FileHashValue $configPath

    if ($null -eq $configHashBefore -and $null -eq $after) {
        return 'No pre-existing config.json was present'
    }

    if ($configHashBefore -ne $after) {
        throw "config.json changed during installation. Before=$configHashBefore After=$after"
    }

    "config.json unchanged: $after"
} | Out-Null

Invoke-Check 'Start Menu shortcut exists' {
    $startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
    $matches = @(
        Get-ChildItem $startMenu -Filter 'GraveOps.lnk' -File -Recurse -ErrorAction SilentlyContinue
    )

    if ($matches.Count -eq 0) {
        throw 'GraveOps.lnk not found in current-user Start Menu.'
    }

    $matches[0].FullName
} | Out-Null

if (-not $SkipFunctionalValidation) {
    Invoke-Check 'Installed-copy functional validation' {
        if (-not (Test-Path $FunctionalValidator -PathType Leaf)) {
            throw "Functional validator missing: $FunctionalValidator"
        }

        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $FunctionalValidator `
            -Root $Root `
            -ExePath $script:installedExe `
            -SkipBuild

        if ($LASTEXITCODE -ne 0) {
            throw "Installed-copy validator exited with code $LASTEXITCODE"
        }

        'Installed GraveOps passed V1.7 functional/lifecycle validation'
    } | Out-Null
}
else {
    Add-Result WARN 'Installed-copy functional validation' 'Skipped by -SkipFunctionalValidation'
}

if (-not $SkipReinstallTest) {
    Stop-GraveOps
    $configHashBeforeReinstall = Get-FileHashValue $configPath

    Invoke-Check 'Same-version reinstall' {
        Invoke-Setup $reinstallLog
    } | Out-Null

    Invoke-Check 'Reinstall preserved user config' {
        $after = Get-FileHashValue $configPath

        if ($configHashBeforeReinstall -ne $after) {
            throw "config.json changed during reinstall. Before=$configHashBeforeReinstall After=$after"
        }

        if ($null -eq $after) {
            'No config.json existed before or after reinstall'
        }
        else {
            "config.json unchanged by reinstall: $after"
        }
    } | Out-Null

    Invoke-Check 'Installed executable still matches published binary after reinstall' {
        if ([string]::IsNullOrWhiteSpace([string]$script:installedExe)) {
            throw 'Installed executable path was not resolved.'
        }

        $publishedExe = Join-Path $Root 'publish\win-x64\GraveOps.exe'
        $publishedHash = Get-FileHashValue $publishedExe
        $installedHash = Get-FileHashValue $script:installedExe

        if ($publishedHash -ne $installedHash) {
            throw "Hash mismatch after reinstall. Published=$publishedHash Installed=$installedHash"
        }

        "SHA256 $installedHash"
    } | Out-Null
}
else {
    Add-Result WARN 'Same-version reinstall' 'Skipped by -SkipReinstallTest'
}

Stop-GraveOps

$pass = @($results | Where-Object Status -eq 'PASS').Count
$warn = @($results | Where-Object Status -eq 'WARN').Count
$fail = @($results | Where-Object Status -eq 'FAIL').Count

Write-Host
Write-Host '============================================================'
Write-Host ' INSTALLER VALIDATION RESULT'
Write-Host '============================================================'
Write-Host " PASS: $pass   WARN: $warn   FAIL: $fail"
Write-Host " Log:  $logPath"
Write-Host " Backup: $backupRoot"
if ($script:installedExe) {
    Write-Host " Installed EXE: $script:installedExe"
}
Write-Host '============================================================'

if ($fail -gt 0) {
    Write-Host ' RESULT: FAIL' -ForegroundColor Red
    exit 1
}
elseif ($warn -gt 0) {
    Write-Host ' RESULT: PASS WITH WARNINGS' -ForegroundColor Yellow
    exit 0
}
else {
    Write-Host ' RESULT: PASS' -ForegroundColor Green
    exit 0
}
