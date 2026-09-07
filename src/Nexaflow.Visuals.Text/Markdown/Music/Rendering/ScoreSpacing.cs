using static Nexaflow.Visuals.Text.Markdown.Music.Rendering.ScoreMetrics;

namespace Nexaflow.Visuals.Text.Markdown.Music.Rendering;

/// <summary>
/// How much air an engraving puts between things — the numbers that are taste rather than notation.
///
/// <para>
/// Everything else about a score is fixed by what the notation means: a staff is five lines, a stem is
/// three and a half spaces, a flat is a flat. These are the ones a reader could reasonably disagree about,
/// and the ones we have in fact changed as the engraving improved.
/// </para>
/// <para>
/// <strong>They are a parameter so that a change to them can be told apart from a change to the drawing.</strong>
/// Held against the previous renderer, an engraving that spaces notes 15% further apart disagrees about
/// every pixel on the page — which buries the thing worth finding, a glyph that came out wrong. Handing
/// the old numbers to the new engraver removes the difference we chose and leaves the differences we did
/// not.
/// </para>
/// </summary>
/// <param name="SlotBase">The room a note takes before its length is counted.</param>
/// <param name="SlotRate">…and how much more it takes as it gets longer.</param>
/// <param name="GroupBase">The same pair, between two notes of one beam group, which sit closer.</param>
/// <param name="GroupRate">See <paramref name="GroupBase"/>.</param>
/// <param name="BarLeadIn">The air after a bar line, before the first note.</param>
/// <param name="BarLeadOut">…and after the last note, before the next bar line.</param>
/// <param name="SectionAir">Extra either side of a bar line that stops the music — a repeat, a double.</param>
public sealed record ScoreSpacing(
    double SlotBase,
    double SlotRate,
    double GroupBase,
    double GroupRate,
    double BarLeadIn,
    double BarLeadOut,
    double SectionAir)
{
    /// <summary>What the engraver uses: open spacing, tight beam groups, air around a repeat.</summary>
    public static readonly ScoreSpacing Current = new(
        SlotBase: ScoreMetrics.SlotBase,
        SlotRate: ScoreMetrics.SlotRate,
        GroupBase: ScoreMetrics.GroupBase,
        GroupRate: ScoreMetrics.GroupRate,
        BarLeadIn: 1.0 * S,
        BarLeadOut: 0.7 * S,
        SectionAir: 1.0 * S);

    /// <summary>
    /// What the previous renderer used: one spacing curve for everything, and less air around a bar line.
    ///
    /// <para>
    /// The air is an estimate rather than a reading of its source — the old renderer has no constants of
    /// its own for it — so it is here to make the two comparable, not to describe that code exactly.
    /// Zeroing it, which is what this said first, made our page look <em>worse</em> at the bar lines than
    /// the old one and turned an artefact of the comparison into an apparent regression.
    /// </para>
    ///
    /// <para>
    /// Kept so the two can be compared without the comparison being about this. It is not a setting
    /// anybody should choose — the current numbers are better, which is why they were changed — it is the
    /// control for an experiment.
    /// </para>
    /// </summary>
    public static readonly ScoreSpacing Previous = new(
        SlotBase: 0.85 * S,
        SlotRate: 2.1 * S,
        GroupBase: 0.85 * S,
        GroupRate: 2.1 * S,
        BarLeadIn: 0.55 * S,
        BarLeadOut: 0.4 * S,
        SectionAir: 0);
}
