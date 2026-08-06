using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GraveOps.Desktop.Linux;

public sealed record PlatformHardeningRecovery(
    string OriginalPath,
    string QuarantinePath,
    string Kind,
    string Reason);

public sealed record PlatformHardeningSnapshot(
    bool Started,
    string ConfigRoot,
    string CacheRoot,
    int JsonFilesChecked,
    int JsonLineFilesChecked,
    int RecoveredFiles,
    int RedactionRuleCount,
    IReadOnlyList<PlatformHardeningRecovery> Recoveries)
{
    public string Summary =>
        $"platform hardening {(Started ? "active" : "not started")} · " +
        $"{JsonFilesChecked + JsonLineFilesChecked} persisted file(s) audited · " +
        $"{RecoveredFiles} recovered · {RedactionRuleCount} redaction rules";
}

public sealed record HardenedProcessResult(
    bool Success,
    bool TimedOut,
    bool Cancelled,
    int ExitCode,
    string Summary,
    string Output);

public sealed class PlatformHardeningService : IPlatformHardeningPort
{
    private const int DefaultOutputLimit = 131072;
    private const int MaximumJsonLineCount = 2000;
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private static readonly Regex AuthorizationHeader = new(
        @"(?im)(\bauthorization\s*:\s*(?:bearer|basic)\s+)[^\s\r\n]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SecretHeader = new(
        @"(?im)(\b(?:x-api-key|api-key|apikey|x-plex-token|cookie|set-cookie)\s*:\s*)[^\r\n]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SecretAssignment = new(
        @"(?i)([""']?(?:api[_-]?key|apikey|x-plex-token|token|access[_-]?token|refresh[_-]?token|password|passwd|secret|client[_-]?secret)[""']?\s*[:=]\s*)(?:""[^""]*""|'[^']*'|[^\s,;&]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SecretQuery = new(
        @"(?i)([?&](?:api[_-]?key|apikey|x-plex-token|token|access[_-]?token|password|secret)=)[^&\s]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex UrlCredentials = new(
        @"(?i)(https?://)[^/\s:@]+:[^/\s@]+@",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex EnvironmentSecret = new(
        @"(?im)(\b(?:API_KEY|APIKEY|TOKEN|ACCESS_TOKEN|REFRESH_TOKEN|PASSWORD|PASSWD|SECRET|CLIENT_SECRET)\s*=\s*)[^\s\r\n]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly object _gate = new();
    private readonly string _configRoot;
    private readonly string _cacheRoot;
    private readonly string _auditPath;
    private readonly string _crashLogPath;
    private PlatformHardeningSnapshot _snapshot;
    private bool _started;
    private bool _disposed;

    public PlatformHardeningService(
        string? configRoot = null,
        string? cacheRoot = null)
    {
        var home = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        var configBase = string.IsNullOrWhiteSpace(configRoot)
            ? Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
            : configRoot;
        if (string.IsNullOrWhiteSpace(configBase))
            configBase = Path.Combine(home, ".config");
        var cacheBase = string.IsNullOrWhiteSpace(cacheRoot)
            ? Environment.GetEnvironmentVariable("XDG_CACHE_HOME")
            : cacheRoot;
        if (string.IsNullOrWhiteSpace(cacheBase))
            cacheBase = Path.Combine(home, ".cache");
        _configRoot = Path.Combine(configBase, "GraveOps");
        _cacheRoot = Path.Combine(cacheBase, "GraveOps");
        _auditPath = Path.Combine(_cacheRoot, "platform-hardening-audit.json");
        _crashLogPath = Path.Combine(_cacheRoot, "platform-crash.log");
        _snapshot = new PlatformHardeningSnapshot(
            false,
            _configRoot,
            _cacheRoot,
            0,
            0,
            0,
            6,
            Array.Empty<PlatformHardeningRecovery>());
    }

    public string AuditPath => _auditPath;
    public string CrashLogPath => _crashLogPath;
    public PlatformHardeningSnapshot Snapshot => _snapshot;

    public void Start()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_started)
                return;

            Directory.CreateDirectory(_configRoot);
            Directory.CreateDirectory(_cacheRoot);
            TrySetPrivatePermissions(_configRoot, directory: true);
            TrySetPrivatePermissions(_cacheRoot, directory: true);

            var recoveries = new List<PlatformHardeningRecovery>();
            var jsonChecked = AuditJsonFiles(recoveries);
            var jsonLinesChecked = AuditJsonLineFiles(recoveries);
            _started = true;
            _snapshot = new PlatformHardeningSnapshot(
                true,
                _configRoot,
                _cacheRoot,
                jsonChecked,
                jsonLinesChecked,
                recoveries.Count,
                6,
                recoveries.ToArray());
            AtomicWrite(
                _auditPath,
                JsonSerializer.Serialize(
                    _snapshot,
                    new JsonSerializerOptions { WriteIndented = true }) +
                Environment.NewLine);
            TrySetPrivatePermissions(_auditPath, directory: false);
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }
    }

    public string Redact(
        string? value,
        int maxCharacters = DefaultOutputLimit)
    {
        var text = value ?? string.Empty;
        text = AuthorizationHeader.Replace(text, "$1[REDACTED]");
        text = SecretHeader.Replace(text, "$1[REDACTED]");
        text = SecretAssignment.Replace(text, "$1[REDACTED]");
        text = SecretQuery.Replace(text, "$1[REDACTED]");
        text = UrlCredentials.Replace(text, "$1[REDACTED]:[REDACTED]@");
        text = EnvironmentSecret.Replace(text, "$1[REDACTED]");
        var limit = Math.Clamp(maxCharacters, 256, 1048576);
        if (text.Length <= limit)
            return text;
        return text[..limit] +
            Environment.NewLine +
            $"… output truncated after {limit} characters";
    }

    public string SanitizeException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return Redact(
            $"{exception.GetType().Name}: {exception.Message}",
            8192);
    }

    public async Task<HardenedProcessResult> RunShellAsync(
        string command,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(command))
        {
            return new HardenedProcessResult(
                false,
                false,
                false,
                -1,
                "No command is available.",
                string.Empty);
        }

        var effectiveTimeout = timeout <= TimeSpan.Zero
            ? TimeSpan.FromSeconds(30)
            : timeout;
        using var timeoutCancellation = new CancellationTokenSource(
            effectiveTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("-lc");
        process.StartInfo.ArgumentList.Add(command);
        process.StartInfo.Environment["LC_ALL"] = "C";
        process.StartInfo.Environment["NO_COLOR"] = "1";

        try
        {
            process.Start();
            var stdout = ReadBoundedAsync(
                process.StandardOutput,
                DefaultOutputLimit,
                linked.Token);
            var stderr = ReadBoundedAsync(
                process.StandardError,
                DefaultOutputLimit,
                linked.Token);
            try
            {
                await process.WaitForExitAsync(linked.Token);
            }
            catch (OperationCanceledException)
            {
                TryKillProcessTree(process);
                var timedOut = timeoutCancellation.IsCancellationRequested &&
                    !cancellationToken.IsCancellationRequested;
                var cancelled = cancellationToken.IsCancellationRequested;
                return new HardenedProcessResult(
                    false,
                    timedOut,
                    cancelled,
                    -1,
                    timedOut ? "Command timed out." : "Command was cancelled.",
                    timedOut
                        ? $"Timeout after {effectiveTimeout.TotalSeconds:0.###} seconds."
                        : "Cancellation requested.");
            }

            var output = string.Join(
                Environment.NewLine,
                new[]
                {
                    await stdout,
                    await stderr
                }.Where(item => !string.IsNullOrWhiteSpace(item)));
            output = Redact(output, DefaultOutputLimit);
            return new HardenedProcessResult(
                process.ExitCode == 0,
                false,
                false,
                process.ExitCode,
                process.ExitCode == 0
                    ? "Command completed."
                    : $"Command failed with exit code {process.ExitCode}.",
                output);
        }
        catch (Exception exception)
        {
            TryKillProcessTree(process);
            return new HardenedProcessResult(
                false,
                false,
                false,
                -1,
                "Command could not be started.",
                SanitizeException(exception));
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
            _disposed = true;
        }
    }

    private int AuditJsonFiles(
        ICollection<PlatformHardeningRecovery> recoveries)
    {
        var checkedCount = 0;
        foreach (var path in Directory
                     .EnumerateFiles(_configRoot, "*.json")
                     .Where(path => !path.EndsWith(
                         ".corrupt.json",
                         StringComparison.OrdinalIgnoreCase))
                     .OrderBy(path => path, StringComparer.Ordinal)
                     .Take(128))
        {
            checkedCount++;
            try
            {
                using var stream = File.OpenRead(path);
                using var _ = JsonDocument.Parse(stream);
                TrySetPrivatePermissions(path, directory: false);
            }
            catch (Exception exception)
            {
                recoveries.Add(RecoverCorruptFile(
                    path,
                    "JSON settings",
                    exception.Message,
                    "{}" + Environment.NewLine));
            }
        }
        return checkedCount;
    }

    private int AuditJsonLineFiles(
        ICollection<PlatformHardeningRecovery> recoveries)
    {
        var checkedCount = 0;
        foreach (var path in Directory
                     .EnumerateFiles(_cacheRoot, "*.jsonl")
                     .OrderBy(path => path, StringComparer.Ordinal)
                     .Take(64))
        {
            checkedCount++;
            try
            {
                var valid = new Queue<string>();
                var invalid = 0;
                foreach (var line in File.ReadLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;
                    try
                    {
                        using var _ = JsonDocument.Parse(line);
                        valid.Enqueue(line);
                        while (valid.Count > MaximumJsonLineCount)
                            valid.Dequeue();
                    }
                    catch
                    {
                        invalid++;
                    }
                }
                if (invalid > 0)
                {
                    var recovery = Quarantine(path, "JSON-lines cache",
                        $"{invalid} malformed record(s) removed");
                    AtomicWrite(
                        path,
                        valid.Count == 0
                            ? string.Empty
                            : string.Join(Environment.NewLine, valid) +
                              Environment.NewLine);
                    TrySetPrivatePermissions(path, directory: false);
                    recoveries.Add(recovery);
                }
                else
                {
                    TrySetPrivatePermissions(path, directory: false);
                }
            }
            catch (Exception exception)
            {
                recoveries.Add(RecoverCorruptFile(
                    path,
                    "JSON-lines cache",
                    exception.Message,
                    string.Empty));
            }
        }
        return checkedCount;
    }

    private PlatformHardeningRecovery RecoverCorruptFile(
        string path,
        string kind,
        string reason,
        string replacement)
    {
        var recovery = Quarantine(path, kind, reason);
        AtomicWrite(path, replacement);
        TrySetPrivatePermissions(path, directory: false);
        return recovery;
    }

    private static PlatformHardeningRecovery Quarantine(
        string path,
        string kind,
        string reason)
    {
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff");
        var quarantine = path + $".corrupt-{stamp}";
        var suffix = 0;
        while (File.Exists(quarantine))
            quarantine = path + $".corrupt-{stamp}-{++suffix}";
        File.Move(path, quarantine);
        TrySetPrivatePermissions(quarantine, directory: false);
        return new PlatformHardeningRecovery(
            path,
            quarantine,
            kind,
            reason);
    }

    private static async Task<string> ReadBoundedAsync(
        StreamReader reader,
        int maxCharacters,
        CancellationToken cancellationToken)
    {
        var output = new StringBuilder(
            Math.Min(maxCharacters, 16384));
        var buffer = new char[4096];
        var truncated = false;
        while (true)
        {
            int read;
            try
            {
                read = await reader.ReadAsync(
                    buffer.AsMemory(0, buffer.Length),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            if (read == 0)
                break;
            var remaining = maxCharacters - output.Length;
            if (remaining > 0)
                output.Append(buffer, 0, Math.Min(read, remaining));
            if (read > remaining)
                truncated = true;
        }
        if (truncated)
        {
            output.Append(Environment.NewLine);
            output.Append(
                $"… output truncated after {maxCharacters} characters");
        }
        return output.ToString().Trim();
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best effort. The caller still reports timeout or cancellation.
        }
    }

    private static void AtomicWrite(string path, string content)
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(path) ?? ".");
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(
                       temporary,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            using (var writer = new StreamWriter(
                       stream,
                       Utf8WithoutBom))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            TrySetPrivatePermissions(temporary, directory: false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
            catch
            {
                // Atomic replacement already failed; preserve original error.
            }
        }
    }

    private static void TrySetPrivatePermissions(
        string path,
        bool directory)
    {
        if (OperatingSystem.IsWindows())
            return;
        try
        {
            var mode = directory
                ? UnixFileMode.UserRead |
                  UnixFileMode.UserWrite |
                  UnixFileMode.UserExecute
                : UnixFileMode.UserRead |
                  UnixFileMode.UserWrite;
            File.SetUnixFileMode(path, mode);
        }
        catch
        {
            // Some mounted filesystems do not support Unix mode changes.
        }
    }

    private void OnUnhandledException(
        object sender,
        UnhandledExceptionEventArgs args)
    {
        var detail = args.ExceptionObject is Exception exception
            ? SanitizeException(exception)
            : Redact(args.ExceptionObject?.ToString(), 8192);
        AppendCrashRecord("Unhandled exception", detail);
    }

    private void OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs args)
    {
        AppendCrashRecord(
            "Unobserved task exception",
            SanitizeException(args.Exception));
        args.SetObserved();
    }

    private void AppendCrashRecord(string kind, string detail)
    {
        try
        {
            var line =
                $"{DateTimeOffset.UtcNow:O}\t{kind}\t{Redact(detail, 8192)}" +
                Environment.NewLine;
            lock (_gate)
            {
                Directory.CreateDirectory(_cacheRoot);
                File.AppendAllText(_crashLogPath, line, Utf8WithoutBom);
                TrySetPrivatePermissions(_crashLogPath, directory: false);
            }
        }
        catch
        {
            // Crash reporting must never cause a second failure.
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
