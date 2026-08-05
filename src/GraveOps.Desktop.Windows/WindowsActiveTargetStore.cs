using System.Text;
using System.Text.Json;

namespace GraveOps.Desktop.Windows;

public interface IActiveTargetStore
{
    Task<string?> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        string targetId,
        CancellationToken cancellationToken = default);
}

public sealed class JsonActiveTargetStore :
    IActiveTargetStore
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

    public JsonActiveTargetStore(
        string filePath)
    {
        _filePath =
            string.IsNullOrWhiteSpace(
                filePath)
                ? throw new ArgumentException(
                    "The active-target path is required.",
                    nameof(filePath))
                : Path.GetFullPath(
                    filePath);
    }

    public string FilePath =>
        _filePath;

    public async Task<string?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(
            cancellationToken);

        try
        {
            if (!File.Exists(
                    _filePath))
            {
                return null;
            }

            var json =
                await File.ReadAllTextAsync(
                    _filePath,
                    cancellationToken);

            if (string.IsNullOrWhiteSpace(
                    json))
            {
                return null;
            }

            try
            {
                var state =
                    JsonSerializer.Deserialize<
                        ActiveTargetState>(
                        json,
                        SerializerOptions);

                var targetId =
                    state?.ActiveTargetId?.Trim();

                return string.IsNullOrWhiteSpace(
                        targetId)
                    ? null
                    : targetId;
            }
            catch (JsonException)
            {
                return null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        string targetId,
        CancellationToken cancellationToken = default)
    {
        var normalizedTargetId =
            string.IsNullOrWhiteSpace(
                targetId)
                ? throw new ArgumentException(
                    "The active target ID is required.",
                    nameof(targetId))
                : targetId.Trim();

        await _gate.WaitAsync(
            cancellationToken);

        try
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
                    new ActiveTargetState(
                        normalizedTargetId),
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
        finally
        {
            _gate.Release();
        }
    }

    private sealed record ActiveTargetState(
        string ActiveTargetId);
}

internal sealed class VolatileActiveTargetStore :
    IActiveTargetStore
{
    private string? _targetId;

    public Task<string?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            _targetId);
    }

    public Task SaveAsync(
        string targetId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _targetId =
            string.IsNullOrWhiteSpace(
                targetId)
                ? throw new ArgumentException(
                    "The active target ID is required.",
                    nameof(targetId))
                : targetId.Trim();

        return Task.CompletedTask;
    }
}