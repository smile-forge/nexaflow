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
    /// The stretch being shown as its own characters rather than as what it says, because somebody is writing in
    /// it — a title set from front matter, a binding, a block of another language. Null where nobody is.
    /// </summary>
    /// <remarks>
    /// Nothing to do with editing, which the element owns. This is what a diagram draws while part of it is being
    /// typed: the characters, so the caret stands between the ones the reader can see.
    /// </remarks>
    public Nexaflow.Visuals.Text.Editing.RawZone? Raw { get; init; }
}
