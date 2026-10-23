using System.IO;

namespace Nexaflow.Core.Services;

/// <summary>
/// What one launch of Nexaflow.exe asks of the app: a window in a named workspace (<c>--context "Name"</c>, from the
/// taskbar JumpList), an item to open (<c>--open "path"</c>, from Explorer's "Open in Nexaflow"), both, or neither.
/// The first launch acts on it itself; a later one hands it to the running instance
/// (<see cref="SingleInstanceService.ForwardLaunch"/>), so the path is made absolute here, against the launching
/// process's own working directory.
/// </summary>
public sealed record LaunchRequest(string? WorkspaceName, string? OpenPath)
{
    /// <summary>The switch naming the file or folder a launch opens. The installer's Explorer verbs pass it.</summary>
    public const string OpenSwitch = "--open";

    public static LaunchRequest Parse(IReadOnlyList<string> args)
        => new(ValueAfter(args, JumpListService.ContextSwitch), AbsolutePath(ValueAfter(args, OpenSwitch)));

    private static string? ValueAfter(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i + 1 < args.Count; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    /// <summary>
    /// <paramref name="given"/> as a full path, or null when it is none. Explorer quotes the item it hands a verb, and
    /// a drive root ends in a backslash, so <c>"C:\"</c> arrives as <c>C:"</c> — the backslash escaped the closing
    /// quote. The stray quote is dropped and the bare drive given back its root, rather than read as the current
    /// directory on that drive.
    /// </summary>
    internal static string? AbsolutePath(string? given)
    {
        var path = given?.Trim().TrimEnd('"');
        if (string.IsNullOrEmpty(path)) return null;
        if (path is [_, ':']) path += Path.DirectorySeparatorChar;

        try { return Path.GetFullPath(path); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }
}
