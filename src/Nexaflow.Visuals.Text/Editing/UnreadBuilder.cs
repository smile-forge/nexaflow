using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// Lays out content nothing could read: the source as its own characters, with why beneath it. The tree it is handed is
/// one verbatim node holding the source, and that node's trouble is the reason.
///
/// <para>
/// A builder rather than a call straight to <see cref="SourceShown"/>, because <see cref="ContentBuilder.Lay"/> is the
/// thing that cannot throw. This is the layout that has to be made once everything else has already failed, so it is
/// made the way every other layout is: its own failure lands in the same catch as any other builder's, over the
/// simplest tree there is.
/// </para>
/// </summary>
internal sealed class UnreadBuilder : ContentBuilder
{
    internal UnreadBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly, Nesting nesting)
        : base(reading, state, style, isReadOnly, nesting) { }

    /// <summary>The whole of it blamed, since nothing read any part of it well enough to blame a part.</summary>
    protected override Laid Build() => AsSource(Reading.Root.Trouble ?? string.Empty);

    protected override FormattedText Characters(string text) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Style.Face(Style.MonoFont), Style.TextSize, Style.Text,
            LayoutText.Density);
}
