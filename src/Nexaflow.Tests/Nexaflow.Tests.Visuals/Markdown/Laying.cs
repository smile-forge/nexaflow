using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Music;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// Content laid out the way every surface lays it out — through the engine (<see cref="ContentEngine"/>) — for a test about
/// what a language draws. There is no other way in: a builder is made by the engine and by nothing else.
/// </summary>
internal static class Laying
{
    /// <summary>
    /// <paramref name="source"/>, written in the language <paramref name="language"/> names — or markdown, where it names none —
    /// laid out at <paramref name="room"/>.
    /// </summary>
    /// <param name="writing">Whether somebody is writing in it, which draws a hole wherever something is still to be written.</param>
    /// <param name="shown">The stretch shown as typed rather than as what it says.</param>
    public static Laid Lay(string? language, string source, double room = double.PositiveInfinity, LayoutStyle? style = null,
                           bool writing = false, RawZone? shown = null, ContentInputs? inputs = null) =>
        new ContentEngine(inputs).Lay(language, EditState.For(source) with { Raw = shown }, style ?? WpfStyles.Dark, room, readOnly: !writing);

    /// <summary><paramref name="source"/> read and worked over as the engine works it over for a builder, with nothing laid out.</summary>
    public static ContentReading Read(string? language, string source, bool writing = false) =>
        new ContentEngine().Read(language, source, writing);

    /// <summary>A formula set at <paramref name="scale"/> — its body size — as a formula on its own is set.</summary>
    /// <param name="placeholders">Whether somebody is writing in it, which shows a hole where an argument is still to be written.</param>
    /// <param name="block">How wide the display is, for a formula carrying a number; nothing says, where it is nought.</param>
    public static Laid Formula(string latex, double scale, bool placeholders = false, RawZone? shownAsWritten = null, double block = 0) =>
        Lay("latex", latex, block > 0 ? block : double.PositiveInfinity, WpfStyles.Dark with { TextSize = scale },
            writing: placeholders, shown: shownAsWritten);

    /// <summary>A tune laid out on a page <paramref name="width"/> wide, as a document lays one out.</summary>
    public static Laid Engraved(string dialect, string source, double width, LayoutStyle? style = null, RawZone? shown = null) =>
        Lay(dialect, source, width, style, shown: shown);

    /// <summary>
    /// What hosts a builder a test makes for itself — standing alone, with nothing in another language to lay out and nothing
    /// found for it: for a test of what every builder of a kind shares.
    /// </summary>
    public static LayoutHost Hosting => new(LayoutStanding.Alone, static (_, _, _) => null);
}
