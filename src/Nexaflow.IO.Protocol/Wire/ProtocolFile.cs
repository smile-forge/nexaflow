using System.Text.Json.Nodes;
using Nexaflow.IO.Protocol.Converters;
using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Values;

namespace Nexaflow.IO.Protocol.Wire;

/// <summary>
/// A protocol, read from what somebody wrote.
/// </summary>
/// <remarks>
/// <para>
/// The file <b>is</b> the protocol — not a rendering of one, and not something a document was converted
/// into. That distinction is the whole point: a document could only say what its shapes could spell, so
/// anything generated from one would inherit exactly the limits the graph exists to escape. A bit field
/// becomes a field here because a field is what it is, not because a group had room for it.
/// </para>
/// <para>
/// <b>Ends are named, never numbered.</b> An edge says <c>"from": "dataOffset"</c>, so a reader can tell
/// what it joins and a reviewer can disagree with it. Naming ends by position in the node array is smaller
/// and unreadable, and it makes every insertion a silent re-pointing of every edge after it — which is the
/// one class of corruption that still loads.
/// </para>
/// <para>
/// <b>Unknown kinds are refused by name.</b> A file with a node this cannot build is an error, never a node
/// quietly skipped: a protocol that loses a part on the way in reads back as one that is almost right, and
/// every capture that happens not to exercise the missing part still passes.
/// </para>
/// </remarks>
public static class ProtocolFile
{
    /// <summary>Everything a written protocol turned out to say.</summary>
    /// <param name="Graph">The nodes and edges.</param>
    /// <param name="Id">What the protocol is called.</param>
    /// <param name="Messages">Each message format it declares, by name.</param>
    /// <param name="Named">Every node, by the id the file gave it.</param>
    public sealed record Loaded(
        ProtocolGraph Graph,
        string Id,
        IReadOnlyDictionary<string, Node> Messages,
        IReadOnlyDictionary<string, Node> Named);

    public static Loaded Read(string written)
    {
        var doc = JsonNode.Parse(written)?.AsObject()
            ?? throw new ProtoTypeException("that is not a written protocol");

        var graph = new ProtocolGraph { Id = Text(doc["protocol"]) ?? "" };

        Dictionary<string, Node> named = new(StringComparer.Ordinal);
        Dictionary<string, Node> messages = new(StringComparer.Ordinal);

        foreach (var entry in Array(doc, "nodes"))
        {
            var at = entry.AsObject();
            var id = Text(at["id"]) ?? throw new ProtoTypeException("a node with no id");

            if (named.ContainsKey(id))
                throw new ProtoTypeException(
                    $"'{id}' is declared twice. Ids are how edges name their ends, so a second one makes "
                  + "every edge naming it ambiguous.");

            var node = Made(at, id);
            named[id] = node;
            graph.Add(node);

            if (node is Message message) messages[id] = message;
        }

        foreach (var entry in Array(doc, "edges"))
        {
            var at = entry.AsObject();
            graph.Add(Made(at, End(named, at, "from"), End(named, at, "to")));
        }

        // The walk begins at the protocol, whose ways on are its message formats. Rooting it at a MESSAGE
        // instead would work for as long as a protocol had exactly one and quietly privilege the first the
        // moment it had two — which is the sort of thing that reads as correct until the second message is
        // added, months later, by somebody else.
        graph.Root = graph.Nodes.OfType<Protocol>().FirstOrDefault()
            ?? throw new ProtoTypeException(
                   $"'{graph.Id}' declares no protocol node, so nothing says where a walk of it begins. "
                 + "The chain is protocol → message → packing → what follows what.");

        if (messages.Count == 0)
            throw new ProtoTypeException(
                $"'{graph.Id}' declares no message format, so there is nothing it could carry");

        foreach (var stranded in messages.Values.Where(m => !graph.To<Then>(m).Any()))
            throw new ProtoTypeException(
                $"nothing leads to the message '{stranded.Name}'. A message hangs off the protocol by the "
              + "same edge a packing hangs off a message, so one nothing reaches can never be walked.");

        Consistency.Check(graph, named, ConverterTable.Default);

        return new Loaded(graph, graph.Id, messages, named);
    }

    private static Node End(IReadOnlyDictionary<string, Node> named, JsonObject at, string which)
    {
        var id = Text(at[which])
            ?? throw new ProtoTypeException($"an edge with no '{which}'");

        return named.TryGetValue(id, out var node)
            ? node
            : throw new ProtoTypeException(
                  $"an edge names '{id}' as its {which}, and no node is called that");
    }

    private static IEnumerable<JsonNode> Array(JsonObject doc, string key)
        => doc[key]?.AsArray().Where(n => n is not null).Select(n => n!)
        ?? throw new ProtoTypeException($"a written protocol needs '{key}'");

    // ── Nodes ─────────────────────────────────────────────────────────────────

    private static Node Made(JsonObject at, string id) => Text(at["kind"]) switch
    {
        "protocol" => new Protocol(id),
        "message" => new Message(id),
        "packing" => new Packing(id),
        "set" => new FieldSet(id),
        "junction" => new Junction(id),
        "end-parse" => new EndParse(id),

        "field" => new Field
        {
            Id = id,
            Form = Form(at["form"]?.AsObject()
                ?? throw new ProtoTypeException($"field '{id}' says nothing about how it becomes octets")),
            As = Text(at["as"]),

            // Applied on the way out and inverted on the way in. Parameterless, because a field has
            // nowhere for an argument to come from — see the check when the protocol loads.
            Via = Text(at["via"]),
        },

        "input" => Outside(at, id, "input"),
        "state" => Outside(at, id, "state"),

        "constant" => new Constant
        {
            Holds = Value(at["holds"] ?? throw new ProtoTypeException($"constant '{id}' holds nothing")),
            Label = Text(at["label"]) ?? id,
            Wants = [],
            Source = Expr.Parse("0"),

            // A constant is the one computation that need not be told: what it holds says which kind it is.
            Gives = Kind(Value(at["holds"]!)),
        },

        "evaluated" => Evaluating(
            Expr.Parse(Text(at["runs"]) ?? throw new ProtoTypeException($"'{id}' evaluates nothing")),
            Text(at["label"]) ?? id,
            Gives(at, id)),

        "converted" => new Converted
        {
            // A name and nothing else. Arguments are edges naming the parameter they feed, so that one may
            // be a constant, a field read a moment ago, or the answer to another calculation.
            Applies = new Conversion(
                Text(at["applies"]) ?? throw new ProtoTypeException($"'{id}' applies nothing")),
            Label = Text(at["label"]) ?? id,
            Wants = [],
            Source = Expr.Parse("0"),
        },

        "coded" => new Coded
        {
            Implementation = Text(at["runs"])
                ?? throw new ProtoTypeException($"'{id}' names no implementation to run"),
            Label = Text(at["label"]) ?? id,
            Wants = [],
            Source = Expr.Parse("0"),
            Gives = Gives(at, id),
        },

        "default" => new Default
        {
            Id = id,
            Value = at["is"] is { } assumed ? Value(assumed) : ProtoValue.Nothing,
            Written = Bool(at["written"], false),
            Omitted = Bool(at["omitted"], false),
            Missing = Enum.Parse<WhenAbsent>(Text(at["missing"]) ?? nameof(WhenAbsent.Assumed), true),
            Because = Text(at["because"]) ?? "",
        },

        "validator" => new Validator
        {
            Id = id,
            Because = Text(at["because"]) ?? "",
            Ranges = [.. (at["ranges"]?.AsArray() ?? []).Where(r => r is not null)
                        .Select(r => new ValueRange(Value(r!["from"]!), Value(r["to"]!)))],
        },

        "set-of-values" => new ValueSet
        {
            Id = id,
            Bounding = Enum.Parse<Bounding>(Text(at["bounding"]) ?? nameof(Bounding.Closed)),
            Exclusivity = Enum.Parse<Exclusivity>(Text(at["exclusivity"]) ?? nameof(Exclusivity.Definitive)),
            Because = Text(at["because"]) ?? "",
            Members = [.. (at["members"]?.AsArray() ?? []).Where(m => m is not null)
                         .Select(m => new SetMember(
                             new ValueRange(Value(m!["from"]!), Value(m["to"]!)), Text(m["denotes"])))],
        },

        var other => throw Cannot(other, id, "a node kind"),
    };

    /// <summary>
    /// A value from outside: one somebody hands in, or one the protocol keeps between messages.
    /// </summary>
    /// <remarks>
    /// The key is what whoever sets it will call it, so it defaults to the specification's own term where
    /// one is given. Naming it after the node would make every caller learn the description's vocabulary
    /// to say something the RFC already has a word for.
    /// </remarks>
    private static Node Outside(JsonObject at, string id, string kind)
    {
        string purpose = Text(at["purpose"]) ?? "";
        string? called = Text(at["as"]);
        var gives = Gives(at, id);

        return kind == "state"
            ? new Context.State { Label = id, As = called, Purpose = purpose, Wants = [], Gives = gives }
            : new Context.Input { Label = id, As = called, Purpose = purpose, Wants = [], Gives = gives };
    }

    /// <summary>An <see cref="Evaluated"/> whose wants are recovered from the expression it runs.</summary>
    private static Evaluated Evaluating(Expr runs, string label, ValueKinds gives)
        => new() { Runs = runs, Label = label, Wants = [], Source = runs, Gives = gives };

    /// <summary>
    /// The kind of value a computation answers with, which it has to say.
    /// </summary>
    /// <remarks>
    /// Required rather than defaulted, because a default is what makes a check into a suggestion: an
    /// unstated kind matches everything, so every edge into an unstated node passes and the rule holds
    /// only where somebody happened to bother. Names are the <see cref="ValueKinds"/> ones — Bytes, Int,
    /// Number, Bool, Text, Instant, Duration, List, Record.
    /// </remarks>
    private static ValueKinds Gives(JsonObject at, string id)
        => Text(at["gives"]) is { } said
            ? Enum.TryParse<ValueKinds>(said, ignoreCase: true, out var kind) && kind != ValueKinds.None
                ? kind
                : throw new ProtoTypeException(
                      $"'{id}' says it gives '{said}', which is not a kind of value. They are: "
                    + string.Join(", ", Enum.GetNames<ValueKinds>()
                                            .Where(n => n is not ("None" or "Any" or "Numeric"))))
            : throw new ProtoTypeException(
                  $"'{id}' does not say what kind of value it gives. Both ends of an edge have to agree "
                + "about that, and a kind nobody stated agrees with everything — which is a check that "
                + "runs only where somebody remembered.");

    /// <summary>Which kind a stated value is, so a constant never has to be told.</summary>
    private static ValueKinds Kind(ProtoValue value) => value switch
    {
        ProtoValue.Bytes => ValueKinds.Bytes,
        ProtoValue.Int => ValueKinds.Int,
        ProtoValue.Num => ValueKinds.Number,
        ProtoValue.Bool => ValueKinds.Bool,
        ProtoValue.Text or ProtoValue.Caseless => ValueKinds.Text,
        ProtoValue.List => ValueKinds.List,
        ProtoValue.Rec => ValueKinds.Record,
        _ => ValueKinds.Null,
    };

    // ── How a value becomes octets ────────────────────────────────────────────

    private static WireForm Form(JsonObject at) => Text(at["of"]) switch
    {
        "run" => new WireForm.Run(Int(at["bits"], "bits")),

        "scalar" => new WireForm.Scalar(
            Int(at["octets"], "octets"), Bool(at["big"], true), Bool(at["signed"], false)),

        // No width given is the ordinary case rather than an omission: something else says how far it
        // runs, by an edge to what computes its extent.
        "opaque" => new WireForm.Opaque(at["octets"] is { } octets ? (int)octets : null),

        "varint" => new WireForm.Varint(
            Enum.Parse<GroupOrder>(Text(at["order"]) ?? nameof(GroupOrder.MostSignificantFirst)),
            Int(at["max"], "max"), Bool(at["minimal"], true)),

        "escaped" => new WireForm.EscapedInline(
            Int(at["limit"], "limit"), Int(at["max"], "max"), Bool(at["minimal"], true)),

        "prefixed" => new WireForm.Prefixed(
            Int(at["marker"], "marker"),
            [.. (at["widths"]?.AsArray() ?? []).Where(w => w is not null).Select(w => (int)w!)],
            Bool(at["minimal"], true)),

        var other => throw Cannot(other, "a field", "a form"),
    };

    // ── Edges ─────────────────────────────────────────────────────────────────

    private static Edge Made(JsonObject at, Node from, Node to) => Text(at["kind"]) switch
    {
        "then" => new Then
        {
            From = from, To = to,
            Key = at["key"] is { } key ? Value(key) : null,
            Otherwise = Bool(at["otherwise"], false),
            Optional = Bool(at["optional"], false),
        },

        "decode" => new Decode
        {
            From = from, To = to,
            Key = at["key"] is { } reading ? Value(reading) : null,
            Otherwise = Bool(at["otherwise"], false),
        },

        "holds" => new Holds { From = from, To = to, Order = Int(at["order"], "order") },
        "decides" => new Decides { From = from, To = to, Reading = Bool(at["reading"], false) },
        "identifies" => new Identifies { From = from, To = to },
        "allowed" => new Allowed { From = from, To = to },
        "assumes" => new Assumes { From = from, To = to },
        "contains" => new Contains { From = from, To = to, Ordinal = Int(at["ordinal"], "ordinal") },

        "requires" => new Requires
        {
            From = from, To = to,
            Sequence = Int(at["sequence"], "sequence"),
            Facet = Text(at["facet"]) ?? "value",

            // Which parameter of the computation this feeds, where it feeds one rather than being part of
            // the value. The reason an argument can be computed at all.
            Parameter = Text(at["parameter"]),
        },

        "computes" => new Computes
        {
            From = from, To = to,
            Facet = Text(at["facet"]) ?? "value",
            Reading = at["reading"] is { } side ? (bool)side : null,
        },

        "checks" => new Checks
        {
            From = from, To = to,
            Because = Text(at["because"]) ?? "",
            Run = Text(at["run"]),
            Order = Int(at["order"], "order", 0),
        },

        var other => throw Cannot(other, $"{from.Name} → {to.Name}", "an edge kind"),
    };

    // ── Values ────────────────────────────────────────────────────────────────

    private static ProtoValue Value(JsonNode node)
    {
        var at = node.AsObject();

        if (at["int"] is { } i) return ProtoValue.Of((long)i);
        if (at["text"] is { } t) return ProtoValue.Of((string)t!);
        if (at["folded"] is { } c) return new ProtoValue.Caseless((string)c!);
        if (at["bool"] is { } b) return ProtoValue.Of((bool)b);
        if (at["num"] is { } n) return new ProtoValue.Num((double)n);
        if (at["hex"] is { } h) return ProtoValue.Of(Convert.FromHexString((string)h!));
        if (at["list"] is { } l)
            return new ProtoValue.List([.. l.AsArray().Where(x => x is not null).Select(x => Value(x!))]);

        return ProtoValue.Nothing;
    }

    // ── Reading the file itself ───────────────────────────────────────────────

    /// <summary>
    /// A string, where the file may have written prose as an array of lines.
    /// </summary>
    /// <remarks>
    /// Descriptions are the reason this exists. A protocol is meant to be read and argued with, and a
    /// paragraph on one JSON line is neither — so <c>about</c> may be a list of lines and is joined back
    /// up here. It costs one branch and it is the difference between a file somebody reviews and a file
    /// somebody skims.
    /// </remarks>
    private static string? Text(JsonNode? node) => node switch
    {
        null => null,
        JsonArray lines => string.Join(" ", lines.Where(l => l is not null).Select(l => (string)l!)),
        _ => node.GetValueKind() == System.Text.Json.JsonValueKind.String ? (string)node! : null,
    };

    private static int Int(JsonNode? node, string what, int? whenMissing = null)
        => node is not null ? (int)node
         : whenMissing ?? throw new ProtoTypeException($"'{what}' is needed and was not given");

    private static bool Bool(JsonNode? node, bool whenMissing)
        => node is null ? whenMissing : (bool)node;

    private static ProtoTypeException Cannot(string? kind, string what, string of)
        => new($"'{kind}' ({what}) is not {of} this reads. It is refused rather than skipped: a protocol "
             + "that loses a part on the way in reads back as one that is almost right, and every capture "
             + "that happens not to exercise the missing part still passes.");
}
