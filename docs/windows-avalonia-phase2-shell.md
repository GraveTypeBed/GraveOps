# Windows Avalonia Phase 2 V4 â€” Mature Read-Only Shell

## Scope

Phase 2 ports the mature GraveOps Avalonia shell language into the isolated Windows branch without importing Linux-only operational services.

Included:

- custom GraveOps title bar;
- navigation rail;
- page header and status strip;
- responsive wrapping dashboard cards;
- read-only Dashboard, Services, Docker, Storage, Integrations, and Warnings pages;
- shared `ILocalHostProbe` snapshot population;
- recommendation and health summaries derived only from the current snapshot;
- separate `publish\win-x64-avalonia` output.

Excluded:

- service start, stop, restart, or enable operations;
- Docker mutations;
- filesystem mutations;
- elevation;
- Safe Mode bypasses;
- replacement or modification of the WPF legacy application.

## Safety contract

The Phase 2 script:

1. requires `windows-avalonia`;
2. requires local HEAD to equal `origin/windows-avalonia`;
3. permits only the known `FilesView.xaml.cs` checkout-normalization drift before editing;
4. fingerprints the known drift and the external WPF project/build/executable;
5. writes only the approved Phase 2 files;
6. builds Debug and Release with warnings as errors;
7. publishes only to `publish\win-x64-avalonia`;
8. stages and commits only the approved Phase 2 files.

## Acceptance

- application launches;
- title-bar drag, minimize, maximize/restore, and close work;
- navigation changes pages;
- refresh repopulates all read-only views;
- narrow windows wrap dashboard summary cards instead of clipping;
- WPF remains launchable from its legacy output;
- no mutation controls appear.
