# Local Windows Provider Foundation

This slice adds a platform adapter for surveying a local Windows target while
preserving GraveOps' target/provider architecture.

## Included

- Local Windows provider descriptor and capability catalog.
- Encoded, noninteractive Windows PowerShell runner.
- Read-only CIM inventory for host, CPU, memory, disks, services and processes.
- Read-only uninstall-registry application inventory.
- Read-only TCP/UDP listener inventory.
- Read-only Docker CLI inventory when Docker is available.
- Read-only System event-log warning/error summaries.
- Shared application catalog/classifier reuse.
- Target lease and provider-envelope preservation.
- Linux-runnable fixture contract tests.
- Optional native Windows validation with `--live`.

## Privacy and safety boundaries

The provider does not collect process command lines or process owners. It does
not use `Win32_Product`, which can trigger MSI consistency checks. The inventory
script contains no service, registry, process, package, firewall or Docker
mutations. It does not collect or persist credentials, API keys or tokens.

## Native Windows validation

From a Windows checkout:

```powershell
dotnet run `
  --project tests/GraveOps.Platform.Windows.ContractTests/GraveOps.Platform.Windows.ContractTests.csproj `
  -- --live
```

The normal contract-test run is fixture-based and remains runnable on Linux.
