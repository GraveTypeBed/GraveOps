namespace GraveOps.Desktop.Linux;

public sealed record TargetEditorProjection(
    bool IsLocal,
    bool IsRemoteLinux,
    bool IsRemoteWindows,
    bool ShowRemoteConnection,
    bool ShowPrivateKey,
    bool ShowPinnedIdentity,
    bool ShowFingerprintScan,
    bool ShowSecret,
    bool ShowWindowsOptions,
    bool CanSaveSecret,
    int DefaultPort,
    LinuxHostAuthentication Authentication,
    string ModeText,
    string PinnedIdentityLabel,
    string PinnedIdentityHelp,
    string SecretLabel);

public static class TargetEditorProjectionPolicy
{
    public static TargetEditorProjection Create(
        LinuxHostKind kind,
        LinuxHostAuthentication authentication,
        bool credentialVaultAvailable)
    {
        var local =
            kind is
                LinuxHostKind.Local or
                LinuxHostKind.LocalWindows;
        var remoteLinux =
            kind ==
            LinuxHostKind.RemoteLinux;
        var remoteWindows =
            kind ==
            LinuxHostKind.RemoteWindows;

        var normalizedAuthentication =
            NormalizeAuthentication(
                kind,
                authentication);

        var privateKey =
            remoteLinux &&
            normalizedAuthentication ==
                LinuxHostAuthentication.PrivateKey;
        var secret =
            remoteWindows ||
            (
                remoteLinux &&
                normalizedAuthentication !=
                    LinuxHostAuthentication.Agent
            );

        var mode =
            kind switch
            {
                LinuxHostKind.Local =>
                    "Native local Linux provider · no transport credentials required",
                LinuxHostKind.LocalWindows =>
                    "Local Windows provider · available only in a Windows client runtime",
                LinuxHostKind.RemoteWindows
                    when normalizedAuthentication ==
                         LinuxHostAuthentication.WinRmBasic =>
                    "Remote Windows over WinRM HTTPS · Basic authentication · system TLS trust required",
                LinuxHostKind.RemoteWindows =>
                    "Remote Windows over WinRM HTTPS · Negotiate authentication · system TLS trust required",
                LinuxHostKind.RemoteLinux
                    when normalizedAuthentication ==
                         LinuxHostAuthentication.PrivateKey =>
                    "Remote Linux over pinned SSH · private key and optional passphrase",
                LinuxHostKind.RemoteLinux
                    when normalizedAuthentication ==
                         LinuxHostAuthentication.Password =>
                    "Remote Linux over pinned SSH · keyring-backed password",
                _ =>
                    "Remote Linux over pinned SSH · SSH agent authentication"
            };

        return new TargetEditorProjection(
            local,
            remoteLinux,
            remoteWindows,
            ShowRemoteConnection:
                !local,
            ShowPrivateKey:
                privateKey,
            ShowPinnedIdentity:
                !local,
            ShowFingerprintScan:
                remoteLinux,
            ShowSecret:
                secret,
            ShowWindowsOptions:
                remoteWindows,
            CanSaveSecret:
                secret &&
                credentialVaultAvailable,
            DefaultPort:
                remoteWindows
                    ? 5986
                    : 22,
            Authentication:
                normalizedAuthentication,
            ModeText:
                mode,
            PinnedIdentityLabel:
                remoteWindows
                    ? "Optional server certificate SHA-256 pin"
                    : "Pinned SSH host-key SHA-256 fingerprint",
            PinnedIdentityHelp:
                remoteWindows
                    ? "Certificate-chain, revocation and hostname validation remain mandatory. A pin is an additional check."
                    : "Scan and save the host key before authentication is attempted.",
            SecretLabel:
                remoteWindows
                    ? "Windows account password"
                    : privateKey
                        ? "Private-key passphrase"
                        : "SSH password");
    }

    public static IReadOnlyList<LinuxHostAuthentication>
        AuthenticationChoices(
            LinuxHostKind kind) =>
        kind switch
        {
            LinuxHostKind.RemoteWindows =>
                new[]
                {
                    LinuxHostAuthentication.WinRmNegotiate,
                    LinuxHostAuthentication.WinRmBasic
                },
            LinuxHostKind.RemoteLinux =>
                new[]
                {
                    LinuxHostAuthentication.Agent,
                    LinuxHostAuthentication.PrivateKey,
                    LinuxHostAuthentication.Password
                },
            _ =>
                new[]
                {
                    LinuxHostAuthentication.Agent
                }
        };

    public static LinuxHostAuthentication
        NormalizeAuthentication(
            LinuxHostKind kind,
            LinuxHostAuthentication authentication)
    {
        var choices =
            AuthenticationChoices(
                kind);

        return choices.Contains(
            authentication)
            ? authentication
            : choices[0];
    }
}
