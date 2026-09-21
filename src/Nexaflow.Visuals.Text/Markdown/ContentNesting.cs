using System.Linq;

using Nexaflow.Markdown.Ast;

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
internal sealed record ContentNesting(IContentLanguage Language, string Named, StyleFormat Style, DiagramRenderOptions? Options)
{
    /// <summary>What a piece has hanging off it, or null where it holds no other language.</summary>
    public static ContentNesting? Of(ContentPart? part) =>
        part?.Children.Select(child => child.Node.Held).OfType<ContentNesting>().FirstOrDefault();

    /// <summary>
    /// <paramref name="body"/> laid out to fit <paramref name="room"/>, ready to be set down — or null where
    /// that language cannot lay anything yet, which leaves the characters to be drawn as themselves.
    /// </summary>
    public ContentInset? At(ContentPart? body, double room)
    {
        if (body is not { Length: > 0 }) return null;

        var laid = Language.Lay(new ContentRequest(body.Text, Style)
        {
            Named = Named, Room = room, At = body.Start, Options = Options,
        });

        return laid is { Exists: true } ? new ContentInset(laid) : null;
    }
}
