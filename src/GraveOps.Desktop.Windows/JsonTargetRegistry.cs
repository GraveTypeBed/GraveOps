using System.Text;
using System.Text.Json;
using GraveOps.Core.Targets;

namespace GraveOps.Desktop.Windows;

public sealed class JsonTargetRegistry :
    ITargetRegistry,
    IDisposable
{
    private static readonly JsonSerializerOptions
        SerializerOptions =
            new()
            {
                PropertyNamingPolicy =
                    JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive =
                    true,
                WriteIndented =
                    true
            };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate =
        new(1, 1);

    public JsonTargetRegistry(
        string filePath)
    {
        _filePath =
            string.IsNullOrWhiteSpace(
                filePath)
                ? throw new ArgumentException(
                    "The target registry path is required.",
                    nameof(filePath))
                : Path.GetFullPath(
                    filePath);
    }

    public string FilePath =>
        _filePath;

    public async Task<IReadOnlyList<TargetProfile>>
        ListAsync(
            CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(
            cancellationToken);

        try
        {
            return (
                await ReadUnsafeAsync(
                    cancellationToken))
                .OrderBy(
                    target =>
                        target.Location)
                .ThenBy(
                    target =>
                        target.DisplayName,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TargetProfile?> FindAsync(
        string targetId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                targetId))
        {
            return null;
        }

        var targets =
            await ListAsync(
                cancellationToken);

        return targets.FirstOrDefault(
            target =>
                target.Id.Equals(
                    targetId,
                    StringComparison.Ordinal));
    }

    public async Task UpsertAsync(
        TargetProfile target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            target);
        target.Validate();

        await _gate.WaitAsync(
            cancellationToken);

        try
        {
            var targets =
                await ReadUnsafeAsync(
                    cancellationToken);

            targets.RemoveAll(
                existing =>
                    existing.Id.Equals(
                        target.Id,
                        StringComparison.Ordinal));

            targets.Add(
                target);

            await WriteUnsafeAsync(
                targets,
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> RemoveAsync(
        string targetId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                targetId))
        {
            return false;
        }

        await _gate.WaitAsync(
            cancellationToken);

        try
        {
            var targets =
                await ReadUnsafeAsync(
                    cancellationToken);

            var removed =
                targets.RemoveAll(
                    target =>
                        target.Id.Equals(
                            targetId,
                            StringComparison.Ordinal)) >
                0;

            if (removed)
            {
                await WriteUnsafeAsync(
                    targets,
                    cancellationToken);
            }

            return removed;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<List<TargetProfile>>
        ReadUnsafeAsync(
            CancellationToken cancellationToken)
    {
        if (!File.Exists(
                _filePath))
        {
            return new List<TargetProfile>();
        }

        var json =
            await File.ReadAllTextAsync(
                _filePath,
                cancellationToken);

        if (string.IsNullOrWhiteSpace(
                json))
        {
            return new List<TargetProfile>();
        }

        var targets =
            JsonSerializer.Deserialize<
                List<TargetProfile>>(
                json,
                SerializerOptions) ??
            new List<TargetProfile>();

        foreach (var target in targets)
        {
            target.Validate();
        }

        var duplicate =
            targets
                .GroupBy(
                    target =>
                        target.Id,
                    StringComparer.Ordinal)
                .FirstOrDefault(
                    group =>
                        group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidDataException(
                $"Target registry contains duplicate ID '{duplicate.Key}'.");
        }

        return targets;
    }

    private async Task WriteUnsafeAsync(
        IReadOnlyList<TargetProfile> targets,
        CancellationToken cancellationToken)
    {
        var directory =
            Path.GetDirectoryName(
                _filePath);

        if (!string.IsNullOrWhiteSpace(
                directory))
        {
            Directory.CreateDirectory(
                directory);
        }

        var json =
            JsonSerializer.Serialize(
                targets,
                SerializerOptions);

        var temporaryPath =
            _filePath +
            "." +
            Guid.NewGuid().ToString("N") +
            ".tmp";

        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                json,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false),
                cancellationToken);

            File.Move(
                temporaryPath,
                _filePath,
                overwrite: true);
        }
        finally
        {
            if (File.Exists(
                    temporaryPath))
            {
                File.Delete(
                    temporaryPath);
            }
        }
    }
}