using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid.Pie.Stages;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Pie;

/// <summary>
/// What a <c>pie</c> block says beyond the lines every diagram shares: <c>showData</c> and a title after the keyword,
/// a <c>title</c> of its own, and a slice per line — a label in quotes, a colon, and a number.
///
/// <para>
/// The rules are Mermaid's: a value is a number greater than nought, and slices are drawn clockwise in the order they
/// are written. A value that is neither is still read as a slice, with the reason on the number — the label is what
/// the reader is looking at, and hiding the whole line because its value is wrong would leave nothing to fix. A value
/// not yet written is no complaint at all: it is a slice still being written.
/// </para>
/// </summary>
public sealed class PieGrammar : IMermaidGrammar
{
    /// <summary>The word after <c>pie</c> that puts each slice's value in the legend.</summary>
    public const string ShowData = "showData";

    /// <inheritdoc/>
    public ContentNode? Header(string arguments)
    {
        var line = MermaidLine.Of(arguments, comments: false);
        if (line.Word(ShowData, PieKinds.ShowData)) line.Space();

        if (!line.Done && !line.Then(rest => MermaidLine.Of(rest, comments: false).Title()))
            line.Held("A pie takes showData and a title after it: pie showData title Key elements.");

        return line.Empty ? null : line.Read(PieKinds.Options, MermaidRoles.Arguments);
    }

    /// <inheritdoc/>
    public ContentNode? Statement(string text)
    {
        var line = MermaidLine.Of(text);
        return MermaidLine.Keyword(line.Written, MermaidLine.TitleWord) is not null ? line.Title() : Slice(line);
    }

    /// <inheritdoc/>
    /// <remarks>A slice with neither its label nor its value written, the caret between its quotes — whatever it follows.</remarks>
    public (string Text, int Caret)? Blank(ContentNode? above) => ("\"\" : ", 1);

    /// <inheritdoc/>
    /// <remarks>A label holds anything but a quote, which is written <c>#quot;</c>.</remarks>
    public MermaidWriting? Escaping(ContentPart part, int caret, string text) => MermaidWriting.Escape(part, caret, text);

    /// <inheritdoc/>
    /// <remarks>
    /// What a slice is drawn in is written in the front matter by position rather than on the slice, so it is worked out and
    /// hung underneath it (<see cref="ResolveSlices"/>). Everything else a pie says, it says in its own characters.
    /// </remarks>
    public IEnumerable<IAstStage> Stages(MermaidBlock block, bool writing) =>
        [new ResolveSlices(PieConfig.Read(block.Config)), new ResolveShares(writing)];

    /// <inheritdoc/>
    /// <remarks>Between a label's quotes, and after a slice's colon.</remarks>
    public bool Holds(ContentNode? holder, ContentNode node) => node.Kind is MermaidKinds.Quoted or MermaidKinds.Amount;

    /// <summary>
    /// One slice: <c>"Calcium" : 42.96</c> — or <c>"Calcium" : </c>, still to be given its value, which stands after the
    /// space left for it: that is where typing it will put it.
    /// </summary>
    private static ContentNode Slice(MermaidLine line)
    {
        if (!line.Quoted(PieRoles.Label, what: "label"))
            return line.Shown("A pie slice is a label in quotes, a colon and a number: \"Calcium\" : 42.96.");

        line.Space();
        if (!line.Token(":")) return line.Shown("A pie slice needs a colon between its label and its value.");

        line.Room();
        line.Amount(PieRoles.Value, MermaidNumber.Positive("A pie slice is worth more than nought, or it is not a slice."));
        return line.Read(PieKinds.Slice);
    }
}
