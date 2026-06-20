using System.IO;

namespace Nexaflow.Visuals.Terminal;

/// <summary>
/// Decides whether a typed line is a shell command (run it) or natural language (hand it to the LLM).
/// Heuristic and instant: the first token is a shell built-in, an executable resolvable on PATH, or an
/// explicit path to an existing file → command; otherwise → a request for the model. Builtins are passed
/// in so each shell (cmd today, PowerShell later) supplies its own.
/// </summary>
public static class CommandClassifier
{
    /// <summary>cmd.exe built-ins and a few ubiquitous console tools, for the default (cmd) terminal.</summary>
    public static readonly IReadOnlySet<string> CmdBuiltins = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "assoc", "break", "call", "cd", "chdir", "cls", "color", "copy", "date", "del", "dir", "echo",
        "endlocal", "erase", "exit", "for", "ftype", "goto", "if", "md", "mkdir", "mklink", "move", "path",
        "pause", "popd", "prompt", "pushd", "rd", "rem", "ren", "rename", "rmdir", "set", "setlocal",
        "shift", "start", "time", "title", "type", "ver", "verify", "vol", "where", "help", "more", "tree",
        "find", "findstr", "cmd", "powershell", "pwsh", "cls",
    };

    public static bool IsCommand(string line, IReadOnlySet<string> builtins)
    {
        line = line.Trim();
        if (line.Length == 0) return true;              // a blank line is for the shell, not the model

        var token = FirstToken(line);
        if (token.Length == 0) return true;

        if (builtins.Contains(token)) return true;

        // An explicit path to an existing executable/script (.\foo.bat, C:\tools\x.exe).
        if (LooksLikePath(token) && ResolvesToFile(token)) return true;

        // A bare program name resolvable on PATH (git, npm, node, dotnet, …).
        return OnPath(token);
    }

    private static string FirstToken(string line)
    {
        if (line[0] == '"')                              // quoted program: "C:\Program Files\x\y.exe" …
        {
            var end = line.IndexOf('"', 1);
            return end > 0 ? line[1..end] : line[1..];
        }
        var sp = line.IndexOfAny([' ', '\t']);
        return sp < 0 ? line : line[..sp];
    }

    private static bool LooksLikePath(string token)
        => token.Contains('\\') || token.Contains('/') || (token.Length > 1 && token[1] == ':');

    private static bool ResolvesToFile(string token)
    {
        try
        {
            if (File.Exists(token)) return true;
            foreach (var ext in PathExt())
                if (File.Exists(token + ext)) return true;
        }
        catch { }
        return false;
    }

    private static bool OnPath(string token)
    {
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var exts = PathExt();
        foreach (var dir in dirs)
        {
            try
            {
                if (File.Exists(Path.Combine(dir, token))) return true;
                foreach (var ext in exts)
                    if (File.Exists(Path.Combine(dir, token + ext))) return true;
            }
            catch { /* malformed PATH entry */ }
        }
        return false;
    }

    private static string[] PathExt()
        => (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.BAT;.CMD;.COM")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
