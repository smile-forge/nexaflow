using System;
using System.Collections.Generic;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Chemistry;
using Nexaflow.Markdown.Latex;
using Nexaflow.Markdown.Matrix;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Music.Abc;
using Nexaflow.Markdown.Music.LilyPond;
using Nexaflow.Markdown.Nomnoml;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Pipeline.Stages;
using Nexaflow.Markdown.Plot;
using Nexaflow.Markdown.Plot.Stages;
using Nexaflow.Markdown.Prose;
using Nexaflow.Markdown.WordCloud;
using Nexaflow.Markdown.Barcode;

using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Barcode;
using Nexaflow.Visuals.Text.Markdown.Chemistry;
using Nexaflow.Visuals.Text.Markdown.Code;
using Nexaflow.Visuals.Text.Markdown.Latex;
using Nexaflow.Visuals.Text.Markdown.Matrix.Aztec;
using Nexaflow.Visuals.Text.Markdown.Matrix.DataMatrix;
using Nexaflow.Visuals.Text.Markdown.Matrix.Pdf417;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Music;
using Nexaflow.Visuals.Text.Markdown.Music.Abc;
using Nexaflow.Visuals.Text.Markdown.Music.LilyPond;
using Nexaflow.Visuals.Text.Markdown.Nomnoml;
using Nexaflow.Visuals.Text.Markdown.Plot;
using Nexaflow.Visuals.Text.Markdown.Prose;
using Nexaflow.Visuals.Text.Markdown.Qr;
using Nexaflow.Visuals.Text.Markdown.Stages;
using Nexaflow.Visuals.Text.Markdown.WordCloud;

namespace Nexaflow.Visuals.Text.Markdown.Languages;

/// <summary>
/// Every language that ships, each described: what parses it, what its parse is worked over by, and what lays it out. The
/// engine runs them (<see cref="ContentEngine"/>); nothing here runs anything.
/// </summary>
internal static class Shipped
{
    /// <summary>What every Mermaid block is worked over by once all else has been: what its words are made of.</summary>
    private static readonly WithWordPieces WordPieces = new();

    /// <summary>
    /// A document. Named by no fence: it is what content is written in where nothing says otherwise.
    /// </summary>
    public static readonly ContentLanguage Markdown = new(
        Reads: static _ => false,
        Parser: MarkdownParser.Parsing,
        Stages: static (_, show) =>
        [
            new WithImages(show.Options?.Pictures),
            new WithLinks(show.Options?.Links),

            // Last of what is read, because a block is only the same as it was when everything worked out about it is too.
            show.Unchanged,

            // After it: a block shown as written is not what was read, and is never kept as if it were.
            show.Shown is { } zone ? new ShowBlocksAsWritten(zone, show.At, show.Reads) : null,
        ],
        Builder: static (reading, show) =>
            new MarkdownBuilder(reading, new EditState(reading.Source, 0, null, show.Shown), show.Style, !show.Writing, show.Nesting));

    /// <summary>
    /// Every kind of diagram Mermaid names, which all arrive under the one fence word. The block's header names the diagram,
    /// and so its stages and its builder; a header naming none is shown as written, with the reason.
    /// </summary>
    public static readonly ContentLanguage Mermaid = new(
        Reads: static word => "mermaid".Equals(word?.Trim(), StringComparison.OrdinalIgnoreCase),
        Parser: static () => static source => ContentParse.Of(MermaidParser.Parse(source)),
        Stages: static (tree, show) => [.. MermaidPipeline.Of(tree, show.Writing), .. Hosted(show), WordPieces],
        Builder: static (reading, show) =>
            (MermaidBuilders.For(MermaidBlock.Of(reading).Diagram) ?? MermaidBuilders.Unknown)(
                    reading, EditState.For(reading.Source) with { Raw = show.Shown }, show.Style, !show.Writing, show.Nesting))
        {
            Editing = new MermaidEditing(),
        };

    /// <summary>
    /// UML class notation written its own way — <see href="https://www.nomnoml.com/"/> — read by a parser of its own and drawn
    /// as the class diagram it describes. Nothing in it is worked out over the whole block: every line says what it means.
    /// </summary>
    public static readonly ContentLanguage Nomnoml = new(
        Reads: static word => "nomnoml".Equals(word?.Trim(), StringComparison.OrdinalIgnoreCase),
        Parser: static () => static source => ContentParse.Of(NomnomlParser.Parse(source)),
        Stages: static (_, _) => [],
        Builder: static (reading, show) => new NomnomlBuilder(reading, EditState.For(reading.Source), show.Style, true, show.Nesting));

    /// <summary>What a host puts between reading a diagram and drawing it: what its words are bound against, and the pictures it names.</summary>
    private static IEnumerable<IAstStage?> Hosted(ContentShowing show) =>
    [
        show.Options?.DataContext is { } data ? new WithBindings(data) : null,
        new WithDiagramPictures(show.Options?.Pictures),
    ];

    /// <summary>A QR symbol — <see href="https://markdown.org/tools/diagrams/qr/"/>.</summary>
    public static readonly ContentLanguage Qr = Symbol(
        static word => "qr".Equals(word?.Trim(), StringComparison.OrdinalIgnoreCase),
        static (reading, show) => new QrBuilder(reading, EditState.For(reading.Source), show.Style, true, show.Nesting));

    /// <summary>An Aztec symbol.</summary>
    public static readonly ContentLanguage Aztec = Symbol(
        static word => word?.Trim().ToLowerInvariant() is "aztec" or "aztec-code",
        static (reading, show) => new AztecBuilder(reading, EditState.For(reading.Source), show.Style, true, show.Nesting));

    /// <summary>A Data Matrix symbol.</summary>
    public static readonly ContentLanguage DataMatrix = Symbol(
        static word => word?.Trim().ToLowerInvariant() is "datamatrix" or "data-matrix",
        static (reading, show) => new DataMatrixBuilder(reading, EditState.For(reading.Source), show.Style, true, show.Nesting));

    /// <summary>A PDF417 symbol.</summary>
    public static readonly ContentLanguage Pdf417 = Symbol(
        static word => "pdf417".Equals(word?.Trim(), StringComparison.OrdinalIgnoreCase),
        static (reading, show) => new Pdf417Builder(reading, EditState.For(reading.Source), show.Style, true, show.Nesting));

    /// <summary>A two-dimensional code: a <c>key: value</c> field a line, drawn by the builder its fence names.</summary>
    private static ContentLanguage Symbol(Func<string?, bool> reads, Func<ContentReading, ContentShowing, ContentBuilder> builder) =>
        new(reads, static () => static source => ContentParse.Of(MatrixParser.Parse(source)), static (_, _) => [], builder);

    /// <summary>A one-dimensional barcode, in whichever symbology the block names.</summary>
    public static readonly ContentLanguage Barcode = new(
        Reads: static word => "barcode".Equals(word?.Trim(), StringComparison.OrdinalIgnoreCase),
        Parser: static () => static source => ContentParse.Of(BarcodeParser.Parse(source)),
        Stages: static (_, show) => BarcodeParser.Stages(holes: show.Writing),
        Builder: static (reading, show) => new BarcodeBuilder(reading, EditState.For(reading.Source), show.Style, !show.Writing, show.Nesting));

    /// <summary>A chemical structure written as SMILES.</summary>
    public static readonly ContentLanguage Smiles = new(
        Reads: static word => "smiles".Equals(word?.Trim(), StringComparison.OrdinalIgnoreCase),
        Parser: static () => static source => ContentParse.Of(SmilesParser.Parse(source)),
        Stages: static (_, _) => SmilesPipeline.Of().Stages,
        Builder: static (reading, show) => new SmilesBuilder(reading, EditState.For(reading.Source), show.Style, true, show.Nesting));

    /// <summary>A formula, written in LaTeX — on a line of its own, or in the middle of a sentence.</summary>
    public static readonly ContentLanguage Latex = new(
        Reads: static word => word?.Trim().ToLowerInvariant() is "latex" or "math" or "tex",
        Parser: static () => static source => ContentParse.Of(TexParser.Parse(source)),
        Stages: static (tree, show) => TexPipeline.Of(LatexBuilder.Draws, Editing(show.Own(tree.Width)), holes: show.Writing).Stages,
        Builder: static (reading, show) =>
                new LatexBuilder(reading, new EditState(reading.Source, 0, null, show.Own(reading.Source.Length)), show.Style, !show.Writing, show.Nesting))
        {
            Editing = new LatexEditing(),
        };

    /// <summary>A tune written in ABC.</summary>
    public static readonly ContentLanguage Abc = new(
        Reads: static word => MusicDialectExtensions.FromTag(word ?? string.Empty) == MusicDialect.Abc,
        Parser: static () => static source => ContentParse.Of(AbcParser.Parse(source)),
        Stages: static (tree, show) => AbcPipeline.Of(AbcBuilder.Draws, Editing(show.Own(tree.Width))).Stages,
        Builder: static (reading, show) => new AbcBuilder(reading, EditState.For(reading.Source), show.Style, true, show.Nesting));

    /// <summary>A tune written in LilyPond.</summary>
    public static readonly ContentLanguage LilyPond = new(
        Reads: static word => MusicDialectExtensions.FromTag(word ?? string.Empty) == MusicDialect.LilyPond,
        Parser: static () => static source => ContentParse.Of(LilyPondParser.Parse(source)),
        Stages: static (tree, show) => LilyPondPipeline.Of(Editing(show.Own(tree.Width))).Stages,
        Builder: static (reading, show) => new LilyPondBuilder(reading, EditState.For(reading.Source), show.Style, true, show.Nesting));

    /// <summary>The stretch being written in, as a pipeline that shows it as typed is told it — or null where there is none to show.</summary>
    private static (int Start, int Length)? Editing(RawZone? zone) =>
        zone is { Length: > 0 } shown ? (shown.Start, shown.Length) : null;

    /// <summary>A table of values against a pair of axes, drawn the way its fence names.</summary>
    public static ContentLanguage Plot(PlotFence fence) => new(
        Reads: word => PlotFences.Named(word ?? string.Empty) == fence,
        Parser: static () => static source => ContentParse.Of(PlotParser.Parse(source)),
        Stages: (_, _) => [new ResolveSettings(fence)],
        Builder: static (reading, show) => new PlotBuilder(reading, EditState.For(reading.Source), show.Style, true, show.Nesting));

    /// <summary>A cloud of words sized by how often each is said.</summary>
    public static readonly ContentLanguage WordCloud = new(
        Reads: static word => "wordcloud".Equals(word?.Trim(), StringComparison.OrdinalIgnoreCase),
        Parser: static () => static source => ContentParse.Of(WordCloudParser.Parse(source)),
        Stages: static (_, show) => [new WordCloud.Stages.WithPictures(show.Options?.Pictures)],
        Builder: static (reading, show) => new WordCloudBuilder(reading, EditState.For(reading.Source), show.Style, true, show.Nesting));

    /// <summary>
    /// Code, in any language a grammar reads — and in any language it does not, which is the same drawing with nothing named.
    /// Coloured where the grammar has read it already, and plain until it has.
    /// </summary>
    public static readonly ContentLanguage Code = new(
        Reads: static word => CodeGrammars.For(word) is not null,
        Parser: static () => static source => ContentParse.Of(CodeParser.Parse(source)),
        Stages: static (_, show) => [new WithHighlights(CodeGrammars.For(show.Named)), new CodeLines()],
            Builder: static (reading, show) => new CodeBuilder(reading, EditState.For(reading.Source), show.Style, true, show.Nesting))
        {
            Editing = new CodeEditing(),
        };
}

/// <summary>What an edit means in a Mermaid diagram.</summary>
internal sealed class MermaidEditing : IContentLanguage
{
    public IOnEdit OnEdit => MermaidEdits.Instance;
}

/// <summary>What an edit means in a formula.</summary>
internal sealed class LatexEditing : IContentLanguage
{
    public IOnEdit OnEdit => LatexEdits.Instance;
}

/// <summary>What code shows and offers: every character a writer typed, and no picture of it.</summary>
internal sealed class CodeEditing : IContentLanguage
{
    /// <summary>Colouring code is still showing it: every character a writer typed is on the page.</summary>
    public bool ShowsWhatWasWritten => true;

    /// <summary>
    /// A picture of code is a worse copy of the code — it cannot be searched, pasted or read by anything — so that is the one
    /// usual button this does not offer.
    /// </summary>
    public BlockCorner Corner(ContentAsk ask) => new(Saves: false);
}
