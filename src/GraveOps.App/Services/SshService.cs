using System.Security.Cryptography;
using GraveOps.App.Models;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace GraveOps.App.Services;

public sealed class SshService
{
    private readonly CredentialManagerService _credentials;
    public SshService(CredentialManagerService credentials) => _credentials = credentials;

    public async Task<SshTestResult> TestAsync(ServerProfile profile, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            string fingerprint = "";
            try
            {
                using var client = BuildClient(profile, fp => fingerprint = fp);
                client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(10);
                client.Connect();
                var hostname = client.RunCommand("hostname").Result.Trim();
                client.Disconnect();
                return new SshTestResult(true, string.IsNullOrWhiteSpace(hostname) ? "Connected" : $"Connected to {hostname}", fingerprint);
            }
            catch (Exception ex)
            {
                return new SshTestResult(false, ex.Message, fingerprint);
            }
        }, cancellationToken);
    }

    public async Task<CommandResult> ExecuteAsync(ServerProfile profile, string command, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            using var client = BuildClient(profile);
            client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(12);
            client.Connect();
            using var cmd = client.CreateCommand(command);
            cmd.CommandTimeout = TimeSpan.FromSeconds(timeoutSeconds);
            var output = cmd.Execute() ?? "";
            var error = cmd.Error ?? "";
            var code = cmd.ExitStatus ?? -1;
            client.Disconnect();
            return new CommandResult(code, output.TrimEnd(), error.TrimEnd());
        }, cancellationToken);
    }

    public SshClient BuildClient(ServerProfile profile, Action<string>? fingerprintObserver = null)
    {
        if (string.IsNullOrWhiteSpace(profile.Host)) throw new InvalidOperationException("Server host is empty.");
        if (string.IsNullOrWhiteSpace(profile.Username)) throw new InvalidOperationException("SSH username is empty.");

        AuthenticationMethod auth;
        if (profile.AuthType == SshAuthType.PrivateKey)
        {
            if (!File.Exists(profile.PrivateKeyPath)) throw new FileNotFoundException("SSH private key was not found.", profile.PrivateKeyPath);
            var passphrase = _credentials.ReadSecret(profile.KeyPassphraseCredentialTarget);
            var key = string.IsNullOrEmpty(passphrase) ? new PrivateKeyFile(profile.PrivateKeyPath) : new PrivateKeyFile(profile.PrivateKeyPath, passphrase);
            auth = new PrivateKeyAuthenticationMethod(profile.Username, key);
        }
        else
        {
            var password = _credentials.ReadSecret(profile.PasswordCredentialTarget)
                ?? throw new InvalidOperationException("No saved SSH password was found for this server. Edit the server profile and save the password first.");
            auth = new PasswordAuthenticationMethod(profile.Username, password);
        }

        var info = new ConnectionInfo(profile.Host, profile.Port, profile.Username, auth);
        var client = new SshClient(info);
        client.HostKeyReceived += (_, e) =>
        {
            var fp = Convert.ToHexString(SHA256.HashData(e.HostKey)).ToLowerInvariant();
            var formatted = string.Join(":", Enumerable.Range(0, fp.Length / 2).Select(i => fp.Substring(i * 2, 2)));
            fingerprintObserver?.Invoke(formatted);
            if (!string.IsNullOrWhiteSpace(profile.HostKeyFingerprint))
            {
                e.CanTrust = string.Equals(Normalize(profile.HostKeyFingerprint), Normalize(formatted), StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                e.CanTrust = true; // TOFU is confirmed and persisted by the Servers UI after Test Connection.
            }
        };
        return client;
    }

    private static string Normalize(string value) => value.Replace("SHA256:", "", StringComparison.OrdinalIgnoreCase).Replace(":", "").Trim();
}
