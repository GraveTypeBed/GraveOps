using GraveOps.Core.Security;
using GraveOps.Core.Targets;

namespace GraveOps.Platform.Windows;

public enum WindowsRemoteAuthentication
{
    Negotiate = 0,
    Basic = 1
}

public sealed record RemoteWindowsConnectionOptions(
    string Host,
    int Port,
    string Username,
    CredentialReference CredentialReference,
    WindowsRemoteAuthentication Authentication,
    TimeSpan OperationTimeout,
    string? PinnedServerCertificateSha256);

public static class RemoteWindowsConnectionParser
{
    public const int DefaultWinRmHttpsPort =
        5986;

    public static TimeSpan DefaultOperationTimeout { get; } =
        TimeSpan.FromSeconds(60);

    public static RemoteWindowsConnectionOptions Parse(
        TargetProfile target)
    {
        ArgumentNullException.ThrowIfNull(
            target);

        target.Validate();

        if (target.Platform !=
                TargetPlatform.Windows ||
            target.Location !=
                TargetLocation.Remote ||
            !target.ProviderId.Equals(
                HostProviderIds.RemoteWindows,
                StringComparison.OrdinalIgnoreCase) ||
            !target.Connection.TransportId.Equals(
                TransportIds.WinRmHttps,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Remote Windows collection requires a remote Windows " +
                "target using the WinRM HTTPS transport.");
        }

        var host =
            target.Connection.Host?.Trim() ??
            string.Empty;

        if (string.IsNullOrWhiteSpace(
                host))
        {
            throw new InvalidOperationException(
                "The remote Windows host is required.");
        }

        var port =
            target.Connection.Port ??
            DefaultWinRmHttpsPort;

        if (port is < 1 or > 65535)
        {
            throw new InvalidOperationException(
                "The WinRM HTTPS port must be between 1 and 65535.");
        }

        var username =
            target.Connection.Username?.Trim() ??
            string.Empty;

        if (string.IsNullOrWhiteSpace(
                username))
        {
            throw new InvalidOperationException(
                "The remote Windows username is required.");
        }

        var credentialReference =
            target.Connection.CredentialReference?.Trim() ??
            string.Empty;

        if (string.IsNullOrWhiteSpace(
                credentialReference))
        {
            throw new InvalidOperationException(
                "The remote Windows credential reference is required.");
        }

        var authenticationText =
            Option(
                target.Connection.Options,
                "authentication") ??
            nameof(
                WindowsRemoteAuthentication.Negotiate);

        if (!Enum.TryParse<
                WindowsRemoteAuthentication>(
                authenticationText,
                ignoreCase: true,
                out var authentication) ||
            authentication is not
                WindowsRemoteAuthentication.Negotiate and not
                WindowsRemoteAuthentication.Basic)
        {
            throw new InvalidOperationException(
                "Remote Windows authentication must be Negotiate or Basic.");
        }

        var timeout =
            ParseOperationTimeout(
                Option(
                    target.Connection.Options,
                    "operation-timeout-seconds"));

        var pin =
            NormalizeSha256Pin(
                target.Connection.PinnedIdentity);

        return new RemoteWindowsConnectionOptions(
            host,
            port,
            username,
            new CredentialReference(
                credentialReference),
            authentication,
            timeout,
            pin);
    }

    public static string? NormalizeSha256Pin(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return null;
        }

        var token =
            value.Trim();

        if (token.StartsWith(
                "sha256:",
                StringComparison.OrdinalIgnoreCase))
        {
            token =
                token["sha256:".Length..];
        }

        var characters =
            new List<char>(
                64);

        foreach (var character in token)
        {
            if (Uri.IsHexDigit(
                    character))
            {
                characters.Add(
                    char.ToUpperInvariant(
                        character));
                continue;
            }

            if (character is ':' or '-' or ' ')
                continue;

            throw new InvalidOperationException(
                "The pinned WinRM certificate identity must be a SHA-256 fingerprint.");
        }

        if (characters.Count != 64)
        {
            throw new InvalidOperationException(
                "The pinned WinRM certificate SHA-256 fingerprint must contain 64 hexadecimal characters.");
        }

        return "SHA256:" +
            new string(
                characters.ToArray());
    }

    private static TimeSpan ParseOperationTimeout(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return DefaultOperationTimeout;
        }

        if (!int.TryParse(
                value,
                out var seconds) ||
            seconds is < 10 or > 300)
        {
            throw new InvalidOperationException(
                "The remote Windows operation timeout must be between 10 and 300 seconds.");
        }

        return TimeSpan.FromSeconds(
            seconds);
    }

    private static string? Option(
        IReadOnlyDictionary<string, string>? options,
        string key)
    {
        if (options is null)
            return null;

        foreach (var pair in options)
        {
            if (pair.Key.Equals(
                    key,
                    StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value?.Trim();
            }
        }

        return null;
    }
}
