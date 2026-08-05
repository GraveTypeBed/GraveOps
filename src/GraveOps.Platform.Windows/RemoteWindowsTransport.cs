using System.Buffers;
using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using GraveOps.Core.Security;
using GraveOps.Core.Targets;

namespace GraveOps.Platform.Windows;

public sealed record WindowsTlsValidationResult(
    bool Success,
    string Summary,
    string FailureMessage = "");

public interface IRemoteWindowsCertificateValidator
{
    Task<WindowsTlsValidationResult> ValidateAsync(
        RemoteWindowsConnectionOptions options,
        CancellationToken cancellationToken = default);
}

public sealed class SystemTrustWindowsCertificateValidator :
    IRemoteWindowsCertificateValidator
{
    public async Task<WindowsTlsValidationResult> ValidateAsync(
        RemoteWindowsConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            options);

        string observedPin =
            string.Empty;
        var observedErrors =
            SslPolicyErrors.None;
        var pinMatches =
            options.PinnedServerCertificateSha256 is null;

        try
        {
            using var client =
                new TcpClient();

            await client.ConnectAsync(
                options.Host,
                options.Port,
                cancellationToken);

            using var stream =
                new SslStream(
                    client.GetStream(),
                    leaveInnerStreamOpen: false,
                    (
                        _,
                        certificate,
                        _,
                        errors) =>
                    {
                        observedErrors =
                            errors;

                        if (certificate is null)
                            return false;

                        observedPin =
                            "SHA256:" +
                            Convert.ToHexString(
                                SHA256.HashData(
                                    certificate.GetRawCertData()));

                        pinMatches =
                            options.PinnedServerCertificateSha256 is null ||
                            options.PinnedServerCertificateSha256.Equals(
                                observedPin,
                                StringComparison.OrdinalIgnoreCase);

                        return errors ==
                                SslPolicyErrors.None &&
                            pinMatches;
                    });

            await stream.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions
                {
                    TargetHost =
                        options.Host,
                    EnabledSslProtocols =
                        SslProtocols.Tls12 |
                        SslProtocols.Tls13,
                    CertificateRevocationCheckMode =
                        X509RevocationMode.Online
                },
                cancellationToken);

            var summary =
                options.PinnedServerCertificateSha256 is null
                    ? "WinRM HTTPS certificate passed system trust and hostname validation."
                    : "WinRM HTTPS certificate passed system trust, hostname validation and SHA-256 pinning.";

            return new WindowsTlsValidationResult(
                true,
                summary);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (
            Exception exception)
            when (
                exception is AuthenticationException or
                SocketException or
                IOException)
        {
            var detail =
                !pinMatches &&
                !string.IsNullOrWhiteSpace(
                    observedPin)
                    ? $"The server certificate pin was {observedPin}."
                    : observedErrors !=
                        SslPolicyErrors.None
                        ? $"TLS policy errors: {observedErrors}."
                        : exception.Message;

            return new WindowsTlsValidationResult(
                false,
                "WinRM HTTPS certificate validation failed.",
                detail);
        }
    }
}

public static class WinRmPowerShellCommand
{
    public static IReadOnlyList<string> BuildArguments(
        string encodedWrapper)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            encodedWrapper);

        return new[]
        {
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-EncodedCommand",
            encodedWrapper
        };
    }
}

public interface IWinRmPowerShellProcessInvoker
{
    string ExecutableName { get; }

    Task<WindowsPowerShellResult> ExecuteAsync(
        string encodedWrapper,
        ReadOnlyMemory<byte> standardInput,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

public sealed class LocalWinRmPowerShellProcessInvoker :
    IWinRmPowerShellProcessInvoker
{
    private static readonly SemaphoreSlim ProcessGate =
        new(2, 2);

    private readonly string _executableName;

    public LocalWinRmPowerShellProcessInvoker(
        string? executableName = null)
    {
        _executableName =
            string.IsNullOrWhiteSpace(
                executableName)
                ? OperatingSystem.IsWindows()
                    ? "powershell.exe"
                    : "pwsh"
                : executableName.Trim();
    }

    public string ExecutableName =>
        _executableName;

    public async Task<WindowsPowerShellResult> ExecuteAsync(
        string encodedWrapper,
        ReadOnlyMemory<byte> standardInput,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            encodedWrapper);

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout));
        }

        var entered =
            false;
        Process? process =
            null;

        using var linked =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        linked.CancelAfter(
            timeout);

        try
        {
            await ProcessGate.WaitAsync(
                linked.Token);
            entered =
                true;

            process =
                new Process
                {
                    StartInfo =
                        new ProcessStartInfo
                        {
                            FileName =
                                _executableName,
                            RedirectStandardInput =
                                true,
                            RedirectStandardOutput =
                                true,
                            RedirectStandardError =
                                true,
                            UseShellExecute =
                                false,
                            CreateNoWindow =
                                true,
                            StandardOutputEncoding =
                                Encoding.UTF8,
                            StandardErrorEncoding =
                                Encoding.UTF8
                        }
                };

            foreach (var argument in
                     WinRmPowerShellCommand.BuildArguments(
                         encodedWrapper))
            {
                process.StartInfo.ArgumentList.Add(
                    argument);
            }

            process.Start();

            var stdoutTask =
                process.StandardOutput.ReadToEndAsync(
                    linked.Token);
            var stderrTask =
                process.StandardError.ReadToEndAsync(
                    linked.Token);

            await process.StandardInput.BaseStream.WriteAsync(
                standardInput,
                linked.Token);
            await process.StandardInput.BaseStream.FlushAsync(
                linked.Token);
            process.StandardInput.Close();

            await process.WaitForExitAsync(
                linked.Token);

            return new WindowsPowerShellResult(
                process.ExitCode,
                (await stdoutTask).Trim(),
                (await stderrTask).Trim());
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            TryKill(
                process);
            throw;
        }
        catch (OperationCanceledException)
        {
            TryKill(
                process);

            return new WindowsPowerShellResult(
                -1,
                string.Empty,
                string.Empty,
                TimedOut: true);
        }
        catch (Exception exception)
        {
            TryKill(
                process);

            return new WindowsPowerShellResult(
                -1,
                string.Empty,
                string.Empty,
                FailureMessage:
                    exception.Message);
        }
        finally
        {
            process?.Dispose();

            if (entered)
                ProcessGate.Release();
        }
    }

    private static void TryKill(
        Process? process)
    {
        try
        {
            if (process is not null &&
                !process.HasExited)
            {
                process.Kill(
                    entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort timeout cleanup.
        }
    }
}

public interface IRemoteWindowsPowerShellExecutor
{
    Task<WindowsPowerShellResult> ExecuteAsync(
        TargetProfile target,
        WindowsPowerShellRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class WinRmHttpsPowerShellExecutor :
    IRemoteWindowsPowerShellExecutor
{
    private const string WrapperScript =
        """
        [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
        $OutputEncoding = [Console]::OutputEncoding
        $ErrorActionPreference = 'Stop'

        $payloadText = [Console]::In.ReadToEnd()
        $payload = $payloadText | ConvertFrom-Json

        $securePassword = ConvertTo-SecureString `
            -String ([string]$payload.Password) `
            -AsPlainText `
            -Force

        $credential =
            [System.Management.Automation.PSCredential]::new(
                [string]$payload.Username,
                $securePassword
            )

        $remoteScript =
            [scriptblock]::Create(
                [string]$payload.RemoteScript
            )

        $sessionOption =
            New-PSSessionOption `
                -OperationTimeout (
                    [int]$payload.OperationTimeoutMilliseconds
                )

        $result =
            Invoke-Command `
                -ComputerName ([string]$payload.Host) `
                -Port ([int]$payload.Port) `
                -UseSSL `
                -Credential $credential `
                -Authentication ([string]$payload.Authentication) `
                -SessionOption $sessionOption `
                -ScriptBlock $remoteScript `
                -ErrorAction Stop

        [Console]::Out.Write(
            (
                @($result) |
                    ForEach-Object { [string]$_ }
            ) -join [Environment]::NewLine
        )
        """;

    private static readonly string EncodedWrapper =
        WindowsPowerShellEncoding.EncodeScript(
            WrapperScript);

    private readonly ICredentialVault _credentialVault;
    private readonly IWinRmPowerShellProcessInvoker _processInvoker;
    private readonly IRemoteWindowsCertificateValidator
        _certificateValidator;

    public WinRmHttpsPowerShellExecutor(
        ICredentialVault credentialVault,
        IWinRmPowerShellProcessInvoker? processInvoker = null,
        IRemoteWindowsCertificateValidator? certificateValidator = null)
    {
        _credentialVault =
            credentialVault ??
            throw new ArgumentNullException(
                nameof(credentialVault));
        _processInvoker =
            processInvoker ??
            new LocalWinRmPowerShellProcessInvoker();
        _certificateValidator =
            certificateValidator ??
            new SystemTrustWindowsCertificateValidator();
    }

    public async Task<WindowsPowerShellResult> ExecuteAsync(
        TargetProfile target,
        WindowsPowerShellRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            target);
        ArgumentNullException.ThrowIfNull(
            request);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.Script);

        cancellationToken.ThrowIfCancellationRequested();

        RemoteWindowsConnectionOptions options;

        try
        {
            options =
                RemoteWindowsConnectionParser.Parse(
                    target);
        }
        catch (Exception exception)
        {
            return Failure(
                exception.Message);
        }

        var executionTimeout =
            request.Timeout ??
            options.OperationTimeout;

        if (executionTimeout <= TimeSpan.Zero)
        {
            return Failure(
                "The remote Windows execution timeout must be positive.");
        }

        using var linked =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        linked.CancelAfter(
            executionTimeout);

        byte[]? payload =
            null;

        try
        {
            var tls =
                await _certificateValidator.ValidateAsync(
                    options,
                    linked.Token);

            if (!tls.Success)
            {
                return Failure(
                    string.IsNullOrWhiteSpace(
                        tls.FailureMessage)
                        ? tls.Summary
                        : $"{tls.Summary} {tls.FailureMessage}");
            }

            if (!_credentialVault.IsAvailable)
            {
                return Failure(
                    "The configured credential vault is unavailable.");
            }

            using var secret =
                await _credentialVault.RetrieveAsync(
                    options.CredentialReference,
                    linked.Token);

            if (secret is null)
            {
                return Failure(
                    "The remote Windows credential was not found in the configured vault.");
            }

            payload =
                BuildPayload(
                    options,
                    request.Script,
                    executionTimeout,
                    secret.Reveal().Span);

            return await _processInvoker.ExecuteAsync(
                EncodedWrapper,
                payload,
                executionTimeout,
                linked.Token);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new WindowsPowerShellResult(
                -1,
                string.Empty,
                string.Empty,
                TimedOut: true);
        }
        catch (Exception exception)
        {
            return Failure(
                exception.Message);
        }
        finally
        {
            if (payload is not null)
            {
                CryptographicOperations.ZeroMemory(
                    payload);
            }
        }
    }

    private static byte[] BuildPayload(
        RemoteWindowsConnectionOptions options,
        string remoteScript,
        TimeSpan timeout,
        ReadOnlySpan<char> password)
    {
        var buffer =
            new ArrayBufferWriter<byte>();

        using (
            var writer =
                new Utf8JsonWriter(
                    buffer))
        {
            writer.WriteStartObject();
            writer.WriteString(
                "Host",
                options.Host);
            writer.WriteNumber(
                "Port",
                options.Port);
            writer.WriteString(
                "Username",
                options.Username);
            writer.WriteString(
                "Password",
                password);
            writer.WriteString(
                "Authentication",
                options.Authentication.ToString());
            writer.WriteNumber(
                "OperationTimeoutMilliseconds",
                Math.Clamp(
                    (long)timeout.TotalMilliseconds,
                    1,
                    int.MaxValue));
            writer.WriteString(
                "RemoteScript",
                remoteScript);
            writer.WriteEndObject();
            writer.Flush();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static WindowsPowerShellResult Failure(
        string message) =>
        new(
            -1,
            string.Empty,
            string.Empty,
            FailureMessage:
                message);
}

public sealed class RemoteWindowsPowerShellRunner :
    IWindowsPowerShellRunner
{
    private readonly TargetProfile _target;
    private readonly RemoteWindowsConnectionOptions _options;
    private readonly IRemoteWindowsPowerShellExecutor _executor;

    public RemoteWindowsPowerShellRunner(
        TargetProfile target,
        IRemoteWindowsPowerShellExecutor executor)
    {
        _target =
            target ??
            throw new ArgumentNullException(
                nameof(target));
        _options =
            RemoteWindowsConnectionParser.Parse(
                target);
        _executor =
            executor ??
            throw new ArgumentNullException(
                nameof(executor));
    }

    public string RunnerId =>
        $"windows.remote-winrm-https.{_target.Id}";

    public bool IsWindowsTarget =>
        true;

    public string MachineNameFallback =>
        _options.Host;

    public Task<WindowsPowerShellResult> ExecuteAsync(
        WindowsPowerShellRequest request,
        CancellationToken cancellationToken = default) =>
        _executor.ExecuteAsync(
            _target,
            request,
            cancellationToken);
}
