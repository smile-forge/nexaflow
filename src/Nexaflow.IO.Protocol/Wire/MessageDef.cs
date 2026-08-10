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

    /// <summary>Every field in the message, nested ones included, in declaration order.</summary>
    public IEnumerable<Field> AllFields => Descendants(Fields);

    internal static IEnumerable<Field> Descendants(IReadOnlyList<Field> fields)
    {
        foreach (var field in fields)
        {
            yield return field;
            foreach (var nested in Descendants(field.Pattern.Nested)) yield return nested;
        }
    }

    /// <summary>Document-time checks over the whole message.</summary>
    public IReadOnlyList<string> Validate()
    {
        List<string> issues = [];
        Check(Fields, new HashSet<string>(StringComparer.Ordinal), issues);
        return issues;
    }

    /// <summary>
    /// One field scope: the ids declared here plus everything visible from outside it.
    ///
    /// <para>
    /// Ids are flat within a scope, which is what lets an expression say <c>fields.byteCount.extent</c>
    /// without spelling a path through the nesting. A chain opens a new one, because its instances are
    /// separate structures — two instances each declaring a length is not a duplicate, and instance 2's
    /// length must not resolve to instance 1's.
    /// </para>
    /// </summary>
    private void Check(IReadOnlyList<Field> fields, IReadOnlySet<string> outer, List<string> issues)
    {
        // Everything declared at this level, nested scopes excluded — those get their own pass.
        List<Field> here = [];
        Gather(fields, here);

        // Patterns are checked with their scope in hand, so a discriminator reading a bit run can be
        // measured against how wide that run actually is.
        var patterns = here.GroupBy(f => f.Id, StringComparer.Ordinal)
                           .ToDictionary(g => g.Key, g => g.First().Pattern, StringComparer.Ordinal);

        foreach (var field in here) issues.AddRange(field.Pattern.Validate(field.Id, patterns));

        foreach (var duplicate in here.GroupBy(f => f.Id, StringComparer.Ordinal).Where(g => g.Count() > 1))
            issues.Add($"message '{Id}': duplicate field id '{duplicate.Key}' in one scope — ids are how "
                     + "fields reference each other, and they are flat across the regions and arms of a scope");

        var visible = new HashSet<string>(outer, StringComparer.Ordinal);
        visible.UnionWith(here.Select(f => f.Id));

        foreach (var (owner, expression, what) in Expressions(here))
            foreach (var referenced in FieldReferences(expression))
                if (!visible.Contains(referenced.Field))
                    issues.Add($"message '{Id}': {what} of '{owner}' references '{referenced.Field}', "
                             + "which is not a field in scope there");

        // Each chain's element is checked against its own scope, with everything out here still visible:
        // a structure may read the message metadata around it.
        foreach (var chain in here.Select(f => f.Pattern).OfType<Pattern.Chain>())
            Check([chain.Element], visible, issues);
    }

    /// <summary>The ids declared in one scope. Both directions resolve names against this — the validator
    /// to decide what is in scope, the encoder to decide what to qualify.</summary>
    internal static IReadOnlySet<string> ScopeIds(IReadOnlyList<Field> fields)
    {
        List<Field> here = [];
        Gather(fields, here);
        return here.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Fields declared in this scope — descending through regions and arms, stopping at anything
    /// that opens a scope of its own.</summary>
    private static void Gather(IReadOnlyList<Field> fields, List<Field> into)
    {
        foreach (var field in fields)
        {
            into.Add(field);
            if (!field.Pattern.ScopesNames) Gather(field.Pattern.Nested, into);
        }
    }

    /// <summary>Every expression the message carries, with enough context to name it in a diagnostic.</summary>
    private static IEnumerable<(string Owner, Expr Expression, string What)> Expressions(IEnumerable<Field> all)
    {
        foreach (var field in all)
        {
            if (field.Value is not null) yield return (field.Id, field.Value, "the value");

            switch (field.Pattern)
            {
                case Pattern.Choice choice: yield return (field.Id, choice.Key, "the discriminator"); break;
                case Pattern.Chain chain: yield return (field.Id, chain.Continues, "the continuation"); break;
                case Pattern.Opaque { Length: { } length }: yield return (field.Id, length, "the length"); break;
                case Pattern.Group { Extent: { } extent }: yield return (field.Id, extent, "the region bound"); break;
            }
        }
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
