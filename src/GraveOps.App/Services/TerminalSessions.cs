using System.Diagnostics;
using GraveOps.App.Models;
using Renci.SshNet;

namespace GraveOps.App.Services;

public interface ITerminalSession : IDisposable
{
    string Title { get; }
    event Action<string>? OutputReceived;
    Task StartAsync();
    Task WriteLineAsync(string text);
    Task StopAsync();
}

internal sealed class TerminalTextFilter
{
    private enum Mode { Normal, Escape, Csi, Osc, OscEscape }
    private Mode _mode;
    private readonly StringBuilder _osc = new();

    public string Push(string chunk)
    {
        if (string.IsNullOrEmpty(chunk)) return "";

        var output = new StringBuilder(chunk.Length);
        foreach (var ch in chunk)
        {
            switch (_mode)
            {
                case Mode.Normal:
                    if (ch == '\u001b') { _mode = Mode.Escape; break; }
                    if (ch == '\r') break;
                    if (ch == '\b')
                    {
                        if (output.Length > 0 && output[^1] != '\n')
                            output.Length--;
                        break;
                    }
                    if (ch == '\0') break;
                    output.Append(ch);
                    break;

                case Mode.Escape:
                    if (ch == '[') { _mode = Mode.Csi; break; }
                    if (ch == ']') { _osc.Clear(); _mode = Mode.Osc; break; }
                    _mode = Mode.Normal;
                    break;

                case Mode.Csi:
                    // Final byte of a CSI sequence.
                    if (ch >= '@' && ch <= '~')
                        _mode = Mode.Normal;
                    break;

                case Mode.Osc:
                    // BEL terminates OSC.
                    if (ch == '\a') { _osc.Clear(); _mode = Mode.Normal; break; }
                    // ST can terminate OSC as ESC backslash.
                    if (ch == '\u001b') { _mode = Mode.OscEscape; break; }
                    _osc.Append(ch);
                    // Safety guard for malformed streams.
                    if (_osc.Length > 8192) { _osc.Clear(); _mode = Mode.Normal; }
                    break;

                case Mode.OscEscape:
                    _osc.Clear();
                    _mode = Mode.Normal;
                    break;
            }
        }

        return StripResidualArtifacts(output.ToString());
    }

    private static string StripResidualArtifacts(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var lines = text.Split('\n');
        var sb = new StringBuilder(text.Length);

        foreach (var original in lines)
        {
            var line = original;

            // Some shell/prompt configurations can emit a bare OSC payload
            // after another layer has consumed ESC + ] already. Only remove
            // the specific terminal-title prefix, never arbitrary "0;" text.
            if (line.StartsWith("0;", StringComparison.Ordinal) &&
                (line.Contains('@') || line.Contains(':')))
            {
                var prompt = FindPromptStart(line);
                line = prompt >= 0 ? line[prompt..] : "";
            }

            if (line.Length > 0)
                sb.Append(line);
            sb.Append('\n');
        }

        if (!text.EndsWith('\n') && sb.Length > 0)
            sb.Length--;

        return sb.ToString();
    }

    private static int FindPromptStart(string line)
    {
        // Locate the second user@host occurrence when a title payload was
        // glued directly to the real prompt.
        var firstAt = line.IndexOf('@');
        if (firstAt < 0) return -1;
        var secondAt = line.IndexOf('@', firstAt + 1);
        if (secondAt < 0) return -1;

        var start = secondAt;
        while (start > 0 && line[start - 1] != '\n' &&
               line[start - 1] != ' ' && line[start - 1] != ';')
            start--;

        return start;
    }
}

public sealed class LocalTerminalSession : ITerminalSession
{
    private readonly string _exe;
    private Process? _process;

    public string Title { get; }
    public event Action<string>? OutputReceived;

    public LocalTerminalSession(string title, string exe)
    {
        Title = title;
        _exe = exe;
    }

    public Task StartAsync()
    {
        var psi = new ProcessStartInfo(_exe)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        if (_exe.EndsWith("powershell.exe", StringComparison.OrdinalIgnoreCase))
            psi.Arguments = "-NoLogo";

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) OutputReceived?.Invoke(e.Data + Environment.NewLine);
        };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) OutputReceived?.Invoke(e.Data + Environment.NewLine);
        };

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        return Task.CompletedTask;
    }

    public Task WriteLineAsync(string text)
    {
        if (_process is null || _process.HasExited)
            throw new InvalidOperationException("Terminal session is not running.");

        // WriteLine preserves embedded newlines in pasted command blocks and
        // guarantees one final newline so the last command executes.
        _process.StandardInput.WriteLine(text);
        _process.StandardInput.Flush();
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        try
        {
            if (_process is { HasExited: false })
                _process.Kill(true);
        }
        catch { }

        _process?.Dispose();
        _process = null;
        return Task.CompletedTask;
    }

    public void Dispose() => StopAsync().GetAwaiter().GetResult();
}

public sealed class SshTerminalSession : ITerminalSession
{
    private readonly SshService _ssh;
    private readonly ServerProfile _profile;
    private readonly TerminalTextFilter _filter = new();
    private readonly StringBuilder _startupBuffer = new();
    private readonly Stopwatch _startupWatch = Stopwatch.StartNew();
    private bool _startupFlushed;
    private SshClient? _client;
    private ShellStream? _shell;
    private CancellationTokenSource? _cts;

    public string Title => $"SSH: {_profile.Name}";
    public event Action<string>? OutputReceived;

    public SshTerminalSession(SshService ssh, ServerProfile profile)
    {
        _ssh = ssh;
        _profile = profile;
    }

    public async Task StartAsync()
    {
        await Task.Run(() =>
        {
            _client = _ssh.BuildClient(_profile);
            _client.Connect();

            // GraveOps displays plain text, not a terminal emulator. TERM=dumb
            // prevents title/color/cursor sequences we cannot meaningfully render.
            _shell = _client.CreateShellStream("dumb", 180, 50, 1600, 1000, 32768);
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => ReadLoop(_cts.Token));
        });
    }

    private void ReadLoop(CancellationToken token)
    {
        var buffer = new byte[16384];
        var decoder = Encoding.UTF8.GetDecoder();
        var chars = new char[Encoding.UTF8.GetMaxCharCount(buffer.Length)];

        while (!token.IsCancellationRequested &&
               _client?.IsConnected == true &&
               _shell is not null)
        {
            try
            {
                if (!_shell.DataAvailable)
                {
                    Thread.Sleep(20);
                    continue;
                }

                var count = _shell.Read(buffer, 0, buffer.Length);
                if (count <= 0) continue;

                var charCount = decoder.GetChars(buffer, 0, count, chars, 0, false);
                var raw = new string(chars, 0, charCount);
                var clean = _filter.Push(raw);
                if (string.IsNullOrEmpty(clean)) continue;

                if (!_startupFlushed)
                {
                    _startupBuffer.Append(clean);
                    var startupText = _startupBuffer.ToString();

                    if (_startupWatch.ElapsedMilliseconds < 1200 &&
                        !LooksLikePrompt(startupText))
                        continue;

                    _startupFlushed = true;
                    _startupBuffer.Clear();

                    var normalized = NormalizeStartup(startupText);
                    if (!string.IsNullOrWhiteSpace(normalized))
                        OutputReceived?.Invoke(normalized);

                    continue;
                }

                OutputReceived?.Invoke(clean);
            }
            catch
            {
                break;
            }
        }
    }

    private static bool LooksLikePrompt(string text)
    {
        var trimmed = text.TrimEnd('\n', '\r');
        return trimmed.EndsWith("$ ", StringComparison.Ordinal) ||
               trimmed.EndsWith("# ", StringComparison.Ordinal) ||
               trimmed.EndsWith("$", StringComparison.Ordinal) ||
               trimmed.EndsWith("#", StringComparison.Ordinal);
    }

    private static string NormalizeStartup(string text)
    {
        text = text.Replace("\r", "").Trim();
        if (text.Length == 0) return "";

        // Linux MOTD fragments can occasionally be delivered twice by an
        // interactive SSH shell. Collapse exact duplicate paragraphs only
        // during startup; normal terminal output is never deduplicated.
        var paragraphs = text.Split(
            new[] { "\n\n" },
            StringSplitOptions.RemoveEmptyEntries);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var output = new StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            var clean = paragraph.Trim();
            if (clean.Length == 0 || !seen.Add(clean)) continue;

            if (output.Length > 0)
                output.AppendLine().AppendLine();

            output.Append(clean);
        }

        return output.Length == 0
            ? ""
            : output.ToString() + Environment.NewLine;
    }

    public Task WriteLineAsync(string text)
    {
        if (_shell is null)
            throw new InvalidOperationException("SSH shell is not connected.");

        // ShellStream.WriteLine sends the full text verbatim, including embedded
        // newlines, then adds one final newline. This supports large pasted
        // scripts, pipelines, quoted commands, continuations and heredocs.
        _shell.WriteLine(text);
        _shell.Flush();
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        try
        {
            _cts?.Cancel();
            _shell?.Dispose();
            if (_client?.IsConnected == true) _client.Disconnect();
            _client?.Dispose();
        }
        catch { }

        _shell = null;
        _client = null;
        _cts?.Dispose();
        _cts = null;
        return Task.CompletedTask;
    }

    public void Dispose() => StopAsync().GetAwaiter().GetResult();
}