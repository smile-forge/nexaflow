using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Pipeline;

/// <summary>
/// One actor between the parser and the builder: a tree in, a tree out.
///
/// <para>
/// <strong>The one rule every stage obeys is that the tree still prints as the source it came from.</strong>
/// A stage may nest differently, replace a piece with another piece, or hang something underneath — as
/// long as the characters that come back out are the ones that went in. That is what lets the tree,
/// rather than the string, be the thing that is edited: what is drawn points back at it, an edit changes
/// it, and it can always say what it is in source again.
/// </para>
/// <para>
/// Anything a stage wants to say that the characters do not say is a <em>derived</em> part
/// (<see cref="Roles.Derived"/>): zero width, printing as nothing, hung underneath the piece it is about.
/// That is how a stage records what a macro means, which accidental a note actually prints, or which
/// syllable sits under it, without the round trip having to be re-argued each time.
/// </para>
/// <para>
/// Nothing here is incremental. An edit can put anything anywhere — a closing brace that reshapes
/// everything after it, a bar line that re-bars a whole tune — so "is this edit contained in that piece"
/// is not a question worth trying to answer cheaply. The tree prints itself back and the whole pipeline
/// runs again: one path, always taken, therefore always right.
/// </para>
/// </summary>
public interface IAstStage
{
    /// <summary>What this stage is called, for the failure message when it breaks the one rule.</summary>
    string Name { get; }

    /// <summary>The same content, read further.</summary>
    ContentNode Run(ContentNode tree);
}

/// <summary>An <see cref="IAstStage"/> written as a function, for a stage with no state to keep.</summary>
public sealed class AstStage(string name, Func<ContentNode, ContentNode> run) : IAstStage
{
    public string Name { get; } = name;

    public ContentNode Run(ContentNode tree) => run(tree);
}
