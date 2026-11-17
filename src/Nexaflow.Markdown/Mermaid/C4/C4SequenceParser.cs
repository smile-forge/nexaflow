using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Binding;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid.Sequence;

namespace Nexaflow.Markdown.Mermaid.C4;

/// <summary>
/// Reads C4's macros written on a timeline — a <c>C4Sequence</c> block, which is two languages in one: C4-PlantUML's macros, and the
/// sequence diagram's own lines written among them.
///
/// <para>
/// A diagram is a language of its own, written in Mermaid's syntax — a <c>mermaid</c> fence says only which
/// (<see cref="MermaidFenceParser"/>) — so this reads its own source rather than being handed rows of it. What a fence holds round
/// any diagram — the front matter, an <c>accTitle</c>, a <c>%%</c> comment, a directive, a binding, the space between lines — is
/// read with the shared pieces (<see cref="MermaidParser"/>). The header is optional, since a <c>C4Sequence</c> fence already says
/// what the block is.
/// </para>
/// <para>
/// A macro, the brace closing a boundary, a title and the lines a pasted diagram brings are C4's, read as a structural diagram reads
/// them (<see cref="C4Parser"/>); every other line is read by the sequence diagram's own pieces (<see cref="SequenceParser"/>) rather than a second copy of them,
/// which is what lets <c>alt</c>, <c>loop</c>, <c>note over</c> and <c>activate</c> nest round the macros correctly. The two
/// nestings are one stack for the same reason — a <c>}</c> and an <c>end</c> each close whichever was opened last — worked out by
/// the stages its language offers the engine, the sequence diagram's own among them.
/// </para>
/// </summary>
public sealed class C4SequenceParser : ITranspile
{
    private C4SequenceParser()
    {
    }

    /// <summary>The language a C4 sequence is written in: what a fence names it by, and the word its header is written with.</summary>
    public const string Language = "C4Sequence";

    /// <summary>The words a C4 sequence's header is written with.</summary>
    public static readonly string[] Keywords = [Language];

    /// <summary>What opens a comment running to the end of its row.</summary>
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

    /// <summary>Whether a hole stands in <paramref name="node"/> while somebody writes in the block: between an argument's quotes, and wherever a line of the sequence diagram has one.</summary>
    public static bool Holds(ContentNode? holder, ContentNode node) => C4Parser.Holds(holder, node) || SequenceParser.Holds(holder, node);

    /// <inheritdoc/>
    /// <remarks>Spelled as a C4 diagram spells it (<see cref="C4Parser.Rewrite"/>).</remarks>
    public static ContentChange? Rewrite(ContentChange change) => C4Parser.Rewrite(change);

    /// <summary>The next line of the block, read and added to <paramref name="lines"/> — or the rows it takes — and where the one after it starts.</summary>
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

        // A binding standing on a line of its own after the header is lines supplied by whatever the diagram is shown against. Held
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

        if (SequenceParser.SeparatorOrComment(source, row, from, to, lines) is { } apart) return apart;

        if (!headed)
        {
            headed = true;
            if (C4Parser.Heading(text, from, Keywords) is { } header)
            {
                lines.Add(MermaidParser.Line(source, row.Start, from, [header], to, row));
                return row.Stop;
            }
        }

        if (MermaidParser.Accessibility(source, row, from, to, lines) is { } stop) return stop;
        if (MermaidParser.SpacedDescription(source, row, from, to, C4Parser.AccessibleDescription, lines) is { } described) return described;

        // A line of C4's own, as much of the source as it takes; anything else is the sequence diagram's, as much of the source as
        // that diagram says it takes. As much of it as is read is what was written, and the rest is the line's.
        var end = C4Parser.Heads(source[from..row.End]) is null ? SequenceParser.Ended(source, from) : C4Parser.Reach(source, from, row);
        var written = source[from..end];
        var said = C4Parser.Own(written, from) ?? SequenceParser.Statement(written, from) ?? ContentNode.Leaf(MermaidKinds.Statement, written, offset: from);

        return MermaidParser.StatementLine(source, row.Start, from, said, end, lines);
    }
}
