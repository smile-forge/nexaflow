namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// Everything laying a diagram out depends on apart from the source itself: what it is drawn with, how much room it
/// has, and whether anybody can write in it.
///
/// <para>
/// One record rather than an argument each, because what a builder needs to be told grows — a view's expansion, the
/// data a label binds to, the languages a nested block can be drawn in — and each of those would otherwise be another
/// parameter threaded through every builder there is. A builder reads what it understands and ignores the rest.
/// </para>
/// </summary>
/// <param name="Palette">The colours the host draws in.</param>
/// <param name="PixelsPerDip">The screen's pixel density, so text is measured as it will be drawn.</param>
/// <param name="Room">How wide the diagram may be. Infinity, or anything not a width, is as wide as it likes.</param>
/// <param name="Writing">Whether somebody is writing in the block, which draws what is still to be written.</param>
internal sealed record DiagramLaying(
    MarkdownPalette Palette,
    double PixelsPerDip = 1,
    double Room = double.PositiveInfinity,
    bool Writing = false)
{
    /// <summary>
    /// Where what the reader has opened and folded is kept between one laying and the next, or null where it is kept
    /// nowhere and the diagram is drawn as its front matter asks every time.
    /// </summary>
    public DiagramViewState? View { get; init; }

    /// <summary>
    /// What a <c>{{…}}</c> written in the diagram is read against, or null where nothing is — in which case a
    /// binding is drawn as the characters it was written with.
    /// </summary>
    public Nexaflow.Markdown.Binding.IDataContext? Data { get; init; }

    /// <summary>
    /// Where the block's first character stands in the document that holds it. Nought for a block of its own, and
    /// the offset of the slice for one written inside another content.
    /// </summary>
    public int At { get; init; }
}
