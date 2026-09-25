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
/// One language's content drawn on an element of its own: laid out by the engine, as a document lays out what it holds in
/// that language, and shown with nothing round it.
///
/// <para>
/// For a test about what a language draws — where its pieces go, what it looks like — rather than about a document. What a
/// document adds, writing in the content and answering what its pieces offer, is tested on a <see cref="MarkdownSurface"/>,
/// where it happens.
/// </para>
/// </summary>
internal static class Alone
{
    /// <summary><paramref name="source"/> as the language <paramref name="language"/> names draws it, told what the options say.</summary>
    public static ContentElement Drawn(string language, string source, DiagramRenderOptions options)
    {
        if (!ContentLanguages.Reads(language)) throw new ArgumentException($"no language reads '{language}'", nameof(language));

        // What a diagram's reader has opened is somewhere to be written down, even with nobody following it.
        var style = options.Palette with { Expansion = options.ViewState ?? new DiagramViewState() };
        var engine = new ContentEngine(options);

        return new ContentElement(source, options.Palette, Content.Of((state, room) => engine.Lay(language, state, style, room, options.ReadOnly)))
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

    /// <summary>A score on an element of its own, written in to by nobody.</summary>
    public static ContentElement Engraved(MusicDialect dialect, string source, StyleFormat palette, double zoom = 1)
    {
        var engine = new ContentEngine();
        var named = dialect == MusicDialect.LilyPond ? "lilypond" : "abc";

        return new(source, palette, (state, room) => engine.Lay(named, state, palette, room, readOnly: true)) { Zoom = zoom };
    }
}
