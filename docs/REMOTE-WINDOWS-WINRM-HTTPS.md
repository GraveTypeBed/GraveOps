# Remote Windows Provider over WinRM HTTPS

This slice reuses the existing Windows snapshot collector through a remote
PowerShell execution adapter. It does not duplicate the host, service, process,
storage, application, listener, Docker or event-log parser.

## Transport boundary

- Target platform: Windows
- Target location: remote
- Provider: `remote-windows`
- Transport: `winrm-https`
- Default port: `5986`
- Supported authentication: `Negotiate` and `Basic`
- Credential source: `ICredentialVault`
- PowerShell host: `powershell.exe` on Windows or `pwsh` elsewhere

The transport requires normal operating-system certificate trust and hostname
validation. `PinnedIdentity` may contain an additional SHA-256 certificate
fingerprint, but a pin never disables certificate-chain or hostname checks.
Self-signed certificates must be installed into the client's trust store.

The implementation never enables `TrustedHosts`, unencrypted WinRM,
`SkipCACheck`, `SkipCNCheck` or `SkipRevocationCheck`.

## Credential handling

The password is retrieved immediately before execution. It is not placed in
process arguments or environment variables. A fixed encoded wrapper is passed
as the PowerShell command, while a bounded JSON payload containing the endpoint,
username, password and remote inventory script is written through standard
input. The payload byte buffer and `SecretValue` are cleared or disposed after
execution.

## Target connection example

```text
ProviderId: remote-windows
Platform: Windows
Location: Remote
TransportId: winrm-https
Host: windows-host.example
Port: 5986
Username: DOMAIN\operator
CredentialReference: graveops/windows/windows-host
PinnedIdentity: SHA256:<64 hexadecimal characters>   # optional
Options:
  authentication: Negotiate
  operation-timeout-seconds: 60
```

## Validation status

The transport, certificate, credential, cancellation, provider and collector
boundaries are fixture-tested on Linux. Native WinRM execution still requires a
configured Windows target and a PowerShell host with WSMan remoting support.
No Windows target is added to the Linux UI by this slice.
