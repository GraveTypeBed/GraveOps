using GraveOps.Platform.Linux;

namespace GraveOps.Desktop.Linux;

internal sealed class LinuxSshScriptExecutor :
    ILinuxSshScriptExecutor
{
    private readonly LinuxHostProfile _profile;
    private readonly LinuxCredentialStore _credentials;
    private readonly string _knownHostsDirectory;
    private readonly SemaphoreSlim _scanGate =
        new(1, 1);

    private LinuxHostKeyScanResult? _scan;

    public LinuxSshScriptExecutor(
        LinuxHostProfile profile,
        LinuxCredentialStore credentials,
        string knownHostsDirectory)
    {
        _profile = profile ??
            throw new ArgumentNullException(
                nameof(profile));
        _credentials = credentials ??
            throw new ArgumentNullException(
                nameof(credentials));
        _knownHostsDirectory =
            string.IsNullOrWhiteSpace(
                knownHostsDirectory)
                ? throw new ArgumentException(
                    "The known-hosts directory is required.",
                    nameof(knownHostsDirectory))
                : knownHostsDirectory;
    }

    public string ExecutorId =>
        "openssh";

    public string CacheKey =>
        _profile.Id;

    public string MachineNameFallback =>
        string.IsNullOrWhiteSpace(
            _profile.Host)
            ? _profile.DisplayName
            : _profile.Host;

    public async Task<LinuxSshScriptResult> ExecuteScriptAsync(
        string script,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            script);

        var scan =
            await GetScanAsync(
                cancellationToken);

        var result =
            await LinuxSshTransport.RunVerifiedScriptAsync(
                _profile,
                _credentials,
                _knownHostsDirectory,
                script,
                suppliedSecret: null,
                scan,
                cancellationToken);

        return new LinuxSshScriptResult(
            result.StandardOutput,
            result.StandardError);
    }

    private async Task<LinuxHostKeyScanResult> GetScanAsync(
        CancellationToken cancellationToken)
    {
        await _scanGate.WaitAsync(
            cancellationToken);

        try
        {
            _scan ??=
                await LinuxSshTransport.ScanFingerprintAsync(
                    _profile,
                    cancellationToken);

            return _scan;
        }
        finally
        {
            _scanGate.Release();
        }
    }
}
