using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Music.Abc;
using Nexaflow.Visuals.Text.Editing;

using Nexaflow.Visuals.Text.Markdown.Music.Rendering;

namespace Nexaflow.Visuals.Text.Markdown.Music.Abc;

/// <summary>
/// An engraved tune: the tree of what was drawn, and the ability to draw it again.
///
/// <para>
/// Engraving happens once, in <see cref="Build"/> — fonts, glyph outlines and all. After that this holds no
/// reference to the engraver at all: both what you can <em>ask</em> about the tune and what you can
/// <em>paint</em> of it come out of <see cref="Root"/>. That is the strongest statement that the tree is
/// complete, and it is why the rules deciding what a drag selected or where an arrow key goes can be
/// exercised without a desktop.
/// </para>
/// <para>
/// It is a <see cref="ContentBuilder"/>, which is where the promise that engraving always produces
/// something is kept. A tune rarely needs it — the reader turns anything it cannot make sense of into
/// notes-shown-as-written rather than failing — but an engraver is a great deal of arithmetic over
/// somebody's half-typed input, and "the score vanished" is never the right way to report a fault in it.
/// </para>
/// </summary>
internal sealed class AbcLayout : ContentBuilder
{
    /// <summary>
    /// How big to set source a tune could not be engraved from at all. A fixed size rather than one
    /// derived from the staff, because there is no staff in that case — this is the fallback for an
    /// engraver that threw, so nothing it would have measured can be trusted.
    /// </summary>
    private const double SourceSize = 13;

    private readonly double _width;
    private readonly Brush _ink;
    private readonly double _pixelsPerDip;
    private readonly (int Start, int Length)? _shownAsWritten;
    private readonly ScoreSpacing? _spacing;

    private AbcLayout(string abc, double width, Brush ink, double pixelsPerDip,
                      (int Start, int Length)? shownAsWritten, ScoreSpacing? spacing)
        : base(abc)
    {
        _width = width;
        _ink = ink;
        _pixelsPerDip = pixelsPerDip;
        _shownAsWritten = shownAsWritten;
        _spacing = spacing;
    }

    /// <summary>The source this was built from.</summary>
    public string Abc => Source;

    /// <summary>The tune, read: every part with its position and what holds it.</summary>
    public ContentReading Reading => _reading ??= ContentReading.Of(AbcPipeline.Read(Source, Draws, _shownAsWritten));

    private ContentReading? _reading;

    /// <summary>What was laid out — the tree, its size, and what could not be read.</summary>
    public Laid Laid { get; private set; } = Laid.Nothing;

    /// <summary>The tune, as a piece. Every question about its shape goes here.</summary>
    public Piece Root => Laid.Root;

    /// <summary>The engraved size in element pixels.</summary>
    public Size Size => Laid.Size;

    /// <summary>Whatever could not be read or could not be drawn — what the host draws a wave under.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics => Laid.Trouble;

    /// <summary>
    /// Reads and engraves <paramref name="abc"/> to fit <paramref name="width"/>.
    /// </summary>
    /// <param name="shownAsWritten">
    /// A stretch to show as the characters written rather than read as music — the piece being edited,
    /// which has to be seen exactly as typed while the tune around it stays engraved.
    /// </param>
    /// <param name="spacing">
    /// How much air to leave between things, or null for what the engraver normally uses. A caller passes
    /// something else only to compare two engravings without the comparison being about this.
    /// </param>
    public static AbcLayout Build(string abc, double width, Brush ink, double pixelsPerDip,
                                  (int Start, int Length)? shownAsWritten = null,
                                  ScoreSpacing? spacing = null)
    {
        var engraved = new AbcLayout(abc, width, ink, pixelsPerDip, shownAsWritten, spacing);
        engraved.Laid = engraved.Lay();
        return engraved;
    }

    protected override Laid Read()
    {
        // One reading, every time, whatever the caret is doing. What cannot be drawn and what is being
        // typed are both settled before this — they come back as pieces that say so, and the builder sets
        // them without having to know which of the two it is looking at.
        var tree = AbcPipeline.Read(Source, Draws, _shownAsWritten);
        _reading = ContentReading.Of(tree);

        return AbcBuilder.Build(Reading, _width, _ink, _pixelsPerDip, _spacing);
    }

    /// <summary>
    /// How a tune sets characters it could not engrave: monospaced, so the reader can count the bar lines
    /// in what they wrote.
    /// </summary>
    protected override FormattedText Characters(string text) =>
        new(text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Consolas"),
            SourceSize,
            Brushes.Black,   // never used: the mark takes the theme's ink at paint time
            _pixelsPerDip);

    /// <summary>
    /// Whether the engraver has a drawing for a named decoration. Asked of the builder rather than of a
    /// table, because what can be drawn is a fact about an engraver: asking the tables instead is what once
    /// put a red wave under a LaTeX command the builder set perfectly well.
    /// </summary>
    private static bool Draws(string decoration) => Decorations.Contains(decoration.ToLowerInvariant());

    private static readonly HashSet<string> Decorations =
    [
        "staccato", "tenuto", "accent", "emphasis", "marcato", "upbow", "downbow", "fermata", "trill",
        "roll", "turn", "uppermordent", "pralltriller", "lowermordent", "mordent", "segno", "coda",
        ">", "^",
    ];
}
