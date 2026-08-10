using Nexaflow.IO.Protocol.Expressions;

namespace Nexaflow.IO.Protocol.Wire;

/// <summary>
/// One field of a message: a shape, where its value comes from on encode, and what it binds to on decode.
///
/// <para>
/// The same declaration serves both directions. That is the property the whole design rests on — two
/// descriptions kept in step by hand is how a parser generator ends up unable to serialise, and how a
/// production packet language ends up needing a hand-written second pass for the outbound path.
/// </para>
/// </summary>
public sealed record Field
{
    /// <summary>Referenceable name — <c>fields.&lt;id&gt;.value</c> and <c>fields.&lt;id&gt;.extent</c>.</summary>
    public required string Id { get; init; }

    public required Pattern Pattern { get; init; }

    /// <summary>
    /// Encode side. Its referenced fields become resolver dependencies automatically, which is what lets a
    /// length that measures a later region schedule without a placeholder or a back-patch pass.
    /// </summary>
    public Expr? Value { get; init; }

    /// <summary>Decode side: the capture name. Defaults to <see cref="Id"/>; a bit group's slices name
    /// themselves.</summary>
    public string? As { get; init; }

    /// <summary>A converter applied after reading and inverted before writing — a fixed-point scale, a
    /// text codec.</summary>
    public string? Via { get; init; }

    public string CaptureName => As ?? Id;
}

/// <summary>An ordered field list. Ordering is explicit here; free arrangement is a packing concern and
/// is deliberately not expressible at this level.</summary>
public sealed record MessageDef
{
    public required string Id { get; init; }
    public required IReadOnlyList<Field> Fields { get; init; }

    /// <summary>Document-time checks over the whole message.</summary>
    public IReadOnlyList<string> Validate()
    {
        List<string> issues = [];

        foreach (var f in Fields) issues.AddRange(f.Pattern.Validate(f.Id));

        var duplicates = Fields.GroupBy(f => f.Id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1).Select(g => g.Key);
        foreach (var d in duplicates)
            issues.Add($"message '{Id}': duplicate field id '{d}' — ids are how fields reference each other");

        // A field referencing one that does not exist is an authoring slip that would otherwise surface as
        // an unresolvable dependency much later, with a worse message.
        var known = Fields.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var f in Fields.Where(f => f.Value is not null))
            foreach (var referenced in FieldReferences(f.Value!))
                if (!known.Contains(referenced.Field))
                    issues.Add($"message '{Id}': field '{f.Id}' references '{referenced.Field}', which is "
                             + "not a field of this message");

        return issues;
    }

    /// <summary>
    /// The fields an expression reads, with the facet it needs of each. Dependencies are <b>derived from
    /// the expression</b> rather than hand-declared alongside it — hand-drawn edges were the reason a
    /// protocol with no declared dependencies at all had nothing the worklist could see.
    /// </summary>
    internal static IEnumerable<(string Field, string Facet)> FieldReferences(Expr e)
    {
        foreach (var node in e.Descendants())
            if (node is Expr.Member { Target: Expr.Member { Target: Expr.Root { Name: "fields" } } inner } outer)
                yield return (inner.Name, outer.Name);
    }
}
