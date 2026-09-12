using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Latex.Stages;

/// <summary>
/// Says what is written correctly and still cannot be drawn — a command nothing has a drawing for — and
/// what is written and not finished: a brace opened and never closed, a command short of an argument.
///
/// <para>
/// Asked of whatever is going to set the formula, because what can be drawn is a fact about a typesetter
/// and this is a reader. Marking a piece leaves it the piece it was: an unclosed brace is still a brace
/// and a command short an argument is still that command. What is added is something to say about it,
/// which is what puts a line under it and a reason in the tooltip.
/// </para>
/// </summary>
/// <param name="draws">
/// Whether whatever is going to set this tree knows how to draw a command, given its name as written,
/// backslash and all.
/// </param>
public sealed class CheckDrawable(Func<string, bool> draws) : IAstStage
{
    public string Name => "latex:drawable";

    public ContentNode Run(ContentNode tree) => this.Check(tree);

    private ContentNode Check(ContentNode node)
    {
        if (node.IsLeaf) return node;

        // Only the name is ever shown, never the whole command. `\textrm{Hello}` is a word set in the
        // wrong face, which is a great deal closer to right than a blank, and the argument of something
        // nobody has heard of is usually ordinary maths a reader can see and would miss.
        //
        // A command that resolved to something is drawable by definition, whatever its own name means.
        // \begin and \end are the structure of an environment rather than anything drawn in it, so asking
        // whether a typesetter has a drawing for them is the wrong question — and answering it put a red
        // wave under the \end of every correctly set array.
        var name = node.Kind == TexKinds.Command
                   && node.Role is not (TexRole.Begin or TexRole.End)
                   && node.Part(Roles.Derived) is null
            ? node.Part(Roles.Name)
            : null;

        var unreadable = name is not null && !draws(name.Text);

        // A brace the writer opened and has not closed. The parser reads the group as running to the end
        // of what there is, which is the right reading — it prints back exactly and it still draws — but
        // read is not the same as finished, and something that has to decide whether to act on a formula
        // needs to be told the difference. Nothing else can tell: a recovered group and a closed one are
        // the same shape, and the only trace of the fault is the closing brace that is not there.
        var unclosed = node.Kind == TexKinds.Group && node.Part(Roles.Close) is null
            ? node.Part(Roles.Open)
            : null;

        // A command short of something it takes. `\frac{a}` is read as far as it goes and drawn as far as
        // it goes, and is not yet a fraction. The table already says what each command takes, so this is
        // asking a question that has an answer rather than inventing one.
        var missing = name is not null && TexCommands.Lookup(name.Text) is { } declared
            ? declared.Arguments.FirstOrDefault(role => node.Part(role) is null)
            : null;

        var rebuilt = new List<ContentNode>(node.Children.Count);
        var moved = false;

        foreach (var child in node.Children)
        {
            if (unreadable && ReferenceEquals(child, name))
            {
                rebuilt.Add(ContentNode.Shown(child.Text, $"there is no {child.Text} to draw", Roles.Name));
                moved = true;
                continue;
            }

            if (ReferenceEquals(child, unclosed))
            {
                rebuilt.Add(ContentNode.Leaf(child.Kind, child.Text, child.Role, $"this {child.Text} is never closed"));
                moved = true;
                continue;
            }

            if (missing is not null && ReferenceEquals(child, name))
            {
                rebuilt.Add(ContentNode.Leaf(child.Kind, child.Text, child.Role, $"{child.Text} has no {missing}"));
                moved = true;
                continue;
            }

            var seen = this.Check(child);
            moved |= !ReferenceEquals(seen, child);
            rebuilt.Add(seen);
        }

        return moved ? node.With(rebuilt) : node;
    }
}
