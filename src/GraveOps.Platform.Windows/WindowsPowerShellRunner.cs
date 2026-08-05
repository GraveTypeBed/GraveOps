using System.Diagnostics;
using System.Text;
using GraveOps.Core.Execution;

namespace GraveOps.Platform.Windows;

public static class WindowsCommandDefaults
{
    public static TimeSpan DefaultTimeout { get; } =
        TimeSpan.FromSeconds(45);
}

public sealed record WindowsPowerShellRequest(
    string Script,
    string OperationName,
    TimeSpan? Timeout = null);

public sealed record WindowsPowerShellResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut = false,
    string FailureMessage = "");

public interface IWindowsPowerShellRunner :
    IQueryRunner<
        WindowsPowerShellRequest,
        WindowsPowerShellResult>
{
    bool IsWindowsTarget { get; }
    string MachineNameFallback { get; }
}

public static class WindowsPowerShellEncoding
{
    public static string EncodeScript(
        string script)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            script);

        return Convert.ToBase64String(
            Encoding.Unicode.GetBytes(
                script));
    }
}

public sealed class LocalWindowsPowerShellRunner :
    IWindowsPowerShellRunner
{
    private static readonly SemaphoreSlim ProcessGate =
        new(3, 3);

    public string RunnerId =>
        "windows.local-powershell";

    public bool IsWindowsTarget =>
        OperatingSystem.IsWindows();

    public string MachineNameFallback =>
        Environment.MachineName;

    public async Task<WindowsPowerShellResult> ExecuteAsync(
        WindowsPowerShellRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            request);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.Script);

        if (!IsWindowsTarget)
        {
            return new WindowsPowerShellResult(
                -1,
                string.Empty,
                string.Empty,
                FailureMessage:
                    "The local Windows PowerShell runner requires Windows.");
        }

        var entered = false;
        Process? process = null;

        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        timeout.CancelAfter(
            request.Timeout ??
            WindowsCommandDefaults.DefaultTimeout);

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
                            FileName =
                                ResolvePowerShellExecutable(),
                            RedirectStandardOutput =
                                true,
                            RedirectStandardError =
                                true,
                            RedirectStandardInput =
                                true,
                            StandardInputEncoding =
                                Encoding.ASCII,
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

            process.StartInfo.ArgumentList.Add(
                "-NoLogo");
            process.StartInfo.ArgumentList.Add(
                "-NoProfile");
            process.StartInfo.ArgumentList.Add(
                "-NonInteractive");
            process.StartInfo.ArgumentList.Add(
                "-ExecutionPolicy");
            process.StartInfo.ArgumentList.Add(
                "Bypass");
            process.StartInfo.ArgumentList.Add(
                "-Command");
            process.StartInfo.ArgumentList.Add(
                "$encoded=[Console]::In.ReadToEnd();" +
                "$script=[Text.Encoding]::Unicode.GetString(" +
                "[Convert]::FromBase64String($encoded));" +
                "&([ScriptBlock]::Create($script))");

            process.Start();

            var stdoutTask =
                process.StandardOutput.ReadToEndAsync(
                    timeout.Token);
            var stderrTask =
                process.StandardError.ReadToEndAsync(
                    timeout.Token);

            await process.StandardInput.WriteAsync(
                WindowsPowerShellEncoding.EncodeScript(
                    request.Script));
            await process.StandardInput.FlushAsync();
            process.StandardInput.Close();

            await process.WaitForExitAsync(
                timeout.Token);

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

    private static string ResolvePowerShellExecutable()
    {
        var windowsDirectory =
            Environment.GetFolderPath(
                Environment.SpecialFolder.Windows);

        if (!string.IsNullOrWhiteSpace(
                windowsDirectory))
        {
            var candidate =
                Path.Combine(
                    windowsDirectory,
                    "System32",
                    "WindowsPowerShell",
                    "v1.0",
                    "powershell.exe");

            if (File.Exists(
                    candidate))
            {
                return candidate;
            }
        }

        return "powershell.exe";
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
