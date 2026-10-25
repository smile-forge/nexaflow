using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Markdown.Scores;
using Nexaflow.Markdown.Scores.Abc;
using Nexaflow.Markdown.Scores.LilyPond;

namespace Nexaflow.Markdown.Layout.Scores;

/// <summary>
/// A tune, engraved onto the layout tree through a <see cref="PortableScoreLayer"/> and set in the middle of its page — whichever
/// notation it is written in, which is the one thing each notation's builder says.
/// </summary>
internal abstract class PortableMusicBuilder : PortableContentBuilder
{
    /// <summary>The size source that could not be engraved is shown at.</summary>
    private const double SourceSize = 13;

    protected PortableMusicBuilder(ContentReading reading, EditState state, PortableStyle style, bool isReadOnly, ILayoutMedium medium,
                                   PortableHost host)
        : base(reading, state, style, isReadOnly, medium, host)
    {
    }

    /// <summary>What reads and engraves the notation this tune is written in.</summary>
    protected abstract Engraver Notation();

    protected sealed override PortableLaid? Build()
    {
        if (!double.IsFinite(Room)) return Score(Engraver.Unbounded);

        var score = Score(Room * Engraver.PageWidth);
        var page = new PortableBuilder(Medium);
        var width = Math.Max(Room, score.Width);

        new PortableInset(score).Set(page, new LayoutPoint((width - score.Width) / 2, 0), Page);

        return new PortableLaid(page.Seal(), width, score.Height, score.Trouble);
    }

    /// <summary>What a tune's page is, as a piece.</summary>
    internal const string Page = "MusicPage";

    private PortableLaid Score(double room)
    {
        var engraver = Notation();
        var tune = engraver.Read();

        var troubled = Engraver.Troubled(tune);
        if (troubled.Count > 0) return AsSource([.. troubled.Select(part => (part, part.Trouble!))], room);

        var layer = new PortableScoreLayer(Medium, Style.Text, PortableScoreFonts.Of(Medium));
        var (width, height) = engraver.Engrave(tune, room, layer);

        return new PortableLaid(layer.Tree!, width, height, []);
    }

    protected override PortableCharacters Characters => new(Style.Face(Style.MonoFont), SourceSize, Style.Text);
}

/// <summary>A tune written in ABC.</summary>
internal sealed class PortableAbcBuilder(ContentReading reading, EditState state, PortableStyle style, bool isReadOnly, ILayoutMedium medium,
                                         PortableHost host)
    : PortableMusicBuilder(reading, state, style, isReadOnly, medium, host)
{
    protected override Engraver Notation() => new AbcEngraver(Reading);
}

/// <summary>A tune written in LilyPond.</summary>
internal sealed class PortableLilyPondBuilder(ContentReading reading, EditState state, PortableStyle style, bool isReadOnly, ILayoutMedium medium,
                                              PortableHost host)
    : PortableMusicBuilder(reading, state, style, isReadOnly, medium, host)
{
    protected override Engraver Notation() => new LilyPondEngraver(Reading);
}
