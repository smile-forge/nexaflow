using System.IO;

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
                new(HexReadFault.AccessDenied,
                    $"Windows won't let this app read \"{name}\". Its permissions may be limited to another " +
                    "account, or it may be protected by the system."),

            FileNotFoundException or DirectoryNotFoundException =>
                new(HexReadFault.Missing,
                    $"\"{name}\" isn't there any more. It may have been moved, renamed or deleted since the tab opened."),

            IOException io when io.HResult is ErrorSharingViolation or ErrorLockViolation =>
                new(HexReadFault.Locked,
                    $"\"{name}\" is open in another program that isn't sharing it. Close whatever is using it and try again."),

            _ => new(HexReadFault.Unreadable,
                     $"\"{name}\" couldn't be read. {ex.Message}"),
        };
    }

    /// <summary>
    /// The file opened but a read came back short. There is no exception to classify — the handle simply
    /// stopped delivering bytes (a network share that dropped, removable media pulled out, a handle left
    /// stale by the file being replaced underneath an open tab).
    /// </summary>
    public static HexReadFailure Truncated(string path) =>
        new(HexReadFault.Unreadable,
            $"\"{DisplayName(path)}\" stopped returning data part-way through. The drive or share it lives on " +
            "may have gone away, or the file may have been replaced while the tab was open.");

    private static string DisplayName(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "This file";
        try { return Path.GetFileName(path) is { Length: > 0 } n ? n : path; }
        catch { return path; }   // a malformed path is still worth naming verbatim
    }
}
