using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid.Sequence;

namespace Nexaflow.Markdown.Mermaid.C4;

/// <summary>
/// One macro call read off a line: its name, and its arguments in the order they were written.
///
/// <para>
/// C4-PlantUML's macro set is one shape throughout — <c>Person(customer, "Banking Customer", "A customer")</c> — and its
/// signatures are long, so a caller skips the middle of one by naming what it wants: <c>$techn="JDBC"</c>. So an argument
/// is asked for by position <em>and</em> by name, and a name wins, which is what <see cref="Argument"/> does.
/// </para>
///
/// <para>
/// A name is read under <see cref="SequenceRoles.Id"/> rather than a role of C4's own, because in a <c>C4Sequence</c> the
/// very same element is named in a native <c>note over</c> or <c>activate</c>: one role for a name in both dialects is what
/// lets renaming it carry to every line that uses it.
/// </para>
/// </summary>
internal sealed record C4Macro(string Name, IReadOnlyList<C4Argument> Arguments)
{
    /// <summary>The call a line states, or one called nothing where it states none.</summary>
    public static C4Macro Of(ContentNode stated)
    {
        var name = stated.SelfAndDescendants()
                         .FirstOrDefault(node => node.Kind == MermaidKinds.Key && node.Role == C4Roles.Macro)?.Text
                   ?? string.Empty;

        var arguments = new List<C4Argument>();

        foreach (var property in stated.SelfAndDescendants().Where(node => node.Kind == MermaidKinds.Property))
        {
            var key = Inner(property, C4Roles.Key);
            var value = Inner(property, C4Roles.Value) ?? Inner(property, SequenceRoles.Id);

            arguments.Add(new C4Argument(key?.Text, value, property));
        }

        return new C4Macro(name, arguments);
    }

    /// <summary>
    /// The argument at a position or under a name: a name wins, which is how a caller skips the middle of a long signature.
    /// Null where it was given neither way.
    /// </summary>
    public C4Argument? Argument(int position, string name)
    {
        foreach (var argument in this.Arguments)
            if (string.Equals(argument.Key, name, StringComparison.OrdinalIgnoreCase))
                return argument;

        var at = 0;
        foreach (var argument in this.Arguments)
        {
            if (argument.Key is not null) continue;
            if (at++ == position) return argument;
        }

        return null;
    }

    /// <summary>What the argument at a position or under a name says — null where it was given neither way, or given empty.</summary>
    public string? Said(int position, string name) => this.Argument(position, name)?.Value is { Width: > 0 } value ? value.Text : null;

    /// <summary>What an argument given by name says, where it was given by name and nowhere else.</summary>
    public string? Named(string name)
    {
        foreach (var argument in this.Arguments)
            if (string.Equals(argument.Key, name, StringComparison.OrdinalIgnoreCase))
                return argument.Value is { Width: > 0 } said ? said.Text : null;

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

    private static ContentNode? Inner(ContentNode property, string role) =>
        property.SelfAndDescendants().FirstOrDefault(node => node.Kind == MermaidKinds.Words && node.Role == role);
}

/// <summary>
/// One argument of a call: the name it was given under where it was given under one, what it says, and the whole of it as
/// written, which is what a stage says it means on.
/// </summary>
internal sealed record C4Argument(string? Key, ContentNode? Value, ContentNode Property);
