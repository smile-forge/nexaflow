using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Values;

namespace Nexaflow.IO.Protocol.Wire;

/// <summary>
/// A protocol graph, written down and read back.
///
/// <para>
/// The point of it is that a graph stops being something only a running process can hold. Everything the
/// codec needs is nodes and edges, so that is what this writes: a list of each, with edges naming their
/// ends by index. Nothing is nested, because nesting is a second way to say what an edge already says and
/// the two would have to be kept agreeing.
/// </para>
///
/// <para>
/// <b>Identity is per-load, and that is fine.</b> Nodes are the same object as each other within one graph
/// and never across two, which is exactly what reference identity meant when a document built one. What it
/// cost to get here was giving up finding a computation by the identity of the expression it came from —
/// the <c>Computes</c> edge says what a computation produces, and that survives being written down.
/// </para>
///
/// <para>
/// <b>It refuses what it cannot carry, by name.</b> The graph has kinds this does not write yet — the
/// state model, chains, assortments — and a partial write that loses them silently would produce a
/// protocol that reads almost right. The one failure worth engineering against is the one that looks like
/// success.
/// </para>
/// </summary>
public static class GraphCodex
{
    /// <summary>Writes a graph as text.</summary>
    public static string Write(ProtocolGraph graph)
    {
        var order = graph.Nodes.Select((n, at) => (Node: n, At: at))
                        .ToDictionary(p => p.Node, p => p.At);

        var nodes = new JsonArray();
        foreach (var node in graph.Nodes) nodes.Add(Wrote(node));

        var edges = new JsonArray();
        foreach (var edge in graph.Edges)
        {
            var written = Wrote(edge);
            written["from"] = order[edge.From];
            written["to"] = order[edge.To];
            edges.Add(written);
        }

        return new JsonObject
        {
            ["id"] = graph.Id,
            ["root"] = order.TryGetValue(graph.Root, out var root) ? root : -1,
            ["nodes"] = nodes,
            ["edges"] = edges,
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Reads one back.</summary>
    public static ProtocolGraph Read(string written)
    {
        var doc = JsonNode.Parse(written)?.AsObject()
            ?? throw new ProtoTypeException("that is not a written graph");

        var graph = new ProtocolGraph { Id = (string?)doc["id"] ?? "" };
        List<Node> made = [];

        foreach (var entry in doc["nodes"]!.AsArray())
        {
            var node = Made(entry!.AsObject());
            made.Add(node);
            graph.Add(node);
        }

        foreach (var entry in doc["edges"]!.AsArray())
        {
            var at = entry!.AsObject();
            graph.Add(Made(at, made[(int)at["from"]!], made[(int)at["to"]!]));
        }

        int root = (int?)doc["root"] ?? -1;
        if (root >= 0) graph.Root = made[root];

        return graph;
    }

    // ── Nodes ─────────────────────────────────────────────────────────────────

    private static JsonObject Wrote(Node node) => node switch
    {
        Field field => new JsonObject
        {
            ["kind"] = "field",
            ["id"] = field.Id,
            ["form"] = Wrote(field.Pattern),
        },

        FieldSet set => new JsonObject { ["kind"] = "set", ["name"] = set.Name },
        Junction fork => new JsonObject { ["kind"] = "junction", ["name"] = fork.Name },
        Packing packing => new JsonObject { ["kind"] = "packing", ["name"] = packing.Name },

        Context outside => new JsonObject
        {
            ["kind"] = "outside",
            ["of"] = outside.GetType().Name.ToLowerInvariant(),
            ["key"] = outside.Key,
            ["purpose"] = outside.Purpose,
        },

        Constant stated => new JsonObject
        {
            ["kind"] = "constant",
            ["label"] = stated.Label,
            ["holds"] = Wrote(stated.Holds),
        },

        Converted applied => new JsonObject
        {
            ["kind"] = "converted",
            ["label"] = applied.Label,
            ["applies"] = applied.Applies.Name,
            ["args"] = new JsonArray([.. applied.Applies.Args.Select(a => (JsonNode)Wrote(a))]),
        },

        Evaluated runs => new JsonObject
        {
            ["kind"] = "evaluated",
            ["label"] = runs.Label,
            ["runs"] = runs.Runs.Render(),
        },

        BitSlice slice => new JsonObject
        {
            ["kind"] = "run",
            ["name"] = slice.Name,
            ["width"] = slice.Width,
        },

        ValueSet set => new JsonObject
        {
            ["kind"] = "set-of-values",
            ["id"] = set.Id,
            ["bounding"] = set.Bounding.ToString(),
            ["exclusivity"] = set.Exclusivity.ToString(),
            ["because"] = set.Because,
            ["members"] = new JsonArray([.. set.Members.Select(m => (JsonNode)new JsonObject
            {
                ["from"] = Wrote(m.Range.From),
                ["to"] = Wrote(m.Range.To),
                ["denotes"] = m.Denotes,
            })]),
        },

        Validator checker => new JsonObject
        {
            ["kind"] = "validator",
            ["id"] = checker.Id,
            ["because"] = checker.Because,
            ["ranges"] = new JsonArray([.. checker.Ranges.Select(r => (JsonNode)new JsonObject
            {
                ["from"] = Wrote(r.From),
                ["to"] = Wrote(r.To),
            })]),
        },

        _ when node.GetType().Name is "MessageRoot" or "Unplaced"
            => new JsonObject { ["kind"] = "origin" },

        _ => throw Cannot(node.GetType().Name, node.Name),
    };

    private static Node Made(JsonObject at) => (string?)at["kind"] switch
    {
        "field" => new Field { Id = (string)at["id"]!, Pattern = MadePattern(at["form"]!.AsObject()) },
        "set" => new FieldSet((string)at["name"]!),
        "junction" => new Junction((string)at["name"]!),
        "packing" => new Packing((string)at["name"]!),
        "origin" => new Origin(),

        "outside" => MadeContext(at),

        "constant" => new Constant
        {
            Holds = MadeValue(at["holds"]!),
            Label = (string?)at["label"] ?? "",
            Wants = [],
            Source = Expr.Parse("0"),
        },

        "evaluated" => Evaluating(Expr.Parse((string)at["runs"]!), (string?)at["label"] ?? ""),

        "converted" => new Converted
        {
            Applies = new Conversion((string)at["applies"]!,
                [.. at["args"]!.AsArray().Select(a => MadeValue(a!))]),
            Label = (string?)at["label"] ?? "",
            Wants = [],
            Source = Expr.Parse("0"),
        },

        "run" => new BitSlice((string)at["name"]!, (int)at["width"]!),

        "set-of-values" => new ValueSet
        {
            Id = (string)at["id"]!,
            Bounding = Enum.Parse<Bounding>((string)at["bounding"]!),
            Exclusivity = Enum.Parse<Exclusivity>((string)at["exclusivity"]!),
            Because = (string?)at["because"] ?? "",
            Members = [.. at["members"]!.AsArray().Select(m => new SetMember(
                new ValueRange(MadeValue(m!["from"]!), MadeValue(m["to"]!)), (string?)m["denotes"]))],
        },

        "validator" => new Validator
        {
            Id = (string)at["id"]!,
            Because = (string?)at["because"] ?? "",
            Ranges = [.. at["ranges"]!.AsArray().Select(r =>
                new ValueRange(MadeValue(r!["from"]!), MadeValue(r["to"]!)))],
        },

        var other => throw new ProtoTypeException($"'{other}' is not a node kind this reads"),
    };

    /// <summary>An <see cref="Evaluated"/> whose wants are recovered from the expression it runs.</summary>
    private static Evaluated Evaluating(Expr runs, string label)
        => new() { Runs = runs, Label = label, Wants = [], Source = runs };

    private static Context MadeContext(JsonObject at)
    {
        string key = (string)at["key"]!, purpose = (string?)at["purpose"] ?? "";

        return (string?)at["of"] switch
        {
            "prompted" => new Context.Prompted { Key = key, Purpose = purpose },
            "secret" => new Context.Secret { Key = key, Purpose = purpose },
            "remembered" => new Context.Remembered { Key = key, Purpose = purpose },
            "ambient" => new Context.Ambient { Key = key, Purpose = purpose },
            "known" => new Context.Known { Key = key, Purpose = purpose },
            "counter" => new Context.Counter { Key = key, Purpose = purpose },
            _ => new Context.Given { Key = key, Purpose = purpose },
        };
    }

    /// <summary>Where a walk begins, for a graph nothing built from a declaration.</summary>
    private sealed class Origin : Node
    {
        public override string Name => "message";
    }

    // ── How a value becomes octets ────────────────────────────────────────────

    private static JsonObject Wrote(Pattern pattern) => pattern switch
    {
        Pattern.Scalar s => new JsonObject
            { ["of"] = "scalar", ["octets"] = s.Octets, ["big"] = s.BigEndian, ["signed"] = s.Signed },

        Pattern.Opaque o => new JsonObject { ["of"] = "opaque", ["octets"] = o.Width },

        Pattern.Varint v => new JsonObject
            { ["of"] = "varint", ["order"] = v.Order.ToString(), ["max"] = v.MaxOctets,
              ["minimal"] = v.Minimal },

        Pattern.EscapedInline e => new JsonObject
            { ["of"] = "escaped", ["limit"] = e.InlineLimit, ["max"] = e.MaxOctets,
              ["minimal"] = e.Minimal },

        Pattern.Prefixed p => new JsonObject
            { ["of"] = "prefixed", ["marker"] = p.Marker, ["minimal"] = p.Minimal,
              ["widths"] = new JsonArray([.. p.Widths.Select(w => (JsonNode)w)]) },

        Pattern.Bits b => new JsonObject
        {
            ["of"] = "bits",
            ["runs"] = new JsonArray([.. b.Slices.Select(s => (JsonNode)new JsonObject
                { ["name"] = s.Name, ["width"] = s.Width })]),
        },

        _ => throw Cannot(pattern.GetType().Name, "a field's form"),
    };

    private static Pattern MadePattern(JsonObject at) => (string?)at["of"] switch
    {
        "scalar" => new Pattern.Scalar((int)at["octets"]!, (bool)at["big"]!, (bool)at["signed"]!),
        "opaque" => new Pattern.Opaque((int?)at["octets"], null),
        "varint" => new Pattern.Varint(Enum.Parse<GroupOrder>((string)at["order"]!),
                                       (int)at["max"]!, (bool)at["minimal"]!),
        "escaped" => new Pattern.EscapedInline((long)at["limit"]!, (int)at["max"]!, (bool)at["minimal"]!),
        "prefixed" => new Pattern.Prefixed((int)at["marker"]!,
                                           [.. at["widths"]!.AsArray().Select(w => (int)w!)],
                                           (bool)at["minimal"]!),
        "bits" => new Pattern.Bits(
            [.. at["runs"]!.AsArray().Select(r => new BitSlice((string)r!["name"]!, (int)r["width"]!))]),

        var other => throw new ProtoTypeException($"'{other}' is not a form this reads"),
    };

    // ── Edges ─────────────────────────────────────────────────────────────────

    private static JsonObject Wrote(Edge edge) => edge switch
    {
        Then way => new JsonObject
        {
            ["kind"] = "then",
            ["key"] = way.Key is null ? null : Wrote(way.Key),
            ["otherwise"] = way.Otherwise,
            ["optional"] = way.Optional,
        },

        Holds held => new JsonObject { ["kind"] = "holds", ["order"] = held.Order },
        Decides decides => new JsonObject { ["kind"] = "decides", ["reading"] = decides.Reading },
        Allowed => new JsonObject { ["kind"] = "allowed" },
        Contains within => new JsonObject { ["kind"] = "contains", ["ordinal"] = within.Ordinal },

        Requires wants => new JsonObject
            { ["kind"] = "requires", ["sequence"] = wants.Sequence, ["facet"] = wants.Facet },

        Computes makes => new JsonObject { ["kind"] = "computes", ["facet"] = makes.Facet },

        Checks check => new JsonObject
        {
            ["kind"] = "checks",
            ["because"] = check.Because,
            ["run"] = check.Run,
            ["order"] = check.Order,
        },

        _ => throw Cannot(edge.GetType().Name, $"{edge.From.Name} → {edge.To.Name}"),
    };

    private static Edge Made(JsonObject at, Node from, Node to) => (string?)at["kind"] switch
    {
        "then" => new Then
        {
            From = from, To = to,
            Key = at["key"] is { } key ? MadeValue(key) : null,
            Otherwise = (bool)at["otherwise"]!,
            Optional = (bool)at["optional"]!,
        },

        "holds" => new Holds { From = from, To = to, Order = (int)at["order"]! },
        "decides" => new Decides { From = from, To = to, Reading = (bool)at["reading"]! },
        "allowed" => new Allowed { From = from, To = to },
        "contains" => new Contains { From = from, To = to, Ordinal = (int)at["ordinal"]! },

        "requires" => new Requires
            { From = from, To = to, Sequence = (int)at["sequence"]!, Facet = (string)at["facet"]! },

        "computes" => new Computes { From = from, To = to, Facet = (string)at["facet"]! },

        "checks" => new Checks
        {
            From = from, To = to,
            Because = (string?)at["because"] ?? "",
            Run = (string?)at["run"],
            Order = (int)at["order"]!,
        },

        var other => throw new ProtoTypeException($"'{other}' is not an edge kind this reads"),
    };

    // ── Values ────────────────────────────────────────────────────────────────

    private static JsonObject Wrote(ProtoValue value) => value switch
    {
        ProtoValue.Int i => new JsonObject { ["int"] = i.Value },
        ProtoValue.Text t => new JsonObject { ["text"] = t.Value },
        ProtoValue.Caseless c => new JsonObject { ["folded"] = c.Value },
        ProtoValue.Bool b => new JsonObject { ["bool"] = b.Value },
        ProtoValue.Num n => new JsonObject { ["num"] = n.Value },
        ProtoValue.Bytes o => new JsonObject { ["hex"] = Convert.ToHexString(o.Value) },
        ProtoValue.Null => new JsonObject { ["null"] = true },

        ProtoValue.List l => new JsonObject
            { ["list"] = new JsonArray([.. l.Items.Select(i => (JsonNode)Wrote(i))]) },

        _ => throw Cannot(value.GetType().Name, "a value"),
    };

    private static ProtoValue MadeValue(JsonNode node)
    {
        var at = node.AsObject();

        if (at["int"] is { } i) return ProtoValue.Of((long)i);
        if (at["text"] is { } t) return ProtoValue.Of((string)t!);
        if (at["folded"] is { } c) return new ProtoValue.Caseless((string)c!);
        if (at["bool"] is { } b) return ProtoValue.Of((bool)b);
        if (at["num"] is { } n) return new ProtoValue.Num((double)n);
        if (at["hex"] is { } h) return ProtoValue.Of(Convert.FromHexString((string)h!));
        if (at["list"] is { } l) return new ProtoValue.List([.. l.AsArray().Select(x => MadeValue(x!))]);

        return ProtoValue.Nothing;
    }

    private static ProtoTypeException Cannot(string kind, string what)
        => new($"'{kind}' ({what}) is not something a written graph carries yet. It is refused rather "
             + "than dropped: a graph that loses a kind on the way out reads back as a protocol that is "
             + "almost right, which is the one failure worth engineering against.");
}
