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
    /// The values this message needs from outside itself.
    ///
    /// <para>
    /// Declared, so that what a document requires in order to run is something the graph can be asked
    /// rather than something a reader has to infer by grepping its expressions.
    /// </para>
    /// </summary>
    public IReadOnlyList<Context> Context { get; init; } = [];

    /// <summary>
    /// What a person has to be asked before this message can be built, and why — computed from the graph
    /// rather than maintained beside it, so it cannot drift from what the document actually reads.
    /// </summary>
    public IEnumerable<Context> Asked
        => Graph.Of<Draws>().Select(e => (Context)e.To).Where(c => c.Asked).Distinct();

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

    /// <summary>
    /// A copy starts with no graph.
    /// </summary>
    /// <remarks>
    /// A record's generated copy constructor copies every field, the memoised graph included — so
    /// <c>document with { Fields = … }</c> produced a document describing one field list and answering
    /// graph questions about another. Harmless while the graph only carried containment nobody
    /// interrogated, and a silent wrong answer the moment selection keys moved onto its edges. Putting
    /// facts in the graph means the graph has to be the one for these facts.
    /// </remarks>
    private MessageDef(MessageDef other)
    {
        Id = other.Id;
        Fields = other.Fields;
        Rules = other.Rules;
        Context = other.Context;

        // The same root, not a new one. Field initialisers run for this constructor too, so leaving it
        // out mints a fresh node and every rule pointing at the original's root stops being about
        // anything — identity is the object, which cuts both ways.
        Root = other.Root;
    }

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

        // Order comes from where the rule was written unless it says otherwise, and lands on the EDGE:
        // one rule can constrain several nodes and need not sit in the same place at each of them.
        for (int i = 0; i < Rules.Count; i++)
            foreach (var target in Rules[i].Applies)
                graph.Add(new Constrains
                {
                    From = Rules[i],
                    To = target,
                    Order = Rules[i].Order == int.MaxValue ? i : Rules[i].Order,
                });

        return graph;
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
                        graph.Add(new Offers { From = field, To = arm, Key = arm.DeclaredKey });
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

            void Outside(Expr expression, string role)
            {
                foreach (var key in ContextReferences(expression))
                    if (Context.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.Ordinal)) is { } source)
                        graph.Add(new Draws { From = field, To = source, Role = role });
            }

            if (field.Value is not null) { Link(field.Value, Roles.Value, Visible); Outside(field.Value, Roles.Value); }

            switch (field.Pattern)
            {
                case Pattern.Choice choice:
                    Link(choice.Key, Roles.Discriminator, Visible);

                    if (choice.Selects is { } selects)
                    {
                        Link(selects, Roles.Selection, Visible);
                        Outside(selects, Roles.Selection);
                    }
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
    public IEnumerable<Rule> RulesOn(Node node)
        => Graph.To<Constrains>(node).OrderBy(e => e.Order).Select(e => (Rule)e.From);

    /// <summary>What a choice offers, and on what. Read from the edges, because that is where the key
    /// lives — the arm knows what shape it is, the edge knows what picks it.</summary>
    public IReadOnlyList<Offers> Offered(Node choice) => [.. Graph.From<Offers>(choice)];

    /// <summary>
    /// The packing a discriminator selects, or a refusal naming what arrived and what was declared.
    /// </summary>
    public Arm Choose(Field field, long key)
    {
        var offered = Offered(field);

        var taken = offered.FirstOrDefault(o => o.Key == key)
                 ?? offered.FirstOrDefault(o => o.IsFallback)
                 ?? throw new ProtoTypeException(
                        $"field '{field.Id}': discriminator {key} (0x{key:x}) matches no arm, and none is "
                      + "declared as the fallback. Declared: "
                      + string.Join(", ", offered.Select(o => o.IsFallback ? $"{o.To.Name}=*"
                                                                           : $"{o.To.Name}={o.Key}")));

        return (Arm)taken.To;
    }

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
        if (outer.Count == 0) { CheckRules(issues); CheckContext(issues); CheckVocabulary(issues); }

        CheckChoices(here, issues);

        // A carry is written on the chain and evaluated INSIDE the structure, so it names the structure's
        // fields rather than these. The graph has always known that; this check did not, because until
        // the vocabulary check made the walk enumerate every expression, a carry was never scope-checked
        // at all. Skipped here and checked below against the scope it is actually read in.
        foreach (var (owner, expression, what, site) in Expressions(here))
            foreach (var referenced in FieldReferences(expression))
                if (site is not ExprSite.Carry && !visible.Contains(referenced.Field))
                    issues.Add($"message '{Id}': {what} of '{owner}' references '{referenced.Field}', "
                             + "which is not a field in scope there");

        // Each chain's element is checked against its own scope, with everything out here still visible:
        // a structure may read the message metadata around it.
        foreach (var field in here)
        {
            if (field.Pattern is not Pattern.Chain chain) continue;

            Check([chain.Element], visible, issues);

            var inside = new HashSet<string>(visible, StringComparer.Ordinal);
            inside.UnionWith(ScopeFields([chain.Element]).Select(f => f.Id));

            foreach (var referenced in chain.Carry is null ? [] : FieldReferences(chain.Carry))
                if (!inside.Contains(referenced.Field))
                    issues.Add($"message '{Id}': the carry of '{field.Id}' references "
                             + $"'{referenced.Field}', which is not a field of the structure it runs in");
        }
    }

    private void CheckContext(List<string> issues)
    {
        var declared = Context.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var (owner, expression, what, _) in Expressions(AllFields))
            foreach (var key in ContextReferences(expression).Distinct())
                if (!declared.Contains(key))
                    issues.Add($"message '{Id}': {what} of '{owner.Name}' reads 'inputs.{key}', which the "
                             + "document never says it needs. An undeclared outside value resolves to "
                             + "nothing and surfaces much later as a type error somewhere unrelated.");

        foreach (var duplicate in Context.GroupBy(c => c.Key, StringComparer.Ordinal).Where(g => g.Count() > 1))
            issues.Add($"message '{Id}': 'inputs.{duplicate.Key}' is declared {duplicate.Count()} times");

        foreach (var source in Context.Where(c => c.Asked && string.IsNullOrWhiteSpace(c.Purpose)))
            issues.Add($"message '{Id}': '{source.Key}' is asked of a person and does not say what for. "
                     + "That sentence is the question they get shown.");

        // Declaring a need nothing reads is how a prompt list grows things nobody wants any more.
        var read = Expressions(AllFields).SelectMany(e => ContextReferences(e.Expression)).ToHashSet(StringComparer.Ordinal);

        foreach (var source in Context.Where(c => !read.Contains(c.Key)))
            issues.Add($"message '{Id}': '{source.Key}' is declared and never read");
    }

    private void CheckRules(List<string> issues)
    {
        var known = AllFields.ToHashSet();

        foreach (var rule in Rules)
        {
            foreach (var target in rule.Applies)
            {
                if (target is Field field && !known.Contains(field))
                    issues.Add($"message '{Id}': the rule {rule} is about '{field.Name}', which is not a "
                             + "field of this message. A reference to a field of some OTHER message is the "
                             + "one mistake naming could not make and pointing can.");

                if (target is not Field && !ReferenceEquals(target, Root))
                    issues.Add($"message '{Id}': the rule {rule} is about something that is not part of it");
            }

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

    /// <summary>Every expression the message carries, with enough context to name it in a diagnostic and
    /// the site that decides which of the walk's roots can answer it.</summary>
    private static IEnumerable<(Field Owner, Expr Expression, string What, ExprSite Site)> Expressions(
        IEnumerable<Field> all)
    {
        foreach (var field in all)
        {
            if (field.Value is not null) yield return (field, field.Value, "the value", ExprSite.Value);

            switch (field.Pattern)
            {
                case Pattern.Choice choice:
                    // Paired with an encode-side reading, the key is the reader's question alone and may
                    // use the reader's vocabulary. Alone, it has to answer in both directions.
                    yield return (field, choice.Key, "the discriminator",
                                  choice.Selects is null ? ExprSite.Discriminator : ExprSite.Recognition);

                    if (choice.Selects is { } selects)
                        yield return (field, selects, "the selection", ExprSite.Selection);
                    break;

                case Pattern.Chain chain:
                    yield return (field, chain.Continues, "the continuation", ExprSite.Continuation);
                    if (chain.Seed is { } seed) yield return (field, seed, "the seed", ExprSite.Seeding);
                    if (chain.Carry is { } carry) yield return (field, carry, "the carry", ExprSite.Carry);
                    break;

                case Pattern.Opaque { Length: { } length }:
                    yield return (field, length, "the length", ExprSite.Length);
                    break;

                case Pattern.Group { Extent: { } extent }:
                    yield return (field, extent, "the region bound", ExprSite.Bound);
                    break;
            }
        }
    }

    /// <summary>
    /// Every expression checked against what its site can answer.
    ///
    /// <para>
    /// The check that would have caught three defects in a row, each of which was a name bound at one
    /// scope-construction site and not another — evaluating to nothing and turning every comparison
    /// against it quietly false. See <see cref="Vocabulary"/> for why availability is a property of the
    /// site rather than of the expression.
    /// </para>
    /// </summary>
    /// <summary>
    /// Every choice, checked against the edges that carry its keys.
    ///
    /// <para>
    /// Moved here from the pattern when the keys moved onto the <see cref="Offers"/> edges: a shape cannot
    /// see the graph, and it should not be the authority on a relationship between two nodes anyway. The
    /// substance is unchanged — the arms must be distinct, exhaustive over what the discriminator can
    /// produce, and a fallback must be needed if one is declared.
    /// </para>
    /// </summary>
    private void CheckChoices(IReadOnlyList<Field> here, List<string> issues)
    {
        var patterns = here.GroupBy(f => f.Id, StringComparer.Ordinal)
                           .ToDictionary(g => g.Key, g => g.First().Pattern, StringComparer.Ordinal);

        foreach (var field in here)
        {
            if (field.Pattern is not Pattern.Choice choice || choice.Arms.Count == 0) continue;

            var offered = Offered(field);

            foreach (var duplicate in offered.GroupBy(o => o.To.Name, StringComparer.Ordinal).Where(g => g.Count() > 1))
                issues.Add($"field '{field.Id}': two arms are both named '{duplicate.Key}' — the name is "
                         + "what a later step branches on, so it has to identify one shape");

            foreach (var duplicate in offered.Where(o => !o.IsFallback).GroupBy(o => o.Key!.Value).Where(g => g.Count() > 1))
                issues.Add($"field '{field.Id}': discriminator {duplicate.Key} selects "
                         + $"{duplicate.Count()} arms — which one applies is not decidable");

            if (offered.Count(o => o.IsFallback) > 1)
                issues.Add($"field '{field.Id}': only one arm may be the fallback");

            var fallback = offered.FirstOrDefault(o => o.IsFallback);

            // Both readings must land on the same arms, so both are proved. A pair that disagreed about
            // its own keyset would be two discriminators wearing one name.
            List<(string Reading, Expr Deciding)> readings = [("discriminator", choice.Key)];
            if (choice.Selects is { } selects) readings.Add(("selection", selects));

            foreach (var (reading, deciding) in readings)
            {
                var reachable = Pattern.ReachableKeys(deciding, patterns);

                if (reachable is null)
                {
                    if (fallback is null)
                        issues.Add($"field '{field.Id}': the engine cannot compute which values this "
                                 + $"{reading} can take, so it cannot prove the arms are exhaustive. "
                                 + "Declare a fallback arm (key null). An unanticipated discriminator "
                                 + "otherwise binds no fields and reports no error, which is worse than "
                                 + "either outcome you would have chosen.");
                    continue;
                }

                foreach (var arm in offered.Where(o => !o.IsFallback))
                    if (!reachable.Contains(arm.Key!.Value))
                        issues.Add($"field '{field.Id}': arm '{arm.To.Name}' is keyed {arm.Key} "
                                 + $"(0x{arm.Key:x}), which this {reading} can never produce — a dead arm "
                                 + "is a mistake about the mask, not a harmless extra");

                var covered = offered.Where(o => !o.IsFallback).Select(o => o.Key!.Value).ToHashSet();
                var missing = reachable.Where(k => !covered.Contains(k)).OrderBy(k => k).ToList();

                if (missing.Count > 0 && fallback is null)
                    issues.Add($"field '{field.Id}': the arms are not exhaustive — nothing handles "
                             + string.Join(", ", missing.Select(k => $"0x{k:x}"))
                             + ". Add the arms, or declare a fallback.");

                if (missing.Count == 0 && fallback is not null)
                    issues.Add($"field '{field.Id}': arm '{fallback.To.Name}' is the fallback, but the "
                             + $"other arms already cover every value this {reading} can take, so it can "
                             + "never be selected");
            }
        }
    }

    private void CheckVocabulary(List<string> issues)
    {
        IEnumerable<(Node Owner, Expr Expression, string What, ExprSite Site)> everywhere =
        [
            .. Expressions(AllFields).Select(e => ((Node)e.Owner, e.Expression, e.What, e.Site)),
            .. Rules.SelectMany(rule => rule.Expressions.Select(
                   e => ((Node)rule, e, "a condition",
                         rule is Rule.Arrangement ? ExprSite.Pairing : ExprSite.Condition))),
        ];

        foreach (var (owner, expression, what, site) in everywhere)
        {
            var answerable = Vocabulary.Available(site);

            foreach (var root in Vocabulary.RootsOf(expression))
            {
                if (answerable.Contains(root)) continue;

                issues.Add(Vocabulary.All.Contains(root)
                    ? $"message '{Id}': {what} of '{owner.Name}' names `{root}`, and "
                    + $"{Vocabulary.Why(root, site)}."
                    : $"message '{Id}': {what} of '{owner.Name}' names `{root}`, which is not part of the "
                    + "walk's vocabulary at all. A root nothing binds reads as nothing and makes every "
                    + $"comparison against it false. The ones that exist are: {string.Join(", ", Vocabulary.All.Order())}.");
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

    /// <summary>The outside values an expression reads. <c>item</c>, <c>carried</c>, <c>room</c> and the
    /// rest are the walk's own vocabulary and are not context; they come from the message.</summary>
    internal static IEnumerable<string> ContextReferences(Expr e)
    {
        foreach (var node in e.Descendants())
            if (node is Expr.Member { Target: Expr.Root { Name: "inputs" } } member)
                yield return member.Name;
    }
}
