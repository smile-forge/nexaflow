using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>One parsed C4 macro call: its name, its positional arguments in order, its
/// <c>$name=value</c> arguments by name, and whether it opened a <c>{</c> block.</summary>
internal sealed record C4Macro(
    string Name,
    IReadOnlyList<string> Positional,
    IReadOnlyDictionary<string, string> Named,
    bool OpensBlock)
{
    /// <summary>
    /// The value of an argument that may be given either way: <c>$descr="…"</c> wins over the
    /// <paramref name="position"/>th positional, which is how C4-PlantUML lets a caller skip the
    /// middle of a long signature. Null when it was given neither way, or given empty.
    /// </summary>
    internal string? Arg(int position, string name)
    {
        if (Named.TryGetValue(name, out var named))
            return named.Length > 0 ? named : null;
        if (position < Positional.Count && Positional[position].Length > 0)
            return Positional[position];
        return null;
    }

    /// <summary>A flag argument: absent ⇒ <paramref name="whenAbsent"/>, present but empty ⇒ true.</summary>
    internal bool Flag(int position, string name, bool whenAbsent = true)
    {
        string? v = Arg(position, name);
        if (v is null) return whenAbsent;
        return !v.Equals("false", StringComparison.OrdinalIgnoreCase) && v != "0";
    }
}

/// <summary>
/// Reads one line of C4-PlantUML source into a <see cref="C4Macro"/>.
///
/// The work that is not obvious: an argument list cannot be split on commas, because arguments carry
/// commas inside quotes (<c>"C#, ASP.NET Core"</c>) and parens inside values
/// (<c>$index=Index()</c>). So the reader tracks quote state and paren depth and splits only at
/// depth zero — which is also what lets it find the macro's own closing paren, and the <c>{</c> that
/// may follow it.
/// </summary>
internal static class C4MacroReader
{
    private static readonly Regex RxBr = new(@"<br\s*/?>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>A macro name followed by its opening paren: <c>Container_Ext(</c>, <c>SHOW_LEGEND(</c>.</summary>
    private static readonly Regex RxHead = new(@"^(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled);

    /// <summary>
    /// Parses <paramref name="line"/> as a macro call. False when the line is not one — a bare
    /// directive (<c>title …</c>), a <c>}</c>, an <c>!include</c>, or anything else the caller
    /// handles itself.
    /// </summary>
    internal static bool TryRead(string line, out C4Macro macro)
    {
        macro = null!;
        line = line.Trim();
        if (line.Length == 0) return false;

        var head = RxHead.Match(line);
        if (!head.Success) return false;

        int open = head.Index + head.Length - 1;             // the '(' itself
        int close = MatchingParen(line, open);
        if (close < 0) return false;                          // unbalanced — not a macro we can trust

        var (positional, named) = SplitArgs(line[(open + 1)..close]);
        bool opensBlock = line[(close + 1)..].TrimStart().StartsWith('{');

        macro = new C4Macro(head.Groups["name"].Value, positional, named, opensBlock);
        return true;
    }

    /// <summary>Index of the paren closing the one at <paramref name="open"/>, or -1. Quote-aware.</summary>
    private static int MatchingParen(string s, int open)
    {
        int depth = 0;
        bool quoted = false;
        for (int i = open; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '"' && (i == 0 || s[i - 1] != '\\')) { quoted = !quoted; continue; }
            if (quoted) continue;
            if (c == '(') depth++;
            else if (c == ')' && --depth == 0) return i;
        }
        return -1;
    }

    /// <summary>
    /// Splits an argument list at top-level commas, separating <c>$name=value</c> arguments out of
    /// the positional run. A named argument does not consume a positional slot, so
    /// <c>Rel(a, b, "x", $tags="t")</c> still has "x" as its third positional.
    /// </summary>
    private static (List<string> positional, Dictionary<string, string> named) SplitArgs(string args)
    {
        var positional = new List<string>();
        var named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in SplitTopLevel(args))
        {
            string arg = raw.Trim();
            if (arg.Length == 0) { positional.Add(string.Empty); continue; }

            if (arg[0] == '$')
            {
                int eq = arg.IndexOf('=');
                if (eq > 1)
                {
                    named[arg[1..eq].Trim()] = Unquote(arg[(eq + 1)..]);
                    continue;
                }
            }
            positional.Add(Unquote(arg));
        }
        return (positional, named);
    }

    /// <summary>Yields the comma-separated pieces of <paramref name="args"/>, ignoring commas inside
    /// quotes or nested parens.</summary>
    private static IEnumerable<string> SplitTopLevel(string args)
    {
        if (args.Trim().Length == 0) yield break;

        var sb = new StringBuilder();
        int depth = 0;
        bool quoted = false;
        for (int i = 0; i < args.Length; i++)
        {
            char c = args[i];
            if (c == '"' && (i == 0 || args[i - 1] != '\\')) { quoted = !quoted; sb.Append(c); continue; }
            if (!quoted)
            {
                if (c == '(') depth++;
                else if (c == ')') depth--;
                else if (c == ',' && depth == 0) { yield return sb.ToString(); sb.Clear(); continue; }
            }
            sb.Append(c);
        }
        yield return sb.ToString();
    }

    /// <summary>Strips surrounding quotes, unescapes <c>\"</c>, turns <c>&lt;br/&gt;</c> into a line
    /// break and decodes HTML entities — the label as it should be displayed.</summary>
    internal static string Unquote(string value)
    {
        string s = value.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"') s = s[1..^1];
        s = s.Replace("\\\"", "\"");
        s = RxBr.Replace(s, "\n");
        return WebUtility.HtmlDecode(s).Trim();
    }

    /// <summary>Splits a <c>$tags="a+b"</c> list.</summary>
    internal static IEnumerable<string> SplitTags(string? tags) =>
        string.IsNullOrWhiteSpace(tags)
            ? []
            : tags!.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Removes a trailing comment. Both dialects are accepted because a C4 fence is written in
    /// Mermaid's world but pasted from PlantUML's: <c>%%</c> is Mermaid's, a leading <c>'</c> is
    /// PlantUML's. An apostrophe only comments when it opens the line — otherwise
    /// <c>"Bob's laptop"</c> would lose its tail.
    /// </summary>
    internal static string StripComment(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith('\'')) return string.Empty;

        int idx = line.IndexOf("%%", StringComparison.Ordinal);
        return idx >= 0 ? line[..idx] : line;
    }
}

/// <summary>
/// The running index behind C4-PlantUML's dynamic numbering: <c>Index()</c> takes the next number,
/// <c>LastIndex()</c> repeats the one just used, and <c>SetIndex(n)</c>/<c>increment(n)</c> move the
/// counter without drawing anything. A relationship's <c>$index</c> is whichever of those it names,
/// or a literal.
/// </summary>
internal sealed class C4IndexCounter
{
    private int _next = 1;

    /// <summary>The number most recently handed out (0 before the first).</summary>
    internal int Last { get; private set; }

    internal int Next(int offset = 1)
    {
        Last = _next;
        _next += Math.Max(1, offset);
        return Last;
    }

    internal void Set(int value)
    {
        _next = value;
    }

    internal void Increment(int offset = 1)
    {
        _next += offset;
    }

    /// <summary>
    /// Resolves an <c>$index</c> expression. Null when there is none — the caller then decides
    /// whether the diagram numbers every relationship anyway.
    /// </summary>
    internal int? Resolve(string? expr)
    {
        if (string.IsNullOrWhiteSpace(expr)) return null;
        string e = expr!.Trim();

        if (e.StartsWith("LastIndex", StringComparison.OrdinalIgnoreCase))
            return Last > 0 ? Last : Next();

        if (e.StartsWith("SetIndex", StringComparison.OrdinalIgnoreCase))
        {
            if (TryArgNumber(e, out int set)) Set(set);
            return Next();
        }

        if (e.StartsWith("Index", StringComparison.OrdinalIgnoreCase))
            return Next(TryArgNumber(e, out int offset) ? offset : 1);

        return int.TryParse(e, NumberStyles.Integer, CultureInfo.InvariantCulture, out int literal)
            ? literal
            : null;
    }

    /// <summary>The number inside a call's parens — the <c>2</c> of <c>Index(2)</c>.</summary>
    private static bool TryArgNumber(string call, out int value)
    {
        value = 0;
        int o = call.IndexOf('('), c = call.LastIndexOf(')');
        if (o < 0 || c <= o) return false;
        return int.TryParse(call[(o + 1)..c].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
