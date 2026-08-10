using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Transforms;
using Nexaflow.IO.Protocol.Values;

namespace Nexaflow.IO.Protocol.Wire;

/// <summary>
/// A converter applied to a field, with the arguments it needs.
///
/// <para>
/// The arguments are the reason this is not just a name. A converter that carries a protocol's constant as
/// a default is a protocol specific hiding in the engine, so every converter that has a choice to make
/// demands it be stated — a group order, a zero rule, a fraction width. A field using one therefore has to
/// be able to state it too, and a bare name cannot.
/// </para>
/// </summary>
public sealed record Conversion(string Name, IReadOnlyList<ProtoValue> Args)
{
    /// <summary>So the common no-argument case still reads as <c>Via = "unascii"</c>. Null in, null out:
    /// a document that computes its converter name and gets nothing means <i>no conversion</i>, not a
    /// conversion with no name.</summary>
    public static implicit operator Conversion?(string? name) => name is null ? null : new(name, []);

    public static Conversion Of(string name, params long[] args)
        => new(name, [.. args.Select(a => ProtoValue.Of(a))]);

    public override string ToString() => Args.Count == 0 ? Name : $"{Name}({string.Join(", ", Args)})";
}

/// <summary>
/// One field of a message: a shape, where its value comes from on encode, and what it binds to on decode.
///
/// <para>
/// The same declaration serves both directions. That is the property the whole design rests on — two
/// descriptions kept in step by hand is how a parser generator ends up unable to serialise, and how a
/// production packet language ends up needing a hand-written second pass for the outbound path.
/// </para>
/// </summary>
public sealed class Field : Node
{
    /// <summary>Referenceable name — <c>fields.&lt;id&gt;.value</c> and <c>fields.&lt;id&gt;.extent</c>.
    /// A name for expressions and for reading, never how anything finds this field.</summary>
    public required string Id { get; init; }

    public override string Name => Id;

    public required Pattern Pattern { get; init; }

    /// <summary>
    /// Encode side. Its referenced fields become resolver dependencies automatically, which is what lets a
    /// length that measures a later region schedule without a placeholder or a back-patch pass.
    /// </summary>
    public Expr? Value { get; init; }

    /// <summary>Decode side: the capture name. Defaults to <see cref="Id"/>; a bit group's slices name
    /// themselves.</summary>
    public string? As { get; init; }

    /// <summary>A converter applied on the way out and inverted on the way in — a fixed-point scale, a
    /// text codec.</summary>
    public Conversion? Via { get; init; }

    /// <summary>
    /// A <b>document-authored</b> transform, applied on the same slot as <see cref="Via"/> but further from
    /// the wire.
    ///
    /// <para>
    /// This is how an encoding rule that belongs to one family reaches a field without living in the
    /// engine. The engine's converters are notions — arithmetic, bit operations, byte sequences; a
    /// composition of them that is one family's rule is written down as a transform, in a document, with
    /// its domain declared. If it were a converter instead, it would be a protocol specific with a general
    /// name, which is the thing the whole model is arranged to prevent.
    /// </para>
    /// </summary>
    public Transform? Through { get; init; }

    public string CaptureName => As ?? Id;
}

/// <summary>An ordered field list. Ordering is explicit here; free arrangement is a packing concern and
/// is deliberately not expressible at this level.</summary>
public sealed record MessageDef
{
    public required string Id { get; init; }
    public required IReadOnlyList<Field> Fields { get; init; }

    /// <summary>
    /// What makes a structurally-valid message illegal anyway.
    ///
    /// <para>
    /// Checked in both directions, after every field is settled — a rule that only ran on decode would let
    /// the engine emit a message it would itself refuse to read.
    /// </para>
    /// </summary>
    public IReadOnlyList<Rule> Rules { get; init; } = [];

    /// <summary>
    /// The message as nodes and relationships.
    ///
    /// <para>
    /// Built once from the declaration, which is a readable way to say a common shape rather than the
    /// model itself. Nesting becomes containment; an expression naming another node becomes a read; a
    /// length and the region it sizes become one edge instead of two expressions that had nothing to do
    /// with each other.
    /// </para>
    /// </summary>
    public ProtocolGraph Graph => _graph ??= Build();
    private ProtocolGraph? _graph;

    /// <summary>Stands for the message itself, so a rule about the whole thing has something to point at
    /// rather than being attached by convention.</summary>
    public Node Root { get; } = new MessageRoot();

    private sealed class MessageRoot : Node
    {
        public override string Name => "message";
    }

    private ProtocolGraph Build()
    {
        var graph = new ProtocolGraph();
        graph.Add(Root);

        Wire(graph, Root, Fields);

        // Every reference an expression makes, materialised in the scope it was written in. Recovering
        // these by scanning text at each encode is what let a reference into an unrealised arm survive
        // until run time.
        LinkReads(graph, ScopeFields(Fields), null);

        foreach (var rule in Rules)
            graph.Add(new Constrains { From = new RuleNode(rule), To = rule.Subject });

        return graph;
    }

    /// <summary>A rule is a node too, so the thing doing the constraining is as addressable as the thing
    /// constrained.</summary>
    private sealed class RuleNode(Rule rule) : Node
    {
        public Rule Rule { get; } = rule;
        public override string Name => Rule.ToString() ?? "rule";
    }

    private static void Wire(ProtocolGraph graph, Node parent, IReadOnlyList<Field> fields)
    {
        for (int i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            graph.Add(new Contains { From = parent, To = field, Ordinal = i });

            switch (field.Pattern)
            {
                case Pattern.Group group:
                    Wire(graph, field, group.Fields);
                    break;

                case Pattern.Choice choice:
                    foreach (var arm in choice.Arms)
                    {
                        graph.Add(new Offers { From = field, To = arm });
                        Wire(graph, arm, arm.Fields);
                    }
                    break;

                case Pattern.Chain chain:
                    graph.Add(new Repeats { From = field, To = chain.Element });
                    Wire(graph, field, [chain.Element]);
                    break;
            }
        }
    }

    /// <summary>
    /// Resolves every expression's references once, in the scope the expression was written in.
    ///
    /// <para>
    /// A chain's <i>carry</i> is the one that reads outside its own scope, and inward rather than outward:
    /// it is written on the chain but evaluated inside the structure, so it names the structure's fields.
    /// Nothing about the text says that; the graph has to.
    /// </para>
    /// </summary>
    private void LinkReads(ProtocolGraph graph, IReadOnlyList<Field> here, IReadOnlyList<Field>? outer)
    {
        Field? Visible(string name)
            => here.FirstOrDefault(f => string.Equals(f.Id, name, StringComparison.Ordinal))
            ?? outer?.FirstOrDefault(f => string.Equals(f.Id, name, StringComparison.Ordinal));

        foreach (var field in here)
        {
            void Link(Expr expression, string role, Func<string, Field?> lookup)
            {
                foreach (var (name, facet) in FieldReferences(expression))
                    if (lookup(name) is { } target)
                        graph.Add(new Reads { From = field, To = target, Facet = facet, Role = role });
            }

            if (field.Value is not null) Link(field.Value, Roles.Value, Visible);

            switch (field.Pattern)
            {
                case Pattern.Choice choice:
                    Link(choice.Key, Roles.Discriminator, Visible);
                    break;

                case Pattern.Opaque { Length: { } length }:
                    Link(length, Roles.Length, Visible);
                    break;

                case Pattern.Group { Extent: { } extent }:
                    Link(extent, Roles.Bound, Visible);
                    break;

                case Pattern.Chain chain:
                {
                    Link(chain.Continues, Roles.Continuation, Visible);
                    if (chain.Seed is not null) Link(chain.Seed, Roles.Seed, Visible);

                    var inside = ScopeFields([chain.Element]);

                    if (chain.Carry is not null)
                        Link(chain.Carry, Roles.Carry,
                             n => inside.FirstOrDefault(f => string.Equals(f.Id, n, StringComparison.Ordinal)));

                    LinkReads(graph, inside, [.. here, .. outer ?? []]);
                    break;
                }
            }
        }
    }

    /// <summary>Every field in the message, nested ones included, in declaration order.</summary>
    public IEnumerable<Field> AllFields => Descendants(Fields);

    /// <summary>The rules that apply to one node. A reference lookup, so a rule on a segment inside a
    /// repeated structure is found every time that structure is realised.</summary>
    public IEnumerable<Rule> RulesOn(Node node) => Rules.Where(r => ReferenceEquals(r.Subject, node));

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

        foreach (var field in here)
        {
            issues.AddRange(field.Pattern.Validate(field.Id, patterns));

            // A transform is document-authored, so it is checked here rather than trusted — containment,
            // totality and a declared domain, the same bar a transform faces anywhere else.
            if (field.Through is { } transform)
                issues.AddRange(transform.Validate().Select(i => $"field '{field.Id}': {i}"));
        }

        foreach (var duplicate in here.GroupBy(f => f.Id, StringComparer.Ordinal).Where(g => g.Count() > 1))
            issues.Add($"message '{Id}': duplicate field id '{duplicate.Key}' in one scope — ids are how "
                     + "fields reference each other, and they are flat across the regions and arms of a scope");

        // Bit runs bind into the same flat namespace as fields, so two groups each naming a run `seconds`
        // would overwrite one another and the second would look like the first had moved. Found by an
        // audit against a specification, which is exactly the sort of thing a hand-built corpus misses.
        var taken = new HashSet<string>(here.Select(f => f.Id), StringComparer.Ordinal);

        foreach (var field in here)
            foreach (var slice in (field.Pattern as Pattern.Bits)?.Slices ?? [])
                if (!taken.Add(slice.Name))
                    issues.Add($"message '{Id}': the bit run '{slice.Name}' in '{field.Id}' collides with "
                             + "another name in this scope — runs bind alongside fields, not underneath them");

        var visible = new HashSet<string>(outer, StringComparer.Ordinal);
        visible.UnionWith(here.Select(f => f.Id));

        // Rules are checked once, against the graph, because a reference does not need a scope to be
        // resolved in — which is the point of it being a reference.
        if (outer.Count == 0) CheckRules(issues);

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

    private void CheckRules(List<string> issues)
    {
        var known = AllFields.ToHashSet();

        foreach (var rule in Rules)
        {
            if (rule.Subject is Field subject && !known.Contains(subject))
                issues.Add($"message '{Id}': the rule {rule} is about '{subject.Name}', which is not a "
                         + "field of this message. A reference to a field of some OTHER message is the one "
                         + "mistake naming could not make and pointing can.");

            if (rule.Subject is not Field && !ReferenceEquals(rule.Subject, Root))
                issues.Add($"message '{Id}': the rule {rule} is about something that is not part of it");

            if (rule is Rule.Domain domain)
            {
                if (domain.Allowed.Count == 0)
                    issues.Add($"message '{Id}': the value rule on '{domain.What}' allows nothing, so no "
                             + "message can satisfy it");

                if (domain.Run is { } run
                    && (domain.Field.Pattern is not Pattern.Bits bits
                        || !bits.Slices.Any(s => string.Equals(s.Name, run, StringComparison.Ordinal))))
                    issues.Add($"message '{Id}': the value rule names the run '{run}', which "
                             + $"'{domain.Field.Name}' does not have");
            }

            if (string.IsNullOrWhiteSpace(rule.Because))
                issues.Add($"message '{Id}': the rule {rule} does not say why. A refusal a reader cannot "
                         + "act on is barely better than none.");
        }
    }

    /// <summary>The ids declared in one scope. Both directions resolve names against this — the validator
    /// to decide what is in scope, the encoder to decide what to qualify.</summary>
    internal static IReadOnlyList<Field> ScopeFields(IReadOnlyList<Field> fields)
    {
        List<Field> here = [];
        Gather(fields, here);
        return here;
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
    private static IEnumerable<(Field Owner, Expr Expression, string What)> Expressions(IEnumerable<Field> all)
    {
        foreach (var field in all)
        {
            if (field.Value is not null) yield return (field, field.Value, "the value");

            switch (field.Pattern)
            {
                case Pattern.Choice choice: yield return (field, choice.Key, "the discriminator"); break;
                case Pattern.Chain chain: yield return (field, chain.Continues, "the continuation"); break;
                case Pattern.Opaque { Length: { } length }: yield return (field, length, "the length"); break;
                case Pattern.Group { Extent: { } extent }: yield return (field, extent, "the region bound"); break;
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
