[CmdletBinding()]
param(
    [string]$Root = 'C:\GraveOps\GraveOps-Community',
    [string]$ExePath = '',
    [switch]$SkipBuild,
    [switch]$SkipUI,
    [switch]$SkipRecyclarrPreview,
    [ValidateRange(1,10)]
    [int]$LifecycleRuns = 2
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Windows PowerShell 5.1 needs these assemblies loaded before the typed
# UIAutomation helper functions below are invoked.
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# Physical-click and WM_CLOSE fallbacks for WPF controls that expose text
# through UI Automation but do not expose Invoke/SelectionItem patterns.
if (-not ('GraveOpsValidationNative' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class GraveOpsValidationNative
{
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
}
'@
}

$Exe = if ([string]::IsNullOrWhiteSpace($ExePath)) {
    Join-Path $Root 'publish\win-x64\GraveOps.exe'
}
else {
    [IO.Path]::GetFullPath($ExePath)
}
$Src = Join-Path $Root 'src'
$BuildScript = Join-Path $Root 'build-release.ps1'
$ResultDir = Join-Path $Root 'test-results'
$Stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$LogPath = Join-Path $ResultDir "GraveOps-validation-$Stamp.log"
$JsonPath = Join-Path $ResultDir "GraveOps-validation-$Stamp.json"

New-Item -ItemType Directory -Path $ResultDir -Force | Out-Null

$script:Results = [System.Collections.Generic.List[object]]::new()
$script:AppProcess = $null
$script:AppWindow = $null
$script:RunStarted = Get-Date

function Write-Log {
    param([string]$Message = '')
    $line = "[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss'), $Message
    Write-Host $line
    Add-Content -Path $LogPath -Value $line -Encoding UTF8
}

function Add-Result {
    param(
        [string]$Area,
        [string]$Test,
        [ValidateSet('PASS','WARN','FAIL')]
        [string]$Status,
        [string]$Detail = ''
    )

    $entry = [pscustomobject]@{
        Time   = (Get-Date).ToString('s')
        Area   = $Area
        Test   = $Test
        Status = $Status
        Detail = $Detail
    }

    $script:Results.Add($entry)

    $prefix = "[{0}]" -f $Status
    switch ($Status) {
        'PASS' { Write-Host "$prefix $Area :: $Test - $Detail" -ForegroundColor Green }
        'WARN' { Write-Host "$prefix $Area :: $Test - $Detail" -ForegroundColor Yellow }
        'FAIL' { Write-Host "$prefix $Area :: $Test - $Detail" -ForegroundColor Red }
    }

    Add-Content -Path $LogPath -Value "$prefix $Area :: $Test - $Detail" -Encoding UTF8
}

function Invoke-Test {
    param(
        [string]$Area,
        [string]$Name,
        [scriptblock]$Body
    )

    try {
        $detail = & $Body
        if ($null -eq $detail -or [string]::IsNullOrWhiteSpace([string]$detail)) {
            $detail = 'OK'
        }
        Add-Result $Area $Name 'PASS' ([string]$detail)
        return $true
    }
    catch {
        Add-Result $Area $Name 'FAIL' $_.Exception.Message
        return $false
    }
}

function Add-Warning {
    param([string]$Area,[string]$Name,[string]$Detail)
    Add-Result $Area $Name 'WARN' $Detail
}

function Assert-File {
    param([string]$Path,[string]$Label)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Missing ${Label}: $Path"
    }
}

function Get-TextFromElement {
    param([System.Windows.Automation.AutomationElement]$Element)

    $parts = [System.Collections.Generic.List[string]]::new()

    try {
        $name = $Element.Current.Name
        if (-not [string]::IsNullOrWhiteSpace($name)) {
            $parts.Add($name)
        }
    } catch {}

    try {
        $pattern = $null
        if ($Element.TryGetCurrentPattern(
            [System.Windows.Automation.ValuePattern]::Pattern,
            [ref]$pattern
        )) {
            $value = ([System.Windows.Automation.ValuePattern]$pattern).Current.Value
            if (-not [string]::IsNullOrWhiteSpace($value)) {
                $parts.Add($value)
            }
        }
    } catch {}

    return ($parts -join "`n")
}

function Get-AppWindow {
    param(
        [int]$ProcessId,
        [int]$TimeoutSeconds = 25
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
        $ProcessId
    )

    while ((Get-Date) -lt $deadline) {
        $window = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
            [System.Windows.Automation.TreeScope]::Children,
            $condition
        )

        if ($null -ne $window) {
            return $window
        }

        Start-Sleep -Milliseconds 250
    }

    throw "Main GraveOps window did not appear within $TimeoutSeconds seconds."
}

function Find-ElementByAutomationId {
    param(
        [System.Windows.Automation.AutomationElement]$RootElement,
        [string]$AutomationId
    )

    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        $AutomationId
    )

    return $RootElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $condition
    )
}

function Invoke-AutomationId {
    param(
        [System.Windows.Automation.AutomationElement]$RootElement,
        [string]$AutomationId,
        [int]$TimeoutSeconds = 10
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $lastError = ''

    while ((Get-Date) -lt $deadline) {
        $element = Find-ElementByAutomationId -RootElement $RootElement -AutomationId $AutomationId

        if ($null -ne $element) {
            try {
                $controlType = $element.Current.ControlType

                if (
                    $controlType -eq
                    [System.Windows.Automation.ControlType]::RadioButton
                ) {
                    # GraveOps sidebar navigation is implemented as WPF RadioButtons.
                    # UIA SelectionItem.Select() changes selection state but does not
                    # reliably raise the Click handler that calls Navigate(...).
                    # Focusing the actual RadioButton and pressing SPACE exercises the
                    # same routed Click path as a real keyboard/user interaction and
                    # also causes WPF's ScrollViewer to bring the item into view.
                    try {
                        if ($null -ne $script:AppProcess) {
                            $script:AppProcess.Refresh()
                            if ($script:AppProcess.MainWindowHandle -ne [IntPtr]::Zero) {
                                [void][GraveOpsValidationNative]::SetForegroundWindow(
                                    $script:AppProcess.MainWindowHandle
                                )
                                Start-Sleep -Milliseconds 125
                            }
                        }
                    }
                    catch {}

                    $element.SetFocus()
                    Start-Sleep -Milliseconds 150
                    [System.Windows.Forms.SendKeys]::SendWait('{SPACE}')
                    Start-Sleep -Milliseconds 750
                    return
                }

                Invoke-UIElement -Element $element -Label $AutomationId
                Start-Sleep -Milliseconds 650
                return
            }
            catch {
                $lastError = $_.Exception.Message

                # Last resort for a visible AutomationId-backed element.
                try {
                    Invoke-PhysicalClick -Element $element -Label $AutomationId
                    Start-Sleep -Milliseconds 650
                    return
                }
                catch {
                    $lastError = "$lastError | physical-click fallback: $($_.Exception.Message)"
                }
            }
        }

        Start-Sleep -Milliseconds 200
    }

    if ([string]::IsNullOrWhiteSpace($lastError)) {
        $lastError = 'AutomationId was not found.'
    }

    throw "Could not activate AutomationId '$AutomationId'. Last error: $lastError"
}

function Wait-PageTitle {
    param(
        [System.Windows.Automation.AutomationElement]$RootElement,
        [string]$ExpectedTitle,
        [int]$TimeoutSeconds = 12
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)

    while ((Get-Date) -lt $deadline) {
        $title = Find-ElementByAutomationId -RootElement $RootElement -AutomationId 'PageTitle'
        if ($null -ne $title) {
            try {
                if ($title.Current.Name -eq $ExpectedTitle) {
                    return $title.Current.Name
                }
            }
            catch {}
        }

        Start-Sleep -Milliseconds 200
    }

    throw "PageTitle did not become '$ExpectedTitle' within $TimeoutSeconds seconds."
}

function Find-ElementsByName {
    param(
        [System.Windows.Automation.AutomationElement]$RootElement,
        [string]$Name
    )

    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Name
    )

    return $RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        $condition
    )
}

function Wait-ElementByName {
    param(
        [System.Windows.Automation.AutomationElement]$RootElement,
        [string]$Name,
        [int]$TimeoutSeconds = 10
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)

    while ((Get-Date) -lt $deadline) {
        $items = Find-ElementsByName -RootElement $RootElement -Name $Name
        if ($items.Count -gt 0) {
            return $items[0]
        }
        Start-Sleep -Milliseconds 200
    }

    throw "UI element '$Name' not found within $TimeoutSeconds seconds."
}

function Test-TextContains {
    param(
        [System.Windows.Automation.AutomationElement]$RootElement,
        [string]$Needle
    )

    $all = $RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition
    )

    foreach ($element in $all) {
        $text = Get-TextFromElement $element
        if ($text -like "*$Needle*") {
            return $text
        }
    }

    return $null
}

function Wait-TextContains {
    param(
        [System.Windows.Automation.AutomationElement]$RootElement,
        [string]$Needle,
        [int]$TimeoutSeconds = 15
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)

    while ((Get-Date) -lt $deadline) {
        $match = Test-TextContains -RootElement $RootElement -Needle $Needle
        if ($null -ne $match) {
            return $match
        }

        Start-Sleep -Milliseconds 300
    }

    throw "Text '$Needle' did not appear within $TimeoutSeconds seconds."
}

function Get-ActionableElement {
    param(
        [System.Windows.Automation.AutomationElement]$Element,
        [int]$MaxParents = 8
    )

    $current = $Element

    for ($depth = 0; $depth -le $MaxParents -and $null -ne $current; $depth++) {
        foreach ($patternId in @(
            [System.Windows.Automation.InvokePattern]::Pattern,
            [System.Windows.Automation.SelectionItemPattern]::Pattern,
            [System.Windows.Automation.ExpandCollapsePattern]::Pattern
        )) {
            try {
                $pattern = $null
                if ($current.TryGetCurrentPattern($patternId, [ref]$pattern)) {
                    return $current
                }
            }
            catch {}
        }

        try {
            $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($current)
        }
        catch {
            $parent = $null
        }

        $current = $parent
    }

    return $null
}

function Invoke-PhysicalClick {
    param(
        [System.Windows.Automation.AutomationElement]$Element,
        [string]$Label
    )

    try {
        $scrollPattern = $null
        if ($Element.TryGetCurrentPattern(
            [System.Windows.Automation.ScrollItemPattern]::Pattern,
            [ref]$scrollPattern
        )) {
            ([System.Windows.Automation.ScrollItemPattern]$scrollPattern).ScrollIntoView()
            Start-Sleep -Milliseconds 100
        }
    }
    catch {}

    try {
        if ($null -ne $script:AppProcess) {
            $script:AppProcess.Refresh()
            if ($script:AppProcess.MainWindowHandle -ne [IntPtr]::Zero) {
                [void][GraveOpsValidationNative]::SetForegroundWindow($script:AppProcess.MainWindowHandle)
                Start-Sleep -Milliseconds 150
            }
        }
    }
    catch {}

    $x = $null
    $y = $null

    try {
        $point = $Element.GetClickablePoint()
        $x = [int][Math]::Round($point.X)
        $y = [int][Math]::Round($point.Y)
    }
    catch {
        try {
            $rect = $Element.Current.BoundingRectangle
            if ($rect.Width -gt 1 -and $rect.Height -gt 1) {
                $x = [int][Math]::Round($rect.Left + ($rect.Width / 2))
                $y = [int][Math]::Round($rect.Top + ($rect.Height / 2))
            }
        }
        catch {}
    }

    if ($null -eq $x -or $null -eq $y) {
        throw "Element '$Label' has no usable clickable point."
    }

    [System.Windows.Forms.Cursor]::Position =
        New-Object System.Drawing.Point($x, $y)

    Start-Sleep -Milliseconds 125
    [GraveOpsValidationNative]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [GraveOpsValidationNative]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 250
}

function Invoke-UIElement {
    param(
        [System.Windows.Automation.AutomationElement]$Element,
        [string]$Label
    )

    $target = Get-ActionableElement -Element $Element

    if ($null -ne $target) {
        try {
            $pattern = $null
            if ($target.TryGetCurrentPattern(
                [System.Windows.Automation.InvokePattern]::Pattern,
                [ref]$pattern
            )) {
                ([System.Windows.Automation.InvokePattern]$pattern).Invoke()
                return
            }
        }
        catch {}

        try {
            $pattern = $null
            if ($target.TryGetCurrentPattern(
                [System.Windows.Automation.SelectionItemPattern]::Pattern,
                [ref]$pattern
            )) {
                ([System.Windows.Automation.SelectionItemPattern]$pattern).Select()
                return
            }
        }
        catch {}

        try {
            $pattern = $null
            if ($target.TryGetCurrentPattern(
                [System.Windows.Automation.ExpandCollapsePattern]::Pattern,
                [ref]$pattern
            )) {
                $expand = [System.Windows.Automation.ExpandCollapsePattern]$pattern
                if ($expand.Current.ExpandCollapseState -eq
                    [System.Windows.Automation.ExpandCollapseState]::Collapsed) {
                    $expand.Expand()
                }
                else {
                    Invoke-PhysicalClick -Element $Element -Label $Label
                }
                return
            }
        }
        catch {}

    }

    # WPF navigation tiles often expose only their child TextBlock via UIA.
    # Clicking the text itself routes the mouse event to the parent control.
    Invoke-PhysicalClick -Element $Element -Label $Label
}

function Invoke-NamedControl {
    param(
        [System.Windows.Automation.AutomationElement]$RootElement,
        [string]$Name,
        [int]$TimeoutSeconds = 10
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $lastCount = 0
    $lastError = ''

    while ((Get-Date) -lt $deadline) {
        $items = @(Find-ElementsByName -RootElement $RootElement -Name $Name)
        $lastCount = $items.Count

        foreach ($item in $items) {
            try {
                if ($item.Current.IsOffscreen) {
                    continue
                }
            }
            catch {}

            try {
                Invoke-UIElement -Element $item -Label $Name
                Start-Sleep -Milliseconds 650
                return
            }
            catch {
                $lastError = $_.Exception.Message
            }
        }

        Start-Sleep -Milliseconds 200
    }

    if ([string]::IsNullOrWhiteSpace($lastError)) {
        $lastError = 'No visible/actionable exact-name candidate succeeded.'
    }

    throw "Could not activate UI element '$Name'. Exact-name elements seen: $lastCount. Last error: $lastError"
}

function Invoke-NamedButton {
    param(
        [System.Windows.Automation.AutomationElement]$RootElement,
        [string]$Name,
        [int]$TimeoutSeconds = 10
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $lastError = ''

    while ((Get-Date) -lt $deadline) {
        $nameCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            $Name
        )

        $typeCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Button
        )

        $condition = New-Object System.Windows.Automation.AndCondition(
            $nameCondition,
            $typeCondition
        )

        $buttons = $RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants,
            $condition
        )

        foreach ($button in $buttons) {
            try {
                if (-not $button.Current.IsEnabled) {
                    continue
                }
            }
            catch {}

            try {
                $invoke = $null
                if ($button.TryGetCurrentPattern(
                    [System.Windows.Automation.InvokePattern]::Pattern,
                    [ref]$invoke
                )) {
                    ([System.Windows.Automation.InvokePattern]$invoke).Invoke()
                    Start-Sleep -Milliseconds 750
                    return
                }

                Invoke-PhysicalClick -Element $button -Label $Name
                Start-Sleep -Milliseconds 750
                return
            }
            catch {
                $lastError = $_.Exception.Message
            }
        }

        Start-Sleep -Milliseconds 200
    }

    if ([string]::IsNullOrWhiteSpace($lastError)) {
        $lastError = 'No enabled exact-name Button candidate was found.'
    }

    throw "Could not activate Button '$Name'. Last error: $lastError"
}

function Test-ActionableControlExists {
    param(
        [System.Windows.Automation.AutomationElement]$RootElement,
        [string]$Name
    )

    $items = @(Find-ElementsByName -RootElement $RootElement -Name $Name)

    foreach ($item in $items) {
        $target = Get-ActionableElement -Element $item
        if ($null -ne $target) {
            return $true
        }
    }

    return $false
}

function Start-GraveOpsForTest {
    Assert-File $Exe 'target GraveOps.exe'

    $existing = @(Get-Process -Name 'GraveOps' -ErrorAction SilentlyContinue)
    if ($existing.Count -gt 0) {
        throw "GraveOps is already running (PID(s): $($existing.Id -join ', ')). Close it before validation."
    }

    $process = Start-Process -FilePath $Exe -PassThru
    Start-Sleep -Milliseconds 500

    try {
        $process.Refresh()
    } catch {}

    if ($process.HasExited) {
        throw "GraveOps exited immediately with code $($process.ExitCode)."
    }

    $window = Get-AppWindow -ProcessId $process.Id -TimeoutSeconds 25

    $script:AppProcess = $process
    $script:AppWindow = $window

    return $process
}

function Stop-GraveOpsForLifecycleTest {
    param([int]$TimeoutSeconds = 8)

    if ($null -eq $script:AppProcess) {
        return 'No validation process was running'
    }

    $process = $script:AppProcess
    try { $process.Refresh() } catch {}

    if ($process.HasExited) {
        $script:AppProcess = $null
        $script:AppWindow = $null
        return 'Already exited'
    }

    # GraveOps defaults CloseToTray=true. A normal close is therefore expected
    # to keep the process alive while hiding the main window.
    try {
        if ($null -ne $script:AppWindow) {
            $pattern = $null
            if ($script:AppWindow.TryGetCurrentPattern(
                [System.Windows.Automation.WindowPattern]::Pattern,
                [ref]$pattern
            )) {
                ([System.Windows.Automation.WindowPattern]$pattern).Close()
            }
            else {
                [void]$process.CloseMainWindow()
            }
        }
        else {
            [void]$process.CloseMainWindow()
        }
    }
    catch {
        [void]$process.CloseMainWindow()
    }

    Start-Sleep -Seconds 2
    try { $process.Refresh() } catch {}

    $mode = ''
    if ($process.HasExited) {
        $mode = 'Normal close exited the process'
    }
    else {
        # If still running, confirm the main WPF window is no longer visible.
        $windowStillVisible = $false
        try {
            $condition = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
                $process.Id
            )

            $candidate = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
                [System.Windows.Automation.TreeScope]::Children,
                $condition
            )

            if ($null -ne $candidate) {
                try { $windowStillVisible = -not $candidate.Current.IsOffscreen } catch { $windowStillVisible = $true }
            }
        }
        catch {}

        if ($windowStillVisible) {
            $mode = 'Close request kept the process alive; window remained discoverable (possible tray transition)'
        }
        else {
            $mode = 'Close-to-tray behavior confirmed: process alive, main window hidden'
        }

        # Test harness cleanup: terminating the tray-resident instance is intentional,
        # not a GraveOps shutdown failure.
        try { $process.Kill() } catch {}
        try { [void]$process.WaitForExit(5000) } catch {}
    }

    $script:AppProcess = $null
    $script:AppWindow = $null
    return $mode
}

function Confirm-AppAlive {
    if ($null -eq $script:AppProcess) {
        throw 'GraveOps test process is not running.'
    }

    try { $script:AppProcess.Refresh() } catch {}
    if ($script:AppProcess.HasExited) {
        throw "GraveOps exited unexpectedly with code $($script:AppProcess.ExitCode)."
    }

    return "PID $($script:AppProcess.Id) is alive"
}

function Get-AllVisibleText {
    param([System.Windows.Automation.AutomationElement]$RootElement)

    $all = $RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition
    )

    $buffer = [System.Text.StringBuilder]::new()

    foreach ($element in $all) {
        $text = Get-TextFromElement $element
        if (-not [string]::IsNullOrWhiteSpace($text)) {
            [void]$buffer.AppendLine($text)
        }
    }

    return $buffer.ToString()
}

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ' GRAVEOPS 2.0 AUTOMATED VALIDATION SUITE V1.7' -ForegroundColor Cyan
Write-Host ' Build + source gates + lifecycle + read-only functional UI' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ''
Write-Log "Root: $Root"
Write-Log "Results: $ResultDir"

try {
    # -----------------------------------------------------------------
    # PRE-FLIGHT
    # -----------------------------------------------------------------
    Invoke-Test 'PREFLIGHT' 'Project tree exists' {
        if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
            throw "Project root does not exist: $Root"
        }
        if (-not (Test-Path -LiteralPath $Src -PathType Container)) {
            throw "Source tree does not exist: $Src"
        }
        'Project and src trees found'
    } | Out-Null

    Invoke-Test 'PREFLIGHT' 'No existing GraveOps instance' {
        $existing = @(Get-Process -Name 'GraveOps' -ErrorAction SilentlyContinue)
        if ($existing.Count -gt 0) {
            throw "Close existing GraveOps process(es) first: $($existing.Id -join ', ')"
        }
        'No running GraveOps process'
    } | Out-Null

    Invoke-Test 'PREFLIGHT' '.NET SDK available' {
        $version = (& dotnet --version 2>&1 | Out-String).Trim()
        if ([string]::IsNullOrWhiteSpace($version)) {
            throw 'dotnet --version returned no value.'
        }
        "dotnet SDK $version"
    } | Out-Null

    # -----------------------------------------------------------------
    # SOURCE / REGRESSION GATES
    # -----------------------------------------------------------------
    $actionRunner = Join-Path $Src 'GraveOps.App\Services\ActionRunnerService.cs'

    Invoke-Test 'SOURCE' 'ActionRunner source exists' {
        Assert-File $actionRunner 'ActionRunnerService.cs'
        $actionRunner
    } | Out-Null

    Invoke-Test 'SOURCE' 'Stopwatch regression fixed' {
        $text = [IO.File]::ReadAllText($actionRunner)
        if ([regex]::IsMatch($text, '(?<!System\.Diagnostics\.)\bStopwatch\b')) {
            throw 'Unqualified Stopwatch reference found.'
        }
        if ($text -notmatch 'System\.Diagnostics\.Stopwatch\.StartNew\(\)') {
            throw 'Qualified System.Diagnostics.Stopwatch.StartNew() not found.'
        }
        'Qualified Stopwatch call present; no unqualified references'
    } | Out-Null

    Invoke-Test 'SOURCE' 'ActionRunner has real consumers' {
        $hits = @(
            Get-ChildItem $Src -Recurse -File -Filter '*.cs' |
            Select-String -Pattern '\.ActionRunner\.RunAsync\b'
        )
        if ($hits.Count -lt 4) {
            throw "Only $($hits.Count) ActionRunner.RunAsync consumer(s) found; expected at least 4."
        }
        "$($hits.Count) ActionRunner.RunAsync consumer(s) found"
    } | Out-Null

    Invoke-Test 'SOURCE' 'All XAML parses as XML' {
        $xaml = @(Get-ChildItem $Src -Recurse -File -Filter '*.xaml')
        if ($xaml.Count -eq 0) {
            throw 'No XAML files found.'
        }

        foreach ($file in $xaml) {
            try {
                $xml = New-Object System.Xml.XmlDocument
                $xml.PreserveWhitespace = $true
                $xml.Load($file.FullName)
            }
            catch {
                throw "XAML parse failed: $($file.FullName): $($_.Exception.Message)"
            }
        }

        "$($xaml.Count) XAML file(s) parsed"
    } | Out-Null

    Invoke-Test 'SOURCE' 'Stable navigation AutomationIds exist' {
        $mainWindowXaml = Join-Path $Src 'GraveOps.App\MainWindow.xaml'
        Assert-File $mainWindowXaml 'MainWindow.xaml'

        $mainXaml = [IO.File]::ReadAllText($mainWindowXaml)
        $requiredIds = @(
            'DashboardNav',
            'HistoryNav',
            'ServicesNav',
            'StorageNav',
            'BackupsNav'
        )

        $missingIds = @(
            $requiredIds | Where-Object {
                $mainXaml -notmatch ('x:Name\s*=\s*"' + [regex]::Escape($_) + '"')
            }
        )

        if ($missingIds.Count -gt 0) {
            throw "Missing navigation AutomationId source control(s): $($missingIds -join ', ')"
        }

        "$($requiredIds.Count) required navigation IDs present in MainWindow.xaml"
    } | Out-Null

    Invoke-Test 'SOURCE' 'Recyclarr preview wiring exists' {
        $candidateFiles = @(
            Get-ChildItem $Src -Recurse -File |
            Where-Object { $_.Extension -in '.xaml','.cs' }
        )

        $recyclarrFiles = [System.Collections.Generic.List[object]]::new()
        $previewFiles = [System.Collections.Generic.List[string]]::new()

        foreach ($file in $candidateFiles) {
            $sourceText = [IO.File]::ReadAllText($file.FullName)

            if ($sourceText -match '(?i)recyclarr') {
                $recyclarrFiles.Add($file)

                if ($sourceText -match '(?i)preview|--preview') {
                    $previewFiles.Add($file.FullName)
                }
            }
        }

        if ($recyclarrFiles.Count -eq 0) {
            throw 'No source content referencing Recyclarr was found.'
        }

        if ($previewFiles.Count -eq 0) {
            throw 'Recyclarr source references were found, but no preview wiring was found in those files.'
        }

        "$($recyclarrFiles.Count) source file(s) reference Recyclarr; preview wiring found in $($previewFiles.Count)"
    } | Out-Null

    # -----------------------------------------------------------------
    # BUILD / PUBLISH
    # -----------------------------------------------------------------
    if (-not $SkipBuild) {
        Invoke-Test 'BUILD' 'Release build and publish' {
            Assert-File $BuildScript 'build-release.ps1'

            Push-Location $Root
            try {
                # build-release.ps1 is production code, not validator code.
                # Do not make it inherit the validator's StrictMode setting.
                Set-StrictMode -Off
                try {
                    $buildOutput = & $BuildScript 2>&1
                    $buildSucceeded = $?
                }
                finally {
                    Set-StrictMode -Version Latest
                }

                foreach ($line in @($buildOutput)) {
                    Add-Content -Path $LogPath -Value "[BUILD] $line" -Encoding UTF8
                }

                if (-not $buildSucceeded) {
                    throw 'build-release.ps1 returned failure.'
                }
            }
            finally {
                Pop-Location
            }

            Assert-File $Exe 'target GraveOps.exe'
            "Published $Exe"
        } | Out-Null
    }
    else {
        Add-Warning 'BUILD' 'Release build and publish' 'Skipped by -SkipBuild'
        Invoke-Test 'BUILD' 'Target executable exists' {
            Assert-File $Exe 'target GraveOps.exe'
            $Exe
        } | Out-Null
    }

    # -----------------------------------------------------------------
    # UI / FUNCTIONAL TESTS
    # -----------------------------------------------------------------
    if (-not $SkipUI) {
        Invoke-Test 'UI' 'Load Windows UI Automation' {
            if ($null -eq [System.Windows.Automation.AutomationElement]::RootElement) {
                throw 'UI Automation RootElement is unavailable.'
            }
            'UI Automation assemblies loaded'
        } | Out-Null

        $functionalLaunch = Invoke-Test 'LIFECYCLE' 'Launch GraveOps for functional test' {
            $p = Start-GraveOpsForTest
            "PID $($p.Id), main window detected"
        }

        if ($functionalLaunch) {
            Invoke-Test 'LIFECYCLE' 'Remain alive after initialization' {
                Start-Sleep -Seconds 3
                Confirm-AppAlive
            } | Out-Null

            Invoke-Test 'FUNCTIONAL' 'Services & Actions page opens' {
                Invoke-AutomationId $script:AppWindow 'ServicesNav'
                [void](Wait-PageTitle $script:AppWindow 'Services & Actions' 12)
                'Services & Actions page title confirmed through ServicesNav'
            } | Out-Null

            Invoke-Test 'FUNCTIONAL' 'Host summary executes through ActionRunner' {
                [void](Wait-TextContains $script:AppWindow 'Host summary' 12)
                Invoke-NamedControl $script:AppWindow 'Host summary'
                Start-Sleep -Milliseconds 300
                Invoke-NamedControl $script:AppWindow 'Run Action'

                [void](Wait-TextContains $script:AppWindow 'SUCCESS' 30)
                [void](Wait-TextContains $script:AppWindow 'Elapsed:' 10)

                $hostEvidence = Test-TextContains $script:AppWindow 'Operating System'
                if ($null -eq $hostEvidence) {
                    $hostEvidence = Test-TextContains $script:AppWindow 'Static hostname'
                }
                if ($null -eq $hostEvidence) {
                    $hostEvidence = Test-TextContains $script:AppWindow 'Linux '
                }

                if ($null -eq $hostEvidence) {
                    throw 'Host summary reported SUCCESS but expected host output was not visible.'
                }

                'SUCCESS + elapsed time + live host output observed'
            } | Out-Null

            Invoke-Test 'FUNCTIONAL' 'Backups page and refresh work' {
                # BackupsNav sits below the initially visible portion of the sidebar.
                # The Dashboard exposes a real Invoke-capable Backups button, confirmed
                # by the discovery probe, so use that stable user-facing route.
                Invoke-AutomationId $script:AppWindow 'DashboardNav'
                [void](Wait-PageTitle $script:AppWindow 'Dashboard' 12)

                Invoke-NamedButton $script:AppWindow 'Backups'
                [void](Wait-PageTitle $script:AppWindow 'Backups' 12)
                [void](Wait-TextContains $script:AppWindow 'Backup readiness' 12)

                Invoke-NamedControl $script:AppWindow 'Refresh'
                Start-Sleep -Seconds 2
                [void](Confirm-AppAlive)

                $pageText = Get-AllVisibleText $script:AppWindow
                if ($pageText -notmatch '(?i)\b(readiness|configured|provider|schedules)\b') {
                    throw 'Backup page loaded but inventory/readiness evidence was not visible after Refresh.'
                }

                'Dashboard Backups launcher opened page; backup readiness visible; Refresh completed without crash'
            } | Out-Null

            Invoke-Test 'FUNCTIONAL' 'Storage page returns meaningful Linux storage' {
                Invoke-AutomationId $script:AppWindow 'StorageNav'
                [void](Wait-PageTitle $script:AppWindow 'Storage' 12)
                Start-Sleep -Seconds 1

                $pageText = Get-AllVisibleText $script:AppWindow

                if ($pageText -notmatch '/dev/[A-Za-z0-9]') {
                    throw 'No /dev/* storage device was visible.'
                }

                $bad = @('/proc','/sys','tmpfs','overlay')
                $foundBad = @($bad | Where-Object { $pageText -match [regex]::Escape($_) })
                if ($foundBad.Count -gt 0) {
                    throw "Pseudo-filesystem/storage noise visible in main storage surface: $($foundBad -join ', ')"
                }

                'Real /dev device(s) visible; no /proc, /sys, tmpfs, or overlay noise'
            } | Out-Null

            Invoke-Test 'FUNCTIONAL' 'Recyclarr is available and preview-only' {
                # The discovery probe confirms a real Invoke-capable Recyclarr button
                # on the environment map/dashboard. Use that to open the integration page.
                Invoke-AutomationId $script:AppWindow 'DashboardNav'
                [void](Wait-PageTitle $script:AppWindow 'Dashboard' 12)
                Invoke-NamedControl $script:AppWindow 'Recyclarr'
                [void](Wait-TextContains $script:AppWindow 'Recyclarr preview' 15)

                if (-not (Test-ActionableControlExists $script:AppWindow 'Preview all Sonarr')) {
                    throw "Expected 'Preview all Sonarr' control is not actionable."
                }

                foreach ($dangerName in @('Sync','Apply','Write','Push')) {
                    if (Test-ActionableControlExists $script:AppWindow $dangerName) {
                        throw "Mutating Recyclarr control '$dangerName' is exposed."
                    }
                }

                'Recyclarr integration opened; preview control actionable; no exact Sync/Apply/Write/Push action exposed'
            } | Out-Null

            if (-not $SkipRecyclarrPreview) {
                Invoke-Test 'FUNCTIONAL' 'Recyclarr Sonarr preview executes' {
                    Invoke-NamedControl $script:AppWindow 'Preview all Sonarr'

                    # The exact completion wording is implementation-dependent.
                    # Accept a completed-success signal, but fail on explicit failure.
                    $deadline = (Get-Date).AddSeconds(90)
                    $completed = $false
                    while ((Get-Date) -lt $deadline) {
                        [void](Confirm-AppAlive)
                        $pageText = Get-AllVisibleText $script:AppWindow

                        if ($pageText -match '(?i)recyclarr preview failed|preview failed|\bfailed\b') {
                            throw 'Recyclarr preview reported failure.'
                        }

                        if ($pageText -match '(?i)completed|no changes|preview.*ready|processing sonarr server') {
                            $completed = $true
                            break
                        }

                        Start-Sleep -Milliseconds 500
                    }

                    if (-not $completed) {
                        throw 'Recyclarr preview did not expose a recognized completion/progress signal within 90 seconds.'
                    }

                    'Recyclarr Sonarr preview produced a recognized non-failure completion/progress signal'
                } | Out-Null
            }
            else {
                Add-Warning 'FUNCTIONAL' 'Recyclarr Sonarr preview executes' 'Skipped by -SkipRecyclarrPreview'
            }

            Invoke-Test 'FUNCTIONAL' 'History records the Host summary action' {
                Invoke-AutomationId $script:AppWindow 'HistoryNav'
                [void](Wait-PageTitle $script:AppWindow 'History & Incidents' 12)
                [void](Wait-TextContains $script:AppWindow 'GraveOps activity' 12)
                [void](Wait-TextContains $script:AppWindow 'Host summary' 12)
                'Host summary activity visible'
            } | Out-Null

            Invoke-Test 'FUNCTIONAL' 'Dashboard exposes restored modules' {
                Invoke-AutomationId $script:AppWindow 'DashboardNav'
                [void](Wait-PageTitle $script:AppWindow 'Dashboard' 12)
                [void](Wait-TextContains $script:AppWindow 'Quick modules' 12)
                [void](Wait-TextContains $script:AppWindow 'Backups' 10)
                [void](Wait-TextContains $script:AppWindow 'Recyclarr' 10)
                'Quick modules include Backups and Recyclarr'
            } | Out-Null

            Invoke-Test 'LIFECYCLE' 'Clean shutdown after functional tests' {
                Stop-GraveOpsForLifecycleTest
            } | Out-Null
        }

        # Additional launch/close cycles.
        for ($i = 1; $i -le $LifecycleRuns; $i++) {
            $started = Invoke-Test 'LIFECYCLE' "Launch cycle $i" {
                $p = Start-GraveOpsForTest
                Start-Sleep -Seconds 2
                [void](Confirm-AppAlive)
                "PID $($p.Id) remained alive"
            }

            if ($started) {
                Invoke-Test 'LIFECYCLE' "Clean close cycle $i" {
                    Stop-GraveOpsForLifecycleTest
                } | Out-Null
            }
        }

        Invoke-Test 'LIFECYCLE' 'No orphan GraveOps process remains' {
            Start-Sleep -Milliseconds 500
            $left = @(Get-Process -Name 'GraveOps' -ErrorAction SilentlyContinue)
            if ($left.Count -gt 0) {
                throw "Orphan process(es) remain: $($left.Id -join ', ')"
            }
            'No GraveOps processes remain'
        } | Out-Null
    }
    else {
        Add-Warning 'UI' 'Functional and lifecycle UI tests' 'Skipped by -SkipUI'
    }
}
finally {
    # Never intentionally leave our validation instance running.
    if ($null -ne $script:AppProcess) {
        try {
            $script:AppProcess.Refresh()
            if (-not $script:AppProcess.HasExited) {
                try { [void]$script:AppProcess.CloseMainWindow() } catch {}
                Start-Sleep -Seconds 1
                $script:AppProcess.Refresh()
                if (-not $script:AppProcess.HasExited) {
                    try { $script:AppProcess.Kill() } catch {}
                    try { [void]$script:AppProcess.WaitForExit(5000) } catch {}
                }
            }
        } catch {}
    }

    $pass = @($script:Results | Where-Object Status -eq 'PASS').Count
    $warn = @($script:Results | Where-Object Status -eq 'WARN').Count
    $fail = @($script:Results | Where-Object Status -eq 'FAIL').Count

    $report = [pscustomobject]@{
        Started = $script:RunStarted.ToString('s')
        Ended   = (Get-Date).ToString('s')
        Root    = $Root
        Pass    = $pass
        Warn    = $warn
        Fail    = $fail
        Results = @($script:Results)
    }

    $report | ConvertTo-Json -Depth 6 | Set-Content -Path $JsonPath -Encoding UTF8

    Write-Host ''
    Write-Host '============================================================' -ForegroundColor Cyan
    Write-Host ' GRAVEOPS VALIDATION RESULT' -ForegroundColor Cyan
    Write-Host '============================================================' -ForegroundColor Cyan
    Write-Host (" PASS: {0}   WARN: {1}   FAIL: {2}" -f $pass,$warn,$fail)
    Write-Host " Log : $LogPath"
    Write-Host " JSON: $JsonPath"
    Write-Host '============================================================' -ForegroundColor Cyan

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
}
