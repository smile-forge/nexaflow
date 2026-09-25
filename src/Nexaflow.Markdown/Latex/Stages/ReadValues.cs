using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Latex.Stages;

/// <summary>
/// Says what each argument that holds a value rather than maths amounts to — a colour's name and the model it is written
/// in, a length, an equation's number, the columns of an <c>array</c> — as what is written between its braces, hung beneath
/// it (<see cref="TexRole.Value"/>).
///
/// <para>
/// A value is read as its characters, and which characters a braced argument holds is a fact about the reading. So it is
/// worked out once here, where printing a piece back is the reader's own business, and whatever sets the formula is told
/// the value rather than printing the formula back to find it.
/// </para>
/// </summary>
public sealed class ReadValues : IAstStage
{
    /// <summary>The commands whose arguments are values rather than maths.</summary>
    private static readonly HashSet<string> Valued = new(StringComparer.Ordinal)
    {
        @"\tag", @"\tag*",
        @"\color", @"\textcolor", @"\colorbox",
        @"\hspace", @"\hspace*", @"\kern", @"\mkern", @"\mspace",
        @"\cfrac",
    };

    public string Name => "tex:values";

    public ContentNode Run(ContentNode tree) => AstRewrite.Each(tree, node => node.Kind switch
    {
        TexKinds.Command when node.Children.FirstOrDefault(child => child.Role == Roles.Name)?.Text is { } name && Valued.Contains(name)
            => Read(node, TexRole.Argument, TexRole.Option),
        TexKinds.Environment when node.Part(TexRole.Begin) is { } begin && TexParser.NameOf(begin) == "array" => Read(node, TexRole.Option),
        _ => node,
    });

    /// <summary>The holder, with the value each of its arguments in these roles holds hung beneath that argument.</summary>
    private static ContentNode Read(ContentNode holder, params string[] roles) =>
        holder.With([.. holder.Children.Select(child =>
            roles.Contains(child.Role) && !child.IsLeaf && child.Said(TexRole.Value) is null
                ? child.Saying(TexKinds.Value, TexRole.Value, Inside(child))
                : child)]);

    /// <summary>What a braced or bracketed argument holds as written: its brackets left off, everything else exactly as typed.</summary>
    private static string Inside(ContentNode argument) =>
        string.Concat(argument.Children.Where(child => child.Role is not (Roles.Open or Roles.Close)).Select(child => child.Print()));
}

