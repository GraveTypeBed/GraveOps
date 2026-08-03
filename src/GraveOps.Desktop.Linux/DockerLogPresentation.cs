using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GraveOps.Desktop.Linux;

public sealed record DockerLogPresentation(
    string RawText,
    string CleanedText,
    int RawLineCount,
    int CleanedEntryCount,
    int CollapsedLineCount);

public static class DockerLogPresenter
{
    private static readonly Regex DockerTimestampRegex =
        new(
            @"^(?<timestamp>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?Z)\s+(?<body>.*)$",
            RegexOptions.Compiled);

    private static readonly Regex InternalTimestampRegex =
        new(
            @"^(?<timestamp>[A-Z][a-z]{2}\s+\d{1,2},\s+\d{4}\s+\d{2}:\d{2}:\d{2})\s+-\s+(?<level>TRACE|DEBUG|INFO|WARN|WARNING|ERROR|FATAL)\s+-\s+(?<body>.*)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SubprocessSourceRegex =
        new(
            @"^(?<source>[^:\[\]]{2,80}?)\s+subprocess:\s*(?<body>.*)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex KnownSourceRegex =
        new(
            @"^(?<source>(?:DUMB|Radarr|Sonarr|Lidarr|Prowlarr|Readarr|Whisparr|Mylar3|Bazarr|Recyclarr|Decypharr|SABnzbd|qBittorrent|Zurg)[^:]{0,48}):\s*(?<body>.*)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EmbeddedSeverityRegex =
        new(
            @"^\[(?<level>TRACE|DEBUG|INFO|WARN|WARNING|ERROR|FATAL)\]\s*(?<body>.*)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex VersionPrefixRegex =
        new(
            @"^\[v[^\]]+\]\s*",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MediaPathRegex =
        new(
            @"(?<path>/[^\r\n]*?\.(?:mkv|mp4|m4v|avi|mov|wmv|ts|m2ts|webm|flv|mpg|mpeg))(?=:\s|$)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex StackFrameRegex =
        new(
            @"^\s*(?:at\s+|---\s+End|Caused by:|InnerException|[A-Za-z0-9_.`+]+(?:Exception|Error)\b)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex FfprobeExitRegex =
        new(
            @"ffprobe exited with non-zero exit-code\s*\((?<code>\d+)[^)]*\):?\s*(?<reason>.*)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SecretRegex =
        new(
            @"(?i)(api[_-]?key|token|password|passphrase|secret)(\s*[:=]\s*)([^\s,;]+)",
            RegexOptions.Compiled);

    private static readonly Regex UrlUserInfoRegex =
        new(
            @"(?i)(https?://)[^/\s:@]+:[^@\s/]+@",
            RegexOptions.Compiled);

    public static DockerLogPresentation Present(string rawText)
    {
        var raw = Redact(rawText);
        var rawLines = raw
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

        var entries = new List<LogEntryBuilder>();
        LogEntryBuilder? current = null;

        foreach (var rawLine in rawLines)
        {
            var line = ParseLine(rawLine);
            if (line is null)
                continue;

            if (!string.IsNullOrWhiteSpace(line.FilePath))
            {
                var key =
                    $"{line.Source}|{line.FilePath}".ToLowerInvariant();

                if (current is null ||
                    !current.Key.Equals(
                        key,
                        StringComparison.Ordinal))
                {
                    FlushCurrent(entries, ref current);
                    current = LogEntryBuilder.ForIncident(key, line);
                }

                current.Add(line);
                continue;
            }

            if (current is not null &&
                current.Source.Equals(
                    line.Source,
                    StringComparison.OrdinalIgnoreCase) &&
                IsRelatedDiagnostic(line.Body))
            {
                current.Add(line);
                continue;
            }

            FlushCurrent(entries, ref current);

            var standaloneKey =
                $"{line.Source}|{line.Severity}|{NormalizeKey(line.Body)}"
                    .ToLowerInvariant();

            if (entries.Count > 0 &&
                entries[^1].Key.Equals(
                    standaloneKey,
                    StringComparison.Ordinal))
            {
                entries[^1].Add(line);
            }
            else
            {
                var standalone =
                    LogEntryBuilder.ForStandalone(
                        standaloneKey,
                        line);
                standalone.Add(line);
                entries.Add(standalone);
            }
        }

        FlushCurrent(entries, ref current);

        var cleaned = Render(entries);
        var collapsed = Math.Max(0, rawLines.Length - entries.Count);

        return new DockerLogPresentation(
            raw,
            cleaned,
            rawLines.Length,
            entries.Count,
            collapsed);
    }

    private static ParsedLogLine? ParseLine(string rawLine)
    {
        var text = rawLine.Trim();
        if (text.Length == 0)
            return null;

        DateTimeOffset? timestamp = null;
        var level = "INFO";

        var dockerMatch = DockerTimestampRegex.Match(text);
        if (dockerMatch.Success)
        {
            timestamp = ParseDockerTimestamp(
                dockerMatch.Groups["timestamp"].Value);
            text = dockerMatch.Groups["body"].Value.Trim();
        }

        var internalMatch = InternalTimestampRegex.Match(text);
        if (internalMatch.Success)
        {
            if (timestamp is null &&
                DateTime.TryParseExact(
                    internalMatch.Groups["timestamp"].Value,
                    "MMM d, yyyy HH:mm:ss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal,
                    out var localTime))
            {
                timestamp = new DateTimeOffset(localTime);
            }

            level = NormalizeSeverity(
                internalMatch.Groups["level"].Value);
            text = internalMatch.Groups["body"].Value.Trim();
        }

        var source = "Container";
        var sourceMatch = SubprocessSourceRegex.Match(text);
        if (!sourceMatch.Success)
            sourceMatch = KnownSourceRegex.Match(text);

        if (sourceMatch.Success)
        {
            source = NormalizeSource(
                sourceMatch.Groups["source"].Value);
            text = sourceMatch.Groups["body"].Value.Trim();
        }

        var embeddedSeverity =
            EmbeddedSeverityRegex.Match(text);
        if (embeddedSeverity.Success)
        {
            level = NormalizeSeverity(
                embeddedSeverity.Groups["level"].Value);
            text = embeddedSeverity.Groups["body"].Value.Trim();
        }
        else if (LooksLikeError(text))
        {
            level = "ERROR";
        }
        else if (LooksLikeWarning(text))
        {
            level = "WARN";
        }

        text = VersionPrefixRegex.Replace(text, string.Empty);

        var pathMatch = MediaPathRegex.Match(text);
        var filePath = pathMatch.Success
            ? pathMatch.Groups["path"].Value
            : null;
        var fileName = filePath is null
            ? null
            : Path.GetFileName(filePath);

        var simplified = SimplifyBody(
            text,
            filePath,
            fileName);

        return new ParsedLogLine(
            timestamp,
            source,
            level,
            simplified,
            filePath,
            fileName);
    }

    private static DateTimeOffset? ParseDockerTimestamp(
        string value)
    {
        var normalized = value;
        var dot = normalized.IndexOf('.');
        var z = normalized.LastIndexOf('Z');

        if (dot >= 0 &&
            z > dot &&
            z - dot - 1 > 7)
        {
            normalized =
                normalized[..(dot + 1)] +
                normalized.Substring(dot + 1, 7) +
                "Z";
        }

        return DateTimeOffset.TryParse(
            normalized,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal |
            DateTimeStyles.AdjustToUniversal,
            out var timestamp)
                ? timestamp
                : null;
    }

    private static string SimplifyBody(
        string body,
        string? filePath,
        string? fileName)
    {
        var value = body.Trim();

        if (!string.IsNullOrWhiteSpace(filePath) &&
            !string.IsNullOrWhiteSpace(fileName))
        {
            value = value.Replace(
                filePath,
                fileName,
                StringComparison.Ordinal);
        }

        value = Regex.Replace(
            value,
            @"^(?:VideoFileInfoReader|DetectSample):\s*",
            string.Empty,
            RegexOptions.IgnoreCase);

        value = Regex.Replace(
            value,
            @"^FFMpegCore\.Exceptions\.FFMpegException:\s*",
            string.Empty,
            RegexOptions.IgnoreCase);

        if (!string.IsNullOrWhiteSpace(fileName))
        {
            value = value.Replace(
                $"Unable to parse media info from file: {fileName}:",
                "Unable to parse media info —",
                StringComparison.OrdinalIgnoreCase);

            value = value.Replace(
                $"Unable to parse media info from file: {fileName}",
                "Unable to parse media info",
                StringComparison.OrdinalIgnoreCase);
        }

        var ffprobe = FfprobeExitRegex.Match(value);
        if (ffprobe.Success)
        {
            var reason =
                ffprobe.Groups["reason"].Value.Trim();
            return string.IsNullOrWhiteSpace(reason)
                ? $"ffprobe exited with code {ffprobe.Groups["code"].Value}"
                : $"ffprobe exited with code {ffprobe.Groups["code"].Value} — {reason}";
        }

        if (value.Contains(
                "Failed to get runtime from the file",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Failed to determine media runtime with ffprobe";
        }

        if (value.StartsWith(
                "at ",
                StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        value = Regex.Replace(
            value,
            @"\s+",
            " ");

        return value.Trim();
    }

    private static bool IsRelatedDiagnostic(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return true;

        return body.Contains(
                   "ffprobe",
                   StringComparison.OrdinalIgnoreCase) ||
               body.Contains(
                   "media info",
                   StringComparison.OrdinalIgnoreCase) ||
               body.Contains(
                   "runtime",
                   StringComparison.OrdinalIgnoreCase) ||
               body.Contains(
                   "FFMpegCore",
                   StringComparison.OrdinalIgnoreCase) ||
               body.Contains(
                   "NzbDrone.Core",
                   StringComparison.OrdinalIgnoreCase) ||
               body.Contains(
                   "End of file",
                   StringComparison.OrdinalIgnoreCase) ||
               body.Contains(
                   "Invalid data",
                   StringComparison.OrdinalIgnoreCase) ||
               body.Contains(
                   "Input/output error",
                   StringComparison.OrdinalIgnoreCase) ||
               StackFrameRegex.IsMatch(body);
    }

    private static string Render(
        IReadOnlyList<LogEntryBuilder> entries)
    {
        if (entries.Count == 0)
        {
            return
                "No readable log entries were found in the captured raw output.";
        }

        var builder = new StringBuilder();

        for (var index = 0; index < entries.Count; index++)
        {
            if (index > 0)
                builder.AppendLine().AppendLine();

            var entry = entries[index];
            var localTime = entry.Timestamp?.ToLocalTime()
                .ToString(
                    "h:mm:ss tt",
                    CultureInfo.CurrentCulture) ??
                "--";

            builder
                .Append(localTime)
                .Append("  ")
                .Append(entry.Source)
                .Append("  ")
                .AppendLine(entry.Severity);

            if (!string.IsNullOrWhiteSpace(entry.FileName))
            {
                builder
                    .Append("File: ")
                    .AppendLine(entry.FileName);
            }

            var visibleMessages = entry.Messages
                .Where(message =>
                    !string.IsNullOrWhiteSpace(message))
                .Take(3)
                .ToArray();

            foreach (var message in visibleMessages)
                builder.AppendLine(message);

            if (entry.RawLines > 1)
            {
                builder
                    .Append("Collapsed ")
                    .Append(entry.RawLines - 1)
                    .Append(" related raw ")
                    .Append(entry.RawLines - 1 == 1
                        ? "line"
                        : "lines");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static void FlushCurrent(
        ICollection<LogEntryBuilder> entries,
        ref LogEntryBuilder? current)
    {
        if (current is null)
            return;

        entries.Add(current);
        current = null;
    }

    private static string NormalizeSource(string value)
    {
        var source = value.Trim();
        if (source.EndsWith(
                " subprocess",
                StringComparison.OrdinalIgnoreCase))
        {
            source = source[..^11].TrimEnd();
        }

        return source.Length == 0
            ? "Container"
            : source;
    }

    private static string NormalizeSeverity(string value) =>
        value.ToUpperInvariant() switch
        {
            "WARNING" => "WARN",
            "FATAL" => "ERROR",
            _ => value.ToUpperInvariant()
        };

    private static bool LooksLikeError(string value) =>
        value.Contains(
            " error",
            StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith(
            "Error",
            StringComparison.OrdinalIgnoreCase) ||
        value.Contains(
            "Exception",
            StringComparison.OrdinalIgnoreCase) ||
        value.Contains(
            " failed",
            StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith(
            "Failed",
            StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeWarning(string value) =>
        value.Contains(
            " warning",
            StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith(
            "Warn",
            StringComparison.OrdinalIgnoreCase);

    private static string NormalizeKey(string value) =>
        Regex.Replace(
            value.Trim(),
            @"\s+",
            " ");

    private static string Redact(string value)
    {
        var clean = value ?? string.Empty;
        clean = SecretRegex.Replace(
            clean,
            "$1$2<redacted>");
        clean = UrlUserInfoRegex.Replace(
            clean,
            "$1<redacted>@");
        return clean.Trim();
    }

    private sealed record ParsedLogLine(
        DateTimeOffset? Timestamp,
        string Source,
        string Severity,
        string Body,
        string? FilePath,
        string? FileName);

    private sealed class LogEntryBuilder
    {
        private readonly HashSet<string> _messageKeys =
            new(StringComparer.OrdinalIgnoreCase);

        private LogEntryBuilder(
            string key,
            ParsedLogLine line,
            string? fileName)
        {
            Key = key;
            Timestamp = line.Timestamp;
            Source = line.Source;
            Severity = line.Severity;
            FileName = fileName;
        }

        public string Key { get; }

        public DateTimeOffset? Timestamp { get; private set; }

        public string Source { get; }

        public string Severity { get; private set; }

        public string? FileName { get; }

        public int RawLines { get; private set; }

        public List<string> Messages { get; } = new();

        public static LogEntryBuilder ForIncident(
            string key,
            ParsedLogLine line) =>
            new(
                key,
                line,
                line.FileName);

        public static LogEntryBuilder ForStandalone(
            string key,
            ParsedLogLine line) =>
            new(
                key,
                line,
                null);

        public void Add(ParsedLogLine line)
        {
            RawLines++;

            Timestamp ??= line.Timestamp;
            Severity = HigherSeverity(
                Severity,
                line.Severity);

            if (string.IsNullOrWhiteSpace(line.Body))
                return;

            var key = NormalizeKey(line.Body);
            if (_messageKeys.Add(key))
                Messages.Add(line.Body);
        }

        private static string HigherSeverity(
            string left,
            string right) =>
            SeverityRank(right) > SeverityRank(left)
                ? right
                : left;

        private static int SeverityRank(string value) =>
            value.ToUpperInvariant() switch
            {
                "ERROR" => 4,
                "WARN" => 3,
                "INFO" => 2,
                "DEBUG" => 1,
                _ => 0
            };
    }
}
