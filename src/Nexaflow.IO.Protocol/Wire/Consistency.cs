using Nexaflow.IO.Protocol.Converters;
using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Values;

namespace Nexaflow.IO.Protocol.Wire;

/// <summary>
/// What a written protocol has to be true of itself, checked when it loads.
/// </summary>
/// <remarks>
/// <para>
/// Every rule here exists because its absence cost something. A graph that is wrong in these ways does not
/// fail where it is wrong — it loads, walks, and produces a message that is the right length and holds a
/// plausible value in the wrong place, or it stalls on a facet nothing will ever settle and reports the
/// facet rather than the cause. Both are hours of reading backwards from a symptom.
/// </para>
/// <para>
/// The one that keeps proving itself: <b>an expression may only name what an edge gives it.</b> A name it
/// has no edge to used to evaluate to nothing, so a length measured nothing, and the failure surfaced
/// three layers away inside a converter as "expected Int, got Null". The scope really is built from the
/// edges — that part was always true — so the gap was never the model, it was that missing was allowed to
/// mean empty.
/// </para>
/// </remarks>
internal static class Consistency
{
    /// <summary>Roots an expression may read that no edge supplies — where the walk is, and what round.</summary>
    private static readonly HashSet<string> Bound =
        new(["item", "ordinal", "remaining", "position"], StringComparer.Ordinal);

    public static void Check(ProtocolGraph graph, IReadOnlyDictionary<string, Node> named,
                             ConverterTable converters)
    {
        Connected(graph);


        foreach (var node in graph.Nodes)
        {
            switch (node)
            {
                case Evaluated evaluated:
                    Parameters(graph, evaluated, evaluated.Runs, converters);
                    Items(graph, evaluated, evaluated.Runs);
                    break;

                case Coded custom: Parameters(graph, custom, null, converters); break;
                case Converted applied: Arguments(graph, converters, applied); break;
                case Field { Via: not null } field: Conversion(converters, field); break;
            }
        }

        Kinds(graph, converters);
    }

    // ── Nothing is stranded ───────────────────────────────────────────────────

    /// <summary>
    /// Every node is joined to the graph by something.
    /// </summary>
    /// <remarks>
    /// There is no such thing as a node that belongs to a protocol and is reached by nothing. One is
    /// either a mistake or a note somebody meant to wire up later — and it reads as neither, because a
    /// description full of prose looks exactly the same whether its nodes are connected or not. Two value
    /// sets shipped in the MQTT description that way, documenting return codes nothing checked.
    /// </remarks>
    private static void Connected(ProtocolGraph graph)
    {
        HashSet<Node> touched = [];

        foreach (var edge in graph.Edges) { touched.Add(edge.From); touched.Add(edge.To); }

        if (graph.Nodes.FirstOrDefault(n => !touched.Contains(n)) is { } stranded)
            throw new ProtoTypeException(
                $"'{stranded.Name}' has no edges at all, so nothing reaches it and it reaches nothing. A "
              + "node joined to the graph by nothing is either a mistake or a note — and a description "
              + "reads the same either way.");
    }

    // ── An expression sees its parameters and nothing else ────────────────────

    /// <summary>
    /// What an expression names and what it declares it takes are the same set, and every parameter is
    /// filled by exactly one edge.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to compare the paths inside the expression against the edges beside it, because where a
    /// value came from was said in both places. Now it is said once — the expression reads a parameter,
    /// the edge fills it — so what is left to check is that the two agree about the parameter's NAME, and
    /// that a kind which cannot go there does not.
    /// </para>
    /// <para>
    /// Both directions still. A name with no parameter is what used to evaluate to nothing; a parameter no
    /// name uses is a declaration that has come adrift from what it was written for, and something waits
    /// on a fact nobody reads.
    /// </para>
    /// </remarks>
    private static void Parameters(ProtocolGraph graph, Computation computation, Expr? runs,
                                   ConverterTable converters)
    {
        HashSet<string> filled = new(StringComparer.Ordinal);

        foreach (var edge in graph.InputsOf(computation))
        {
            if (edge.Parameter is not { } parameter)
                throw new ProtoTypeException(
                    $"'{computation.Name}' is given '{edge.To.Name}' and nothing says which of its "
                  + "parameters that fills. Everything an expression reads is a parameter it declares.");

            if (!computation.Takes.TryGetValue(parameter, out var takes))
                throw new ProtoTypeException(
                    $"'{computation.Name}' is given a '{parameter}', which it does not take. It takes: "
                  + (computation.Takes.Count == 0
                        ? "nothing" : string.Join(", ", computation.Takes.Keys.Order())));

            if (!filled.Add(parameter))
                throw new ProtoTypeException(
                    $"'{computation.Name}' is given two edges for '{parameter}'");

            var gives = Supplies(edge, converters);

            if (gives != ValueKinds.Any && (takes & gives) == 0)
                throw new ProtoTypeException(
                    $"'{computation.Name}' takes {takes} as its '{parameter}', and is handed {gives} by "
                  + $"'{edge.To.Name}'. Both ends of an edge have to agree about the kind of value on it.");
        }

        foreach (var parameter in computation.Takes.Keys)
            if (!filled.Contains(parameter))
                throw new ProtoTypeException(
                    $"'{computation.Name}' takes a '{parameter}' and no edge fills it");

        if (runs is null) return;

        var free = runs.FreeRootNames();

        foreach (var name in free)
            if (!computation.Takes.ContainsKey(name) && !Bound.Contains(name))
                throw new ProtoTypeException(
                    $"'{computation.Name}' reads `{name}`, which it does not take. An expression sees its "
                  + "own parameters and the four the walk binds — item, ordinal, remaining, position — and "
                  + $"nothing else. It runs: {runs.Render()}");

        foreach (var parameter in computation.Takes.Keys)
            if (!free.Contains(parameter))
                throw new ProtoTypeException(
                    $"'{computation.Name}' takes a '{parameter}' and never reads it. It runs: "
                  + runs.Render());
    }

    /// <summary>
    /// What an expression reads of the item it is written once per, against what an item turned out to be.
    /// </summary>
    /// <remarks>
    /// The last name that was not answerable to anything. A set written once per item binds <c>item</c>,
    /// and until a list said what its items look like, <c>item.filter</c> was a member of a record nobody
    /// had described — so a typo in it read as nothing, exactly the way a missing edge used to.
    /// </remarks>
    private static void Items(ProtocolGraph graph, Computation computation, Expr runs)
    {
        var reads = runs.Descendants().OfType<Expr.Member>()
                        .Where(m => m.Target is Expr.Root { Name: "item" })
                        .Select(m => m.Name)
                        .ToList();

        bool bare = runs.FreeRootNames().Contains("item")
                 && runs.Descendants().OfType<Expr.Root>().Any(r => r.Name == "item")
                 && reads.Count == 0;

        if (reads.Count == 0 && !bare) return;

        if (Element(graph, computation) is not { } of)
            throw new ProtoTypeException(
                $"'{computation.Name}' reads `item`, and nothing says what an item is. The list a set is "
              + "written once per item of has to declare its items with `of`.");

        if (bare && of.Members is not null)
            throw new ProtoTypeException(
                $"'{computation.Name}' reads `item` whole, and an item here is {of}");

        foreach (var member in reads)
            if (of.Members is null || !of.Members.ContainsKey(member))
                throw new ProtoTypeException(
                    $"'{computation.Name}' reads `item.{member}`, and an item here is {of}");
    }

    /// <summary>What one item is, for the repetition this computation is written inside.</summary>
    /// <remarks>
    /// Found by asking what the computation produces a fact for, then which repeating set holds that —
    /// the same question the run asks when it binds <c>item</c>, so the two cannot answer differently.
    /// </remarks>
    private static Shape? Element(ProtocolGraph graph, Computation computation)
    {
        if (graph.To<Computes>(computation).FirstOrDefault()?.From is not { } owner) return null;

        foreach (var set in graph.Nodes.OfType<FieldSet>())
            if (graph.Repeating(set) is { } over && Under(graph, set, owner))
                return (over.To as Computation)?.Of;

        return null;
    }

    private static bool Under(ProtocolGraph graph, Node set, Node place)
        => graph.Members(set).Any(m => ReferenceEquals(m, place)
                                    || (m is FieldSet inner && Under(graph, inner, place)));

    // ── Converters get what they take ─────────────────────────────────────────

    private static void Arguments(ProtocolGraph graph, ConverterTable converters, Converted applied)
    {
        if (!converters.TryGet(applied.Applies.Name, out var converter) || converter is null)
            throw new ProtoTypeException(
                $"'{applied.Name}' applies '{applied.Applies.Name}', which is not a converter this knows");

        HashSet<string> given = new(StringComparer.Ordinal);

        foreach (var edge in graph.InputsOf(applied).Where(e => e.Parameter is not null))
        {
            if (!converter.Parameters.Any(x => x.Name == edge.Parameter))
                throw new ProtoTypeException(
                    $"'{applied.Name}' is given a '{edge.Parameter}', which '{converter.Name}' does not "
                  + "take. It takes: "
                  + (converter.Parameters.Count == 0
                        ? "nothing but a value"
                        : string.Join(", ", converter.Parameters)));

            if (!given.Add(edge.Parameter!))
                throw new ProtoTypeException(
                    $"'{applied.Name}' is given two edges for '{edge.Parameter}'");

            var takes = converter.Parameters.First(x => x.Name == edge.Parameter).Kind;
            var gives = Supplies(edge, converters);

            if (gives != ValueKinds.Any && (takes & gives) == 0)
                throw new ProtoTypeException(
                    $"'{converter.Name}' takes {takes} as its '{edge.Parameter}', and is handed {gives} by "
                  + $"'{edge.To.Name}'. Both ends of an edge have to agree about the kind of value on it.");
        }
    }

    /// <summary>
    /// A field's own conversion is one that needs nothing said beside it.
    /// </summary>
    /// <remarks>
    /// A parameter is an edge, and the edges into a field say what follows what. So <c>via</c> covers the
    /// conversions that take only a value — <c>utf8</c>, <c>ipv4</c>, <c>mac</c> — and a conversion that
    /// needs a width is a computation, which has edges. Refused here rather than left to fail at the
    /// moment a message is read, where the missing argument surfaces as an index out of range.
    /// </remarks>
    private static void Conversion(ConverterTable converters, Field field)
    {
        if (!converters.TryGet(field.Via!.Name, out var converter) || converter is null)
            throw new ProtoTypeException(
                $"field '{field.Id}' converts by '{field.Via.Name}', which is not a converter this knows");

        if (converter.Inverse is null)
            throw new ProtoTypeException(
                $"field '{field.Id}' converts by '{converter.Name}', which declares no inverse — so what "
              + "it wrote could never be read back.");

        converters.TryGet(converter.Inverse, out var back);

        if (converter.Parameters.Count > 0 || back?.Parameters.Count > 0)
            throw new ProtoTypeException(
                $"field '{field.Id}' converts by '{converter.Name}', which takes "
              + $"{string.Join(", ", converter.Parameters.Concat(back?.Parameters ?? []).Distinct())} — and "
              + "a field has nowhere for an argument to come from. A conversion that takes one is a "
              + "computation, which has edges.");
    }

    // ── Both ends of an edge agree about the kind ─────────────────────────────

    private static void Kinds(ProtocolGraph graph, ConverterTable converters)
    {
        foreach (var edge in graph.Of<Computes>())
        {
            if (edge.To is not Computation from) continue;

            var gives = Answers(from, converters);

            if (gives == ValueKinds.Any) continue;

            var wants = Wanted(edge, converters);

            if (wants == ValueKinds.Any || (wants & gives) != 0) continue;

            throw new ProtoTypeException(
                $"'{edge.From.Name}' takes its {edge.Facet} from '{from.Name}', which gives "
              + $"{gives} where {wants} is what can go there. Both ends of an edge have to agree "
              + "about the kind of value on it.");
        }

        foreach (var edge in graph.Of<Requires>())
        {
            if (edge.Parameter is not null || edge.From is not Converted applied) continue;
            if (!converters.TryGet(applied.Applies.Name, out var converter) || converter is null) continue;

            var gives = Supplies(edge, converters);

            if (gives == ValueKinds.Any || (converter.Accepts & gives) != 0) continue;

            throw new ProtoTypeException(
                $"'{applied.Name}' applies '{converter.Name}', which takes {converter.Accepts}, and is "
              + $"handed {gives} by '{edge.To.Name}'.");
        }
    }

    /// <summary>What a computation answers with — said on the node, or read off the table for a converter,
    /// which declared both its sides long before anything checked them.</summary>
    private static ValueKinds Answers(Computation computation, ConverterTable converters)
        => computation is Converted applied
        && converters.TryGet(applied.Applies.Name, out var converter) && converter is not null
            ? converter.Produces
            : computation.Gives;

    /// <summary>What can go into the fact a producer is being asked for.</summary>
    private static ValueKinds Wanted(Computes edge, ConverterTable converters) => edge.Facet switch
    {
        "extent" => ValueKinds.Int,
        "presence" => ValueKinds.Bool,
        "each" => ValueKinds.List,
        "value" when edge.From is Field field => Lays(field, converters),
        _ => ValueKinds.Any,
    };

    /// <summary>What a field can lay down, which is its form's kind — or its conversion's, where it has one.</summary>
    private static ValueKinds Lays(Field field, ConverterTable converters)
        => field.Via is { } via && converters.TryGet(via.Name, out var converter) && converter is not null
            ? converter.Accepts
            : field.Form switch
            {
                WireForm.Opaque => ValueKinds.Bytes,
                _ => ValueKinds.Int,
            };

    /// <summary>What a node hands over, for the fact the edge asks of it.</summary>
    private static ValueKinds Supplies(Requires edge, ConverterTable converters) => edge.Facet switch
    {
        "extent" => ValueKinds.Int,
        "emitted" or "octets" => ValueKinds.Bytes,
        "presence" => ValueKinds.Bool,
        "each" => ValueKinds.List,
        "value" => edge.To switch
        {
            Computation computation => computation.Gives,
            Field field => Lays(field, converters),
            _ => ValueKinds.Any,
        },
        _ => ValueKinds.Any,
    };
}
