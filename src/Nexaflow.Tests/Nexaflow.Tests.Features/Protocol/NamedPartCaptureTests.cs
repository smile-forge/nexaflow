using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// Five compression pointers, none of which mentions an offset.
///
/// <para>
/// The protocol is mDNS. Its response repeats names by pointing at where an earlier one was written, and
/// the last protocol in the corpus that could not be <i>written</i> was this one: the query round-trips,
/// and the response could only ever be read.
/// </para>
///
/// <para>
/// What was missing was not a way to compute offsets. It was somewhere for a pointer to point. A
/// compression pointer does not mean "octet 23" — it means "the rest of this is <i>local</i>", and a name
/// is something the protocol has a word for rather than a range of bytes. So the document declares the
/// parts it has, gives each the protocol's own name for it, and every pointer names one. The offsets fall
/// out of the layout and appear nowhere in the description.
/// </para>
///
/// <para>
/// The corpus called the hard case a pointer whose target lies <b>inside another record's data</b> —
/// <c>c0 46</c> reaches into the middle of the SRV record's payload. Under a model of byte ranges that is
/// a hazard; under one of named parts it is the ordinary case, because the host name is a thing this
/// message has whether or not another record happens to enclose it.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("DynamicProtocol end-to-end codec — tree nodes land with the engine")]
public class NamedPartCaptureTests
{
    private static Pattern U8 => new Pattern.Scalar(1, BigEndian: true);
    private static Pattern U16 => new Pattern.Scalar(2, BigEndian: true);
    private static Pattern U32 => new Pattern.Scalar(4, BigEndian: true);

    private static Field Number(string id, Pattern width, string source) => new()
    {
        Id = id,
        Pattern = width,
        Value = Expr.Parse(source),
    };

    /// <summary>One label: a count of octets and that many octets. There are no dots on the wire.</summary>
    private static Field Label(string id, string source) => new()
    {
        Id = id,
        Pattern = new Pattern.Group(
        [
            new Field { Id = $"{id}Length", Pattern = U8, Value = Expr.Parse($"fields.{id}Text.extent") },
            new Field
            {
                Id = $"{id}Text",
                Pattern = Pattern.Opaque.Measured(Expr.Parse($"fields.{id}Length.value")),
                Value = Expr.Parse(source),
                Via = "unascii",
            },
        ]),
    };

    /// <summary>A reference, spelled the way this family spells one: the offset with its top two bits set.
    /// The rendering is here because how a reference is written down is a protocol's business; that one
    /// exists at all is the part the engine knows.</summary>
    private static Field Pointer(string id, Concept to) => new()
    {
        Id = id,
        Pattern = U16,
        Points = new Locating(to, Expr.Parse("0xc000 bor position")),
    };

    /// <summary>A record, and the region its length bounds. The length measures the data going out and
    /// bounds it coming in, so a run of character-strings knows where to stop.</summary>
    private static Field Record(string id, Field name, long type, long recordClass, string ttl,
                                IReadOnlyList<Field> content) =>
        new()
        {
            Id = id,
            Pattern = new Pattern.Group(
            [
                name,
                Number($"{id}Type", U16, $"0x{type:x4}"),
                Number($"{id}Class", U16, $"0x{recordClass:x4}"),
                Number($"{id}Ttl", U32, ttl),
                Number($"{id}Length", U16, $"fields.{id}Data.extent"),
                new Field
                {
                    Id = $"{id}Data",
                    Pattern = new Pattern.Group(content, Expr.Parse($"fields.{id}Length.value")),
                },
            ]),
        };

    internal static MessageDef Definition()
    {
        // ── the parts the protocol has names for ──────────────────────────────

        // "local", and the zero octet that ends every name. A pointer at this is a name ending in .local.
        var rootName = new Field
        {
            Id = "rootName",
            Pattern = new Pattern.Group([Label("localLabel", "inputs.root"), Number("nameEnd", U8, "0")]),
        };

        // "_http._tcp.local" — what is being advertised.
        var serviceName = new Field
        {
            Id = "serviceName",
            Pattern = new Pattern.Group(
                [Label("serviceLabel", "inputs.service"), Label("protocolLabel", "inputs.protocol"), rootName]),
        };

        var theRoot = new Concept { Id = "theRoot", Of = rootName, About = "the domain every name here sits under." };
        var theService = new Concept { Id = "theService", Of = serviceName, About = "the service being advertised." };

        // "nexaprint._http._tcp.local" — this instance of it. Written once, inside the PTR record's data,
        // and referred to by every record that is about it.
        var instanceName = new Field
        {
            Id = "instanceName",
            Pattern = new Pattern.Group([Label("instanceLabel", "inputs.instance"), Pointer("toService", theService)]),
        };

        var theInstance = new Concept
        {
            Id = "theInstance",
            Of = instanceName,
            About = "this particular instance of the service — the name the SRV and TXT records answer to.",
        };

        // "nexaprint.local" — the machine. Written inside the SRV record's data, and pointed at from the
        // A record, which is the case the corpus called a hazard.
        var hostName = new Field
        {
            Id = "hostName",
            Pattern = new Pattern.Group([Label("hostLabel", "inputs.host"), Pointer("toRoot", theRoot)]),
        };

        var theHost = new Concept { Id = "theHost", Of = hostName, About = "the machine the service runs on." };

        // ── the message ───────────────────────────────────────────────────────

        return new MessageDef
        {
            Id = "advertisement",

            Concepts = [theRoot, theService, theInstance, theHost],

            Context = Context.Given.These(
                "root", "service", "protocol", "instance", "host",
                "sharedTtl", "uniqueTtl", "priority", "weight", "port", "address", "notes"),

            Fields =
            [
                Number("dnsId", U16, "0"),
                Number("flags", U16, "0x8400"),
                Number("questionCount", U16, "0"),
                Number("answerCount", U16, "1"),
                Number("authorityCount", U16, "0"),
                Number("additionalCount", U16, "3"),

                // PTR: the service points at this instance of it. Its name is written out in full because
                // nothing precedes it to point at.
                Record("pointer", serviceName, 0x000c, 0x0001, "inputs.sharedTtl", [instanceName]),

                // SRV: where to reach it. Its own name is a reference, and so is its target's domain.
                Record("service", Pointer("serviceRecordName", theInstance), 0x0021, 0x8001, "inputs.uniqueTtl",
                [
                    Number("priority", U16, "inputs.priority"),
                    Number("weight", U16, "inputs.weight"),
                    Number("port", U16, "inputs.port"),
                    hostName,
                ]),

                // TXT: what it can do.
                Record("text", Pointer("textRecordName", theInstance), 0x0010, 0x8001, "inputs.sharedTtl",
                [
                    new Field
                    {
                        Id = "notes",
                        Pattern = new Pattern.Chain(Label("note", "item"), Expr.Parse("room > 0")),
                        Value = Expr.Parse("inputs.notes"),
                    },
                ]),

                // A: the address. Its name points into the middle of the SRV record above.
                Record("address", Pointer("addressRecordName", theHost), 0x0001, 0x8001, "inputs.uniqueTtl",
                [
                    new Field
                    {
                        Id = "addressBytes",
                        Pattern = new Pattern.Opaque(4),
                        Value = Expr.Parse("inputs.address |> ipv4()"),
                    },
                ]),
            ],
        };
    }

    private static byte[] Capture() => ProtocolCorpus.Get("mdns").Captures[1].Bytes;

    // ── The capture ───────────────────────────────────────────────────────────

    [TestMethod]
    public void The_document_validates()
    {
        var issues = new MessageCodec(Definition()).Validate();
        Assert.AreEqual(0, issues.Count, string.Join("\n", issues));
    }

    [TestMethod]
    public void Every_pointer_reads_back_as_the_part_it_names()
    {
        var decoded = new MessageCodec(Definition()).Decode(Capture());

        Assert.AreEqual(0xc00c, decoded["toService"].AsInt(), "the service, at 12");
        Assert.AreEqual(0xc028, decoded["serviceRecordName"].AsInt(), "the instance, at 40");
        Assert.AreEqual(0xc017, decoded["toRoot"].AsInt(), "the domain, at 23 — inside the answer's own name");
        Assert.AreEqual(0xc028, decoded["textRecordName"].AsInt());
        Assert.AreEqual(0xc046, decoded["addressRecordName"].AsInt(), "the host, at 70 — inside the SRV data");

        Assert.AreEqual("_http", decoded["serviceLabelText"].AsText());
        Assert.AreEqual("nexaprint", decoded["instanceLabelText"].AsText());
        Assert.AreEqual("local", decoded["localLabelText"].AsText());

        CollectionAssert.AreEqual(new[] { "txtvers=1", "path=/" },
            decoded["notes"].AsList().Cast<ProtoValue.Rec>()
                   .Select(n => n.Members["noteText"].AsText()).ToArray());
    }

    [TestMethod]
    public void The_response_re_encodes_to_the_exact_original_octets()
    {
        // Every offset withheld. Five pointers, four record lengths and three label lengths are all
        // derived, and the inputs are names and numbers — no position appears anywhere in them.
        var original = Capture();
        var codec = new MessageCodec(Definition());
        var reEncoded = codec.Encode(Inputs());

        Assert.AreEqual(original.Length, reEncoded.Length, "length");
        CollectionAssert.AreEqual(original, reEncoded,
            $"expected {Convert.ToHexString(original).ToLowerInvariant()}"
          + $"\nactual   {Convert.ToHexString(reEncoded).ToLowerInvariant()}");
    }

    [TestMethod]
    public void Renaming_the_instance_moves_every_pointer_that_follows_it()
    {
        // The whole point of naming the parts. A longer instance label pushes the SRV record along, so the
        // host name lands later, so the A record's pointer changes — and nothing in the inputs says so.
        var codec = new MessageCodec(Definition());
        var inputs = Inputs();

        inputs.Set("inputs", With(inputs.Root("inputs"), ("instance", ProtoValue.Of("nexaprinter"))));

        var moved = codec.Decode(codec.Encode(inputs));

        Assert.AreEqual(0xc00c, moved["toService"].AsInt(), "the service did not move");
        Assert.AreEqual(0xc017, moved["toRoot"].AsInt(), "nor the domain");
        Assert.AreEqual(0xc028, moved["serviceRecordName"].AsInt(), "nor the instance, which precedes the change");
        Assert.AreEqual(0xc048, moved["addressRecordName"].AsInt(), "but the host moved by the two added octets");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static EvalScope Inputs() => new EvalScope().Set("inputs", EvalScope.Record(
        ("root", ProtoValue.Of("local")),
        ("service", ProtoValue.Of("_http")),
        ("protocol", ProtoValue.Of("_tcp")),
        ("instance", ProtoValue.Of("nexaprint")),
        ("host", ProtoValue.Of("nexaprint")),
        ("sharedTtl", ProtoValue.Of(4500L)),
        ("uniqueTtl", ProtoValue.Of(120L)),
        ("priority", ProtoValue.Of(0L)),
        ("weight", ProtoValue.Of(0L)),
        ("port", ProtoValue.Of(80L)),
        ("address", ProtoValue.Of("192.168.1.42")),
        ("notes", new ProtoValue.List([ProtoValue.Of("txtvers=1"), ProtoValue.Of("path=/")]))));

    private static ProtoValue With(ProtoValue record, params (string Name, ProtoValue Value)[] overrides)
    {
        var members = record is ProtoValue.Rec r
            ? new Dictionary<string, ProtoValue>(r.Members, StringComparer.Ordinal)
            : new Dictionary<string, ProtoValue>(StringComparer.Ordinal);

        foreach (var (name, value) in overrides) members[name] = value;
        return new ProtoValue.Rec(members);
    }
}
