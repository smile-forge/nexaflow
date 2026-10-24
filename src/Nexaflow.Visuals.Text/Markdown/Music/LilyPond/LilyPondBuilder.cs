using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Scores;
using Nexaflow.Markdown.Scores.LilyPond;

namespace Nexaflow.Visuals.Text.Markdown.Music.LilyPond;

/// <summary>Music written in LilyPond, engraved onto the WPF layout tree — read by <see cref="LilyPondEngraver"/>.</summary>
internal sealed class LilyPondBuilder : MusicBuilder
{
    internal LilyPondBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly, Nesting nesting)
        : base(reading, state, style, isReadOnly, nesting) { }

    protected override Engraver Notation() => new LilyPondEngraver(Reading);
}
