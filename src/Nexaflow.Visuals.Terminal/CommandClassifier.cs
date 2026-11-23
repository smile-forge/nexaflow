using System.IO;
using Nexaflow.IO.Common;

namespace Nexaflow.Visuals.Terminal;

/// <summary>What a line typed in the AI bar is, with a console showing.</summary>
public enum ConsoleLineVerdict
{
    /// <summary>Words for the assistant.</summary>
    Request,

    /// <summary>It reads either way, so a model is asked.</summary>
    Unclear,

    /// <summary>A command line, which runs with no model seeing it.</summary>
    Command,
}

/// <summary>
/// Judges a line typed in the AI bar over a console — never one typed in the console, which is the shell's. It is instant
/// and runs as the person types. Both mistakes cost, unequally: a request run as a command can destroy ("rm the temp
/// files" deletes files named for those words), and a command taken for a request can show a model a secret. So a line
/// with a command's shape — a known name, a path, a flag, an assignment — and no sentence's is a command; a sentence
/// that opens with no command is a request; whatever has both shapes, or neither, is unclear and runs only if a model
/// says it is a command.
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
        "find", "findstr", "cmd", "powershell", "pwsh",
    };

    /// <summary>POSIX shell built-ins and the everyday tools every Unix-like system has, known before — or without — the
    /// shell's own list of programs. Names there differ by case, so they match exactly.</summary>
    public static readonly IReadOnlySet<string> PosixBuiltins = new HashSet<string>(StringComparer.Ordinal)
    {
        "alias", "bg", "cd", "command", "echo", "eval", "exec", "exit", "export", "fg", "hash", "history", "jobs", "kill",
        "printf", "pwd", "read", "set", "source", "test", "type", "ulimit", "umask", "unalias", "unset", "wait", ".",
        "cat", "chmod", "chown", "clear", "cp", "df", "du", "env", "file", "find", "grep", "head", "less", "ln", "ls",
        "man", "mkdir", "more", "mv", "ps", "rm", "rmdir", "sed", "awk", "sort", "sudo", "tail", "tar", "top", "touch",
        "tree", "uname", "which", "whoami", "bash", "sh", "zsh",
    };

    // Command names that are also everyday verbs: followed only by plain words they read as a request just as well.
    private static readonly HashSet<string> EverydayVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "at", "change", "choice", "clear", "compact", "convert", "copy", "cut", "del", "erase", "expand", "file", "find",
        "format", "head", "help", "host", "install", "join", "kill", "link", "look", "make", "more", "move", "net", "open",
        "print", "query", "rd", "read", "reboot", "recover", "ren", "rename", "replace", "reset", "rm", "rmdir", "run",
        "shutdown", "sort", "split", "start", "stop", "test", "time", "touch", "tree", "type", "wait", "wall", "watch",
        "where", "which", "who", "write",
    };

    // A whole line that is an answer to the assistant as readily as a command — `yes` is also a program that never ends.
    private static readonly HashSet<string> Replies = new(StringComparer.OrdinalIgnoreCase)
    {
        "cancel", "continue", "done", "hello", "hi", "no", "ok", "okay", "please", "stop", "sure", "thanks", "yes",
    };

    // Words a sentence has and a command's arguments seldom do.
    private static readonly HashSet<string> SentenceWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "about", "an", "and", "any", "are", "be", "by", "did", "does", "doesn't", "don't", "everything", "for", "from",
        "has", "have", "here", "how", "i", "if", "in", "into", "is", "isn't", "it", "it's", "me", "my", "not", "of", "on",
        "or", "our", "please", "some", "something", "than", "that", "the", "them", "there", "these", "this", "those", "to",
        "was", "were", "what", "what's", "when", "where", "who", "why", "with", "you", "your",
    };

    // How a request opens, where no command of that name exists.
    private static readonly HashSet<string> RequestOpeners = new(StringComparer.OrdinalIgnoreCase)
    {
        "are", "can", "could", "describe", "did", "do", "does", "don't", "explain", "how", "i", "i'd", "i'm", "is", "list",
        "please", "show", "summarise", "summarize", "tell", "what", "what's", "whats", "when", "where", "where's", "which",
        "who", "why", "would",
    };

    private static readonly string[] ProgramExtensions = [".bat", ".cmd", ".com", ".exe", ".ps1", ".py", ".sh"];

    /// <summary>What <paramref name="line"/> is.</summary>
    /// <param name="line">The line as typed in the bar.</param>
    /// <param name="knowsCommand">Whether the console's shell runs something by that name. Asked only of a bare name,
    /// never of a path, so judging a line never reaches for a share.</param>
    public static ConsoleLineVerdict JudgeLine(string line, Func<string, bool> knowsCommand)
    {
        var text = line.Trim();

        // A control character is a key to the shell, so a line holding one is never typed into it unasked.
        if (text.Length == 0 || text.Any(c => char.IsControl(c) && c is not ('\t' or '\r' or '\n')))
            return ConsoleLineVerdict.Request;

        var words = SplitWords(text);
        var first = words[0].Text;
        var args  = words.Skip(1).ToList();
        var bare  = args.Where(word => !word.Quoted).Select(word => word.Text).ToList();

        bool sentence  = text.EndsWith('?') || bare.Any(word => SentenceWords.Contains(WithoutPunctuation(word)));
        bool severalLines = text.Contains('\n');

        if (IsInvocation(first))
            return sentence || severalLines ? ConsoleLineVerdict.Unclear : ConsoleLineVerdict.Command;

        if (args.Count == 0 && Replies.Contains(WithoutPunctuation(first))) return ConsoleLineVerdict.Unclear;

        bool dashFlag  = bare.Any(IsDashFlag);
        bool operators = bare.Any(HasOperator);

        if (knowsCommand(first))
        {
            if (sentence || severalLines) return ConsoleLineVerdict.Unclear;
            if (dashFlag || operators || bare.Any(IsSlashFlag)) return ConsoleLineVerdict.Command;

            int plainWords = bare.Count(IsPlainWord);
            if (plainWords > 0 && EverydayVerbs.Contains(first)) return ConsoleLineVerdict.Unclear;
            if (plainWords < args.Count) return ConsoleLineVerdict.Command;   // a file, a number, something quoted
            return plainWords < 3 ? ConsoleLineVerdict.Command : ConsoleLineVerdict.Unclear;
        }

        if (RequestOpeners.Contains(WithoutPunctuation(first))) return ConsoleLineVerdict.Request;
        if (severalLines) return sentence ? ConsoleLineVerdict.Request : ConsoleLineVerdict.Unclear;
        if (sentence) return dashFlag || operators ? ConsoleLineVerdict.Unclear : ConsoleLineVerdict.Request;

        // Flags on a name not known — an alias, a program installed since the list was read — are still a command's.
        return dashFlag ? ConsoleLineVerdict.Command : ConsoleLineVerdict.Unclear;
    }

    /// <summary>Whether <paramref name="name"/> is a program on this computer's PATH.</summary>
    [LocalDiskOnly("the local shell's PATH, on this computer")]
    public static bool IsOnPath(string name)
    {
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;

        var folders = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.BAT;.CMD;.COM")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var folder in folders)
        {
            var candidate = Path.Combine(folder, name);
            if (File.Exists(candidate) || extensions.Any(extension => File.Exists(candidate + extension))) return true;
        }
        return false;
    }

    // Words as a shell takes them. A quote opens one only at its start or after '=', so "what's" is a word.
    private static List<(string Text, bool Quoted)> SplitWords(string text)
    {
        var words = new List<(string, bool)>();
        int at = 0;
        while (at < text.Length)
        {
            while (at < text.Length && char.IsWhiteSpace(text[at])) at++;
            if (at >= text.Length) break;

            int  start  = at;
            bool quoted = false;
            while (at < text.Length && !char.IsWhiteSpace(text[at]))
            {
                char c = text[at];
                if (c is '"' or '\'' && (at == start || text[at - 1] == '='))
                {
                    quoted = true;
                    int close = text.IndexOf(c, at + 1);
                    at = close < 0 ? text.Length : close + 1;
                }
                else at++;
            }
            words.Add((text[start..at], quoted));
        }
        return words;
    }

    // A first word that can only be something to run: a path, a script's file name, a variable set for the command.
    private static bool IsInvocation(string word)
    {
        var name = word.Trim('"', '\'');
        if (name.Length == 0 || name.Contains("://", StringComparison.Ordinal)) return false;
        if (name.Contains('/') || name.Contains('\\')) return true;
        if (name.Length > 1 && char.IsAsciiLetter(name[0]) && name[1] == ':') return true;
        if (name.Length > 3 && ProgramExtensions.Any(extension => name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
            return true;

        int equals = name.IndexOf('=');
        return equals > 0
               && (char.IsAsciiLetter(name[0]) || name[0] == '_')
               && name[..equals].All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
    }

    private static bool IsDashFlag(string word)
    {
        int dashes = word.StartsWith("--", StringComparison.Ordinal) ? 2 : word.StartsWith('-') ? 1 : 0;
        return dashes > 0 && word.Length > dashes && char.IsAsciiLetterOrDigit(word[dashes]);
    }

    // cmd's switches: /s, /q, /a:h — and not /tmp, which is a path.
    private static bool IsSlashFlag(string word)
        => word.Length is >= 2 && word[0] == '/' && (char.IsAsciiLetter(word[1]) || word[1] == '?')
           && (word.Length <= 2 || word[2] == ':');

    private static bool HasOperator(string word)
        => word.IndexOfAny(['|', '<', '>', ';']) >= 0
           || word.Contains("&&", StringComparison.Ordinal) || word.Contains("$(", StringComparison.Ordinal);

    private static bool IsPlainWord(string word)
    {
        var trimmed = WithoutPunctuation(word);
        return trimmed.Length > 0 && trimmed.All(char.IsLetter);
    }

    private static string WithoutPunctuation(string word) => word.TrimEnd('.', ',', '!', '?', ':', ';');
}
