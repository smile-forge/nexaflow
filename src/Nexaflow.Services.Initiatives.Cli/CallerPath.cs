using Nexaflow.Services.Initiatives.Cli.Daemon;

namespace Nexaflow.Services.Initiatives.Cli;

/// <summary>
/// Where a path typed on the command line is measured from.
/// <para>
/// Every invocation is served by the resident process for the tree, which was started once, from somewhere
/// else, and answers callers standing in several different directories at the same time. So the process's
/// own current directory is not the caller's, and <c>Path.GetFullPath(p)</c> — which silently uses it — is
/// wrong for anything the caller typed. It resolved <c>nfi batch tree.batch</c> against the daemon's
/// directory and reported "no such script file" for a file that was plainly there.
/// </para>
/// <para>
/// The caller's directory is already carried on the request (<see cref="RequestScope"/>), so the fix is to
/// measure from it rather than from the process — here, once, for every verb. In a one-shot process (the
/// daemon itself, or a build that never starts one) the two are the same directory and this is a no-op.
/// </para>
/// </summary>
internal static class CallerPath
{
    /// <summary>The directory the caller ran the command in — from the request when serving one, else this
    /// process's own, which is the same answer when there is no daemon in the picture.</summary>
    internal static string Directory =>
        RequestScope.Directory ?? System.IO.Directory.GetCurrentDirectory();

    /// <summary>An absolute form of <paramref name="path"/>, relative paths measured from the caller.
    /// Unresolvable text (a node id, a regex — the parser hands us whatever was typed) comes back
    /// unchanged, so the caller's own "no such file" is what gets reported rather than an exception.</summary>
    internal static string Of(string path)
    {
        if (FromMsys(path) is { } windows) path = windows;
        try { return Path.GetFullPath(path, Directory); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return path; }
    }

    /// <summary>
    /// The Windows form of a Git Bash drive path — <c>/c/dir/x.cs</c>, <c>/cygdrive/c/…</c> or
    /// <c>/mnt/c/…</c> — or null when <paramref name="path"/> is not one.
    /// <para>
    /// With <c>MSYS2_ARG_CONV_EXCL='*'</c> set, which this tool asks for so that a <c>//</c> comment survives,
    /// the shell stops converting anything, and the paths a Git Bash user naturally types arrived verbatim.
    /// Windows reads <c>/d/codedev/x</c> as rooted on the current drive — <c>D:\d\codedev\x</c> — so the
    /// payload was "not found" and the only way through was wrapping every path in <c>cygpath -w</c>. The
    /// shape is unambiguous enough to convert here: a single drive letter as the first segment, on a drive
    /// that exists. A path rooted anywhere else in the MSYS tree (<c>/tmp</c>, <c>/home</c>) has no answer
    /// without the shell's own mount table, and is left for <see cref="PosixPathHint"/> to explain.
    /// </para>
    /// </summary>
    internal static string? FromMsys(string path)
    {
        if (!OperatingSystem.IsWindows()) return null;

        var m = MsysDrive.Match(path);
        if (!m.Success) return null;

        var drive = char.ToUpperInvariant(m.Groups["drive"].Value[0]) + @":\";
        if (!System.IO.Directory.Exists(drive)) return null;

        return drive + m.Groups["rest"].Value.TrimStart('/').Replace('/', '\\');
    }

    private static readonly System.Text.RegularExpressions.Regex MsysDrive =
        new(@"^/(?:cygdrive/|mnt/)?(?<drive>[A-Za-z])(?<rest>/.*)?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>Whether the caller is typing into a Git Bash / MSYS shell — asked of the caller's environment,
    /// not the daemon's.</summary>
    internal static bool IsMsysCaller => RequestScope.CallerVariable("MSYSTEM") is { Length: > 0 };

    /// <summary>
    /// The explanation to add to "no such file" when <paramref name="path"/> is a POSIX path this tool cannot
    /// translate — rooted in the MSYS tree rather than on a drive — or an empty string when it is not one.
    /// </summary>
    internal static string PosixPathHint(string path) =>
        OperatingSystem.IsWindows() && path.StartsWith('/') && !path.StartsWith("//", StringComparison.Ordinal)
        && FromMsys(path) is null
            ? " — that is a POSIX path, and only drive paths (/c/…) are translated here. Pass the Windows form, "
            + $"e.g. \"$(cygpath -w '{path}')\"."
            : "";

    /// <summary>Whether <paramref name="path"/> names a directory <b>the caller can see</b>. This is the
    /// test that separates a misplaced <c>&lt;root&gt;</c> from a node id, so measuring it in the daemon's
    /// directory made <c>nfi tree src</c> anywhere on the machine mean "the whole tree of the repo the
    /// daemon happens to live in".</summary>
    internal static bool IsDirectory(string path) =>
        path.Length > 0 && System.IO.Directory.Exists(Of(path));
}
