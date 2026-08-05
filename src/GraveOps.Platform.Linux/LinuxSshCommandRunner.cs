using System.Globalization;
using System.Text;

namespace GraveOps.Platform.Linux;

public sealed record LinuxSshScriptResult(
    string StandardOutput,
    string StandardError);

public interface ILinuxSshScriptExecutor
{
    string ExecutorId { get; }
    string CacheKey { get; }
    string MachineNameFallback { get; }

    Task<LinuxSshScriptResult> ExecuteScriptAsync(
        string script,
        CancellationToken cancellationToken = default);
}

public sealed class SshLinuxCommandRunner :
    ILinuxCommandRunner
{
    private readonly ILinuxSshScriptExecutor _executor;
    private readonly SemaphoreSlim _executionGate =
        new(1, 1);

    public SshLinuxCommandRunner(
        ILinuxSshScriptExecutor executor)
    {
        _executor = executor ??
            throw new ArgumentNullException(
                nameof(executor));
    }

    public string RunnerId =>
        $"linux.ssh.{_executor.ExecutorId}";

    public string CacheKey =>
        _executor.CacheKey;

    public bool IsLinuxTarget =>
        true;

    public string MachineNameFallback =>
        _executor.MachineNameFallback;

    public async Task<LinuxCommandResult> ExecuteAsync(
        LinuxCommandRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.Executable);

        var entered = false;
        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        timeout.CancelAfter(
            request.Timeout ??
            LinuxCommandDefaults.DefaultTimeout);

        try
        {
            // Serialize the SSH command stream. OpenSSH connection
            // multiplexing then reuses the authenticated master session.
            await _executionGate.WaitAsync(
                timeout.Token);
            entered = true;

            var script =
                BuildScript(
                    request.Executable,
                    request.Arguments ??
                    Array.Empty<string>(),
                    out var exitMarker);

            var result =
                await _executor.ExecuteScriptAsync(
                    script,
                    timeout.Token);

            return ParseResult(
                result,
                exitMarker);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new LinuxCommandResult(
                -1,
                string.Empty,
                string.Empty,
                TimedOut: true);
        }
        catch (Exception exception)
        {
            return new LinuxCommandResult(
                -1,
                string.Empty,
                string.Empty,
                FailureMessage: exception.Message);
        }
        finally
        {
            if (entered)
                _executionGate.Release();
        }
    }

    public async Task<LinuxTextFileResult> ReadTextFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var result =
            await ExecuteAsync(
                new LinuxCommandRequest(
                    "cat",
                    new[]
                    {
                        "--",
                        path
                    },
                    $"read {path}"),
                cancellationToken);

        if (result.TimedOut)
        {
            return new LinuxTextFileResult(
                false,
                string.Empty,
                "The remote file read timed out.");
        }

        if (!string.IsNullOrWhiteSpace(
                result.FailureMessage))
        {
            return new LinuxTextFileResult(
                false,
                string.Empty,
                result.FailureMessage);
        }

        return result.ExitCode == 0
            ? new LinuxTextFileResult(
                true,
                result.StandardOutput)
            : new LinuxTextFileResult(
                false,
                string.Empty,
                string.IsNullOrWhiteSpace(
                    result.StandardError)
                    ? $"Remote cat exited with code {result.ExitCode}."
                    : result.StandardError);
    }

    public async Task<int> GetLogicalProcessorCountAsync(
        CancellationToken cancellationToken = default)
    {
        var result =
            await ExecuteAsync(
                new LinuxCommandRequest(
                    "sh",
                    new[]
                    {
                        "-c",
                        "getconf _NPROCESSORS_ONLN 2>/dev/null || " +
                        "nproc 2>/dev/null || printf '1\\n'"
                    },
                    "logical processor count"),
                cancellationToken);

        var token =
            result.StandardOutput
                .Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();

        return int.TryParse(
                   token,
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out var count)
            ? Math.Max(1, count)
            : 1;
    }

    private static string BuildScript(
        string executable,
        IReadOnlyList<string> arguments,
        out string exitMarker)
    {
        exitMarker =
            $"__GRAVEOPS_EXIT_{Guid.NewGuid():N}__";

        var command =
            new StringBuilder(
                ShellQuote(executable));

        foreach (var argument in arguments)
        {
            command.Append(' ');
            command.Append(
                ShellQuote(argument));
        }

        return $"""
                set +e
                export LC_ALL=C
                {command}
                graveops_exit=$?
                printf '\n{exitMarker}%s\n' "$graveops_exit" >&2
                exit 0
                """;
    }

    private static LinuxCommandResult ParseResult(
        LinuxSshScriptResult result,
        string exitMarker)
    {
        var stdout =
            result.StandardOutput ??
            string.Empty;
        var stderr =
            result.StandardError ??
            string.Empty;

        var markerIndex =
            stderr.LastIndexOf(
                exitMarker,
                StringComparison.Ordinal);

        if (markerIndex < 0)
        {
            return new LinuxCommandResult(
                -1,
                stdout.Trim(),
                stderr.Trim(),
                FailureMessage:
                    "The SSH command response did not include its exit-code marker.");
        }

        var afterMarker =
            stderr[
                (markerIndex + exitMarker.Length)..];
        var lineEnd =
            afterMarker.IndexOfAny(
                '\r',
                '\n');
        var exitText =
            (lineEnd >= 0
                ? afterMarker[..lineEnd]
                : afterMarker)
            .Trim();

        if (!int.TryParse(
                exitText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var exitCode))
        {
            return new LinuxCommandResult(
                -1,
                stdout.Trim(),
                stderr.Trim(),
                FailureMessage:
                    "The SSH command response contained an invalid exit code.");
        }

        var beforeMarker =
            stderr[..markerIndex]
                .TrimEnd(
                    '\r',
                    '\n');
        var afterMarkerLine =
            lineEnd >= 0
                ? afterMarker[(lineEnd + 1)..]
                    .Trim()
                : string.Empty;

        var cleanError =
            string.IsNullOrWhiteSpace(
                afterMarkerLine)
                ? beforeMarker
                : string.IsNullOrWhiteSpace(
                    beforeMarker)
                    ? afterMarkerLine
                    : beforeMarker +
                      Environment.NewLine +
                      afterMarkerLine;

        return new LinuxCommandResult(
            exitCode,
            stdout.Trim(),
            cleanError.Trim());
    }

    private static string ShellQuote(
        string value) =>
        "'" +
        (value ?? string.Empty)
            .Replace(
                "'",
                "'\"'\"'",
                StringComparison.Ordinal) +
        "'";
}
