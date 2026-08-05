namespace GraveOps.Platform.Windows;

public static class WindowsInventoryPowerShell
{
    public static string Script { get; } =
        """
        [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
        $OutputEncoding = [Console]::OutputEncoding
        $ErrorActionPreference = 'Stop'

        function Invoke-GraveOpsRead {
            param(
                [scriptblock]$Script,
                [object]$Fallback
            )

            try {
                & $Script
            }
            catch {
                $Fallback
            }
        }

        function Convert-GraveOpsText {
            param(
                [object]$Value,
                [int]$MaximumLength = 600
            )

            if ($null -eq $Value) {
                return ''
            }

            $text = ([string]$Value -replace '\s+', ' ').Trim()

            if ($text.Length -gt $MaximumLength) {
                return $text.Substring(0, $MaximumLength)
            }

            return $text
        }

        function Protect-GraveOpsText {
            param(
                [object]$Value,
                [int]$MaximumLength = 600
            )

            $text = Convert-GraveOpsText $Value $MaximumLength

            $text = [regex]::Replace(
                $text,
                '(?i)\b(password|passphrase|token|api[_ -]?key|authorization)\b\s*[:=]\s*\S+',
                '$1=[REDACTED]'
            )

            $text = [regex]::Replace(
                $text,
                '(?i)(--?(?:password|passphrase|token|api[-_]?key|authorization))\s+\S+',
                '$1 [REDACTED]'
            )

            $text = [regex]::Replace(
                $text,
                '(?i)\bbearer\s+[A-Za-z0-9._~+/=-]+',
                'Bearer [REDACTED]'
            )

            return $text
        }

        function Convert-GraveOpsExecutablePath {
            param(
                [object]$Value
            )

            $text = Convert-GraveOpsText $Value 1200

            if (-not $text) {
                return ''
            }

            $match = [regex]::Match(
                $text,
                '^(?:"([^"]+\.exe)"|(.+?\.exe))(?:\s|$)',
                [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
            )

            if (-not $match.Success) {
                return ''
            }

            if ($match.Groups[1].Success) {
                return $match.Groups[1].Value
            }

            return $match.Groups[2].Value.Trim()
        }

        $os = Invoke-GraveOpsRead {
            Get-CimInstance Win32_OperatingSystem |
                Select-Object -First 1
        } $null

        $computer = Invoke-GraveOpsRead {
            Get-CimInstance Win32_ComputerSystem |
                Select-Object -First 1
        } $null

        $processors = @(
            Invoke-GraveOpsRead {
                @(Get-CimInstance Win32_Processor)
            } @()
        )

        $hostname = if ($computer -and $computer.Name) {
            [string]$computer.Name
        }
        else {
            [string]$env:COMPUTERNAME
        }

        $operatingSystemParts = @(
            if ($os) {
                [string]$os.Caption
                [string]$os.Version

                if ($os.BuildNumber) {
                    "Build $($os.BuildNumber)"
                }
            }
        ) |
            Where-Object { $_ } |
            Select-Object -Unique |
            ForEach-Object { $_.Trim() }

        $operatingSystem =
            if ($operatingSystemParts.Count -gt 0) {
                $operatingSystemParts -join ' '
            }
            else {
                'Windows'
            }

        $kernel = if ($os) {
            "$($os.Version) Build $($os.BuildNumber)".Trim()
        }
        else {
            '--'
        }

        $uptime = if ($os -and $os.LastBootUpTime) {
            $span = (Get-Date) - $os.LastBootUpTime
            '{0}d {1}h {2}m' -f `
                [int][Math]::Floor($span.TotalDays), `
                $span.Hours, `
                $span.Minutes
        }
        else {
            '--'
        }

        $cpuNames = @(
            $processors |
                ForEach-Object { Convert-GraveOpsText $_.Name 200 } |
                Where-Object { $_ } |
                Select-Object -Unique
        )

        $cpuModel = if ($cpuNames.Count -gt 0) {
            $cpuNames -join ' · '
        }
        else {
            '--'
        }

        $logicalProcessorCount = 0

        foreach ($processor in $processors) {
            $logicalProcessorCount +=
                [int]$processor.NumberOfLogicalProcessors
        }

        if ($logicalProcessorCount -lt 1) {
            $logicalProcessorCount =
                [Math]::Max(1, [Environment]::ProcessorCount)
        }

        $cpuLoad = $null
        $loadValues = @(
            $processors |
                Where-Object { $null -ne $_.LoadPercentage } |
                ForEach-Object { [double]$_.LoadPercentage }
        )

        if ($loadValues.Count -gt 0) {
            $cpuLoad =
                ($loadValues |
                    Measure-Object -Average).Average
        }

        $totalMemoryKilobytes = if ($os) {
            [long]$os.TotalVisibleMemorySize
        }
        else {
            0
        }

        $freeMemoryKilobytes = if ($os) {
            [long]$os.FreePhysicalMemory
        }
        else {
            0
        }

        $ipAddresses = @(
            Invoke-GraveOpsRead {
                Get-CimInstance Win32_NetworkAdapterConfiguration `
                    -Filter "IPEnabled = True" |
                    ForEach-Object {
                        @($_.IPAddress)
                    } |
                    Where-Object {
                        $_ -and
                        $_ -ne '127.0.0.1' -and
                        $_ -ne '::1' -and
                        $_ -notlike '169.254.*'
                    } |
                    Select-Object -Unique
            } @()
        )

        $storage = @(
            Invoke-GraveOpsRead {
                Get-CimInstance Win32_LogicalDisk `
                    -Filter "DriveType = 3" |
                    ForEach-Object {
                        [ordered]@{
                            DeviceId = [string]$_.DeviceID
                            VolumeName = Convert-GraveOpsText $_.VolumeName 200
                            FileSystem = [string]$_.FileSystem
                            Size = [long]$_.Size
                            FreeSpace = [long]$_.FreeSpace
                        }
                    }
            } @()
        )

        $identityPattern =
            '(?i)(plex|jellyfin|emby|tautulli|kometa|sonarr|radarr|lidarr|prowlarr|readarr|whisparr|mylar|bazarr|seerr|overseerr|jellyseerr|sabnzbd|qbittorrent|recyclarr|configarr|profilarr|autobrr|unpackerr|cleanuparr|tdarr|maintainerr|pihole|pi-hole|decypharr|zurg|zilean|flaresolverr|docker|containerd|mullvad|ssh)'

        $services = @(
            Invoke-GraveOpsRead {
                Get-CimInstance Win32_Service |
                    Where-Object {
                        $_.Name -match $identityPattern -or
                        $_.DisplayName -match $identityPattern -or
                        $_.PathName -match $identityPattern
                    } |
                    ForEach-Object {
                        [ordered]@{
                            Name = [string]$_.Name
                            DisplayName = Convert-GraveOpsText $_.DisplayName 250
                            State = [string]$_.State
                            StartMode = [string]$_.StartMode
                            PathName =
                                Convert-GraveOpsExecutablePath $_.PathName
                        }
                    }
            } @()
        )

        $processes = @(
            Invoke-GraveOpsRead {
                Get-CimInstance Win32_Process |
                    Sort-Object WorkingSetSize -Descending |
                    Select-Object -First 500 |
                    ForEach-Object {
                        [ordered]@{
                            ProcessId = [int]$_.ProcessId
                            Name = [string]$_.Name
                            ExecutablePath =
                                Convert-GraveOpsText $_.ExecutablePath 600
                            WorkingSetSize = [long]$_.WorkingSetSize
                            KernelModeTime = [long]$_.KernelModeTime
                            UserModeTime = [long]$_.UserModeTime
                        }
                    }
            } @()
        )

        $uninstallRoots = @(
            'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*'
            'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
        )

        $installedApplications = @(
            foreach ($root in $uninstallRoots) {
                Get-ItemProperty -Path $root -ErrorAction SilentlyContinue |
                    Where-Object { $_.DisplayName } |
                    ForEach-Object {
                        [ordered]@{
                            Name = Convert-GraveOpsText $_.DisplayName 250
                            Version = Convert-GraveOpsText $_.DisplayVersion 120
                            Publisher = Convert-GraveOpsText $_.Publisher 200
                            InstallLocation =
                                Convert-GraveOpsText $_.InstallLocation 600
                            Source = if (
                                $root -like '*WOW6432Node*'
                            ) {
                                'HKLM 32-bit uninstall registry'
                            }
                            else {
                                'HKLM uninstall registry'
                            }
                        }
                    }
            }
        ) |
            Sort-Object Name, Version -Unique

        $processNameById = @{}

        foreach ($process in $processes) {
            $processNameById[[string]$process.ProcessId] =
                [string]$process.Name
        }

        $networkListeners = @()

        $networkListeners += @(
            Invoke-GraveOpsRead {
                Get-NetTCPConnection `
                    -State Listen `
                    -ErrorAction Stop |
                    ForEach-Object {
                        $processId = [int]$_.OwningProcess
                        [ordered]@{
                            Protocol = 'TCP'
                            LocalAddress = [string]$_.LocalAddress
                            LocalPort = [int]$_.LocalPort
                            OwningProcess = $processId
                            ProcessName =
                                [string]$processNameById[
                                    [string]$processId
                                ]
                        }
                    }
            } @()
        )

        $networkListeners += @(
            Invoke-GraveOpsRead {
                Get-NetUDPEndpoint `
                    -ErrorAction Stop |
                    ForEach-Object {
                        $processId = [int]$_.OwningProcess
                        [ordered]@{
                            Protocol = 'UDP'
                            LocalAddress = [string]$_.LocalAddress
                            LocalPort = [int]$_.LocalPort
                            OwningProcess = $processId
                            ProcessName =
                                [string]$processNameById[
                                    [string]$processId
                                ]
                        }
                    }
            } @()
        )

        $events = @(
            Invoke-GraveOpsRead {
                Get-WinEvent `
                    -FilterHashtable @{
                        LogName = 'System'
                        Level = 1, 2, 3
                        StartTime = (Get-Date).AddHours(-24)
                    } `
                    -MaxEvents 50 `
                    -ErrorAction Stop |
                    ForEach-Object {
                        [ordered]@{
                            TimeCreated =
                                if ($_.TimeCreated) {
                                    $_.TimeCreated.ToString('o')
                                }
                                else {
                                    ''
                                }
                            Id = [int]$_.Id
                            Provider =
                                Convert-GraveOpsText $_.ProviderName 180
                            Level =
                                Convert-GraveOpsText $_.LevelDisplayName 80
                            Message =
                                Protect-GraveOpsText $_.Message 600
                        }
                    }
            } @()
        )

        $dockerVersion = ''
        $containers = @()

        if (Get-Command docker -ErrorAction SilentlyContinue) {
            $dockerVersion = @(
                & docker version `
                    --format '{{.Server.Version}}' `
                    2>$null
            ) |
                Select-Object -First 1

            if ($LASTEXITCODE -eq 0 -and $dockerVersion) {
                $containerLines = @(
                    & docker ps -a `
                        --format '{{json .}}' `
                        2>$null
                )

                foreach ($line in $containerLines) {
                    if (-not $line) {
                        continue
                    }

                    try {
                        $item = $line | ConvertFrom-Json

                        $containers += [ordered]@{
                            Name = [string]$item.Names
                            Image = [string]$item.Image
                            State = [string]$item.State
                            Status = Convert-GraveOpsText $item.Status 250
                            Ports = Convert-GraveOpsText $item.Ports 500
                        }
                    }
                    catch {
                    }
                }
            }
            else {
                $dockerVersion = ''
            }
        }

        $failedServices = @(
            $services |
                Where-Object {
                    $_.StartMode -eq 'Auto' -and
                    $_.State -ne 'Running'
                } |
                ForEach-Object { $_.Name }
        )

        $document = [ordered]@{
            Hostname = $hostname
            OperatingSystem = $operatingSystem
            Kernel = $kernel
            Uptime = $uptime
            SystemState = if ($os) { 'Running' } else { 'Unknown' }
            CpuModel = $cpuModel
            CpuLoadPercent = $cpuLoad
            LogicalProcessorCount = $logicalProcessorCount
            TotalMemoryKilobytes = $totalMemoryKilobytes
            FreeMemoryKilobytes = $freeMemoryKilobytes
            IpAddresses = @($ipAddresses)
            DockerVersion = [string]$dockerVersion
            Storage = @($storage)
            Services = @($services)
            Processes = @($processes)
            InstalledApplications = @($installedApplications)
            NetworkListeners = @($networkListeners)
            Containers = @($containers)
            FailedServices = @($failedServices)
            Events = @($events)
        }

        $document |
            ConvertTo-Json -Depth 8 -Compress
        """;
}
