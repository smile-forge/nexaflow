using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Latex;

/// <summary>
/// The hole an unwritten argument leaves, as the layout knows it.
///
/// <para>
/// A placeholder is a symbol in the parse, put there by the typesetter wherever an argument was left
/// empty — so it lays out, hit-tests, selects, drags and is replaced exactly as a character does, and
/// none of the editing machinery has a special case for it. It exists in the parse and never in the
/// source: what the reader wrote is <c>{}</c>, and that is what gets saved, copied and solved.
/// </para>
/// <para>
/// All that is left for this side is recognising one, which is a question about the drawn thing rather
/// than about the text — hence a kind rather than a command.
/// </para>
/// </summary>
public static class LatexPlaceholder
{
    /// <summary>
    /// What the typesetter's hollow box is called. The layout names each piece after the box that drew
    /// it, so this is how a hole introduces itself.
    /// </summary>
    public const string Kind = "PlaceholderBox";

    /// <summary>Whether this piece of the formula is a hole waiting to be written in.</summary>
    public static bool IsPlaceholder(this ILayoutNode node) => node.Kind == Kind;
}
