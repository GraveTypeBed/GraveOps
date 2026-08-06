using System.Diagnostics;
using GraveOps.Core.Execution;

namespace GraveOps.Platform.Linux;

public static class LinuxCommandDefaults
{
    public static TimeSpan DefaultTimeout { get; } =
        TimeSpan.FromSeconds(15);
}

public sealed record LinuxCommandRequest(
    string Executable,
    IReadOnlyList<string> Arguments,
    string OperationName,
    TimeSpan? Timeout = null);

public sealed record LinuxCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut = false,
    string FailureMessage = "");

public sealed record LinuxTextFileResult(
    bool Success,
    string Content,
    string Error = "");

public interface ILinuxCommandRunner :
    IQueryRunner<LinuxCommandRequest, LinuxCommandResult>
{
    string CacheKey { get; }
    bool IsLinuxTarget { get; }
    string MachineNameFallback { get; }

    Task<LinuxTextFileResult> ReadTextFileAsync(
        string path,
        CancellationToken cancellationToken = default);

    Task<int> GetLogicalProcessorCountAsync(
        CancellationToken cancellationToken = default);
}

public sealed class LocalLinuxCommandRunner :
    ILinuxCommandRunner
{
    private static readonly SemaphoreSlim ProcessGate =
        new(4, 4);

    public string RunnerId =>
        "linux.local-process";

    public string CacheKey =>
        "local";

    public bool IsLinuxTarget =>
        OperatingSystem.IsLinux();

    public string MachineNameFallback =>
        Environment.MachineName;

    public Task<int> GetLogicalProcessorCountAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            Math.Max(1, Environment.ProcessorCount));
    }

    public async Task<LinuxTextFileResult> ReadTextFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            if (!File.Exists(path))
            {
                return new LinuxTextFileResult(
                    false,
                    string.Empty);
            }

            return new LinuxTextFileResult(
                true,
                await File.ReadAllTextAsync(
                    path,
                    cancellationToken));
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new LinuxTextFileResult(
                false,
                string.Empty,
                exception.Message);
        }
    }

    public async Task<LinuxCommandResult> ExecuteAsync(
        LinuxCommandRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.Executable);

        var entered = false;
        Process? process = null;
        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        timeout.CancelAfter(
            request.Timeout ??
            LinuxCommandDefaults.DefaultTimeout);

        try
        {
            await ProcessGate.WaitAsync(
                timeout.Token);
            entered = true;

            process =
                new Process
                {
                    StartInfo =
                        new ProcessStartInfo
                        {
                            FileName = request.Executable,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                };

            foreach (var argument in request.Arguments)
            {
                process.StartInfo.ArgumentList.Add(
                    argument);
            }

            process.Start();

            var stdoutTask =
                process.StandardOutput.ReadToEndAsync(
                    timeout.Token);
            var stderrTask =
                process.StandardError.ReadToEndAsync(
                    timeout.Token);

            await process.WaitForExitAsync(
                timeout.Token);

            return new LinuxCommandResult(
                process.ExitCode,
                (await stdoutTask).Trim(),
                (await stderrTask).Trim());
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw;
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return new LinuxCommandResult(
                -1,
                string.Empty,
                string.Empty,
                TimedOut: true);
        }
        catch (Exception exception)
        {
            TryKill(process);
            return new LinuxCommandResult(
                -1,
                string.Empty,
                string.Empty,
                FailureMessage: exception.Message);
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
