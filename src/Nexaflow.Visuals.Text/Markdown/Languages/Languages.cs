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
using Nexaflow.Markdown.Prose;
using Nexaflow.Markdown.WordCloud;
using Nexaflow.Markdown.Barcode;
using Nexaflow.Markdown.Barcode.Stages;
using Nexaflow.Markdown.Matrix.Aztec.Stages;
using Nexaflow.Markdown.Matrix.DataMatrix.Stages;
using Nexaflow.Markdown.Matrix.Pdf417.Stages;
using Nexaflow.Markdown.Matrix.Qr.Stages;

using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Barcode;
using Nexaflow.Visuals.Text.Markdown.Chemistry;
using Nexaflow.Visuals.Text.Markdown.Code;
using Nexaflow.Visuals.Text.Markdown.Latex;
using Nexaflow.Visuals.Text.Markdown.Matrix;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Music;
using Nexaflow.Visuals.Text.Markdown.Music.Abc;
using Nexaflow.Visuals.Text.Markdown.Music.LilyPond;
using Nexaflow.Visuals.Text.Markdown.Nomnoml;
using Nexaflow.Visuals.Text.Markdown.Plot;
using Nexaflow.Visuals.Text.Markdown.Prose;
using Nexaflow.Visuals.Text.Markdown.Stages;
using Nexaflow.Visuals.Text.Markdown.WordCloud;
using Nexaflow.Markdown.WordCloud.Stages;
using System.Linq;
using Nexaflow.Syntax;

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
            new WithImages(show.Inputs.Pictures),
            new WithLinks(show.Inputs.Links),

            // Last of what is read, because a block is only the same as it was when everything worked out about it is too.
            show.Unchanged,

            // After it: a block shown as written is not what was read, and is never kept as if it were.
            show.Shown is { } zone ? new ShowBlocksAsWritten(zone, show.At, show.Reads) : null,
        ],
        Builder: typeof(MarkdownBuilder))
        {
            Writable = true,
            Editing = new EditedBy(MarkdownEdits.Instance),
        };

    /// <summary>
    /// <c>mermaid</c>: a block whose header names the diagram it is, held whole as written in that diagram's own language — which
    /// is what is drawn, nothing of this one being (<see cref="MermaidFenceParser"/>). A header naming no diagram is shown as
    /// written, the keyword marked.
    /// </summary>
    public static readonly ContentLanguage Mermaid = new(
        Reads: static word => "mermaid".Equals(word?.Trim(), StringComparison.OrdinalIgnoreCase),
        Parser: static () => static source => ContentParse.Of(MermaidFenceParser.Parse(source)),
        Stages: static (_, _) => [],
        Builder: typeof(MermaidFenceBuilder))
        {
            // Whoever writes in the block writes in the diagram, which is laid out as this is — and whose own handler is told the edit.
            Writable = true,
        };

    /// <summary>
    /// Every diagram, each a language of its own, answering to the words its header is written with. They read their lines with the
    /// same parser and are worked over by the stages the header names — which is sharing, not being one language: each is drawn by
    /// its own builder, and says what an edit means through its own handler.
    /// </summary>
    public static readonly IReadOnlyList<ContentLanguage> Diagrams =
    [
        .. Enum.GetValues<MermaidDiagram>()
               .Where(diagram => MermaidBuilders.For(diagram) is not null)
               .Select(diagram => Diagram(diagram, MermaidBuilders.For(diagram)!)),
    ];

    /// <summary><paramref name="diagram"/>, drawn by <paramref name="builder"/>.</summary>
    private static ContentLanguage Diagram(MermaidDiagram diagram, Type builder) => new(
        Reads: word => !string.IsNullOrWhiteSpace(word) && MermaidDiagrams.Named(word.Trim()) == diagram,
        Parser: static () => static source => ContentParse.Of(MermaidParser.Parse(source)),
        Stages: static (tree, show) => [.. MermaidPipeline.Of(tree, show.Writing), .. Hosted(show), WordPieces],
        Builder: builder)
        {
            Writable = true,
            Editing = DiagramEdits.For(diagram) is IContentLanguage own ? own : new EditedBy(DiagramEdits.For(diagram)),
            Bind = MermaidParser.Bind,
            SafeFormatText = MermaidParser.SafeFormatText,
        };

    /// <summary>
    /// UML class notation written its own way — <see href="https://www.nomnoml.com/"/> — read by a parser of its own and drawn
    /// as the class diagram it describes. Nothing in it is worked out over the whole block: every line says what it means.
    /// </summary>
    public static readonly ContentLanguage Nomnoml = new(
        Reads: static word => "nomnoml".Equals(word?.Trim(), StringComparison.OrdinalIgnoreCase),
        Parser: static () => static source => ContentParse.Of(NomnomlParser.Parse(source)),
        Stages: static (_, _) => [],
        Builder: typeof(NomnomlBuilder));

    /// <summary>What a host puts between reading a diagram and drawing it: what its words are bound against, and the pictures it names.</summary>
    private static IEnumerable<IAstStage?> Hosted(ContentShowing show) =>
    [
        show.Inputs.Data is { } data ? new WithBindings(data) : null,
        new WithDiagramPictures(show.Inputs.Pictures),
    ];

    /// <summary>A QR symbol — <see href="https://markdown.org/tools/diagrams/qr/"/>.</summary>
    public static readonly ContentLanguage Qr = Symbol("qr",
        static word => "qr".Equals(word?.Trim(), StringComparison.OrdinalIgnoreCase),
        new EncodeQr());

    /// <summary>An Aztec symbol.</summary>
    public static readonly ContentLanguage Aztec = Symbol("aztec",
        static word => word?.Trim().ToLowerInvariant() is "aztec" or "aztec-code",
        new EncodeAztec());

    /// <summary>A Data Matrix symbol.</summary>
    public static readonly ContentLanguage DataMatrix = Symbol("datamatrix",
        static word => word?.Trim().ToLowerInvariant() is "datamatrix" or "data-matrix",
        new EncodeDataMatrix());

    /// <summary>A PDF417 symbol.</summary>
    public static readonly ContentLanguage Pdf417 = Symbol("pdf417",
        static word => "pdf417".Equals(word?.Trim(), StringComparison.OrdinalIgnoreCase),
        new EncodePdf417());

    /// <summary>A 2D code, <paramref name="named"/>: read as fields, encoded by its own stage, and drawn as the symbol that comes to.</summary>
    private static ContentLanguage Symbol(string named, Func<string?, bool> reads, IAstStage encode) =>
        new(reads, () => source => ContentParse.Of(MatrixParser.Parse(source, named)), (_, _) => [encode], typeof(MatrixBuilder));

    /// <summary>A one-dimensional barcode, in whichever symbology the block names.</summary>
    public static readonly ContentLanguage Barcode = new(
        Reads: static word => "barcode".Equals(word?.Trim(), StringComparison.OrdinalIgnoreCase),
        Parser: static () => static source => ContentParse.Of(BarcodeParser.Parse(source)),
        Stages: static (_, show) => show.Writing ? [new HoldValue(), new EncodeBarcode(writing: true)] : [new EncodeBarcode(writing: false)],
        Builder: typeof(BarcodeBuilder))
        {
            Writable = true,
        };

    /// <summary>A chemical structure written as SMILES.</summary>
    public static readonly ContentLanguage Smiles = new(
        Reads: static word => "smiles".Equals(word?.Trim(), StringComparison.OrdinalIgnoreCase),
        Parser: static () => static source => ContentParse.Of(SmilesParser.Parse(source)),
        Stages: static (_, _) => SmilesPipeline.Of().Stages,
        Builder: typeof(SmilesBuilder));

    /// <summary>A formula, written in LaTeX — on a line of its own, or in the middle of a sentence.</summary>
    public static readonly ContentLanguage Latex = new(
        Reads: static word => word?.Trim().ToLowerInvariant() is "latex" or "math" or "tex",
        Parser: static () => static source => ContentParse.Of(TexParser.Parse(source)),
        Stages: static (tree, show) => TexPipeline.Of(Editing(show.Own(tree.Width)), holes: show.Writing).Stages,
        Builder: typeof(LatexBuilder))
        {
            Writable = true,
            Editing = new EditedBy(LatexEdits.Instance),
        };

    /// <summary>A tune written in ABC.</summary>
    public static readonly ContentLanguage Abc = new(
        Reads: static word => MusicDialectExtensions.FromTag(word ?? string.Empty) == MusicDialect.Abc,
        Parser: static () => static source => ContentParse.Of(AbcParser.Parse(source)),
        Stages: static (tree, show) => AbcPipeline.Of(Editing(show.Own(tree.Width))).Stages,
        Builder: typeof(AbcBuilder));

    /// <summary>A tune written in LilyPond.</summary>
    public static readonly ContentLanguage LilyPond = new(
        Reads: static word => MusicDialectExtensions.FromTag(word ?? string.Empty) == MusicDialect.LilyPond,
        Parser: static () => static source => ContentParse.Of(LilyPondParser.Parse(source)),
        Stages: static (tree, show) => LilyPondPipeline.Of(Editing(show.Own(tree.Width))).Stages,
        Builder: typeof(LilyPondBuilder));

    /// <summary>The stretch being written in, as a pipeline that shows it as typed is told it — or null where there is none to show.</summary>
    private static (int Start, int Length)? Editing(RawZone? zone) =>
        zone is { Length: > 0 } shown ? (shown.Start, shown.Length) : null;

    /// <summary>A table of values against a pair of axes, drawn the way its fence names.</summary>
    public static ContentLanguage Plot(PlotFence fence) => new(
        Reads: word => PlotFences.Named(word ?? string.Empty) == fence,
        Parser: () => source => ContentParse.Of(PlotParser.Parse(source, fence.ToString().ToLowerInvariant())),
        Stages: (_, _) => PlotPipeline.Of(fence).Stages,
        Builder: typeof(PlotBuilder));

    /// <summary>A cloud of words sized by how often each is said.</summary>
    public static readonly ContentLanguage WordCloud = new(
        Reads: static word => "wordcloud".Equals(word?.Trim(), StringComparison.OrdinalIgnoreCase),
        Parser: static () => static source => ContentParse.Of(WordCloudParser.Parse(source)),
        Stages: static (_, show) => [new WordCloud.Stages.WithPictures(show.Inputs.Pictures), new ResolveCloud(), new ResolveWords()],
        Builder: typeof(WordCloudBuilder));

    /// <summary>What code shows and offers, in any grammar.</summary>
    private static readonly IContentLanguage CodeEdits = new CodeEditing();

    /// <summary>Code in a language no grammar reads: the same drawing, with nothing named — what a fence nothing reads is shown as.</summary>
    public static readonly ContentLanguage Code = Coded(null);

    /// <summary>
    /// Code in every grammar there is, each a language of its own, answering to the words that name it. Drawn at once as written,
    /// and coloured once its grammar has read it, which is never on the way to drawing.
    /// </summary>
    public static readonly IReadOnlyList<ContentLanguage> Codes = [.. TreeSitterLanguages.Grammars.Select(grammar => Coded(grammar))];

    /// <summary>Code in <paramref name="grammar"/>, or in none: read as written first, and by the grammar second (<see cref="CodeSpans"/>).</summary>
    private static ContentLanguage Coded(string? grammar) => new(
        Reads: word => grammar is not null && string.Equals(CodeGrammars.For(word), grammar, StringComparison.OrdinalIgnoreCase),
        Parser: () => source => ContentParse.Of(CodeParser.Parse(source, grammar ?? string.Empty)),
        Stages: static (_, _) => [new CodeLines()],
        Builder: typeof(CodeBuilder))
        {
            Editing = CodeEdits,
            SlowParser = grammar is null ? null : source => ContentParse.Of(CodeParser.Parse(source, grammar, CodeSpans.Read(grammar, source))),
        };
}

/// <summary>A language whose edits are told to <paramref name="onEdit"/> — or, where that is null, one with nothing of its own to say about an edit.</summary>
internal sealed class EditedBy(IOnEdit? onEdit) : IContentLanguage
{
    public IOnEdit? OnEdit => onEdit;
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
