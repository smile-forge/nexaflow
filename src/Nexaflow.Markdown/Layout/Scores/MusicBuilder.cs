using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Markdown.Scores;
using Nexaflow.Markdown.Scores.Abc;
using Nexaflow.Markdown.Scores.LilyPond;

namespace Nexaflow.Markdown.Layout.Scores;

/// <summary>
/// A tune, engraved onto the layout tree through a <see cref="ScoreLayer"/> and set in the middle of its page — whichever
/// notation it is written in, which is the one thing each notation's builder says.
/// </summary>
internal abstract class MusicBuilder : ContentBuilder
{
    /// <summary>The size source that could not be engraved is shown at.</summary>
    private const double SourceSize = 13;

    protected MusicBuilder(ContentReading reading, EditState state, LayoutStyle style, bool isReadOnly, ILayoutMedium medium,
                                   LayoutHost host)
        : base(reading, state, style, isReadOnly, medium, host)
    {
    }

    /// <summary>What reads and engraves the notation this tune is written in.</summary>
    protected abstract Engraver Notation();

    protected sealed override Laid? Build()
    {
        if (!double.IsFinite(Room)) return Score(Engraver.Unbounded);

        var score = Score(Room * Engraver.PageWidth);
        var page = new LayoutBuilder(Medium);
        var width = Math.Max(Room, score.Width);

        new LayoutInset(score).Set(page, new LayoutPoint((width - score.Width) / 2, 0), Page);

        return new Laid(page.Seal(), width, score.Height, score.Trouble);
    }

    /// <summary>What a tune's page is, as a piece.</summary>
    internal const string Page = "MusicPage";

    private Laid Score(double room)
    {
        var engraver = Notation();
        var tune = engraver.Read();

        var troubled = Engraver.Troubled(tune);
        if (troubled.Count > 0) return AsSource([.. troubled.Select(part => (part, part.Trouble!))], room);

        var layer = new ScoreLayer(Medium, Style.Text, ScoreFonts.Of(Medium));
        var (width, height) = engraver.Engrave(tune, room, layer);

        return new Laid(layer.Tree!, width, height, []);
    }

    protected override LayoutCharacters Characters => new(Style.Face(Style.MonoFont), SourceSize, Style.Text);
}

/// <summary>A tune written in ABC.</summary>
internal sealed class AbcBuilder(ContentReading reading, EditState state, LayoutStyle style, bool isReadOnly, ILayoutMedium medium,
                                         LayoutHost host)
    : MusicBuilder(reading, state, style, isReadOnly, medium, host)
{
    protected override Engraver Notation() => new AbcEngraver(Reading);
}

/// <summary>A tune written in LilyPond.</summary>
internal sealed class LilyPondBuilder(ContentReading reading, EditState state, LayoutStyle style, bool isReadOnly, ILayoutMedium medium,
                                              LayoutHost host)
    : MusicBuilder(reading, state, style, isReadOnly, medium, host)
{
    protected override Engraver Notation() => new LilyPondEngraver(Reading);
}
