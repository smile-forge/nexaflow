using System.Collections.Generic;
using System.Windows;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// What a builder makes: a tree of pieces, how much room it wants, and whatever could not be read.
///
/// <para>
/// <strong>There is one of these, not one per kind of content.</strong> A builder is the last place
/// anything knows what it is drawing — it reads a parse tree and lays pieces out — and everything after
/// it is the same code whatever was built: the painter, the hit test, the caret, the selection, the
/// element that hosts it. That is what makes a thing nobody has thought of yet cost a builder and
/// nothing else, and it is why a score, a formula and a barcode no longer have a layout type each.
/// </para>
/// <para>
/// The size is stated rather than taken from the root, because how much room content wants is a fact
/// about the content: a typeset formula leaves its spacing out, since a strut is as tall as the line it
/// reserves and counting it would pad the element with margin nothing is drawn in. Something built from
/// several of these takes the union.
/// </para>
/// </summary>
/// <param name="Tree">Every piece that was laid out, and what each drew.</param>
/// <param name="Size">How much room it wants, in element pixels.</param>
/// <param name="Trouble">
/// Whatever could not be read — what the host draws a wave under. Empty for content that parsed
/// cleanly, which is most of it and none of it while somebody is typing.
/// </param>
public sealed record Laid(LayoutTree Tree, Size Size, IReadOnlyList<Diagnostic> Trouble)
{
    /// <summary>Nothing laid out at all — what a builder handed content it could make nothing of returns.</summary>
    public static Laid Nothing { get; } = new(new LayoutBuilder().Seal(), new Size(0, 0), []);

    /// <summary>The whole of it, as a piece — where every query starts.</summary>
    public Piece Root => Tree.Root;

    /// <summary>Whether anything was laid out.</summary>
    public bool Exists => Tree.Count > 0;
}
