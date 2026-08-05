# Avalonia Windows Target Management

This slice moves the canonical Linux Avalonia client from Linux-only host
persistence and dispatch to the shared Core target/provider contracts.

## Targets exposed by the Linux client

- local Linux;
- remote Linux over fingerprint-pinned SSH;
- remote Windows over WinRM HTTPS.

The composed provider registry also contains the local Windows provider so the
desktop composition matches the shared architecture. A local Windows target is
not offered by the Linux client because native local Windows capture requires a
Windows runtime.

## Persistence migration

The client writes `targets.json` under the existing GraveOps XDG configuration
directory. When `targets.json` does not exist, existing `hosts.json` profiles
are imported with their IDs, endpoint data, role, authentication mode,
private-key path, pinned SSH identity, and last-detected timestamp preserved.

The original `hosts.json` remains in place as rollback evidence. Neither file
contains passwords or passphrases. Existing Secret Service entries remain
usable because opaque references map back to the same target ID and credential
kind:

```text
graveops/target/<target-id>/password
graveops/target/<target-id>/passphrase
```

## Provider composition and refresh ownership

The Avalonia client registers these `IHostProvider` implementations together:

- `local-linux`;
- `remote-linux-ssh`;
- `local-windows`;
- `remote-windows`.

Refreshes resolve through `IHostProviderRegistry`. The selected provider creates
the `TargetSnapshotEnvelope<HostSnapshot>` consumed by the existing target
selection and refresh-generation checks. A late result from an earlier target
or refresh cannot become current UI state.

## Remote Windows editor

Remote Windows targets configure:

- host and WinRM HTTPS port, default `5986`;
- username;
- Negotiate or Basic authentication;
- operation timeout from 10 through 300 seconds;
- optional SHA-256 server-certificate pin;
- keyring-backed password reference.

Certificate pinning is additional to normal certificate-chain, revocation, and
hostname validation. The client does not enable TrustedHosts, unencrypted
WinRM, or certificate-validation bypasses.

## Capability-driven navigation

Services, containers, storage, logs, and backup workspaces are projected from
reported target capabilities. Linux journal and Windows event-log capabilities
both satisfy the logs workspace. Backup inventory remains local-Linux-only.

Mutation controls remain enabled only for the local Linux provider. Remote
Linux and remote Windows targets stay read-only.

## Validation status

The slice includes contract coverage for target conversion, legacy migration,
registry round trips, editor projection, provider composition, capability
navigation, target deletion fallback, and unsafe Windows profiles. The apply
script also parses AXAML, rejects duplicate named controls, verifies the new
Windows controls, and checks every declared event handler.

Native WinRM and native local Windows runtime validation remain separate,
platform-dependent gates.
