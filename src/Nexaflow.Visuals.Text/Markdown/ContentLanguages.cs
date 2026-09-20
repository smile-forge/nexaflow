using System;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Chemistry;
using Nexaflow.Visuals.Text.Markdown.Latex;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Music.Abc;
using Nexaflow.Visuals.Text.Markdown.Music.LilyPond;
using Nexaflow.Visuals.Text.Markdown.Qr;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// The languages that can be drawn <em>inside</em> another one's words — a tune on a flowchart node, a formula on a
/// class, a molecule in a table cell.
///
/// <para>
/// A parallel table to <see cref="DiagramRenderer"/>'s, and deliberately so: that one hands back a
/// <c>FrameworkElement</c>, which is a thing to put in a document and not a thing to put inside a drawing. This hands
/// back a <see cref="Laid"/> — pieces and marks, which graft straight into the tree being built, each still standing
/// for the part of the source it was drawn from.
/// </para>
/// <para>
/// Only what takes a palette, a density and a width belongs here. A language needing more of its own — a barcode's
/// block, a plot's fence — is read by its handler before it is drawn, and nothing has yet wanted one inside a label.
/// </para>
/// </summary>
internal static class ContentLanguages
{
    /// <summary>What opens a nested block.</summary>
    public const string Fence = "```";

    /// <summary>
    /// The language a run of words is a fenced block of, and where its source begins inside them — or false where the
    /// words are just words.
    /// </summary>
    /// <remarks>
    /// Read off the characters as they were written, never off what they decode to, because what is found is an
    /// offset into the source and an entity code is not one character there.
    /// </remarks>
    public static bool Fenced(string? written, out string language, out int at)
    {
        language = string.Empty;
        at = 0;

        if (written is null || !written.StartsWith(Fence, StringComparison.Ordinal)) return false;

        var named = Fence.Length;
        while (named < written.Length && !char.IsWhiteSpace(written[named])) named++;

        language = written[Fence.Length..named];
        if (language.Length == 0) return false;

        at = named;
        while (at < written.Length && char.IsWhiteSpace(written[at])) at++;

        return at < written.Length;
    }

    /// <summary>
    /// That language's source, laid out — or null for one nothing here draws, which leaves the words to be drawn as
    /// the words they are.
    /// </summary>
    public static Laid? Lay(string language, string source, MarkdownPalette palette, double pixelsPerDip, double room) =>
        language.ToLowerInvariant() switch
        {
            "abc" => AbcBuilder.Build(source, Room(room), palette.Text, pixelsPerDip),
            "lilypond" or "ly" => LilyPondBuilder.Build(source, Room(room), palette.Text, pixelsPerDip),
            "latex" or "math" or "tex" => LatexBuilder.Build(source, 1.0, pixelsPerDip: pixelsPerDip),
            "smiles" => SmilesBuilder.Build(source, palette, pixelsPerDip, room),
            "qr" => QrBuilder.Build(source, palette, pixelsPerDip),
            "mermaid" => MermaidBuilders.Lay(source, palette, pixelsPerDip, room),
            _ => null,
        };

    /// <summary>A width to engrave against, since a score set to infinity has nowhere to break.</summary>
    private const double Widest = 420;

    private static double Room(double room) => double.IsFinite(room) && room > 0 ? Math.Min(room, Widest) : Widest;
}
