using GraveOps.App.Models;
using Renci.SshNet;

namespace GraveOps.App.Services;

public sealed class SftpService
{
    private readonly CredentialManagerService _credentials;
    public SftpService(CredentialManagerService credentials) => _credentials = credentials;

    private SftpClient Build(ServerProfile profile)
    {
        AuthenticationMethod auth;
        if (profile.AuthType == SshAuthType.PrivateKey)
        {
            var passphrase = _credentials.ReadSecret(profile.KeyPassphraseCredentialTarget);
            var key = string.IsNullOrEmpty(passphrase) ? new PrivateKeyFile(profile.PrivateKeyPath) : new PrivateKeyFile(profile.PrivateKeyPath, passphrase);
            auth = new PrivateKeyAuthenticationMethod(profile.Username, key);
        }
        else
        {
            var password = _credentials.ReadSecret(profile.PasswordCredentialTarget) ?? throw new InvalidOperationException("Saved SSH password not found.");
            auth = new PasswordAuthenticationMethod(profile.Username, password);
        }
        return new SftpClient(new ConnectionInfo(profile.Host, profile.Port, profile.Username, auth));
    }

    public Task<List<RemoteFileItem>> ListAsync(ServerProfile profile, string path) => Task.Run(() =>
    {
        using var client = Build(profile); client.Connect();
        var items = client.ListDirectory(path)
            .Where(x => x.Name is not "." and not "..")
            .Select(x => new RemoteFileItem
            {
                Name = x.Name,
                FullPath = x.FullName,
                IsDirectory = x.IsDirectory,
                Size = x.Attributes.Size,
                LastWriteTime = x.LastWriteTime
            }).OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        client.Disconnect(); return items;
    });

    public Task DownloadAsync(ServerProfile profile, string remotePath, string localPath) => Task.Run(() =>
    {
        using var client = Build(profile); client.Connect();
        using var fs = File.Create(localPath); client.DownloadFile(remotePath, fs); client.Disconnect();
    });

    public Task UploadAsync(ServerProfile profile, string localPath, string remotePath) => Task.Run(() =>
    {
        using var client = Build(profile); client.Connect();
        using var fs = File.OpenRead(localPath); client.UploadFile(fs, remotePath, true); client.Disconnect();
    });

    public Task<string> ReadTextAsync(ServerProfile profile, string remotePath) => Task.Run(() =>
    {
        using var client = Build(profile); client.Connect();
        using var ms = new MemoryStream(); client.DownloadFile(remotePath, ms); client.Disconnect();
        return Encoding.UTF8.GetString(ms.ToArray());
    });
    public Task WriteTextAsync(ServerProfile profile, string remotePath, string content) => Task.Run(() =>
    {
        using var client = Build(profile); client.Connect();
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(content)); client.UploadFile(ms, remotePath, true); client.Disconnect();
    });

}
