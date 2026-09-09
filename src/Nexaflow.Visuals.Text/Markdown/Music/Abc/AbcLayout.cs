using System;
using System.Collections.Generic;
using System.Linq;
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
/// </summary>
internal sealed class AbcLayout
{
    private AbcLayout(string abc, ContentReading reading, LayoutTree tree, Size size,
                      IReadOnlyList<Diagnostic> diagnostics)
    {
        Abc = abc;
        Reading = reading;
        Tree = tree;
        Size = size;
        Diagnostics = diagnostics;
    }

    /// <summary>The source this was built from.</summary>
    public string Abc { get; }

    /// <summary>The tune, read: every part with its position and what holds it.</summary>
    public ContentReading Reading { get; }

    /// <summary>What was drawn where — every question about the tune's shape goes here.</summary>
    public LayoutTree Tree { get; }

    /// <summary>The whole tune, as a piece.</summary>
    public Piece Root => Tree.Root;

    /// <summary>The engraved size in element pixels.</summary>
    public Size Size { get; }

    /// <summary>Whatever could not be read or could not be drawn — what the host draws a wave under.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

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
        // One reading, every time, whatever the caret is doing. What cannot be drawn and what is being
        // typed are both settled before this — they come back as pieces that say so, and the builder sets
        // them without having to know which of the two it is looking at.
        var tree = AbcPipeline.Read(abc, Draws, shownAsWritten);
        var reading = ContentReading.Of(tree);

        var (laid, size) = AbcBuilder.Build(reading, width, ink, pixelsPerDip, spacing);

        // Asked of the tree rather than collected on the way through it. A piece that could not be read
        // carries the reason, so there is one place the answer lives and no second list to fall out of step
        // with it — and a piece being typed carries nothing, which is how it draws without being complained
        // about.
        var trouble = reading.Root.SelfAndDescendants()
            .Where(part => part.Trouble is not null && part.Length > 0)
            .Select(part => new Diagnostic(part.Start, part.Length, DiagnosticSeverity.Warning, part.Trouble!)
            {
                Part = part,
            })
            .ToList();

        return new AbcLayout(abc, reading, laid, size, trouble);
    }

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

    // ── Painting ────────────────────────────────────────────────────────────

    /// <summary>
    /// Paints the tune, or one piece of it, by walking the tree it was engraved into. The same walk that
    /// answers a hit test, so the picture and the answers cannot disagree about where anything is.
    /// </summary>
    public void Paint(DrawingContext dc, Brush foreground, Piece subtree = default) =>
        LayoutPainter.Paint(dc, subtree.Exists ? subtree : Root, foreground);
}
