using System.IO;
using Nexaflow.IO.Common.Locations;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Hex.Buffer;

/// <summary>Why the file behind a hex view could not be read. Chooses the wording of the retry prompt.</summary>
public enum HexReadFault
{
    /// <summary>Nothing at that path any more — moved, renamed or deleted.</summary>
    Missing,
    /// <summary>The path resolves but Windows refused the read.</summary>
    AccessDenied,
    /// <summary>Another program holds the file open and is not sharing it for reading.</summary>
    Locked,
    /// <summary>It opened but the bytes would not come back — a dropped network share, failing media, a stale handle.</summary>
    Unreadable,
}

/// <summary>
/// A failed read, described in the user's terms rather than the exception's.
/// <para>
/// The buffer records one of these instead of quietly serving zeroes: an unreadable file and a file
/// genuinely full of <c>0x00</c> render identically, so without this the viewer states a falsehood
/// about the bytes on disk and looks like it worked.
/// </para>
/// </summary>
public sealed record HexReadFailure(HexReadFault Fault, string Message)
{
    private const int ErrorSharingViolation = unchecked((int)0x80070020);
    private const int ErrorLockViolation    = unchecked((int)0x80070021);

    /// <summary>Classifies <paramref name="ex"/> from opening or reading <paramref name="path"/>.</summary>
    public static HexReadFailure For(string path, Exception ex)
    {
        var name = DisplayName(path);
        return ex switch
        {
            UnauthorizedAccessException =>
                new(HexReadFault.AccessDenied, Str.Format("Hex.ReadFailure.AccessDeniedFormat", name)),

            FileNotFoundException or DirectoryNotFoundException =>
                new(HexReadFault.Missing, Str.Format("Hex.ReadFailure.MissingFormat", name)),

            // How the file system says the same two wherever the file is — on disk, in an archive or on a location.
            LocationException { Error: LocationError.AccessDenied } =>
                new(HexReadFault.AccessDenied, Str.Format("Hex.ReadFailure.AccessDeniedFormat", name)),

            LocationException { Error: LocationError.NotFound } =>
                new(HexReadFault.Missing, Str.Format("Hex.ReadFailure.MissingFormat", name)),

            IOException io when io.HResult is ErrorSharingViolation or ErrorLockViolation =>
                new(HexReadFault.Locked, Str.Format("Hex.ReadFailure.LockedFormat", name)),

            _ => new(HexReadFault.Unreadable, Str.Format("Hex.ReadFailure.UnreadableFormat", name, ex.Message)),
        };
    }

    /// <summary>
    /// The file opened but a read came back short. There is no exception to classify — the handle simply
    /// stopped delivering bytes (a network share that dropped, removable media pulled out, a handle left
    /// stale by the file being replaced underneath an open tab).
    /// </summary>
    public static HexReadFailure Truncated(string path) =>
        new(HexReadFault.Unreadable, Str.Format("Hex.ReadFailure.TruncatedFormat", DisplayName(path)));

    private static string DisplayName(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return Str.Get("Hex.ReadFailure.ThisFile");
        try { return Path.GetFileName(path) is { Length: > 0 } n ? n : path; }
        catch { return path; }   // a malformed path is still worth naming verbatim
    }
}
