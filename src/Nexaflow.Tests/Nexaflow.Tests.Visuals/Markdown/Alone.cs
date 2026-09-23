using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using ContentElement = Nexaflow.Visuals.Text.Editing.ContentElement;
using Nexaflow.Visuals.Text.Markdown.Languages;
using Nexaflow.Visuals.Text.Markdown.Music;
using Nexaflow.Visuals.Text.Markdown.Music.Abc;
using Nexaflow.Visuals.Text.Markdown.Music.LilyPond;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// One language's content drawn on an element of its own: the tree a document grafts, laid through the language's own
/// entry (<see cref="IContentLanguage.Lay"/>) and shown with nothing round it.
///
/// <para>
/// For a test about a builder — what a diagram draws, where its pieces go, what it looks like — rather than about a
/// document. What a document adds, writing in the content and answering what its pieces offer, is tested on a
/// <see cref="MarkdownSurface"/>, where it happens.
/// </para>
/// </summary>
internal static class Alone
{
    /// <summary>
    /// <paramref name="source"/> as the language <paramref name="language"/> names draws it, told what the options say.
    /// A language that has nothing to draw leaves the characters, as a document does.
    /// </summary>
    public static ContentElement Drawn(string language, string source, DiagramRenderOptions options)
    {
        var reads = ContentLanguages.For(language)
                    ?? throw new ArgumentException($"no language reads '{language}'", nameof(language));

        // What a diagram's reader has opened is somewhere to be written down, even with nobody following it.
        var style = options.Palette with { Expansion = options.ViewState ?? new DiagramViewState() };

        return new ContentElement(source, options.Palette, Content.Of((state, room) =>
            reads.Lay(new ContentRequest(state.Source, style)
            {
                Named = language,
                Room = room,
                IsReadOnly = options.ReadOnly,
                Options = options,
                Shown = state.Raw,
            })
            ?? Characters(state.Source, options.Palette)))
        {
            IsReadOnly = options.ReadOnly,
            Cursor = Cursors.Arrow,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 10),
        };
    }

    /// <summary>The same, in a palette, with nothing else said.</summary>
    public static ContentElement Drawn(string language, string source, StyleFormat? palette = null, Func<string, bool>? onNavigate = null) =>
        Drawn(language, source, DiagramRenderOptions.For(palette ?? StyleFormat.FromTheme(), onNavigate));

    /// <summary>
    /// A score as its engraver alone sets it: at exactly the width it is given, with no page round it — for a test about
    /// the engraving rather than about where a page puts it (<see cref="MusicLanguage"/>).
    /// </summary>
    public static ContentElement Engraved(MusicDialect dialect, string source, StyleFormat palette, double zoom = 1) =>
        new(source, palette, (state, room) =>
        {
            (int Start, int Length)? typed = state.Raw is { } raw ? (raw.Start, raw.End - raw.Start) : null;

            return dialect == MusicDialect.LilyPond
                ? LilyPondBuilder.Lay(state.Source, room, palette, typed)
                : AbcBuilder.Lay(state.Source, room, palette, typed);
        })
        {
            Zoom = zoom,
        };

    /// <summary>The characters, for content that had nothing else to draw.</summary>
    private static Laid Characters(string source, StyleFormat palette) =>
        LayoutText.Shown(source,
                         new FormattedText(source.Length == 0 ? " " : source, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                           new Typeface("Consolas"), palette.TextSize, palette.Text, LayoutText.Density),
                         []);
}
