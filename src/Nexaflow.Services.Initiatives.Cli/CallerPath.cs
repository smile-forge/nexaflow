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
        try { return Path.GetFullPath(path, Directory); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return path; }
    }

    /// <summary>Whether <paramref name="path"/> names a directory <b>the caller can see</b>. This is the
    /// test that separates a misplaced <c>&lt;root&gt;</c> from a node id, so measuring it in the daemon's
    /// directory made <c>nfi tree src</c> anywhere on the machine mean "the whole tree of the repo the
    /// daemon happens to live in".</summary>
    internal static bool IsDirectory(string path) =>
        path.Length > 0 && System.IO.Directory.Exists(Of(path));
}
