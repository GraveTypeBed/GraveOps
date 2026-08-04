# Windows Avalonia Media Hub Parity

## Purpose

Move the Windows client from a raw integration evidence table toward the canonical Linux media navigation and workspace model.

## Included

- dynamic Library and Acquisition navigation labels;
- Plex and qBittorrent subnavigation when those applications are detected;
- dedicated read-only Plex and qBittorrent workspace foundations;
- a Media Hub application-fleet view with detected and running totals;
- cleanup of literal PowerShell tab markers and registry display-icon artifacts;
- corrected Linux-shell startup activity wording;
- wrapped Docker status text instead of destructive truncation.

## Deferred

- Plex API authentication and sessions;
- qBittorrent Web API authentication and queue/history data;
- service, process or download-client mutations;
- secret storage;
- automatic endpoint configuration.

## Safety

This phase remains read-only, keeps the existing shared `HostSnapshot` contract, publishes only to `publish\win-x64-avalonia`, fingerprints the WPF legacy tree, audits every runtime control lookup, and requires a successful launch/refresh smoke test before commit.
