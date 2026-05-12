using System.Runtime.InteropServices;

namespace Nexaflow.Core.Services;

/// <summary>
/// Resolves Windows known-folder paths via SHGetKnownFolderPath, which honours
/// folder redirection (registry overrides under
/// HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders).
/// Supported on Windows 11 / Windows 10.
/// </summary>
public static class KnownFolderService
{
    // ── Well-known GUIDs (Shell32) ─────────────────────────────────────────

    /// <summary>Documents  – FOLDERID_Documents</summary>
    public static readonly Guid Documents = new("FDD39AD0-238F-46AF-ADB4-6C85480369C7");

    /// <summary>Pictures   – FOLDERID_Pictures</summary>
    public static readonly Guid Pictures  = new("33E28130-4E1E-4676-835A-98395C3BC3BB");

    /// <summary>Videos     – FOLDERID_Videos</summary>
    public static readonly Guid Videos    = new("18989B1D-99B5-455B-841C-AB7C74E4DDFC");

    /// <summary>Music      – FOLDERID_Music</summary>
    public static readonly Guid Music     = new("4BD8D571-6D19-48D3-BE97-422220080E43");

    /// <summary>Desktop    – FOLDERID_Desktop</summary>
    public static readonly Guid Desktop   = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641");

    /// <summary>Downloads  – FOLDERID_Downloads</summary>
    public static readonly Guid Downloads = new("374DE290-123F-4565-9164-39C4925E467B");

    // ── P/Invoke ───────────────────────────────────────────────────────────

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true,
               PreserveSig = false)]
    private static extern void SHGetKnownFolderPath(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rfid,
        uint dwFlags,
        nint hToken,
        out nint ppszPath);

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the resolved path for a known folder GUID.
    /// Falls back to <see cref="FallbackPath"/> if the call fails.
    /// </summary>
    public static string Resolve(Guid folderId)
    {
        nint ptr = nint.Zero;
        try
        {
            SHGetKnownFolderPath(folderId, 0, nint.Zero, out ptr);
            return Marshal.PtrToStringUni(ptr) ?? FallbackPath(folderId);
        }
        catch
        {
            return FallbackPath(folderId);
        }
        finally
        {
            if (ptr != nint.Zero)
                Marshal.FreeCoTaskMem(ptr);
        }
    }

    // ── Convenience properties ─────────────────────────────────────────────

    public static string DocumentsPath => Resolve(Documents);
    public static string PicturesPath  => Resolve(Pictures);
    public static string VideosPath    => Resolve(Videos);
    public static string MusicPath     => Resolve(Music);
    public static string DesktopPath   => Resolve(Desktop);
    public static string DownloadsPath => Resolve(Downloads);

    // ── Fallback (should never be needed on Win 11) ───────────────────────

    private static string FallbackPath(Guid folderId)
    {
        if (folderId == Documents) return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (folderId == Pictures)  return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (folderId == Videos)    return Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        if (folderId == Music)     return Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        if (folderId == Desktop)   return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }
}
