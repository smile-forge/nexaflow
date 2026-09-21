using System;
using Nexaflow.Markdown.Ast;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Chemistry;
using Nexaflow.Visuals.Text.Markdown.Latex;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Music.Abc;
using Nexaflow.Visuals.Text.Markdown.Music.LilyPond;
using Nexaflow.Visuals.Text.Markdown.Qr;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// The languages that can be drawn <em>inside</em> another one's content — a tune on a flowchart node, a molecule in
/// a song's lyrics, a barcode in a formula. Nothing here is about any one of them: a language is a name, a parser and
/// a builder, and what holds it only has to know it found one.
///
/// <para>
/// A parallel table to <see cref="DiagramRenderer"/>'s, and deliberately so: that one hands back a
/// <c>FrameworkElement</c>, which is a thing to put in a document and not a thing to put inside a drawing. This hands
/// back a <see cref="Laid"/> — pieces and marks, which graft straight into the tree being built.
/// </para>
/// <para>
/// The inner content goes through its own parse, its own pipeline and its own builder, exactly as it would if nothing
/// held it; the only thing it is told is where it was written, so every part of it names the characters a reader is
/// selecting in the document that holds it.
/// </para>
/// </summary>
internal static class ContentLanguages
{
    /// <summary>
    /// That content laid out where it was written — or null for a language nothing here draws, which leaves the
    /// characters to be drawn as the characters they are.
    /// </summary>
    public static Laid? Lay(ContentLink link, StyleFormat palette, double room) =>
        link.Language.ToLowerInvariant() switch
        {
            "abc" => AbcBuilder.Lay(link.Source, Room(room), palette, at: link.At),
            "lilypond" or "ly" => LilyPondBuilder.Lay(link.Source, Room(room), palette, at: link.At),
            "latex" or "math" or "tex" => LatexBuilder.Lay(link.Source, palette, at: link.At),
            "smiles" => SmilesBuilder.Lay(link.Source, palette, room, at: link.At),
            "qr" => QrBuilder.Lay(link.Source, palette),
            "mermaid" => MermaidBuilders.Lay(link.Source, palette, room, at: link.At),
            _ => null,
        };

    /// <summary>
    /// The content a part is a whole block of, laid out to be set down where its words would have gone — or null
    /// where the part is words and nothing more.
    /// </summary>
    public static ContentInset? Inset(ContentPart? part, StyleFormat palette, double room) =>
        ContentLink.Of(part) is { } link && Lay(link, palette, room) is { Exists: true } laid
            ? new ContentInset(laid)
            : null;

    /// <summary>A width to engrave against, since a score set to infinity has nowhere to break.</summary>
    private const double Widest = 420;

    private static double Room(double room) => double.IsFinite(room) && room > 0 ? Math.Min(room, Widest) : Widest;
}
