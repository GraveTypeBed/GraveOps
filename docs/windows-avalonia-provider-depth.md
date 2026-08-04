# Windows Avalonia Provider Depth

## Goal

Extend the native Windows provider underneath the canonical Linux Avalonia shell. This phase changes telemetry and discovery rather than redesigning the shell.

## Added

- physical host memory from `GlobalMemoryStatusEx`;
- dynamic catalog-related Windows service discovery through `Win32_Service`;
- running process discovery through `Win32_Process`;
- installed-application discovery through Windows uninstall registry keys;
- relevant listening-port evidence through `Get-NetTCPConnection`;
- Docker CLI resolution from both `PATH` and Docker Desktop's standard installation path;
- Docker Desktop/engine distinction when the application is present but the engine is unavailable;
- integration evidence composed from services, Docker containers, processes, installed applications and matching listeners;
- ASCII-safe separators in the Windows shell.

## Safety

- all probes are read-only;
- command lines and secrets are not captured;
- no service, process, Docker, registry or filesystem mutation is performed;
- WPF source and `publish\win-x64` remain fingerprint-protected;
- publish output remains isolated at `publish\win-x64-avalonia`;
- the published executable must survive a startup and automatic-refresh smoke test before commit.
