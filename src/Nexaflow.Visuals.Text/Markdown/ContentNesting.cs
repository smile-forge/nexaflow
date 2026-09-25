using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// Which language reads what is written inside a piece of content — worked out once, by a stage, where the
/// table of languages is in scope, and hung on the piece itself.
///
/// <para>
/// <strong>The builder keeps every decision that was its.</strong> How much room something gets, what has to
/// sit around it and in what order the tree is walked are all answers only a builder has, so none of them are
/// settled here. What is settled is the one thing a builder has no business asking: which of the languages a
/// host assembled reads this. By the time a builder sees the node the answer is on it, and the builder asks
/// for an inset at a size it chose.
/// </para>
/// <para>
/// Hung as a derived part, which takes up no source, prints as nothing and is nowhere to be found by an
/// offset — so a tree carrying it still says exactly what was written.
/// </para>
/// </summary>
/// <param name="Language">What reads it.</param>
/// <param name="Style">What this showing of the content is drawn in.</param>
/// <param name="Options">What the host said about diagrams, where it said anything.</param>
internal sealed record ContentNesting(IContentLanguage Language, string Named, StyleFormat Style, DiagramRenderOptions? Options, string Written)
{
    /// <summary>What a piece has hanging off it, or null where it holds no other language.</summary>
    public static ContentNesting? Of(ContentPart? part) =>
        part?.Children.Select(child => child.Node.Held).OfType<ContentNesting>().FirstOrDefault();

    /// <summary>
    /// Every part holding content another language is written in, from <paramref name="part"/> up the tree — the innermost
    /// first. Each is the whole of what that language is written in, its delimiters and all: what an edit landing inside it is
    /// that language's, and what is shown where that language could not draw it.
    /// </summary>
    public static IEnumerable<ContentPart> Holders(ContentPart? part)
    {
        for (var holder = part; holder is not null; holder = holder.Parent)
            if (Of(holder) is not null) yield return holder;
    }

    /// <summary>
    /// <paramref name="body"/> laid out to fit <paramref name="room"/>, ready to be set down where it draws (<see cref="ContentInset.Draws"/>)
    /// — and where it does not, saying why in its trouble. Null where that language had nothing to say.
    /// </summary>
    /// <param name="shown">
    /// The stretch the document being written is showing as typed. Handed on only where it falls inside the body,
    /// because then it is this language's stretch — a command half spelled — and nobody else's to draw.
    /// </param>
    /// <param name="isReadOnly">Whether anybody is writing, so a hole is drawn where something is still to be written.</param>
    public ContentInset? At(ContentPart? body, double room, RawZone? shown = null, bool isReadOnly = true)
    {
        if (body is null) return null;

        var laid = Language.Lay(new ContentRequest(Written, Style)
        {
            Named = Named, Room = room, At = body.Start, Options = Options,
            Shown = shown is { } zone && Holds(body, zone) ? zone : null,
            IsReadOnly = isReadOnly,
        });

        // Handed back whether it drew or not, since one that drew nothing may still say why — and what goes there then is
        // the characters somebody typed, with that. Null only where the language had nothing to say at all.
        return laid is null ? null : new ContentInset(laid);
    }

    /// <summary>Whether a stretch shown as typed falls inside the body — this language's to show, not the document's.</summary>
    public static bool Holds(ContentPart body, RawZone zone)
    {
        var (start, length) = Own(body);
        return start <= zone.Start && zone.End <= start + length;
    }

    /// <summary>
    /// Whether a stretch shown as typed lies inside another language's source somewhere in <paramref name="part"/>, so
    /// that language is the one to show it and the document around it goes on drawing as it reads.
    /// </summary>
    public static bool Nests(ContentPart part, RawZone zone) =>
        part.SelfAndDescendants().Any(inner => Of(inner) is not null && inner.Part(Roles.Body) is { } body && Holds(body, zone));

    /// <summary>
    /// The stretch of a body that is the language's own: all of it but the line ending that closes its last line, which is a
    /// piece of its own (<see cref="Nexaflow.Markdown.Prose.Stages.WithClosingLines"/>). That ending belongs to the line the
    /// closing delimiter stands on — written into, it would put what was typed on that line and the delimiter would no longer
    /// close anything.
    /// </summary>
    public static (int Start, int Length) Own(ContentPart body) =>
        body.Children.Count > 0 && body.Children[^1] is { Role: Roles.Trivia } closing
            ? (body.Start, closing.Start - body.Start)
            : (body.Start, body.Length);
}
