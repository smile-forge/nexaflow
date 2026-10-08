using System;
using System.Collections.Generic;

using System.Linq;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Binding;
using Nexaflow.Markdown.Editing;

namespace Nexaflow.Markdown.Mermaid.Flowchart;

/// <summary>
/// Reads a flowchart: a <c>flowchart</c> or a <c>graph</c> block, and a swimlane, which is the same syntax laid out in lanes.
///
/// <para>
/// A diagram is a language of its own — a <c>mermaid</c> fence says only which (<see cref="MermaidFenceParser"/>) — so this
/// reads its own source rather than being handed rows of it. What a fence holds around any diagram is the same whichever is
/// written in it: the front matter, the header, an <c>accTitle</c>, a <c>%%</c> comment, a directive, a binding, and the space
/// between lines. Those are read by the shared reader, because they are facts about a mermaid block rather than about a chart.
/// </para>
/// <para>
/// What the lines say is this one's, and is read from the characters rather than line by line. A line is where most statements
/// end, but it is not where every token does: a label written between quotes runs to its closing quote wherever that stands, so
/// a string written across two lines is one string rather than two halves with a boundary through it.
/// </para>
/// </summary>
public sealed class FlowchartParser : ITranspile
{
    private FlowchartParser()
    {
    }

    private static readonly FlowchartGrammar Chart = new();

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

        return new BlockNode(MermaidParser.Named(lines), lines, MermaidKinds.Block);
    }

    /// <inheritdoc/>
    public static ContentChange? Rewrite(ContentChange change) => MermaidParser.Rewrite(change, Spelled);

    /// <summary>
    /// A change as a chart spells it.
    ///
    /// <para>
    /// What a node is called is the one thing a chart spells differently from the rest of Mermaid: a link, a <c>class</c>, a
    /// <c>style</c> and a <c>click</c> line all name a node by it, and the characters a name may hold are the whole of what tells
    /// one name from the next. A character that would end a name is not written at all, because writing it would leave every line
    /// that named the node joining something that is not there.
    /// </para>
    /// </summary>
    private static string? Spelled(ContentPart part, string text) =>
        part.Role == FlowchartRoles.Id ? Fits(part, text) ? text : null
        : Quoted(part) ? MermaidText.Quoted(text.ReplaceLineEndings(MermaidParser.LineBreak))
        : MermaidParser.Spelled(part, text);

    /// <summary>
    /// Whether <paramref name="text"/> can go into what something is called.
    ///
    /// <para>
    /// A node's name ends at the first character that is not part of one, so none of those may go in at all. A subgraph's name is
    /// written in words, spaces and all — <c>subgraph Sales team</c> is one name — so a space is a letter of that one.
    /// </para>
    /// </summary>
    private static bool Fits(ContentPart part, string text) =>
        FlowchartGrammar.Subgraphed(part)
            ? text.All(letter => FlowchartGrammar.Bare(letter) || letter is ' ' or '\t')
            : text.All(FlowchartGrammar.Bare);

    /// <summary>
    /// Whether what is written here stands between quotes — which is what makes a quote in it the end of it, and a line ending the
    /// end of the line it was written on. Asked of what holds it as well as of itself, because the words between a pair of quotes
    /// are a run of their own inside them.
    /// </summary>
    private static bool Quoted(ContentPart part)
    {
        for (var at = part; at is not null; at = at.Parent)
            if (at.Kind == MermaidKinds.Quoted) return true;

        return false;
    }

    /// <summary>The next line of the block, read and added to <paramref name="lines"/>, and where the one after it starts.</summary>
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

        if (text.StartsWith("%%", StringComparison.Ordinal))
        {
            lines.Add(MermaidParser.Line(source, row.Start, from,
                                         [ContentNode.Leaf(Kinds.Comment, text, Roles.Trivia, offset: from)], to, row));
            return row.Stop;
        }

        // A binding standing on a line of its own after the header is lines of the chart supplied by whatever it is shown
        // against. Held as written; read into its place before the chart is worked over.
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
            lines.Add(MermaidParser.Line(source, row.Start, from,
                                         [MermaidParser.Header(text, from, new MermaidParser.Reading())], to, row));
            return row.Stop;
        }

        if (MermaidParser.Accessibility(source, row, from, to, lines) is { } stop) return stop;

        // The statement, which is as much of the source as it takes rather than as much as the row holds. As much of that as
        // the chart reads is what was written, and the rest is the line's.
        var end = Ended(source, from);
        var said = Chart.Statement(source[from..end], from);
        var took = said is null ? end : from + said.Width;
        var last = MermaidParser.Row.Containing(source, Math.Max(from, took - 1));

        lines.Add(MermaidParser.Line(source, row.Start, from,
                                     [said ?? ContentNode.Leaf(MermaidKinds.Statement, source[from..end], offset: from)], took, last));

        return last.Stop;
    }

    /// <summary>
    /// Where the statement starting at <paramref name="from"/> ends: the first line ending outside a quoted run, and the end of the
    /// block where none follows. The space after it belongs to the line rather than to the statement.
    ///
    /// <para>
    /// A line ending is where a statement ends, and a quote is where that stops being true — which is the whole of why a chart
    /// reads its own source. A quote nothing closes is a quote like any other character, so one typed by mistake costs the line it
    /// is on rather than everything written under it.
    /// </para>
    /// </summary>
    private static int Ended(string source, int from)
    {
        var end = source.Length;

        for (var at = from; at < source.Length; at++)
        {
            if (source[at] == '\n') { end = at; break; }
            if (source[at] != '"') continue;

            var close = source.IndexOf('"', at + 1);
            if (close >= 0) at = close;
        }

        while (end > from && char.IsWhiteSpace(source[end - 1])) end--;

        return end;
    }
}
