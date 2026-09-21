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
        MermaidBuilders.Lay(request.Source, request.Style, request.Room, at: request.At, options: request.Options)
        ?? UnknownDiagramBuilder.Lay(request.Source, request.Style, request.Room);

    public FrameworkElement Draw(string source, DiagramRenderOptions options) =>
        MermaidBuilders.Element(source, MermaidBlock.Read(source).Diagram, options)
        ?? UnknownDiagramBuilder.Element(source, options);
}

/// <summary>UML class notation written shorter — <see href="https://www.nomnoml.com/"/>.</summary>
public sealed class NomnomlLanguage : IContentLanguage
{
    /// <summary>The fence word this answers to.</summary>
    public const string Name = "nomnoml";

    public bool Reads(string? language) => Name.Equals(language?.Trim(), StringComparison.OrdinalIgnoreCase);

    public FrameworkElement Draw(string source, DiagramRenderOptions options) =>
        MermaidBuilder.Host(source, options, static (r, s, f, o) => new NomnomlBuilder(r, s, f, o),
                            options.ReadOnly, NomnomlDiagram.Grammar);
}

/// <summary>A QR symbol — <see href="https://markdown.org/tools/diagrams/qr/"/>.</summary>
public sealed class QrLanguage : IContentLanguage
{
    public bool Reads(string? language) => "qr".Equals(language?.Trim(), StringComparison.OrdinalIgnoreCase);

    public Laid? Lay(ContentRequest request) => QrBuilder.Lay(request.Source, request.Style);

    public FrameworkElement Draw(string source, DiagramRenderOptions options) => QrBuilder.Element(source, options);
}

/// <summary>An Aztec symbol.</summary>
public sealed class AztecLanguage : IContentLanguage
{
    public bool Reads(string? language) =>
        language?.Trim().ToLowerInvariant() is "aztec" or "aztec-code";

    public Laid? Lay(ContentRequest request) => AztecBuilder.Lay(request.Source, request.Style);

    public FrameworkElement Draw(string source, DiagramRenderOptions options) => AztecBuilder.Element(source, options);
}

/// <summary>A Data Matrix symbol.</summary>
public sealed class DataMatrixLanguage : IContentLanguage
{
    public bool Reads(string? language) =>
        language?.Trim().ToLowerInvariant() is "datamatrix" or "data-matrix";

    public Laid? Lay(ContentRequest request) => DataMatrixBuilder.Lay(request.Source, request.Style);

    public FrameworkElement Draw(string source, DiagramRenderOptions options) => DataMatrixBuilder.Element(source, options);
}

/// <summary>A PDF417 symbol.</summary>
public sealed class Pdf417Language : IContentLanguage
{
    public bool Reads(string? language) => "pdf417".Equals(language?.Trim(), StringComparison.OrdinalIgnoreCase);

    public Laid? Lay(ContentRequest request) => Pdf417Builder.Lay(request.Source, request.Style);

    public FrameworkElement Draw(string source, DiagramRenderOptions options) => Pdf417Builder.Element(source, options);
}

/// <summary>A chemical structure written as SMILES.</summary>
public sealed class SmilesLanguage : IContentLanguage
{
    public bool Reads(string? language) => "smiles".Equals(language?.Trim(), StringComparison.OrdinalIgnoreCase);

    public Laid? Lay(ContentRequest request) =>
        SmilesBuilder.Lay(request.Source, request.Style, request.Room, request.At);

    public FrameworkElement Draw(string source, DiagramRenderOptions options) => SmilesBuilder.Element(source, options);
}

/// <summary>A formula, written in LaTeX.</summary>
/// <remarks>
/// Drawn by <see cref="FormulaElement"/> rather than by a <c>ContentElement</c> of its own, because a formula
/// is the one content that is also written inline in a sentence.
/// </remarks>
public sealed class LatexLanguage : IContentLanguage
{
    public bool Reads(string? language) => language?.Trim().ToLowerInvariant() is "latex" or "math" or "tex";

    public Laid? Lay(ContentRequest request) =>
        LatexBuilder.Lay(request.Source, request.Style, at: request.At);

    public FrameworkElement Draw(string source, DiagramRenderOptions options) =>
        new ContentElement(source, options.Palette,
                           (state, _) => LatexBuilder.Lay(state.Source, options.Palette)) { IsReadOnly = true };
}

/// <summary>A tune, in whichever notation names itself.</summary>
public sealed class MusicLanguage(MusicDialect dialect) : IContentLanguage
{
    public bool Reads(string? language) => MusicDialectExtensions.FromTag(language ?? string.Empty) == dialect;

    public Laid? Lay(ContentRequest request) =>
        dialect == MusicDialect.LilyPond
            ? LilyPondBuilder.Lay(request.Source, Engraved(request.Room), request.Style, at: request.At)
            : AbcBuilder.Lay(request.Source, Engraved(request.Room), request.Style, at: request.At);

    public FrameworkElement Draw(string source, DiagramRenderOptions options) =>
        new MusicScore(dialect, source, options.Palette, options.SourceOffset);

    /// <summary>A width to engrave against, since a score set to infinity has nowhere to break.</summary>
    private static double Engraved(double room) =>
        double.IsFinite(room) && room > 0 ? Math.Min(room, 420) : 420;
}

/// <summary>A table of values against a pair of axes.</summary>
public sealed class PlotLanguage(PlotFence fence) : IContentLanguage
{
    public bool Reads(string? language) => PlotFences.Named(language ?? string.Empty) == fence;

    public FrameworkElement Draw(string source, DiagramRenderOptions options) =>
        PlotBuilder.Element(source, fence, options);
}

/// <summary>A cloud of words sized by how often each is said.</summary>
public sealed class WordCloudLanguage : IContentLanguage
{
    public bool Reads(string? language) => "wordcloud".Equals(language?.Trim(), StringComparison.OrdinalIgnoreCase);

    public Laid? Lay(ContentRequest request) =>
        WordCloudBuilder.Lay(request.Source, request.Style, request.Room, at: request.At);

    public FrameworkElement Draw(string source, DiagramRenderOptions options) =>
        WordCloudBuilder.Element(source, options);
}

/// <summary>A one-dimensional barcode, in whichever symbology the block names.</summary>
public sealed class BarcodeLanguage : IContentLanguage
{
    public bool Reads(string? language) => "barcode".Equals(language?.Trim(), StringComparison.OrdinalIgnoreCase);

    public FrameworkElement Draw(string source, DiagramRenderOptions options)
    {
        if (!BarcodeBlockParser.TryParse(source, out var block, out string? error))
            return DiagramRenderer.ErrorElement(error!, source);

        var placed = block!.At(block.ValueStart + options.SourceOffset);

        return new ContentElement(placed.Value, options.Palette,
            (state, _) => BarcodeBuilder.Build(placed.With(state.Source), options.Palette))
        {
            // Where the value sits inside the fence that produced it. Without it the host reads this as a
            // block that IS its content — which only a $$…$$ formula is — and puts the delimiters back on
            // every edit, so typing a digit into a barcode turned it into a formula.
            SourceStart = placed.ValueStart,
            SourceLength = placed.Value.Length,

            // Air between one barcode and the next. The quiet zone inside the symbol is part of the symbol —
            // it is what a scanner needs either side of the bars — and being the same white as the ground it
            // separates nothing to the eye: a page of barcodes ran together into one field with bars in it.
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 6, 0, 10),
        };
    }
}
