[CmdletBinding()]
param(
    [string]$Root = 'C:\GraveOps\GraveOps-Community',
    [string]$InstallerPath = '',
    [switch]$LeaveUninstalled
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($InstallerPath)) {
    $InstallerPath = Join-Path $Root 'dist\GraveOps-Setup-2.0-RC2.exe'
}

$ResultsRoot = Join-Path $Root 'test-results'
New-Item -ItemType Directory -Force -Path $ResultsRoot | Out-Null

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$logPath = Join-Path $ResultsRoot "GraveOps-uninstall-validation-$stamp.log"
$uninstallLog = Join-Path $ResultsRoot "GraveOps-uninstall-$stamp.log"
$reinstallLog = Join-Path $ResultsRoot "GraveOps-post-uninstall-reinstall-$stamp.log"
$backupRoot = Join-Path $ResultsRoot "uninstall-backups\$stamp"

$results = New-Object System.Collections.Generic.List[object]
$script:entry = $null
$script:installDir = $null
$script:installedExe = $null
$script:uninstaller = $null

function Add-Result {
    param(
        [ValidateSet('PASS','WARN','FAIL')]
        [string]$Status,
        [string]$Name,
        [string]$Detail
    )

    $result = [pscustomobject]@{
        Status = $Status
        Name = $Name
        Detail = $Detail
    }

    $results.Add($result)

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

function Get-HashOrNull {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) { return $null }
    if (-not (Test-Path $Path -PathType Leaf)) { return $null }

    return (Get-FileHash $Path -Algorithm SHA256).Hash
}

function Get-GraveOpsUninstallEntry {
    $roots = @(
        'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
    )

    $appIdNeedle = '0C406F81-B8B5-4C27-922B-C2B38C9A1E5E'
    $entries = @(Get-ItemProperty $roots -ErrorAction SilentlyContinue)

    return @(
        $entries | Where-Object {
            $keyName = [string]$_.PSChildName
            -not [string]::IsNullOrWhiteSpace($keyName) -and
            $keyName.IndexOf($appIdNeedle, [StringComparison]::OrdinalIgnoreCase) -ge 0
        }
    ) | Select-Object -First 1
}

function Get-PropertyValue {
    param(
        $Object,
        [string]$Name
    )

    if ($null -eq $Object) { return $null }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }

    return [string]$property.Value
}

function Resolve-Uninstaller {
    param($Entry)

    $quiet = Get-PropertyValue $Entry 'QuietUninstallString'
    $normal = Get-PropertyValue $Entry 'UninstallString'

    foreach ($candidate in @($quiet, $normal)) {
        if ([string]::IsNullOrWhiteSpace($candidate)) { continue }

        if ($candidate -match '^\s*"([^"]+)"') {
            $path = $matches[1]
        }
        else {
            $path = ($candidate -split '\s+')[0]
        }

        if (Test-Path $path -PathType Leaf) {
            return $path
        }
    }

    throw 'Unable to resolve the GraveOps uninstaller executable.'
}

function Resolve-InstallDir {
    param($Entry)

    $location = Get-PropertyValue $Entry 'InstallLocation'
    if (-not [string]::IsNullOrWhiteSpace($location)) {
        return $location.Trim('"').TrimEnd('\')
    }

    $icon = Get-PropertyValue $Entry 'DisplayIcon'
    if (-not [string]::IsNullOrWhiteSpace($icon)) {
        $iconPath = $icon.Trim('"') -replace ',\d+$',''
        if (Test-Path $iconPath -PathType Leaf) {
            return Split-Path $iconPath -Parent
        }
    }

    $uninstaller = Resolve-Uninstaller $Entry
    return Split-Path $uninstaller -Parent
}

function Get-StartMenuShortcut {
    $startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
    return @(
        Get-ChildItem $startMenu -Filter 'GraveOps.lnk' -File -Recurse -ErrorAction SilentlyContinue
    ) | Select-Object -First 1
}

function Invoke-SilentInstall {
    param([string]$LogFile)

    if (-not (Test-Path $InstallerPath -PathType Leaf)) {
        throw "Installer missing: $InstallerPath"
    }

    $args = @(
        '/VERYSILENT',
        '/SUPPRESSMSGBOXES',
        '/NORESTART',
        '/NORESTARTAPPLICATIONS',
        "/LOG=$LogFile"
    )

    $p = Start-Process -FilePath $InstallerPath -ArgumentList $args -PassThru -Wait
    if ($p.ExitCode -ne 0) {
        throw "Installer exited with code $($p.ExitCode). See $LogFile"
    }

    return "Installer exit code 0; log $LogFile"
}

Write-Host '============================================================'
Write-Host ' GRAVEOPS 2.0 UNINSTALL VALIDATION'
Write-Host ' Remove app + preserve user data + optional reinstall'
Write-Host '============================================================'
Write-Host

$ready = Invoke-Check 'Installed GraveOps registration exists' {
    $script:entry = Get-GraveOpsUninstallEntry
    if ($null -eq $script:entry) {
        throw 'GraveOps uninstall registration was not found by AppId.'
    }

    $displayName = Get-PropertyValue $script:entry 'DisplayName'
    $displayVersion = Get-PropertyValue $script:entry 'DisplayVersion'

    "Key=$($script:entry.PSChildName) | DisplayName=$displayName | DisplayVersion=$displayVersion"
}

if (-not $ready) { exit 1 }

Invoke-Check 'Resolve installed copy and uninstaller' {
    $script:installDir = Resolve-InstallDir $script:entry
    $script:installedExe = Join-Path $script:installDir 'GraveOps.exe'
    $script:uninstaller = Resolve-Uninstaller $script:entry

    if (-not (Test-Path $script:installedExe -PathType Leaf)) {
        throw "Installed GraveOps.exe missing: $script:installedExe"
    }

    if (-not (Test-Path $script:uninstaller -PathType Leaf)) {
        throw "Uninstaller missing: $script:uninstaller"
    }

    "Installed=$script:installedExe | Uninstaller=$script:uninstaller"
} | Out-Null

Invoke-Check 'Stop GraveOps before uninstall' {
    Stop-GraveOps

    if (Get-Process GraveOps -ErrorAction SilentlyContinue) {
        throw 'GraveOps is still running.'
    }

    'No GraveOps processes running'
} | Out-Null

$appDataDir = Join-Path $env:APPDATA 'GraveOps'
$configPath = Join-Path $appDataDir 'config.json'
$configHashBefore = Get-HashOrNull $configPath

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

$publishedExe = Join-Path $Root 'publish\win-x64\GraveOps.exe'
$publishedHashBefore = Get-HashOrNull $publishedExe

Invoke-Check 'Silent uninstall' {
    $args = @(
        '/VERYSILENT',
        '/SUPPRESSMSGBOXES',
        '/NORESTART',
        "/LOG=$uninstallLog"
    )

    $p = Start-Process -FilePath $script:uninstaller -ArgumentList $args -PassThru -Wait
    if ($p.ExitCode -ne 0) {
        throw "Uninstaller exited with code $($p.ExitCode). See $uninstallLog"
    }

    "Uninstaller exit code 0; log $uninstallLog"
} | Out-Null

Invoke-Check 'Installed executable removed' {
    if (Test-Path $script:installedExe -PathType Leaf) {
        throw "Installed executable still exists: $script:installedExe"
    }

    'Installed GraveOps.exe removed'
} | Out-Null

Invoke-Check 'Uninstall registration removed' {
    $remaining = Get-GraveOpsUninstallEntry
    if ($null -ne $remaining) {
        throw "GraveOps uninstall key still exists: $($remaining.PSChildName)"
    }

    'GraveOps uninstall registration removed'
} | Out-Null

Invoke-Check 'Start Menu shortcut removed' {
    $shortcut = Get-StartMenuShortcut
    if ($null -ne $shortcut) {
        throw "Start Menu shortcut still exists: $($shortcut.FullName)"
    }

    'GraveOps Start Menu shortcut removed'
} | Out-Null

Invoke-Check 'User config preserved by uninstall' {
    $after = Get-HashOrNull $configPath

    if ($null -eq $configHashBefore -and $null -eq $after) {
        return 'No config.json existed before or after uninstall'
    }

    if ($configHashBefore -ne $after) {
        throw "config.json changed or was removed. Before=$configHashBefore After=$after"
    }

    "config.json unchanged: $after"
} | Out-Null

Invoke-Check 'Development build remains untouched' {
    if (-not (Test-Path $Root -PathType Container)) {
        throw "Development root disappeared: $Root"
    }

    $afterHash = Get-HashOrNull $publishedExe

    if ($publishedHashBefore -ne $afterHash) {
        throw "Published development binary changed. Before=$publishedHashBefore After=$afterHash"
    }

    "Development tree intact; published EXE SHA256 $afterHash"
} | Out-Null

if (-not $LeaveUninstalled) {
    Invoke-Check 'Reinstall after uninstall test' {
        Invoke-SilentInstall $reinstallLog
    } | Out-Null

    Invoke-Check 'Reinstalled copy and registration restored' {
        $restoredEntry = Get-GraveOpsUninstallEntry
        if ($null -eq $restoredEntry) {
            throw 'GraveOps uninstall registration was not recreated.'
        }

        $restoredDir = Resolve-InstallDir $restoredEntry
        $restoredExe = Join-Path $restoredDir 'GraveOps.exe'

        if (-not (Test-Path $restoredExe -PathType Leaf)) {
            throw "Reinstalled executable missing: $restoredExe"
        }

        "Restored $restoredExe"
    } | Out-Null

    Invoke-Check 'User config still preserved after reinstall' {
        $after = Get-HashOrNull $configPath

        if ($configHashBefore -ne $after) {
            throw "config.json changed after uninstall/reinstall. Before=$configHashBefore After=$after"
        }

        if ($null -eq $after) {
            'No config.json existed before or after uninstall/reinstall'
        }
        else {
            "config.json unchanged: $after"
        }
    } | Out-Null

    Invoke-Check 'Start Menu shortcut restored' {
        $shortcut = Get-StartMenuShortcut
        if ($null -eq $shortcut) {
            throw 'GraveOps Start Menu shortcut was not recreated.'
        }

        $shortcut.FullName
    } | Out-Null
}
else {
    Add-Result WARN 'Reinstall after uninstall test' 'Skipped by -LeaveUninstalled; GraveOps remains uninstalled'
}

Stop-GraveOps

$pass = @($results | Where-Object Status -eq 'PASS').Count
$warn = @($results | Where-Object Status -eq 'WARN').Count
$fail = @($results | Where-Object Status -eq 'FAIL').Count

Write-Host
Write-Host '============================================================'
Write-Host ' UNINSTALL VALIDATION RESULT'
Write-Host '============================================================'
Write-Host " PASS: $pass   WARN: $warn   FAIL: $fail"
Write-Host " Log: $logPath"
Write-Host " Backup: $backupRoot"
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
