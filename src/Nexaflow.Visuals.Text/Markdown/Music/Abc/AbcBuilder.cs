using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Scores;
using Nexaflow.Markdown.Scores.Abc;

namespace Nexaflow.Visuals.Text.Markdown.Music.Abc;

/// <summary>A tune written in ABC, engraved onto the WPF layout tree — read by <see cref="AbcEngraver"/>.</summary>
internal sealed class AbcBuilder : MusicBuilder
{
    internal AbcBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly, Nesting nesting)
        : base(reading, state, style, isReadOnly, nesting) { }

    protected override Engraver Notation() => new AbcEngraver(Reading);
}
