using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout.Prose;

namespace Nexaflow.Markdown.Layout;

/// <summary>
/// Lays out content nothing could read: the source as its own characters, with why beneath it. The tree it is handed is one verbatim
/// node holding the source, and that node's trouble is the reason.
///
/// <para>
/// A builder rather than a call straight to <see cref="PortableSourceShown"/>, because <see cref="PortableContentBuilder.Lay"/> is the
/// thing that cannot throw. This is the layout that has to be made once everything else has already failed, so it is made the way
/// every other layout is: its own failure lands in the same catch as any other builder's, over the simplest tree there is.
/// </para>
/// </summary>
public sealed class PortableUnreadBuilder(ContentReading reading, EditState state, PortableStyle style, bool isReadOnly, ILayoutMedium medium,
                                          PortableNesting nesting)
    : PortableContentBuilder(reading, state, style, isReadOnly, medium, nesting)
{
    /// <summary>The whole of it blamed, since nothing read any part of it well enough to blame a part.</summary>
    protected override PortableLaid Build() => AsSource(Reading.Root.Trouble ?? string.Empty);

    protected override PortableCharacters Characters => new(Style.Face(Style.MonoFont), Style.TextSize, Style.Text);
}
