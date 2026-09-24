using System;
using System.Windows;

using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Plot;

using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Barcode;
using Nexaflow.Visuals.Text.Markdown.Chemistry;
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
using Nexaflow.Visuals.Text.Markdown.Qr;
using Nexaflow.Visuals.Text.Markdown.WordCloud;
using ContentElement = Nexaflow.Visuals.Text.Editing.ContentElement;
using Nexaflow.Markdown.Nomnoml;
using Nexaflow.Visuals.Text.Markdown.Code;

namespace Nexaflow.Visuals.Text.Markdown.Languages;

/// <summary>Every kind of diagram Mermaid names, which all arrive under the one fence word.</summary>
/// <remarks>
/// The block is read once and the diagram its header names chooses the builder. A header naming no type
/// falls to <see cref="UnknownDiagramBuilder"/>, which shows the block as written with the reason —
/// so a mermaid fence always draws something.
/// </remarks>
public sealed class MermaidLanguage : IContentLanguage
{
    public bool Reads(string? language) => "mermaid".Equals(language?.Trim(), StringComparison.OrdinalIgnoreCase);

    public Laid? Lay(ContentRequest request) =>
        MermaidBuilders.Lay(request.Source, request.Style, request.Room, writing: !request.IsReadOnly,
                            at: request.At, options: request.Options, shown: request.Shown);

    public IOnEdit OnEdit => MermaidEdits.Instance;
}

/// <summary>UML class notation written shorter — <see href="https://www.nomnoml.com/"/>.</summary>
public sealed class NomnomlLanguage : IContentLanguage
{
    /// <summary>The fence word this answers to.</summary>
    public const string Name = "nomnoml";

    public bool Reads(string? language) => Name.Equals(language?.Trim(), StringComparison.OrdinalIgnoreCase);

    public Laid? Lay(ContentRequest request) =>
        MermaidBuilders.Lay(static (r, s, f, o) => new NomnomlBuilder(r, s, f, o), NomnomlDiagram.Grammar,
                            request.Source, request.Style, request.Room, request.At, request.Options);
}

/// <summary>A QR symbol — <see href="https://markdown.org/tools/diagrams/qr/"/>.</summary>
public sealed class QrLanguage : IContentLanguage
{
    public bool Reads(string? language) => "qr".Equals(language?.Trim(), StringComparison.OrdinalIgnoreCase);

    public Laid? Lay(ContentRequest request) => QrBuilder.Lay(request.Source, request.Style, request.At);
}

/// <summary>An Aztec symbol.</summary>
public sealed class AztecLanguage : IContentLanguage
{
    public bool Reads(string? language) =>
        language?.Trim().ToLowerInvariant() is "aztec" or "aztec-code";

    public Laid? Lay(ContentRequest request) => AztecBuilder.Lay(request.Source, request.Style, request.At);
}

/// <summary>A Data Matrix symbol.</summary>
public sealed class DataMatrixLanguage : IContentLanguage
{
    public bool Reads(string? language) =>
        language?.Trim().ToLowerInvariant() is "datamatrix" or "data-matrix";

    public Laid? Lay(ContentRequest request) => DataMatrixBuilder.Lay(request.Source, request.Style, request.At);
}

/// <summary>A PDF417 symbol.</summary>
public sealed class Pdf417Language : IContentLanguage
{
    public bool Reads(string? language) => "pdf417".Equals(language?.Trim(), StringComparison.OrdinalIgnoreCase);

    public Laid? Lay(ContentRequest request) => Pdf417Builder.Lay(request.Source, request.Style, request.At);
}

/// <summary>A chemical structure written as SMILES.</summary>
public sealed class SmilesLanguage : IContentLanguage
{
    public bool Reads(string? language) => "smiles".Equals(language?.Trim(), StringComparison.OrdinalIgnoreCase);

    public Laid? Lay(ContentRequest request) =>
        SmilesBuilder.Lay(request.Source, request.Style, request.Room, request.At);
}

/// <summary>A formula, written in LaTeX — on a line of its own, or in the middle of a sentence.</summary>
public sealed class LatexLanguage : IContentLanguage
{
    public bool Reads(string? language) => language?.Trim().ToLowerInvariant() is "latex" or "math" or "tex";

    public Laid? Lay(ContentRequest request) =>
        LatexBuilder.Lay(request.Source, request.Style,
                         shownAsWritten: request.Shown is { } shown ? new RawZone(shown.Start - request.At, shown.End - request.At) : null,
                         placeholders: !request.IsReadOnly,
                         block: double.IsFinite(request.Room) ? request.Room : 0,
                         at: request.At);

    public IOnEdit OnEdit => LatexEdits.Instance;
}

/// <summary>
/// A tune, in whichever notation it was written — ABC or LilyPond, which decides only which engraver reads it.
///
/// <para>
/// Given a page, the music takes a share of its width and sits in the middle of it, the block being the whole width with
/// the margins inside. A score set edge to edge across a wide window is a score nobody can read: the eye has to travel
/// the whole width to follow one system, and the systems stop looking like lines of music. Printed music has margins for
/// the same reason prose does. What fills the share is the engraver's; how wide the share is belongs to the page.
/// </para>
/// </summary>
public sealed class MusicLanguage(MusicDialect dialect) : IContentLanguage
{
    /// <summary>How much of a page's width the music takes.</summary>
    public const double PageWidth = 0.8;

    /// <summary>A width to engrave against where there is no page, since a score set to infinity has nowhere to break.</summary>
    private const double Unbounded = 420;

    public bool Reads(string? language) => MusicDialectExtensions.FromTag(language ?? string.Empty) == dialect;

    public Laid? Lay(ContentRequest request)
    {
        if (!double.IsFinite(request.Room) || request.Room <= 0) return Engraved(request, Unbounded);

        if (Engraved(request, request.Room * PageWidth) is not { } score) return null;

        var page = new LayoutBuilder();
        var width = Math.Max(request.Room, score.Size.Width);

        new ContentInset(score).Set(page, new Point((width - score.Size.Width) / 2, 0), MusicPiece.Page);

        return new Laid(page.Seal(), new Size(width, score.Size.Height), score.Trouble);
    }

    private Laid? Engraved(ContentRequest request, double room) =>
        dialect == MusicDialect.LilyPond
            ? LilyPondBuilder.Lay(request.Source, room, request.Style, at: request.At)
            : AbcBuilder.Lay(request.Source, room, request.Style, at: request.At);
}

/// <summary>The kinds of piece a score's page is made of, round what its engraver drew.</summary>
public static class MusicPiece
{
    /// <summary>The whole width a score was given, the music set in the middle of it.</summary>
    public const string Page = "MusicPage";
}

/// <summary>A table of values against a pair of axes.</summary>
public sealed class PlotLanguage(PlotFence fence) : IContentLanguage
{
    public bool Reads(string? language) => PlotFences.Named(language ?? string.Empty) == fence;

    public Laid? Lay(ContentRequest request) =>
        PlotBuilder.Build(request.Source, fence, request.Style, Panel(request.Room), request.At);

    /// <summary>A width to plot against, since a panel given infinity has no axis to scale.</summary>
    private static double Panel(double room) =>
        double.IsFinite(room) && room > 0 ? Math.Min(room, 560) : 560;
}

/// <summary>A cloud of words sized by how often each is said.</summary>
public sealed class WordCloudLanguage : IContentLanguage
{
    public bool Reads(string? language) => "wordcloud".Equals(language?.Trim(), StringComparison.OrdinalIgnoreCase);

    public Laid? Lay(ContentRequest request) =>
        WordCloudBuilder.Lay(request.Source, request.Style, request.Room, at: request.At);
}

/// <summary>A one-dimensional barcode, in whichever symbology the block names.</summary>
public sealed class BarcodeLanguage : IContentLanguage
{
    public bool Reads(string? language) => "barcode".Equals(language?.Trim(), StringComparison.OrdinalIgnoreCase);

    public Laid? Lay(ContentRequest request)
    {
        // A block that will not read has no symbol in it to draw, and saying so is the caller's: what it puts
        // there instead is the characters somebody typed, which is the only thing left to fix.
        if (!BarcodeBlockParser.TryParse(request.Source, out var block, out _)) return null;

        return BarcodeBuilder.Build(block!.At(block.ValueStart + request.At), request.Style);
    }
}

/// <summary>
/// Code, in any language a grammar reads — and in any language it does not, which is the same drawing with
/// nothing named.
///
/// <para>
/// Last in the table on purpose: it answers to a great many words, and a fence calling itself something a
/// real language already claims should reach that language. It is asked only once nothing else has.
/// </para>
/// </summary>
public sealed class CodeLanguage : IContentLanguage
{
    public bool Reads(string? language) => CodeGrammars.For(language) is not null;

    /// <summary>Colouring code is still showing it: every character a writer typed is on the page.</summary>
    public bool ShowsWhatWasWritten => true;

    /// <summary>
    /// A picture of code is a worse copy of the code — it cannot be searched, pasted or read by anything —
    /// so that is the one usual button this does not offer.
    /// </summary>
    public BlockCorner Corner(ContentAsk ask) => new(Saves: false);

    public Laid? Lay(ContentRequest request) =>
        CodeBuilder.Lay(request.Source, CodeGrammars.For(request.Named), request.Style, request.Room, request.At);
}
