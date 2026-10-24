using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Scores;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Music;

/// <summary>
/// A tune, engraved onto the WPF layout tree. The engraving is the shared engraver's (<see cref="Engraver"/>), whichever notation
/// the tune is written in; what is WPF's is the page it is set on, the layer it is drawn through (<see cref="WpfScoreLayer"/>),
/// and the source shown as written where something in it is wrong.
/// </summary>
internal abstract class MusicBuilder : ContentBuilder
{
    /// <summary>Fallback size for source that couldn't be engraved — fixed, since in that case there's no staff to derive it from.</summary>
    private const double SourceSize = 13;

    protected MusicBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly, Nesting nesting)
        : base(reading, state, style, isReadOnly, nesting)
    {
    }

    /// <summary>The notation's own reading of the tune, which engraves it.</summary>
    protected abstract Engraver Notation();

    /// <summary>Reads and engraves the tune — given a page, in a share of its width, set in the middle of it.</summary>
    protected sealed override Laid? Build()
    {
        if (!double.IsFinite(Room)) return Score(Engraver.Unbounded);

        var score = Score(Room * Engraver.PageWidth);
        var page = new LayoutBuilder();
        var width = System.Math.Max(Room, score.Size.Width);

        new ContentInset(score).Set(page, new Point((width - score.Size.Width) / 2, 0), MusicPiece.Page);

        return new Laid(page.Seal(), new Size(width, score.Size.Height), score.Trouble);
    }

    /// <summary>The tune engraved at <paramref name="room"/>, or its source shown as written where anything in it is wrong.</summary>
    private Laid Score(double room)
    {
        var engraver = Notation();
        var tune = engraver.Read();

        var troubled = Engraver.Troubled(tune);
        if (troubled.Count > 0) return AsSource([.. troubled.Select(part => (part, part.Trouble!))], room);

        var layer = new WpfScoreLayer(Style);
        var (width, height) = engraver.Engrave(tune, room, layer);

        return new Laid(layer.Tree!, new Size(width, height), []);
    }

    /// <summary>Unengraveable source is set monospaced, so a reader can count bar lines in it.</summary>
    protected override CharacterSetting Characters =>
        new(Style.Face(Style.MonoFont), SourceSize,
            Brushes.Black);   // never used: the mark takes the theme's ink at paint time
}
