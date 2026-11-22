using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Binding;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid.Flowchart;

namespace Nexaflow.Markdown.Mermaid.Swimlane;

/// <summary>
/// Reads a swimlane — a <c>swimlane-beta</c> block, which is a flowchart laid out in lanes: its top-level subgraphs are the lanes, and
/// everything else about it is a flowchart's — and says how what is written into one is spelled.
///
/// <para>
/// A diagram is a language of its own, written in Mermaid's syntax — a <c>mermaid</c> fence says only which
/// (<see cref="MermaidFenceParser"/>) — so this reads its own source rather than being handed rows of it. What a fence holds round
/// any diagram — the front matter, an <c>accTitle</c>, a <c>%%</c> comment, a directive, a binding, the space between lines — is
/// read with the shared pieces (<see cref="MermaidParser"/>). The header is optional, since a <c>swimlane</c> fence already says
/// what the block is. Mermaid reads a swimlane's lines as a flowchart's, so this reads them with the flowchart's pieces
/// (<see cref="FlowchartParser"/>) — its header, every statement, and where a quoted label carries a statement past its line.
/// </para>
/// </summary>
public sealed class SwimlaneParser : ITranspile
{
    private SwimlaneParser()
    {
    }

    /// <summary>The language a swimlane is written in: what a fence names it by.</summary>
    public const string Language = "swimlane";

    /// <summary>The words a swimlane's header is written with — Mermaid's own, and the language's name.</summary>
    public static readonly string[] Keywords = ["swimlane-beta", Language];

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

    /// <summary>Whether a hole stands in <paramref name="node"/> while somebody writes in the block: where one would in a flowchart.</summary>
    public static bool Holds(ContentNode? holder, ContentNode node) => FlowchartParser.Holds(holder, node);

    /// <inheritdoc/>
    /// <remarks>Spelled as a flowchart spells it (<see cref="FlowchartParser.Rewrite"/>).</remarks>
    public static ContentChange? Rewrite(ContentChange change) => FlowchartParser.Rewrite(change);

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

        // A binding standing on a line of its own after the header is lines supplied by whatever the swimlane is shown against. Held
        // as written; read into its place before the swimlane is worked over.
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

            // A header may end at a ; with the chart's first statement after it on its row, as Mermaid lets a chart be written on one line.
            var cut = text.IndexOf(';');
            var head = cut < 0 ? text : text[..(cut + 1)];

            if (FlowchartParser.Heading(head, from, Keywords) is { } header)
                return MermaidParser.StatementLine(source, row.Start, from, header, from + head.Length, lines);
        }

        if (MermaidParser.Accessibility(source, row, from, to, lines) is { } stop) return stop;

        // The statement, which is as much of the source as it takes rather than as much as the row holds — a quoted label, or
        // metadata in braces, carries it past its row, and a ; ends it with the next one after it on the row.
        var end = FlowchartParser.Ended(source, from);
        var said = FlowchartParser.Statement(source[from..end], from) ?? ContentNode.Leaf(MermaidKinds.Statement, source[from..end], offset: from);

        return MermaidParser.StatementLine(source, row.Start, from, said, end, lines);
    }
}
