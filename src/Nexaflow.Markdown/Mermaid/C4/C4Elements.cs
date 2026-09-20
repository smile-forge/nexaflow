using System.Text;

namespace Nexaflow.Markdown.Mermaid.C4;

/// <summary>The C4 abstraction an element sits at, which is what its stereotype says it is.</summary>
public enum C4Level
{
    Person,
    System,
    Container,
    Component,

    /// <summary>A deployment node — a boundary drawn as a box of its own rather than an element.</summary>
    Node,
}

/// <summary>
/// The outline an element is drawn with, which varies apart from its level: a <c>ContainerDb</c> is a Container drawn as a
/// cylinder, and a person is a card with a head above it whatever level the diagram is at.
/// </summary>
public enum C4Shape { Box, Person, Database, Queue }

/// <summary>
/// What an element macro's name says the element is, and what a card then says about itself.
///
/// <para>
/// The macro names are systematic — <c>{Person|System|Container|Component}{Db|Queue}?{_Ext}?</c> — so they are read apart
/// rather than listed, and the same reading serves a structural diagram and a C4 sequence.
/// </para>
/// </summary>
public static class C4Elements
{
    /// <summary>What a macro's name says: the level, the outline, and whether it is somebody else's.</summary>
    public static (C4Level Level, C4Shape Shape, bool External) Sorted(string name)
    {
        var word = name.ToLowerInvariant();
        var external = word.EndsWith("_ext", StringComparison.Ordinal);
        if (external) word = word[..^4];

        foreach (var (prefix, level) in Levels)
        {
            if (!word.StartsWith(prefix, StringComparison.Ordinal)) continue;

            return (level, word[prefix.Length..] switch
            {
                "db" => C4Shape.Database,
                "queue" => C4Shape.Queue,
                _ => level == C4Level.Person ? C4Shape.Person : C4Shape.Box,
            }, external);
        }

        return (C4Level.System, C4Shape.Box, external);
    }

    /// <summary>
    /// The line in brackets under a card's name — C4's stereotype, which nobody writes as one run and which is therefore
    /// worked out rather than copied.
    /// </summary>
    public static string Stereotyped(C4Level level, bool external, string? technology, string? over, bool hidden)
    {
        if (hidden) return string.Empty;

        var says = new StringBuilder("[");
        says.Append(over ?? Worded(level));

        if (external) says.Append(" (external)");
        if (technology is { Length: > 0 }) says.Append(": ").Append(technology.Trim());

        return says.Append(']').ToString();
    }

    /// <summary>What a level is called where it is written out.</summary>
    public static string Worded(C4Level level) => level switch
    {
        C4Level.Person => "Person",
        C4Level.Container => "Container",
        C4Level.Component => "Component",
        C4Level.Node => "Deployment Node",
        _ => "Software System",
    };

    /// <summary>
    /// The keys an <c>UpdateElementStyle</c> may have named this element by, in C4-PlantUML's vocabulary — weakest first, so
    /// a style naming <c>external_container_db</c> lands over one naming <c>container</c>.
    /// </summary>
    public static IEnumerable<string> Keys(C4Level level, C4Shape shape, bool external)
    {
        var kind = Keyed(level);
        var suffix = shape switch { C4Shape.Database => "_db", C4Shape.Queue => "_queue", _ => string.Empty };

        yield return kind;
        if (suffix.Length > 0) yield return kind + suffix;

        if (!external) yield break;

        yield return "external_" + kind;
        if (suffix.Length > 0) yield return "external_" + kind + suffix;
    }

    /// <summary>What an outline a style asks for by name comes to, or what the macro said where it asks for none.</summary>
    public static C4Shape Shaped(C4Shape shape, string? over) => over switch
    {
        "database" or "db" => C4Shape.Database,
        "queue" => C4Shape.Queue,
        "rounded" or "roundedboxshape" or "eightsidedshape" => C4Shape.Box,
        _ => shape,
    };

    /// <summary>
    /// Which band of C4's grading an element takes. C4's information is the grading rather than the particular colours —
    /// the deeper the colour the higher the abstraction, and one colour for "not ours" whatever the level — so what the model
    /// says is which band, and what a band comes to is the drawing's.
    /// </summary>
    public static int Banded(C4Level level, bool external) => external ? External : (int)level;

    /// <summary>The band an element somebody else owns takes, past the levels.</summary>
    public const int External = 5;

    private static string Keyed(C4Level level) => level switch
    {
        C4Level.Person => "person",
        C4Level.Container => "container",
        C4Level.Component => "component",
        C4Level.Node => "node",
        _ => "system",
    };

    private static readonly (string Prefix, C4Level Level)[] Levels =
        [("person", C4Level.Person), ("system", C4Level.System),
         ("container", C4Level.Container), ("component", C4Level.Component)];
}
