using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.Services.Initiatives.Cli;

/// <summary>
/// Several <c>graph edit</c> commands written down as one script: a command per line, exactly as it would follow
/// <c>nfi graph edit</c>, with any multi-line text in a block underneath it rather than squeezed through a shell.
/// <code>
/// # a comment
/// substitute code:src/A.cs#T:A/M:Run
/// &lt;&lt;&lt; find
/// Old();
/// &gt;&gt;&gt;
/// &lt;&lt;&lt; text
/// New();
/// &gt;&gt;&gt;
/// rename code:src/A.cs#T:A/M:Helper --to Assist
/// move code:src/A.cs#T:Parser --to file:src/Parser.cs
/// </code>
/// <para>
/// A block opens on the line after its command with <c>&lt;&lt;&lt;</c> or <c>&lt;&lt;&lt; text</c> (the command's
/// text) or <c>&lt;&lt;&lt; find</c>, and closes at a line holding only <c>&gt;&gt;&gt;</c> — or, for text that itself
/// holds such a line, at a word of your choosing: <c>&lt;&lt;&lt; text END</c> closes at <c>END</c>. Nothing inside a
/// block is interpreted: no escapes, no quoting, no shell.
/// </para>
/// </summary>
internal static class EditScript
{
    /// <summary>One command: the script line it starts on, and its arguments with any blocks folded in as
    /// <c>--text</c> / <c>--find</c>.</summary>
    internal sealed record Command(int Line, string[] Args);

    public static bool TryParse(string script, out List<Command> commands, out string error)
    {
        commands = [];
        error    = "";
        var lines = script.ReplaceLineEndings("\n").Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            if (line.StartsWith("<<<", StringComparison.Ordinal))
            {
                error = $"line {i + 1}: a <<< block goes directly under the command it belongs to, and there is none above it.";
                return false;
            }

            var number = i + 1;
            var args   = Program.Tokenize(line);

            while (i + 1 < lines.Length && lines[i + 1].TrimStart().StartsWith("<<<", StringComparison.Ordinal))
            {
                i++;
                var opener = lines[i].Trim()[3..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var kind   = opener.Length > 0 ? opener[0] : "text";
                var close  = opener.Length > 1 ? opener[1] : ">>>";

                if (kind is not ("text" or "find"))
                {
                    error = $"line {i + 1}: a block opens with '<<< text' or '<<< find', not '<<< {kind}'.";
                    return false;
                }

                var body   = new List<string>();
                var closed = false;
                for (i++; i < lines.Length; i++)
                {
                    if (lines[i].Trim() == close) { closed = true; break; }
                    body.Add(lines[i]);
                }

                if (!closed)
                {
                    error = $"line {number}: its '<<< {kind}' block is never closed with a line holding only '{close}'.";
                    return false;
                }

                var flag = kind == "find" ? "--find" : "--text";
                if (args.Contains(flag, StringComparer.Ordinal))
                {
                    error = $"line {number}: {flag} is given twice, on the line and as a block.";
                    return false;
                }

                args.Add(flag);
                args.Add(string.Join("\n", body));
            }

            commands.Add(new Command(number, [.. args]));
        }

        if (commands.Count > 0) return true;
        error = "the script holds no commands.";
        return false;
    }
}
