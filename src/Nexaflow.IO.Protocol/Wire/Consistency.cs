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

    /// <summary>Roots that name something an edge has to reach.</summary>
    private static readonly HashSet<string> Reached =
        new(["fields", "sets", "inputs", "state"], StringComparer.Ordinal);

    public static void Check(ProtocolGraph graph, IReadOnlyDictionary<string, Node> named,
                             ConverterTable converters)
    {
        Connected(graph);

        foreach (var node in graph.Nodes)
        {
            switch (node)
            {
                case Evaluated evaluated: Reaches(graph, named, evaluated); break;
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

    // ── An expression sees its edges and nothing else ─────────────────────────

    /// <summary>
    /// What an expression names, and what its edges give it, are the same set.
    /// </summary>
    /// <remarks>
    /// Both directions. A name with no edge is the failure that evaluates to nothing; an edge no name uses
    /// is a declaration that has come adrift from the expression it was written for, which is how a
    /// computation ends up waiting on a facet it no longer reads.
    /// </remarks>
    private static void Reaches(ProtocolGraph graph, IReadOnlyDictionary<string, Node> named,
                                Evaluated evaluated)
    {
        var wanted = Named(evaluated.Runs);

        HashSet<string> supplied = new(StringComparer.Ordinal);

        foreach (var edge in graph.InputsOf(evaluated))
        {
            string root = edge.To switch
            {
                Context.Input => "inputs",
                Context.State => "state",
                FieldSet => "sets",
                _ => "fields",
            };

            // An outside value is reached whole; a part of the message is asked for one fact about it, and
            // which fact is what the edge says.
            supplied.Add(root is "inputs" or "state"
                ? $"{root}.{Plainly(edge.To.Name)}"
                : $"{root}.{edge.To.Name}.{Settles(edge.Facet)}");
        }

        foreach (var name in wanted)
            if (!supplied.Contains(name))
                throw new ProtoTypeException(
                    $"'{evaluated.Name}' reads `{name}` and has no edge that gives it. An expression sees "
                  + "exactly what its own edges supply, so this one evaluates to nothing — and a length "
                  + $"measuring nothing fails somewhere else entirely. It runs: {evaluated.Runs.Render()}");

        foreach (var name in supplied)
            if (!wanted.Contains(name))
                throw new ProtoTypeException(
                    $"'{evaluated.Name}' requires `{name}` and never reads it. It runs: "
                  + $"{evaluated.Runs.Render()}");

        // And what it names has to be there — a check the edge already made, kept here because the message
        // is about the expression rather than about an edge somebody would then go looking for.
        foreach (var name in wanted.Where(n => n.StartsWith("fields.", StringComparison.Ordinal)))
            if (!named.ContainsKey(name.Split('.')[1]))
                throw new ProtoTypeException($"'{evaluated.Name}' reads `{name}`, and no node is called that");
    }

    /// <summary>
    /// Every path an expression reads that an edge would have to supply, as <c>root.name.facet</c>.
    /// </summary>
    /// <remarks>
    /// <b>An id with a dot in it cannot be spelled here.</b> <c>sets.a.b.extent</c> is member access three
    /// deep, so a set called <c>a.b</c> is unreachable by the only syntax there is to reach it with — which
    /// is why this yields <c>sets.a.b</c> and the edge check then refuses it by name, instead of the
    /// expression quietly finding nothing.
    /// </remarks>
    private static HashSet<string> Named(Expr runs)
    {
        HashSet<string> found = new(StringComparer.Ordinal);
        var free = runs.FreeRootNames();

        // The whole chain, not every prefix of it. `sets.header.extent` contains `sets.header` as a
        // sub-expression, and counting that too asks for a fact nobody named.
        HashSet<Expr> inner = [.. runs.Descendants().OfType<Expr.Member>().Select(m => m.Target)];

        foreach (var member in runs.Descendants().OfType<Expr.Member>())
        {
            if (inner.Contains(member)) continue;
            if (Bottom(member) is not { } root || !Reached.Contains(root.Name)) continue;
            if (!free.Contains(root.Name)) continue;

            var path = Path(member);

            // `inputs.x` and `state.x` are whole; `fields.x` and `sets.x` are asked for a particular fact,
            // and which fact is what the edge says.
            found.Add(root.Name is "inputs" or "state"
                ? string.Join('.', path.Take(2))
                : string.Join('.', path.Take(3)));
        }

        return found;
    }

    private static Expr.Root? Bottom(Expr at) => at switch
    {
        Expr.Root root => root,
        Expr.Member member => Bottom(member.Target),
        _ => null,
    };

    private static List<string> Path(Expr at)
    {
        List<string> parts = [];

        for (var here = at; here is not null;)
            switch (here)
            {
                case Expr.Member member: parts.Insert(0, member.Name); here = member.Target; break;
                case Expr.Root root: parts.Insert(0, root.Name); here = null; break;
                default: here = null; break;
            }

        return parts;
    }

    /// <summary>The name an expression reaches a node by, less the kind that prefixes its id.</summary>
    private static string Plainly(string name)
        => name.StartsWith("input.", StringComparison.Ordinal) ? name["input.".Length..]
         : name.StartsWith("state.", StringComparison.Ordinal) ? name["state.".Length..]
         : name;

    /// <summary>What an expression calls the fact an edge asks for.</summary>
    private static string Settles(string facet) => facet switch
    {
        "emitted" => "octets",
        var other => other,
    };

    // ── Converters get what they take ─────────────────────────────────────────

    private static void Arguments(ProtocolGraph graph, ConverterTable converters, Converted applied)
    {
        if (!converters.TryGet(applied.Applies.Name, out var converter) || converter is null)
            throw new ProtoTypeException(
                $"'{applied.Name}' applies '{applied.Applies.Name}', which is not a converter this knows");

        HashSet<string> given = new(StringComparer.Ordinal);

        foreach (var edge in graph.InputsOf(applied).Where(e => e.Parameter is not null))
        {
            if (!converter.Parameters.Contains(edge.Parameter!, StringComparer.Ordinal))
                throw new ProtoTypeException(
                    $"'{applied.Name}' is given a '{edge.Parameter}', which '{converter.Name}' does not "
                  + "take. It takes: "
                  + (converter.Parameters.Count == 0
                        ? "nothing but a value"
                        : string.Join(", ", converter.Parameters)));

            if (!given.Add(edge.Parameter!))
                throw new ProtoTypeException(
                    $"'{applied.Name}' is given two edges for '{edge.Parameter}'");
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
