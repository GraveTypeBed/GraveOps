# Windows Avalonia Linux-Shell Parity Foundation

The `linux-client` Avalonia application is the canonical GraveOps desktop user interface.

This phase imports the exact Linux `App.axaml` theme and replaces the temporary Windows navigation and header with the Linux shell hierarchy:

- Overview
- Media
- Infrastructure
- Operator
- active-server selector
- two-row page header
- Quick bar
- Ctrl+K command search
- Overview, Jobs and Activity drawers

The validated Phase 2 Dashboard and Windows host provider remain in place. Linux page destinations that do not yet have Windows data use a clearly marked read-only parity page rather than imitating the WPF application.

No service, Docker, filesystem, backup, terminal or elevation actions are enabled.

Canonical Linux commit: `b050979dc29e008e352057ee1d106dea4406bfba`
