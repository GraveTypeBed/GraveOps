using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using GraveOps.Core.Security;

namespace GraveOps.Platform.Windows;

public sealed class WindowsCredentialVault :
    ICredentialVault
{
    private const uint CredentialTypeGeneric = 1;
    private const uint CredentialPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const int MaximumCredentialBlobBytes = 2560;

    public string VaultId =>
        "windows-credential-manager";

    public bool IsAvailable =>
        OperatingSystem.IsWindows();

    public Task StoreAsync(
        CredentialReference reference,
        SecretValue secret,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateReference(reference);
        ArgumentNullException.ThrowIfNull(secret);
        EnsureAvailable();

        var characters = secret.Reveal().Span;
        var byteCount =
            Encoding.Unicode.GetByteCount(characters);

        if (byteCount > MaximumCredentialBlobBytes)
        {
            throw new InvalidOperationException(
                "The credential is too large for Windows Credential Manager.");
        }

        var bytes =
            GC.AllocateUninitializedArray<byte>(
                byteCount);

        Encoding.Unicode.GetBytes(
            characters,
            bytes);

        var blob =
            bytes.Length == 0
                ? IntPtr.Zero
                : Marshal.AllocCoTaskMem(
                    bytes.Length);

        try
        {
            if (bytes.Length > 0)
            {
                Marshal.Copy(
                    bytes,
                    0,
                    blob,
                    bytes.Length);
            }

            var credential =
                new NativeCredential
                {
                    Type =
                        CredentialTypeGeneric,
                    TargetName =
                        reference.Value,
                    CredentialBlobSize =
                        checked((uint)bytes.Length),
                    CredentialBlob =
                        blob,
                    Persist =
                        CredentialPersistLocalMachine,
                    UserName =
                        Environment.UserName
                };

            if (!NativeMethods.CredWrite(
                    ref credential,
                    0))
            {
                throw CreateNativeFailure(
                    "store",
                    reference);
            }
        }
        finally
        {
            ZeroUnmanaged(
                blob,
                bytes.Length);

            if (blob != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(
                    blob);
            }

            Array.Clear(
                bytes);
        }

        return Task.CompletedTask;
    }

    public Task<SecretValue?> RetrieveAsync(
        CredentialReference reference,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateReference(reference);
        EnsureAvailable();

        if (!NativeMethods.CredRead(
                reference.Value,
                CredentialTypeGeneric,
                0,
                out var credentialPointer))
        {
            var error =
                Marshal.GetLastWin32Error();

            if (error == ErrorNotFound)
            {
                return Task.FromResult<
                    SecretValue?>(
                    null);
            }

            throw new Win32Exception(
                error,
                $"Windows Credential Manager could not retrieve '{reference.Value}'.");
        }

        try
        {
            var credential =
                Marshal.PtrToStructure<
                    NativeCredential>(
                    credentialPointer);

            if (credential.CredentialBlob == IntPtr.Zero ||
                credential.CredentialBlobSize == 0)
            {
                return Task.FromResult<
                    SecretValue?>(
                    new SecretValue(
                        string.Empty));
            }

            var length =
                checked((int)credential.CredentialBlobSize);

            var bytes =
                GC.AllocateUninitializedArray<byte>(
                    length);

            try
            {
                Marshal.Copy(
                    credential.CredentialBlob,
                    bytes,
                    0,
                    bytes.Length);

                return Task.FromResult<
                    SecretValue?>(
                    new SecretValue(
                        Encoding.Unicode.GetString(
                            bytes)));
            }
            finally
            {
                Array.Clear(
                    bytes);
            }
        }
        finally
        {
            NativeMethods.CredFree(
                credentialPointer);
        }
    }

    public Task DeleteAsync(
        CredentialReference reference,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateReference(reference);
        EnsureAvailable();

        if (!NativeMethods.CredDelete(
                reference.Value,
                CredentialTypeGeneric,
                0))
        {
            var error =
                Marshal.GetLastWin32Error();

            if (error != ErrorNotFound)
            {
                throw new Win32Exception(
                    error,
                    $"Windows Credential Manager could not delete '{reference.Value}'.");
            }
        }

        return Task.CompletedTask;
    }

    private static void ValidateReference(
        CredentialReference reference)
    {
        if (string.IsNullOrWhiteSpace(
                reference.Value))
        {
            throw new ArgumentException(
                "The credential reference is required.",
                nameof(reference));
        }
    }

    private void EnsureAvailable()
    {
        if (!IsAvailable)
        {
            throw new PlatformNotSupportedException(
                "Windows Credential Manager requires a Windows runtime.");
        }
    }

    private static Win32Exception CreateNativeFailure(
        string operation,
        CredentialReference reference)
    {
        var error =
            Marshal.GetLastWin32Error();

        return new Win32Exception(
            error,
            $"Windows Credential Manager could not {operation} '{reference.Value}'.");
    }

    private static void ZeroUnmanaged(
        IntPtr pointer,
        int length)
    {
        if (pointer == IntPtr.Zero)
            return;

        for (var index = 0;
             index < length;
             index++)
        {
            Marshal.WriteByte(
                pointer,
                index,
                0);
        }
    }

    [StructLayout(
        LayoutKind.Sequential,
        CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string TargetName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? Comment;

        public System.Runtime.InteropServices.ComTypes.FILETIME
            LastWritten;

        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? TargetAlias;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? UserName;
    }

    private static class NativeMethods
    {
        [DllImport(
            "advapi32.dll",
            EntryPoint = "CredWriteW",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CredWrite(
            [In] ref NativeCredential credential,
            [In] uint flags);

        [DllImport(
            "advapi32.dll",
            EntryPoint = "CredReadW",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CredRead(
            string target,
            uint type,
            uint reservedFlag,
            out IntPtr credentialPointer);

        [DllImport(
            "advapi32.dll",
            EntryPoint = "CredDeleteW",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CredDelete(
            string target,
            uint type,
            uint flags);

        [DllImport(
            "advapi32.dll")]
        internal static extern void CredFree(
            IntPtr credentialPointer);
    }
}