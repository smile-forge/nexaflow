using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid.Sequence;

namespace Nexaflow.Markdown.Mermaid.C4;

/// <summary>
/// One macro call read back off the tree: its name, and its arguments in the order they were written.
///
/// <para>
/// C4-PlantUML's macro set is one shape throughout — <c>Person(customer, "Banking Customer", "A customer")</c> — and its
/// signatures are long, so a caller skips the middle of one by naming what it wants: <c>$techn="JDBC"</c>. So an argument
/// is asked for by position <em>and</em> by name, and a name wins, which is what <see cref="Part"/> does.
/// </para>
///
/// <para>
/// A name is read under <see cref="SequenceRoles.Id"/> rather than a role of C4's own, because in a <c>C4Sequence</c> the
/// very same element is named in a native <c>note over</c> or <c>activate</c>: one role for a name in both dialects is what
/// lets renaming it carry to every line that uses it.
/// </para>
/// </summary>
public sealed record C4Macro(string Name, IReadOnlyList<C4Argument> Arguments)
{
    /// <summary>The call a line states, or one called nothing where it states none.</summary>
    public static C4Macro Of(ContentPart stated)
    {
        var name = stated.SelfAndDescendants()
                         .FirstOrDefault(part => part.Kind == MermaidKinds.Key && part.Role == C4Roles.Macro)?.Text
                   ?? string.Empty;

        var arguments = new List<C4Argument>();

        foreach (var property in stated.SelfAndDescendants().Where(part => part.Kind == MermaidKinds.Property))
        {
            var key = Inner(property, C4Roles.Key);
            var value = Inner(property, C4Roles.Value) ?? Inner(property, SequenceRoles.Id);

            arguments.Add(new C4Argument(key?.Text, value));
        }

        return new C4Macro(name, arguments);
    }

    /// <summary>
    /// The argument at a position or under a name: a name wins, which is how a caller skips the middle of a long signature.
    /// Null where it was given neither way, or given empty.
    /// </summary>
    public ContentPart? Part(int position, string name)
    {
        foreach (var argument in this.Arguments)
            if (string.Equals(argument.Key, name, StringComparison.OrdinalIgnoreCase))
                return argument.Value is { Length: > 0 } ? argument.Value : null;

        var at = 0;
        foreach (var argument in this.Arguments)
        {
            if (argument.Key is not null) continue;
            if (at++ != position) continue;

            return argument.Value is { Length: > 0 } ? argument.Value : null;
        }

        return null;
    }

    /// <summary>What that argument says.</summary>
    public string? Said(int position, string name) => this.Part(position, name)?.Text;

    /// <summary>What an argument given by name says, where it was given by name and nowhere else.</summary>
    public string? Named(string name)
    {
        foreach (var argument in this.Arguments)
            if (string.Equals(argument.Key, name, StringComparison.OrdinalIgnoreCase))
                return argument.Value is { Length: > 0 } said ? said.Text : null;

        return null;
    }

    /// <summary>A flag argument: absent means yes, and only <c>false</c> or <c>0</c> means no.</summary>
    public bool Flag(int position, string name)
    {
        if (this.Said(position, name) is not { } said) return true;

        return !said.Equals("false", StringComparison.OrdinalIgnoreCase) && said != "0";
    }

    /// <summary>A whole number written as an argument, where one is.</summary>
    public static int? Number(string? said) => MermaidNumber.Read(said) is { } value ? (int)value : null;

    /// <summary>The tags of a <c>$tags="a+b"</c>, which C4-PlantUML joins with a plus.</summary>
    public static IReadOnlyList<string> Tagged(string? tags) =>
        tags is { Length: > 0 }
            ? [.. tags.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)]
            : [];

    private static ContentPart? Inner(ContentPart property, string role) =>
        property.SelfAndDescendants().FirstOrDefault(part => part.Kind == MermaidKinds.Words && part.Role == role);
}

/// <summary>One argument of a call: the name it was given under where it was given under one, and what it says.</summary>
public sealed record C4Argument(string? Key, ContentPart? Value);
