using System.ComponentModel;
using System.Runtime.InteropServices;
using GraveOps.App.Models;

namespace GraveOps.App.Services;

public sealed class PlexTokenStore
{
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const int MaxCredentialBlobBytes = 2560;

    public string TargetFor(ServerProfile server)
        => $"{ProductIdentity.CredentialNamespace}/PlexToken/{server.Id:N}";

    public bool HasToken(ServerProfile server)
        => TryRead(server, out _);

    public void Save(ServerProfile server, string token)
    {
        token = (token ?? "").Trim();

        if (token.Length == 0)
            throw new InvalidOperationException(
                "Enter a Plex token before saving.");

        var blob = Encoding.Unicode.GetBytes(token);

        if (blob.Length > MaxCredentialBlobBytes)
            throw new InvalidOperationException(
                "The Plex token is too large for Windows Credential Manager.");

        var blobPtr = IntPtr.Zero;

        try
        {
            blobPtr = Marshal.AllocHGlobal(blob.Length);
            Marshal.Copy(blob, 0, blobPtr, blob.Length);

            var credential = new NativeCredential
            {
                Type = CredTypeGeneric,
                TargetName = TargetFor(server),
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = blobPtr,
                Persist = CredPersistLocalMachine,
                UserName = server.Name
            };

            if (!CredWriteW(ref credential, 0))
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Windows Credential Manager could not save the Plex token.");
        }
        finally
        {
            Array.Clear(blob, 0, blob.Length);

            if (blobPtr != IntPtr.Zero)
            {
                Marshal.Copy(
                    new byte[blob.Length],
                    0,
                    blobPtr,
                    blob.Length);
                Marshal.FreeHGlobal(blobPtr);
            }
        }
    }

    public bool TryRead(ServerProfile server, out string token)
    {
        token = "";

        if (!CredReadW(
                TargetFor(server),
                CredTypeGeneric,
                0,
                out var credentialPtr))
        {
            return false;
        }

        try
        {
            var credential =
                Marshal.PtrToStructure<NativeCredential>(
                    credentialPtr);

            if (credential.CredentialBlob == IntPtr.Zero ||
                credential.CredentialBlobSize == 0)
                return false;

            token =
                Marshal.PtrToStringUni(
                    credential.CredentialBlob,
                    (int)credential.CredentialBlobSize / 2)
                ?? "";

            token = token.TrimEnd('\0').Trim();
            return token.Length > 0;
        }
        finally
        {
            CredFree(credentialPtr);
        }
    }

    public void Delete(ServerProfile server)
    {
        if (CredDeleteW(
                TargetFor(server),
                CredTypeGeneric,
                0))
            return;

        var error = Marshal.GetLastWin32Error();

        if (error != ErrorNotFound)
            throw new Win32Exception(
                error,
                "Windows Credential Manager could not remove the Plex token.");
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

        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? TargetAlias;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string UserName;
    }

    [DllImport(
        "Advapi32.dll",
        EntryPoint = "CredWriteW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWriteW(
        ref NativeCredential credential,
        uint flags);

    [DllImport(
        "Advapi32.dll",
        EntryPoint = "CredReadW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredReadW(
        string target,
        uint type,
        uint reservedFlag,
        out IntPtr credentialPtr);

    [DllImport(
        "Advapi32.dll",
        EntryPoint = "CredDeleteW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDeleteW(
        string target,
        uint type,
        uint flags);

    [DllImport("Advapi32.dll")]
    private static extern void CredFree(
        IntPtr buffer);
}