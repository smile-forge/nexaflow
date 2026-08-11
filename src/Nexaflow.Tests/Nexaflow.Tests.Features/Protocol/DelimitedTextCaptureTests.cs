using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// A message with no widths, no length fields and nothing fixed anywhere.
///
/// <para>
/// The protocol is SSDP. Every extent in both captures is set by a separator — a space, a colon, a
/// carriage-return pair — and the number of header lines is unbounded and stated nowhere. It is the one
/// protocol in the corpus that genuinely needed a new shape rather than a new arrangement of existing
/// ones: a span that ends where the next separator begins.
/// </para>
///
/// <para>
/// The separator is not swallowed. It stays a field, which leaves something to write it back out with and
/// somewhere to fix its value — so the two octets that end a line are checked on the way in rather than
/// assumed, the same as any other constant.
/// </para>
///
/// <para>
/// What the corpus listed as four further blockers turned out not to be shape at all. Header order not
/// being significant, headers needing to be addressable by name, and a date needing parsing are all
/// questions about what the octets <i>mean</i>; the wire has an ordered list of lines either way. And the
/// leading space after a colon is simply part of the value: one header in the response has neither space
/// nor value, and treating the space as structure is what makes that a special case instead of a short one.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("DynamicProtocol end-to-end codec — tree nodes land with the engine")]
public class DelimitedTextCaptureTests
{
    private static readonly byte[] LineEnd = [0x0d, 0x0a];

    /// <summary>A separator that is always the same octets, so the document fixes it and the engine checks
    /// it arrived.</summary>
    private static Field Literal(string id, string octets) => new()
    {
        Id = id,
        Pattern = new Pattern.Opaque(octets.Length),
        Value = Expr.Parse($"'{octets}' |> unascii()"),
    };

    private static Field Upto(string id, string separator, string source) => new()
    {
        Id = id,
        Pattern = Pattern.Opaque.Before(separator),
        Value = Expr.Parse(source),
        Via = "unascii",
    };

    /// <summary>
    /// One header line. The value runs from the colon to the end of the line, leading space included —
    /// which is why a header with no value and no space is a zero-length span rather than a shape of its own.
    /// </summary>
    private static Field HeaderLine() => new()
    {
        Id = "line",
        Pattern = new Pattern.Group(
        [
            Upto("headerName", ":", "item.headerName"),
            Literal("headerColon", ":"),
            new Field
            {
                Id = "headerValue",
                Pattern = Pattern.Opaque.Before(LineEnd),
                Value = Expr.Parse("item.headerValue"),
                Via = "unascii",
            },
            new Field
            {
                Id = "headerEnd",
                Pattern = new Pattern.Opaque(2),
                Value = Expr.Parse("'0d0a' |> unhex()"),
            },
        ]),
    };

    /// <summary>The header block and the blank line that ends it. There is no count anywhere: another line
    /// follows while the next octet is not the one that starts the blank line.</summary>
    private static Field[] Headers() =>
    [
        new Field
        {
            Id = "headers",
            Value = Expr.Parse("inputs.headers"),
            Pattern = new Pattern.Chain(HeaderLine(), Expr.Parse("room > 0 && peek != 0x0d")),
        },
        new Field
        {
            Id = "blankLine",
            Pattern = new Pattern.Opaque(2),
            Value = Expr.Parse("'0d0a' |> unhex()"),
        },
    ];

    private static MessageDef Ask() => new()
    {
        Id = "ask",
        Context = Context.Given.These("method", "target", "version", "headers"),
        Fields =
        [
            Upto("method", " ", "inputs.method"),
            Literal("afterMethod", " "),
            Upto("target", " ", "inputs.target"),
            Literal("afterTarget", " "),
            new Field
            {
                Id = "version",
                Pattern = Pattern.Opaque.Before(LineEnd),
                Value = Expr.Parse("inputs.version"),
                Via = "unascii",
            },
            Literal("afterVersion", "\r\n"),
            .. Headers(),
        ],
    };

    private static MessageDef Answer() => new()
    {
        Id = "answer",
        Context = Context.Given.These("version", "code", "reason", "headers"),
        Fields =
        [
            Upto("version", " ", "inputs.version"),
            Literal("afterVersion", " "),
            Upto("code", " ", "inputs.code"),
            Literal("afterCode", " "),
            new Field
            {
                Id = "reason",
                Pattern = Pattern.Opaque.Before(LineEnd),
                Value = Expr.Parse("inputs.reason"),
                Via = "unascii",
            },
            Literal("afterReason", "\r\n"),
            .. Headers(),
        ],
    };

    private static byte[] Capture(int index) => ProtocolCorpus.Get("ssdp").Captures[index].Bytes;

    // ── The captures ──────────────────────────────────────────────────────────

    [TestMethod]
    public void Both_documents_validate()
    {
        foreach (var message in (MessageDef[])[Ask(), Answer()])
        {
            var issues = new MessageCodec(message).Validate();
            Assert.AreEqual(0, issues.Count, $"{message.Id}:\n" + string.Join("\n", issues));
        }
    }

    [TestMethod]
    public void The_request_decodes_into_a_line_and_a_list_of_headers()
    {
        var decoded = new MessageCodec(Ask()).Decode(Capture(0));

        Assert.AreEqual("M-SEARCH", decoded["method"].AsText());
        Assert.AreEqual("*", decoded["target"].AsText());
        Assert.AreEqual("HTTP/1.1", decoded["version"].AsText());

        var headers = decoded["headers"].AsList().Cast<ProtoValue.Rec>().ToList();
        Assert.AreEqual(5, headers.Count, "no count anywhere — the blank line is what ends it");

        CollectionAssert.AreEqual(new[] { "HOST", "MAN", "MX", "ST", "USER-AGENT" },
            headers.Select(h => h.Members["headerName"].AsText()).ToArray());

        // The space after the colon is part of the value, not structure.
        Assert.AreEqual(" 239.255.255.250:1900", headers[0].Members["headerValue"].AsText());
        Assert.AreEqual(" \"ssdp:discover\"", headers[1].Members["headerValue"].AsText(),
            "the quotes are on the wire");
    }

    [TestMethod]
    public void A_header_with_no_value_and_no_space_is_a_short_span_rather_than_a_special_case()
    {
        // The response carries `EXT:` followed straight by the line end. Under a name-colon-space-value
        // template that is a shape of its own; under a span that runs to the next separator it is a span
        // of zero octets, which needs no saying.
        var decoded = new MessageCodec(Answer()).Decode(Capture(1));

        var headers = decoded["headers"].AsList().Cast<ProtoValue.Rec>().ToList();
        var ext = headers.Single(h => h.Members["headerName"].AsText() == "EXT");

        Assert.AreEqual("", ext.Members["headerValue"].AsText());

        Assert.AreEqual("200", decoded["code"].AsText());
        Assert.AreEqual("OK", decoded["reason"].AsText());
        Assert.AreEqual(9, headers.Count);
    }

    [TestMethod]
    public void Both_captures_re_encode_to_the_exact_original_octets()
    {
        // Every separator is withheld: the spaces, the colons and the line ends are all values the
        // document fixes, so an encode that got one wrong would differ and a decode that met a different
        // one would already have been refused.
        foreach (var (index, message) in ((int, MessageDef)[])[(0, Ask()), (1, Answer())])
        {
            var original = Capture(index);
            var codec = new MessageCodec(message);
            var reEncoded = codec.Encode(InputsFrom(codec.Decode(original)));

            Assert.AreEqual(original.Length, reEncoded.Length, $"capture {index}: length");
            CollectionAssert.AreEqual(original, reEncoded,
                $"capture {index} did not round-trip.\nexpected {System.Text.Encoding.ASCII.GetString(original)}"
              + $"\nactual   {System.Text.Encoding.ASCII.GetString(reEncoded)}");
        }
    }

    [TestMethod]
    public void Reordering_the_headers_produces_a_different_message_with_the_same_meaning()
    {
        // Order is not significant to the protocol and is entirely significant to the octets. The wire
        // layer keeps the list it was given; deciding that two orderings say the same thing is the job of
        // whatever reads them, not of the codec.
        var codec = new MessageCodec(Ask());
        var inputs = InputsFrom(codec.Decode(Capture(0)));

        var headers = ((ProtoValue.List)Member(inputs.Root("inputs"), "headers")).Items;
        inputs.Set("inputs", With(inputs.Root("inputs"),
            ("headers", new ProtoValue.List([.. headers.Reverse()]))));

        var shuffled = codec.Encode(inputs);

        Assert.AreEqual(Capture(0).Length, shuffled.Length, "the same octets in a different order");
        CollectionAssert.AreNotEqual(Capture(0), shuffled);

        CollectionAssert.AreEqual(new[] { "USER-AGENT", "ST", "MX", "MAN", "HOST" },
            codec.Decode(shuffled)["headers"].AsList().Cast<ProtoValue.Rec>()
                 .Select(h => h.Members["headerName"].AsText()).ToArray());
    }

    // ── What it refuses ───────────────────────────────────────────────────────

    [TestMethod]
    public void A_value_containing_its_own_separator_is_refused_rather_than_written()
    {
        // What was written would not read back as what was meant: the scan would stop early and every
        // field after it would be a different field.
        var codec = new MessageCodec(Ask());
        var inputs = InputsFrom(codec.Decode(Capture(0)));

        inputs.Set("inputs", With(inputs.Root("inputs"), ("method", ProtoValue.Of("M SEARCH"))));

        var ex = Assert.ThrowsExactly<ProtoTypeException>(() => codec.Encode(inputs));
        StringAssert.Contains(ex.Message, "would not read back as what was meant");
    }

    [TestMethod]
    public void A_line_that_never_ends_is_refused_rather_than_scanned_forever()
    {
        // A scan with no bound is a promise about data nobody has read yet.
        var truncated = (byte[])[.. Capture(0)[..40]];

        var ex = Assert.ThrowsExactly<ProtoTypeException>(() => new MessageCodec(Ask()).Decode(truncated));
        StringAssert.Contains(ex.Message, "nothing else says where this span ends");
    }

    [TestMethod]
    public void A_span_may_not_say_how_long_it_is_in_two_ways_at_once()
    {
        var confused = new MessageDef
        {
            Id = "confused",
            Context = Context.Given.These("x"),
            Fields =
            [
                new Field
                {
                    Id = "x",
                    Pattern = new Pattern.Opaque(4, null, LineEnd),
                    Value = Expr.Parse("inputs.x"),
                },
            ],
        };

        StringAssert.Contains(string.Join("\n", confused.Validate()), "exactly one extent");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static EvalScope InputsFrom(DecodeResult decoded)
    {
        Dictionary<string, ProtoValue> inputs = new(StringComparer.Ordinal);

        foreach (var (name, value) in decoded.Captures)
            if (!name.StartsWith("after", StringComparison.Ordinal) && name is not "blankLine")
                inputs[name] = value;

        inputs["headers"] = new ProtoValue.List(
        [
            .. decoded["headers"].AsList().Cast<ProtoValue.Rec>().Select(h => EvalScope.Record(
                ("headerName", h.Members["headerName"]),
                ("headerValue", h.Members["headerValue"]))),
        ]);

        return new EvalScope().Set("inputs", new ProtoValue.Rec(inputs));
    }

    private static ProtoValue Member(ProtoValue record, string name)
        => record is ProtoValue.Rec r && r.Members.TryGetValue(name, out var v) ? v : ProtoValue.Nothing;

    private static ProtoValue With(ProtoValue record, params (string Name, ProtoValue Value)[] overrides)
    {
        var members = record is ProtoValue.Rec r
            ? new Dictionary<string, ProtoValue>(r.Members, StringComparer.Ordinal)
            : new Dictionary<string, ProtoValue>(StringComparer.Ordinal);

        foreach (var (name, value) in overrides) members[name] = value;
        return new ProtoValue.Rec(members);
    }
}
