using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Nexaflow.Syntax;

/// <summary>
/// The physical shape of a source file — its line endings, whether it ends with one, and what one level of
/// indentation looks like in it — kept apart from the file's meaning so an edit can be written back looking
/// like the file it went into.
/// <para>
/// This exists because the three ways a structural edit visibly goes wrong have nothing to do with the code
/// being inserted. A block pasted into a CRLF file with LF endings leaves a mixed file that every diff tool
/// reports as a whole-file change. A method written flush-left lands flush-left inside a class. A `\n` typed
/// on a command line arrives as a backslash and an `n`. None of those are the caller's problem to think
/// about, so none of them are the caller's problem here: text arrives normalised to LF at whatever
/// indentation it was written, and leaves matching its destination.
/// </para>
/// </summary>
public sealed record SourceText
{
    private SourceText(IReadOnlyList<string> lines, string newline, bool finalNewline)
    {
        Lines        = lines;
        Newline      = newline;
        FinalNewline = finalNewline;
    }

    /// <summary>The file's lines, newline characters stripped. A trailing newline is <see cref="FinalNewline"/>,
    /// never an empty last element — that distinction is what makes a round trip exact.</summary>
    public IReadOnlyList<string> Lines { get; }

    /// <summary>The file's dominant line ending, reproduced on every line written back.</summary>
    public string Newline { get; }

    /// <summary>Whether the file ends with a line ending. Adding or dropping one is a real diff line.</summary>
    public bool FinalNewline { get; }

    /// <summary>
    /// Reads raw file content. Mixed endings are resolved to whichever is commoner, because a file has to be
    /// written back with one and picking the majority leaves the fewest lines changed.
    /// </summary>
    public static SourceText Of(string raw)
    {
        var crlf = Count(raw, "\r\n");
        var lf   = raw.Count(c => c == '\n') - crlf;
        var cr   = raw.Count(c => c == '\r') - crlf;

        var newline = crlf >= lf && crlf >= cr && crlf > 0 ? "\r\n"
                    : cr > lf && cr > 0                    ? "\r"
                    : "\n";

        var normalised   = raw.Replace("\r\n", "\n").Replace('\r', '\n');
        var finalNewline = normalised.EndsWith('\n');
        var body         = finalNewline ? normalised[..^1] : normalised;

        return new SourceText(body.Length == 0 && !finalNewline ? [] : body.Split('\n'), newline, finalNewline);
    }

    /// <summary>The file as it should be written: these lines, this file's endings.</summary>
    public string Compose(IReadOnlyList<string> lines) =>
        string.Join(Newline, lines) + (FinalNewline && lines.Count > 0 ? Newline : "");

    public string Compose() => Compose(Lines);

    private static int Count(string s, string needle)
    {
        int n = 0, i = 0;
        while ((i = s.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }

    // ── Indentation ─────────────────────────────────────────────────────────

    /// <summary>The leading whitespace of a line, which is the indentation an edit at that line must match.</summary>
    public static string IndentOf(string line)
    {
        var i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
        return line[..i];
    }

    /// <summary>
    /// One indent level as this file writes it: a tab if the file indents with tabs, otherwise the smallest
    /// non-zero space step actually used. Guessed from the file rather than assumed, so a 2-space codebase
    /// does not get 4-space members appended to it.
    /// </summary>
    public string IndentUnit()
    {
        var indents = Lines.Where(l => l.Trim().Length > 0).Select(IndentOf).ToList();
        if (indents.Any(i => i.StartsWith('\t'))) return "\t";

        // The shallowest indented line: one level in from the margin is, by definition, one level. Asking
        // instead which of 2/4/8 most lines divide by picks 2 for every file, since 4-space indents divide
        // by it too.
        var widths = indents.Select(i => i.Length).Where(w => w > 0).ToList();
        return widths.Count == 0 ? "    " : new string(' ', Math.Clamp(widths.Min(), 1, 8));
    }

    /// <summary>
    /// Re-indents a block to sit at <paramref name="indent"/>: the block's own common indentation is stripped
    /// first, so the caller may write it flush-left, or lifted straight out of another file at whatever depth
    /// it had there, and it lands correctly either way. Blank lines stay blank rather than becoming trailing
    /// whitespace.
    /// </summary>
    public static IReadOnlyList<string> Reindent(IReadOnlyList<string> block, string indent)
    {
        var common = CommonIndent(block);
        return [.. block.Select(l => l.Trim().Length == 0
            ? ""
            : indent + (l.Length >= common.Length && l.StartsWith(common, StringComparison.Ordinal)
                        ? l[common.Length..]
                        : l.TrimStart()))];
    }

    /// <summary>The longest indentation every non-blank line in the block begins with.</summary>
    public static string CommonIndent(IReadOnlyList<string> block)
    {
        string? common = null;
        foreach (var line in block)
        {
            if (line.Trim().Length == 0) continue;
            var indent = IndentOf(line);
            if (common is null) { common = indent; continue; }

            var n = 0;
            while (n < common.Length && n < indent.Length && common[n] == indent[n]) n++;
            common = common[..n];
        }
        return common ?? "";
    }

    // ── Splicing ────────────────────────────────────────────────────────────

    /// <summary>Replaces the 0-based line range [<paramref name="from"/>..<paramref name="to"/>] with
    /// <paramref name="replacement"/>. An empty replacement deletes the range.</summary>
    public static IReadOnlyList<string> Splice(IReadOnlyList<string> lines, int from, int to,
                                               IReadOnlyList<string> replacement)
    {
        var result = new List<string>(lines.Count - (to - from + 1) + replacement.Count);
        for (var i = 0; i < from; i++) result.Add(lines[i]);
        result.AddRange(replacement);
        for (var i = to + 1; i < lines.Count; i++) result.Add(lines[i]);
        return result;
    }

    // ── Escapes ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Decodes the escapes a shell cannot pass through literally — <c>\n</c>, <c>\t</c>, <c>\r</c>,
    /// <c>\0</c>, <c>\\</c>, <c>\"</c>, <c>\'</c> and <c>\uXXXX</c>. An unrecognised escape is left exactly as
    /// written rather than swallowed, so a Windows path or a regex in the payload survives intact.
    /// </summary>
    public static string Unescape(string s)
    {
        if (!s.Contains('\\')) return s;

        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] != '\\' || i + 1 >= s.Length) { sb.Append(s[i]); continue; }

            var c = s[++i];
            switch (c)
            {
                case 'n':  sb.Append('\n'); break;
                case 'r':  sb.Append('\r'); break;
                case 't':  sb.Append('\t'); break;
                case '0':  sb.Append('\0'); break;
                case '\\': sb.Append('\\'); break;
                case '"':  sb.Append('"');  break;
                case '\'': sb.Append('\''); break;
                case 'u' when i + 4 < s.Length
                           && ushort.TryParse(s.AsSpan(i + 1, 4), System.Globalization.NumberStyles.HexNumber,
                                              System.Globalization.CultureInfo.InvariantCulture, out var code):
                    sb.Append((char)code);
                    i += 4;
                    break;
                default: sb.Append('\\').Append(c); break;   // not an escape we own — leave it alone
            }
        }
        return sb.ToString();
    }

    /// <summary>Splits caller-supplied text into lines, whatever endings it arrived with.</summary>
    public static IReadOnlyList<string> BlockOf(string text)
    {
        var normalised = text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (normalised.EndsWith('\n')) normalised = normalised[..^1];   // a trailing newline is the splice's job
        return normalised.Length == 0 ? [] : normalised.Split('\n');
    }
}
