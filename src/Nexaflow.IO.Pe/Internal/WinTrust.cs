using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Nexaflow.IO.Pe.Internal;

/// <summary>
/// <c>WinVerifyTrust</c>, the only authority on whether Windows actually trusts an image. Parsing the
/// embedded signature ourselves says what the file <em>claims</em>; this says whether the OS agrees.
/// <para>
/// Two routes, tried in order, because they answer different questions. The <b>file</b> route checks
/// an embedded signature. When there is none — which is the norm for drivers and for a good deal of
/// in-box Windows, <c>notepad.exe</c> included — the file is almost certainly <b>catalog</b>-signed
/// instead: its hash is listed in a signed <c>.cat</c> under the system catalog store. Only the
/// catalog route sees that, so checking the file route alone reports half of Windows as unsigned.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class WinTrust
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
    private static readonly Guid DriverActionVerify = new("F750E6C3-38EE-11D1-85E5-00C04FC295EE");

    private const uint UiNone            = 2;
    private const uint RevokeWholeChain  = 0x0000_0001;
    private const uint ChoiceFile        = 1;
    private const uint ChoiceCatalog     = 2;
    private const uint StateActionVerify = 1;
    private const uint StateActionClose  = 2;
    /// <summary>Consult cached CRLs only — revocation still gets checked, but the call can never
    /// block on a network fetch, which matters when this runs behind a viewer tab.</summary>
    private const uint CacheOnlyUrlRetrieval = 0x0000_1000;

    private const int TrustENoSignature        = unchecked((int)0x800B0100);
    private const int TrustESubjectFormUnknown = unchecked((int)0x800B0003);
    private const int TrustEProviderUnknown    = unchecked((int)0x800B0001);
    private const int TrustEBadDigest          = unchecked((int)0x80096010);
    private const int TrustEExplicitDistrust   = unchecked((int)0x800B0111);
    private const int TrustESubjectNotTrusted  = unchecked((int)0x800B0004);
    private const int CertEUntrustedRoot       = unchecked((int)0x800B0109);
    private const int CertEChaining            = unchecked((int)0x800B010A);
    private const int CertEExpired             = unchecked((int)0x800B0101);
    private const int CertERevoked             = unchecked((int)0x800B010C);
    private const int CryptESecuritySettings   = unchecked((int)0x80092026);

    /// <summary>The outcome, plus the catalog that vouched for the file when one did.</summary>
    public readonly record struct Result(PeTrustVerdict Verdict, string Detail, string? CatalogPath);

    // ── Structures ────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInfo
    {
        public uint   cbStruct;
        public IntPtr pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CatalogInfoNative
    {
        public uint   cbStruct;
        public uint   dwCatalogVersion;
        public IntPtr pcwszCatalogFilePath;
        public IntPtr pcwszMemberTag;
        public IntPtr pcwszMemberFilePath;
        public IntPtr hMemberFile;
        public IntPtr pbCalculatedFileHash;
        public uint   cbCalculatedFileHash;
        public IntPtr pcCatalogContext;
        public IntPtr hCatAdmin;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        public uint   cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint   dwUIChoice;
        public uint   fdwRevocationChecks;
        public uint   dwUnionChoice;
        public IntPtr pUnion;          // WINTRUST_FILE_INFO* or WINTRUST_CATALOG_INFO*
        public uint   dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint   dwProvFlags;
        public uint   dwUIContext;
        public IntPtr pSignatureSettings;
    }

    /// <summary>
    /// CATALOG_INFO. The path is an inline fixed buffer rather than a marshalled string because
    /// source-generated P/Invoke only accepts blittable structs (SYSLIB1051).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct CatalogFileInfo
    {
        public uint cbStruct;
        public unsafe fixed char wszCatalogFile[260];   // MAX_PATH
    }

    // ── Imports ───────────────────────────────────────────────────────────────

    [LibraryImport("wintrust.dll", SetLastError = false)]
    private static partial int WinVerifyTrust(IntPtr hwnd, ref Guid actionId, IntPtr data);

    [LibraryImport("wintrust.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptCATAdminAcquireContext2(
        out IntPtr catAdmin, IntPtr subsystem, string? hashAlgorithm, IntPtr strongHashPolicy, uint flags);

    [LibraryImport("wintrust.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptCATAdminCalcHashFromFileHandle2(
        IntPtr catAdmin, SafeFileHandle file, ref uint hashSize, byte[]? hash, uint flags);

    [LibraryImport("wintrust.dll", SetLastError = true)]
    private static partial IntPtr CryptCATAdminEnumCatalogFromHash(
        IntPtr catAdmin, byte[] hash, uint hashSize, uint flags, IntPtr previous);

    [LibraryImport("wintrust.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptCATCatalogInfoFromContext(IntPtr catInfo, ref CatalogFileInfo info, uint flags);

    [LibraryImport("wintrust.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptCATAdminReleaseCatalogContext(IntPtr catAdmin, IntPtr catInfo, uint flags);

    [LibraryImport("wintrust.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptCATAdminReleaseContext(IntPtr catAdmin, uint flags);

    // ── Entry point ───────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies <paramref name="path"/>. Never throws — an unavailable wintrust.dll (Server Core, a
    /// sandbox) degrades to <see cref="PeTrustVerdict.NotChecked"/> rather than failing the parse.
    /// </summary>
    public static Result Verify(string path)
    {
        try
        {
            var (hr, _) = VerifyEmbedded(path);

            // "No signature" is not an answer yet — the file may be vouched for by a catalog.
            if (hr is TrustENoSignature or TrustESubjectFormUnknown or TrustEProviderUnknown &&
                TryVerifyCatalog(path) is { } catalog)
                return catalog;

            var (verdict, detail) = Translate(hr);
            return new Result(verdict, detail, null);
        }
        catch (DllNotFoundException)
        {
            return new Result(PeTrustVerdict.NotChecked, "wintrust.dll is not available on this system.", null);
        }
        catch (EntryPointNotFoundException)
        {
            return new Result(PeTrustVerdict.NotChecked, "WinVerifyTrust is not available on this system.", null);
        }
        catch (Exception e)
        {
            return new Result(PeTrustVerdict.NotChecked, $"Trust verification failed: {e.Message}", null);
        }
    }

    // ── Embedded-signature route ──────────────────────────────────────────────

    private static (int Hr, bool Ran) VerifyEmbedded(string path)
    {
        IntPtr pathPtr = IntPtr.Zero, filePtr = IntPtr.Zero;
        try
        {
            pathPtr = Marshal.StringToHGlobalUni(path);
            var file = new FileInfo
            {
                cbStruct      = (uint)Marshal.SizeOf<FileInfo>(),
                pcwszFilePath = pathPtr,
            };
            filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfo>());
            Marshal.StructureToPtr(file, filePtr, false);

            return (Invoke(GenericVerifyV2, ChoiceFile, filePtr), true);
        }
        finally
        {
            if (filePtr != IntPtr.Zero) Marshal.FreeHGlobal(filePtr);
            if (pathPtr != IntPtr.Zero) Marshal.FreeHGlobal(pathPtr);
        }
    }

    // ── Catalog route ─────────────────────────────────────────────────────────

    /// <summary>
    /// Looks the file's hash up in the system catalog store and, when a catalog claims it, verifies
    /// through that catalog. Returns null when no catalog covers the file, which is the genuine
    /// "unsigned" case. SHA-256 catalogs are tried first, then SHA-1 for older ones.
    /// </summary>
    private static Result? TryVerifyCatalog(string path)
    {
        foreach (string algorithm in (string[])["SHA256", "SHA1"])
        {
            if (TryVerifyCatalog(path, algorithm) is { } result) return result;
        }
        return null;
    }

    private static Result? TryVerifyCatalog(string path, string algorithm)
    {
        IntPtr catAdmin = IntPtr.Zero;
        IntPtr catInfo  = IntPtr.Zero;

        try
        {
            if (!CryptCATAdminAcquireContext2(out catAdmin, IntPtr.Zero, algorithm, IntPtr.Zero, 0) ||
                catAdmin == IntPtr.Zero)
                return null;

            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read,
                                               FileShare.ReadWrite | FileShare.Delete);

            uint hashSize = 0;
            CryptCATAdminCalcHashFromFileHandle2(catAdmin, handle, ref hashSize, null, 0);
            if (hashSize == 0) return null;

            var hash = new byte[hashSize];
            if (!CryptCATAdminCalcHashFromFileHandle2(catAdmin, handle, ref hashSize, hash, 0)) return null;

            catInfo = CryptCATAdminEnumCatalogFromHash(catAdmin, hash, hashSize, 0, IntPtr.Zero);
            if (catInfo == IntPtr.Zero) return null;   // no catalog claims this file

            string catalogFile;
            unsafe
            {
                var info = new CatalogFileInfo { cbStruct = (uint)sizeof(CatalogFileInfo) };
                if (!CryptCATCatalogInfoFromContext(catInfo, ref info, 0)) return null;
                catalogFile = new string(info.wszCatalogFile);
            }
            if (catalogFile.Length == 0) return null;

            // The member tag is the file hash as an upper-case hex string.
            string memberTag = Convert.ToHexString(hash);
            int    hr        = VerifyThroughCatalog(path, catalogFile, memberTag, hash, catAdmin);

            var (verdict, detail) = Translate(hr);
            if (verdict == PeTrustVerdict.Valid)
                detail = $"Trusted via the security catalog {Path.GetFileName(catalogFile)}.";

            return new Result(verdict, detail, catalogFile);
        }
        catch (Exception)
        {
            return null;   // catalog verification is best-effort; the embedded verdict still stands
        }
        finally
        {
            if (catInfo  != IntPtr.Zero) CryptCATAdminReleaseCatalogContext(catAdmin, catInfo, 0);
            if (catAdmin != IntPtr.Zero) CryptCATAdminReleaseContext(catAdmin, 0);
        }
    }

    private static int VerifyThroughCatalog(
        string memberPath, string catalogPath, string memberTag, byte[] hash, IntPtr catAdmin)
    {
        IntPtr catalogPtr = IntPtr.Zero, memberPtr = IntPtr.Zero, tagPtr = IntPtr.Zero;
        IntPtr hashPtr    = IntPtr.Zero, infoPtr   = IntPtr.Zero;

        try
        {
            catalogPtr = Marshal.StringToHGlobalUni(catalogPath);
            memberPtr  = Marshal.StringToHGlobalUni(memberPath);
            tagPtr     = Marshal.StringToHGlobalUni(memberTag);
            hashPtr    = Marshal.AllocHGlobal(hash.Length);
            Marshal.Copy(hash, 0, hashPtr, hash.Length);

            var catalog = new CatalogInfoNative
            {
                cbStruct             = (uint)Marshal.SizeOf<CatalogInfoNative>(),
                pcwszCatalogFilePath = catalogPtr,
                pcwszMemberTag       = tagPtr,
                pcwszMemberFilePath  = memberPtr,
                pbCalculatedFileHash = hashPtr,
                cbCalculatedFileHash = (uint)hash.Length,
                hCatAdmin            = catAdmin,
            };
            infoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<CatalogInfoNative>());
            Marshal.StructureToPtr(catalog, infoPtr, false);

            // The driver action GUID is what the OS itself uses for catalog-backed verification and
            // is correct for user-mode members too.
            return Invoke(DriverActionVerify, ChoiceCatalog, infoPtr);
        }
        finally
        {
            if (infoPtr    != IntPtr.Zero) Marshal.FreeHGlobal(infoPtr);
            if (hashPtr    != IntPtr.Zero) Marshal.FreeHGlobal(hashPtr);
            if (tagPtr     != IntPtr.Zero) Marshal.FreeHGlobal(tagPtr);
            if (memberPtr  != IntPtr.Zero) Marshal.FreeHGlobal(memberPtr);
            if (catalogPtr != IntPtr.Zero) Marshal.FreeHGlobal(catalogPtr);
        }
    }

    // ── Shared call/close dance ───────────────────────────────────────────────

    /// <summary>Runs a verify and then the mandatory close pass that frees the provider's state.</summary>
    private static int Invoke(Guid action, uint unionChoice, IntPtr union)
    {
        IntPtr dataPtr = Marshal.AllocHGlobal(Marshal.SizeOf<TrustData>());
        try
        {
            var data = new TrustData
            {
                cbStruct            = (uint)Marshal.SizeOf<TrustData>(),
                dwUIChoice          = UiNone,
                fdwRevocationChecks = RevokeWholeChain,
                dwUnionChoice       = unionChoice,
                pUnion              = union,
                dwStateAction       = StateActionVerify,
                dwProvFlags         = CacheOnlyUrlRetrieval,
            };
            Marshal.StructureToPtr(data, dataPtr, false);

            var action2 = action;
            int result  = WinVerifyTrust(IntPtr.Zero, ref action2, dataPtr);

            var closing = Marshal.PtrToStructure<TrustData>(dataPtr);
            closing.dwStateAction = StateActionClose;
            Marshal.StructureToPtr(closing, dataPtr, false);
            WinVerifyTrust(IntPtr.Zero, ref action2, dataPtr);

            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(dataPtr);
        }
    }

    private static (PeTrustVerdict, string) Translate(int hr) => hr switch
    {
        0 => (PeTrustVerdict.Valid, "The signature is valid and trusted."),

        TrustENoSignature or TrustESubjectFormUnknown or TrustEProviderUnknown =>
            (PeTrustVerdict.Unsigned, "The file is not signed, and no catalog entry covers it."),

        TrustEBadDigest =>
            (PeTrustVerdict.Malformed, "The file has been modified since it was signed — the digest does not match."),

        CertERevoked =>
            (PeTrustVerdict.Revoked, "The signing certificate has been revoked."),

        CertEExpired =>
            (PeTrustVerdict.Expired, "The signing certificate has expired."),

        CertEUntrustedRoot or CertEChaining =>
            (PeTrustVerdict.Untrusted, "The certificate chain does not terminate in a trusted root."),

        TrustEExplicitDistrust =>
            (PeTrustVerdict.Untrusted, "The signature is explicitly distrusted by an administrator or by the user."),

        TrustESubjectNotTrusted =>
            (PeTrustVerdict.Untrusted, "The signature is present but was not trusted by the policy provider."),

        CryptESecuritySettings =>
            (PeTrustVerdict.Untrusted, "Local security settings prevented the signature from being verified."),

        _ => (PeTrustVerdict.Untrusted, $"WinVerifyTrust returned 0x{hr:X8}."),
    };
}
