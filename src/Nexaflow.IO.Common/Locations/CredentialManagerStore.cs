using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Nexaflow.IO.Common.Locations;

/// <summary>
/// Connections' secrets in Windows Credential Manager, as generic credentials of the signed-in user: each under its
/// connection's target (<c>nexaflow:{connectionId}</c>), persisted on this computer, readable by no other account and
/// needing no admin rights. Windows protects them; nothing of the app's ever holds one on disk.
/// </summary>
public sealed class CredentialManagerStore : ISecretStore
{
    /// <summary>The signed-in user's Credential Manager.</summary>
    public static CredentialManagerStore Default { get; } = new();

    private const int GenericCredential   = 1;      // CRED_TYPE_GENERIC
    private const int PersistLocalMachine = 2;      // CRED_PERSIST_LOCAL_MACHINE: this computer, this user
    private const int NotFound            = 1168;   // ERROR_NOT_FOUND
    private const int MaxSecretBytes      = 5 * 512; // CRED_MAX_CREDENTIAL_BLOB_SIZE

    public string? Read(string target)
    {
        if (!CredRead(target, GenericCredential, 0, out var stored))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == NotFound) return null;
            throw new Win32Exception(error, $"The secret for '{target}' could not be read from Credential Manager.");
        }
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(stored);
            return credential.CredentialBlobSize == 0
                ? string.Empty
                : Marshal.PtrToStringUni(credential.CredentialBlob, credential.CredentialBlobSize / sizeof(char));
        }
        finally { CredFree(stored); }
    }

    public void Write(string target, string? userName, string secret)
    {
        int size = secret.Length * sizeof(char);
        if (size > MaxSecretBytes)
            throw new ArgumentException($"A secret holds at most {MaxSecretBytes / sizeof(char)} characters.", nameof(secret));

        var blob = Marshal.StringToCoTaskMemUni(secret);
        try
        {
            var credential = new Credential
            {
                Type               = GenericCredential,
                TargetName         = target,
                CredentialBlobSize = size,
                CredentialBlob     = blob,
                Persist            = PersistLocalMachine,
                UserName           = userName,
            };
            if (!CredWrite(ref credential, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"The secret for '{target}' could not be stored in Credential Manager.");
        }
        finally { Marshal.ZeroFreeCoTaskMemUnicode(blob); }
    }

    public bool Delete(string target)
    {
        if (CredDelete(target, GenericCredential, 0)) return true;
        int error = Marshal.GetLastWin32Error();
        if (error == NotFound) return false;
        throw new Win32Exception(error, $"The secret for '{target}' could not be removed from Credential Manager.");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public int     Flags;
        public int     Type;
        public string  TargetName;
        public string? Comment;
        public long    LastWritten;
        public int     CredentialBlobSize;
        public IntPtr  CredentialBlob;
        public int     Persist;
        public int     AttributeCount;
        public IntPtr  Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential credential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
