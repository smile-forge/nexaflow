using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Binding;
using Nexaflow.Markdown.Editing;

namespace Nexaflow.Markdown.Mermaid.Ishikawa;

/// <summary>
/// Reads an Ishikawa diagram — an <c>ishikawa</c> (<c>ishikawa-beta</c>) block: the event the diagram is about, and the causes
/// under it — and says how what is written into one is spelled.
///
/// <para>
/// A diagram is a language of its own, written in Mermaid's syntax — a <c>mermaid</c> fence says only which
/// (<see cref="MermaidFenceParser"/>) — so this reads its own source rather than being handed rows of it. What a fence holds round
/// any diagram — the front matter, an <c>accTitle</c>, a comment, a directive, a binding, the space between lines — is read with the
/// shared pieces (<see cref="MermaidParser"/>), because those are facts about a block written in Mermaid's syntax rather than about
/// an Ishikawa diagram. The header is optional, since an <c>ishikawa</c> fence already says what the block is.
/// </para>
/// <para>
/// The rules are Mermaid's. Every line is what it says, to its end, so each statement is its row: the first is the event — the
/// problem the diagram is about — and every line after it a cause, under the nearest line before it that is indented less, which
/// is the order and indentation the lines are written in, so nothing is worked out over the block but what the front matter asks
/// for. The event may follow the keyword on the header line. Nothing on a line is anything but text — a quote, a bracket, a
/// <c>title</c>: a <c>%%</c> starting a line is a comment, and one later on it is part of what it says.
/// </para>
/// </summary>
public sealed class IshikawaParser : ITranspile
{
    private IshikawaParser()
    {
    }

    /// <summary>The language an Ishikawa diagram is written in: what a fence names it by.</summary>
    public const string Language = "ishikawa";

    /// <summary>The words an Ishikawa diagram's header is written with — Mermaid's both.</summary>
    public static readonly string[] Keywords = ["ishikawa-beta", Language];

    /// <summary>What opens a comment running to the end of its row, where it starts the line.</summary>
    private const string Comment = "%%";

    /// <summary>Reads a block into its lines, every character kept.</summary>
    public static ContentNode Parse(string? source)
    {
        source ??= string.Empty;

        var lines = new List<ContentNode>();
        var at = 0;
        var headed = false;

        if (MermaidParser.Fences(source) is (var open, var close))
        {
            // Only blank lines come before front matter; that is what makes it front matter.
            for (; at < open.Start; at = Next(source, at, ref headed, lines)) { }

            lines.Add(MermaidParser.FrontMatter(source, open, close));
            at = close.Stop;
        }

        while (at < source.Length) at = Next(source, at, ref headed, lines);

        return new BlockNode(Language, lines, MermaidKinds.Block);
    }

    /// <inheritdoc/>
    /// <remarks>Spelled as Mermaid spells it: nothing an Ishikawa diagram writes is spelled a way of its own.</remarks>
    public static ContentChange? Rewrite(ContentChange change) => MermaidParser.Rewrite(change);

    // ── Lines ───────────────────────────────────────────────────────────────

    /// <summary>The next line of the block, read and added to <paramref name="lines"/>, and where the one after it starts.</summary>
    /// <param name="headed">Whether the first line saying anything has been read: only it may be the header.</param>
    private static int Next(string source, int at, ref bool headed, List<ContentNode> lines)
    {
        var row = MermaidParser.Row.At(source, at);
        var (from, to) = row.Text(source);
        var text = source[from..to];

        if (text.Length == 0)
        {
            lines.Add(MermaidParser.Line(source, row.Start, from, [], to, row));
            return row.Stop;
        }

        if (text.StartsWith("%%{", StringComparison.Ordinal)) return MermaidParser.Directive(source, row, from, to, lines);

        if (text.StartsWith(Comment, StringComparison.Ordinal))
        {
            lines.Add(MermaidParser.Line(source, row.Start, from,
                                         [ContentNode.Leaf(Kinds.Comment, text, Roles.Trivia, offset: from)], to, row));
            return row.Stop;
        }

        // A binding standing on a line of its own after the header is causes supplied by whatever the diagram is shown against. Held
        // as written; read into its place before the diagram is worked over.
        if (BoundText.Path(text) is not null)
        {
            lines.Add(MermaidParser.Line(source, row.Start, from,
                [headed
                    ? ContentNode.Leaf(Kinds.BoundContent, text, offset: from)
                    : ContentNode.Shown(text, "A diagram is not bound whole: its first line names its type, and a binding after it supplies lines of it.", offset: from)],
                to, row));
            return row.Stop;
        }

        if (!headed)
        {
            headed = true;
            if (Heading(text, from) is { } header)
            {
                lines.Add(MermaidParser.Line(source, row.Start, from, [header], to, row));
                return row.Stop;
            }
        }

        if (MermaidParser.Accessibility(source, row, from, to, lines) is { } stop) return stop;

        // The cause, handed the line to the end of its row: as much of it as is read is what was written, and the rest is the line's.
        var said = Cause(source[from..row.End], from);
        lines.Add(MermaidParser.Line(source, row.Start, from, [said], from + said.Width, row));
        return row.Stop;
    }

    // ── The header ──────────────────────────────────────────────────────────

    /// <summary>
    /// The header — <c>ishikawa</c> or <c>ishikawa-beta</c>, and the event where it follows on the same line:
    /// <c>ishikawa-beta Blurry Photo</c> — or null where the line says something else, which an <c>ishikawa</c> fence needs no
    /// header before.
    /// </summary>
    private static ContentNode? Heading(string text, int at)
    {
        if (MermaidLine.Keyword(text, Keywords) is not { } keyword) return null;

        var pieces = new List<ContentNode> { ContentNode.Leaf(MermaidKinds.Keyword, text[..keyword.Length], Roles.Name, offset: at) };

        var rest = text[keyword.Length..];
        var gap = MermaidParser.Leading(rest);
        var after = at + keyword.Length + gap;

        if (gap > 0) pieces.Add(MermaidParser.Space(rest[..gap], at + keyword.Length));
        if (gap < rest.Length) pieces.Add(Cause(rest[gap..], after));

        return ContentNode.Branch(MermaidKinds.Header, pieces, offset: at);
    }

    // ── Causes ──────────────────────────────────────────────────────────────

    /// <summary>A line, as what it says.</summary>
    private static ContentNode Cause(string text, int at)
    {
        var line = MermaidLine.Of(text, at, comments: false);
        line.Words(IshikawaRoles.Says);
        return line.Read(IshikawaKinds.Cause);
    }
}
