namespace GraveOps.Core.Security;

public readonly record struct CredentialReference(string Value)
{
    public override string ToString() => Value;
}

public sealed class SecretValue : IDisposable
{
    private char[]? _buffer;

    public SecretValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _buffer = value.ToCharArray();
    }

    public ReadOnlyMemory<char> Reveal()
    {
        ObjectDisposedException.ThrowIf(
            _buffer is null,
            this);

        return _buffer;
    }

    public override string ToString() => "[REDACTED]";

    public void Dispose()
    {
        if (_buffer is null)
            return;

        Array.Clear(_buffer);
        _buffer = null;
        GC.SuppressFinalize(this);
    }
}

public interface ICredentialVault
{
    string VaultId { get; }

    bool IsAvailable { get; }

    Task StoreAsync(
        CredentialReference reference,
        SecretValue secret,
        CancellationToken cancellationToken = default);

    Task<SecretValue?> RetrieveAsync(
        CredentialReference reference,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        CredentialReference reference,
        CancellationToken cancellationToken = default);
}
