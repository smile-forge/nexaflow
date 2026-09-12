using System.Linq;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Pipeline.Stages;
using Nexaflow.Markdown.Latex.Stages;

namespace Nexaflow.Markdown.Latex;

/// <summary>
/// The actors LaTeX runs between the parser and the builder, and the order they run in.
///
/// <para>
/// The order is the only thing here that is an argument. A macro means what its definition says, so it is
/// expanded before anything asks what a piece is; a sign written as several things is gathered next, inside
/// an expansion as much as outside one; and only then is it worth saying where something still has to go
/// and what cannot be drawn. Last is a stretch shown exactly as typed while somebody is mid-keystroke: a
/// half-written command is invalid almost by definition, and saying so on every keystroke would be the
/// wrong thing to draw.
/// </para>
/// <para>
/// Every stage keeps the one rule of <see cref="AstPipeline"/>: the tree still prints as the source it came
/// from. Nothing here is incremental — an edit can put anything anywhere, including a <c>}</c> that
/// reshapes everything after it, so the tree prints itself back and all of this runs again.
/// </para>
/// </summary>
public static class TexPipeline
{
    /// <summary>
    /// The tree to build a formula from: what was written, with what each macro means hung beneath it, and
    /// anything that cannot be drawn — and anything currently being typed — shown as the characters it is
    /// made of.
    /// </summary>
    /// <param name="draws">
    /// Whether whatever is going to set this tree knows how to draw a command, given its name as
    /// written, backslash and all. Asked for rather than known, because what can be drawn is a fact
    /// about a typesetter and this is a reader.
    /// </param>
    /// <param name="editing">
    /// A stretch somebody is in the middle of typing, shown rather than read for as long as they are.
    /// </param>
    /// <param name="holes">
    /// Whether an argument or a cell left empty gets a hole standing in it. Asked for by a surface being
    /// written on, where the hole is how a reader sees there is something still to write and how they aim
    /// at it; off by default, because a box in a formula that is only being read would simply be wrong.
    /// </param>
    public static ContentNode Read(string latex, Func<string, bool>? draws = null,
                                   (int Start, int Length)? editing = null, bool holes = false) =>
        Of(draws, editing, holes).Run(TexParser.Parse(latex));

    /// <summary>The pipeline itself, for anything that wants to run the stages over a tree it already has.</summary>
    public static AstPipeline Of(Func<string, bool>? draws = null, (int Start, int Length)? editing = null,
                                 bool holes = false) =>
        new AstPipeline(
            new ExpandMacros(),     // what each shorthand name stands for
            new GatherSigns())      // a sign written as several things, as the one thing it means
            .Then(holes ? new WithHoles(Holds) : null)
            .Then(draws is null ? null : new CheckDrawable(draws))
            .Then(ShowAsWritten.Of(editing));

    /// <summary>
    /// Whether a piece is somewhere content belongs: a braced argument or a table cell — except an argument
    /// its command may take empty.
    /// <para>
    /// Arguments that may be written empty mean "the default" when they are, not "not written yet":
    /// <c>\genfrac{}{}{}{}{a}{b}</c> is a plain fraction, and a hole in each of its first four arguments was
    /// four problems reported against a formula with nothing wrong in it — enough, inline, to show it as
    /// source.
    /// </para>
    /// </summary>
    private static bool Holds(ContentNode? holder, ContentNode node)
    {
        if (node.Kind is not (TexKinds.Group or TexKinds.Cell)) return false;
        if (node.Kind != TexKinds.Group || holder is not { Kind: TexKinds.Command } command) return true;

        var mayBeEmpty = command.Part(Roles.Name)?.Text is { } name && TexCommands.Lookup(name) is { } declared
            ? declared.MayBeEmpty
            : 0;

        var argument = command.Children
            .Where(child => child.Kind == TexKinds.Group)
            .TakeWhile(child => !ReferenceEquals(child, node))
            .Count();

        return argument >= mayBeEmpty;
    }
}
