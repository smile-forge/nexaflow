using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

using Nexaflow.Icons;
using Nexaflow.Localization;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// What the renderer's own verbs are called and drawn as, and the icons a language draws a control of its own as — each a shape in the
/// sixteen-by-sixteen box a ribbon button gives a picture, made by the medium the content is laid in and kept for it, so whatever shows
/// the ribbon fills it in its own ink.
/// </summary>
public static class RibbonMarks
{
    /// <summary>The face the renderer's own marks are drawn in — Segoe MDL2 Assets, which every Windows the app runs on carries.</summary>
    public const string IconFont = "Segoe MDL2 Assets";

    /// <summary>How wide and high a mark is: the box a ribbon button gives a picture.</summary>
    private const double Side = 16;

    /// <summary>
    /// What an intent is called where a reader has to read it. The verbs the renderer knows are named here so every
    /// diagram words them the same way; anything else says what it said for itself, and falls back to its verb.
    /// </summary>
    public static string Names(LayoutIntent intent) => intent.Verb switch
    {
        LayoutVerbs.Navigate => Str.Get("Markdown.Ribbon.OpenLink"),
        LayoutVerbs.Expand => Str.Get("Markdown.Ribbon.Expand"),
        LayoutVerbs.Collapse => Str.Get("Markdown.Ribbon.Collapse"),
        LayoutVerbs.Copy => Str.Get("Markdown.Ribbon.Copy"),
        LayoutVerbs.Cut => Str.Get("Markdown.Ribbon.Cut"),
        LayoutVerbs.Paste => Str.Get("Markdown.Ribbon.Paste"),
        LayoutVerbs.Save => Str.Get("Markdown.Ribbon.SavePicture"),
        _ => intent.Tip is { Length: > 0 } said ? said : intent.Verb,
    };

    /// <summary>
    /// The mark an intent is drawn as where there is room only for a mark — a block's corner, a ribbon's button — in
    /// <see cref="IconFont"/>, or null where it has none and is named instead.
    /// </summary>
    public static string? Icon(LayoutIntent intent) => intent.Verb switch
    {
        LayoutVerbs.Copy => "\uE8C8",
        LayoutVerbs.Save => "\uE74E",
        LayoutVerbs.Navigate => "\uE8A7",
        LayoutVerbs.Paste => "\uE77F",
        LayoutVerbs.Cut => "\uE8C6",
        _ => null,
    };

    /// <summary>
    /// <paramref name="intent"/>'s mark (<see cref="Icon"/>) as a shape in a button's box, or null where it has none — how a ribbon
    /// button for one of the renderer's own verbs is drawn.
    /// </summary>
    public static LayoutShape? Marked(ILayoutMedium medium, LayoutIntent intent) =>
        Icon(intent) is { } mark ? Outlined(medium, Kept(medium).MarkFace, mark) : null;

    /// <summary>
    /// <paramref name="icon"/> as a shape in a button's box — its glyph's outline in the style's icon face, centred — or null where it
    /// is no icon that face draws, and the control is named instead: how a language draws a control of its own as an icon.
    /// </summary>
    public static LayoutShape? Drawn(ILayoutMedium medium, LayoutStyle style, IconRef icon) =>
        icon.IsFluent && FluentGlyphs.Of(icon) is { Length: > 0 } glyph ? Outlined(medium, style.IconFace, glyph) : null;

    /// <summary>A glyph's outline, centred in a button's box — or null where it draws nothing. Each is made once for a medium.</summary>
    private static LayoutShape? Outlined(ILayoutMedium medium, LayoutFace face, string glyph) =>
        Kept(medium).Outlines.GetOrAdd((face, glyph), static (key, medium) =>
        {
            var codepoint = char.ConvertToUtf32(key.Glyph, 0);
            if (medium.GlyphOutline(key.Face, codepoint, Side, default) is not { Bounds: { IsEmpty: false } bounds }) return null;

            return medium.GlyphOutline(key.Face, codepoint, Side,
                                       new LayoutPoint(((Side - bounds.Width) / 2) - bounds.X, ((Side - bounds.Height) / 2) - bounds.Y));
        }, medium);

    /// <summary>What is kept for a medium: the face its marks are drawn in, and every outline drawn so far.</summary>
    private sealed class Marks(ILayoutMedium medium)
    {
        public LayoutFace MarkFace { get; } = medium.Face(IconFont);

        public ConcurrentDictionary<(LayoutFace Face, string Glyph), LayoutShape?> Outlines { get; } = new();
    }

    private static Marks Kept(ILayoutMedium medium) => Made.GetValue(medium, static medium => new Marks(medium));

    private static readonly ConditionalWeakTable<ILayoutMedium, Marks> Made = new();
}
