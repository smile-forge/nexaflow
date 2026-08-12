using Nexaflow.IO.Protocol.Converters;
using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Transforms;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;
using System.Text;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// Values that are not what they look like: a target with escapes in it, and a credential written in an
/// alphabet chosen so that it cannot contain the punctuation around it.
///
/// <para>
/// Both are the same idea and it is the one thing a delimited protocol cannot do without. A request target
/// runs up to a space, so a target containing a space has to say so some other way; a credential is
/// arbitrary octets on a line that ends at a newline. Escaping is what makes a separator-terminated span
/// able to carry its own separator, and until a document can say which escaping, every text protocol is
/// restricted to values that happen not to need any.
/// </para>
///
/// <para>
/// The engine had base64 and had nothing for percent-encoding, so <c>percent</c>/<c>unpercent</c> are new
/// here. The argument they take is the point of them: only RFC 3986's unreserved set is universal, and
/// which characters past it a component leaves alone is that component's business — a target keeps
/// <c>/</c>, a query keeps <c>&amp;</c> and <c>=</c>. A converter that decided for itself would escape the
/// delimiters the component is built out of, and the general name would be sitting over one component's
/// rule.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("DynamicProtocol end-to-end codec — tree nodes land with the engine")]
public class HttpEncodedValueTests
{
    private static readonly byte[] LineEnd = [0x0d, 0x0a];

    private static readonly Evaluator Eval = new();

    private static ProtoValue Run(string expr) => Eval.Eval(expr, new EvalScope());

    private static Field Literal(string id, string octets) => new()
    {
        Id = id,
        Pattern = new Pattern.Opaque(octets.Length),
        Value = Expr.Parse($"'{octets}' |> unascii()"),
    };

    private static Field Line(string id, string source) => new()
    {
        Id = id,
        Pattern = Pattern.Opaque.Before(LineEnd),
        Value = Expr.Parse(source),
        Via = "unascii",
    };

    // ── The document ──────────────────────────────────────────────────────────

    /// <summary>
    /// Base64 as a document-authored transform rather than as the field's converter.
    ///
    /// <para>
    /// Not a preference. A converter sits at the wire and a span writes octets, so the last step before one
    /// has to produce octets — and <c>base64</c> produces text, because that is what base64 is. So the
    /// composition has to happen somewhere, and a transform is where a composition of the engine's notions
    /// that belongs to one protocol is supposed to live. It uses the engine's own
    /// <c>base64</c>/<c>unbase64</c>; what the document adds is the text codec after them and the statement
    /// that the pair is a bijection over octets.
    /// </para>
    /// </summary>
    private static readonly Transform AsBase64 = new()
    {
        Name = "base64Text",
        Forward = Expr.Parse("value |> base64() |> unascii()"),
        Inverse = Expr.Parse("value |> ascii() |> unbase64()"),

        // Every octet run, with nothing excluded — which is unusual enough to be worth saying out loud
        // rather than leaving the field blank. Base64 is total and injective over octets, so the forward
        // law has no exceptions to declare.
        Domain = Expr.Parse("true"),

        // The backward law does not hold everywhere, though. `unbase64` accepts whitespace and tolerates
        // spellings this never emits, so text that arrived padded oddly decodes fine and re-encodes
        // canonically. A quotient, and the canonical form is what this writes.
        Canonical = false,

        Summary = "octets as base64 text, and back",
    };

    /// <summary>The request target, escaped the way a target is escaped: everything but the unreserved set
    /// and the separator between its segments.</summary>
    private static Conversion Escaped => new("percent", [ProtoValue.Of("/")]);

    private static MessageDef Ask() => new()
    {
        Id = "request",
        Context = Context.Given.These("method", "target", "version", "host", "credentials", "headers"),
        Fields =
        [
            new Field
            {
                Id = "method",
                Pattern = Pattern.Opaque.Before(" "),
                Value = Expr.Parse("inputs.method"),
                Via = "unascii",
            },
            Literal("afterMethod", " "),

            // The one field in the corpus whose wire form and value differ by an encoding rather than by a
            // codec: `/a%20b` on the wire is `/a b`, and the span it sits in ends at a space.
            new Field
            {
                Id = "target",
                Pattern = Pattern.Opaque.Before(" "),
                Value = Expr.Parse("inputs.target"),
                Via = Escaped,
            },
            Literal("afterTarget", " "),

            Line("version", "inputs.version"),
            Literal("afterVersion", "\r\n"),

            new Field
            {
                Id = "headers",
                Value = Expr.Parse("inputs.headers"),
                Pattern = new Pattern.Assorted(
                    new Field { Id = "headerName", Pattern = Pattern.Opaque.Before(": "), Via = "unascii" },
                    [
                        Arm.On("host", ProtoValue.Of("Host"),
                        [
                            Literal("hostColon", ": "),
                            Line("hostValue", "inputs.host"),
                            Literal("hostEnd", "\r\n"),
                        ]),

                        Arm.On("authorization", ProtoValue.Of("Authorization"),
                        [
                            Literal("authorizationColon", ": "),

                            // The scheme is a constant field rather than part of the value, so what the
                            // value holds is the credential and nothing else — and a document that meant
                            // Bearer would say so here instead of teaching every reader to strip a prefix.
                            Literal("authorizationScheme", "Basic "),

                            new Field
                            {
                                Id = "credentials",
                                Pattern = Pattern.Opaque.Before(LineEnd),
                                Value = Expr.Parse("inputs.credentials"),
                                Through = AsBase64,
                            },

                            Literal("authorizationEnd", "\r\n"),
                        ]),

                        Arm.Otherwise("other",
                        [
                            Literal("otherColon", ": "),
                            Line("otherValue", "item.otherValue"),
                            Literal("otherEnd", "\r\n"),
                        ], repeats: true),
                    ],
                    Expr.Parse("room > 0 && peek != 0x0d")),
            },

            Literal("blankLine", "\r\n"),
        ],
    };

    private const string Capture =
        "GET /a%20b HTTP/1.1\r\n"
      + "Host: example.org\r\n"
      + "Authorization: Basic dXNlcjpwYXNz\r\n"
      + "\r\n";

    private static byte[] Octets => [.. Capture.Select(c => (byte)c)];

    private static ProtoValue Component(string sort) => EvalScope.Record(("sort", ProtoValue.Of(sort)));

    private static EvalScope Inputs() => new EvalScope().Set("inputs", EvalScope.Record(
        ("method", ProtoValue.Of("GET")),
        ("target", ProtoValue.Of("/a b")),
        ("version", ProtoValue.Of("HTTP/1.1")),
        ("host", ProtoValue.Of("example.org")),
        ("credentials", ProtoValue.Of(Encoding.ASCII.GetBytes("user:pass"))),
        ("headers", new ProtoValue.List([Component("host"), Component("authorization")]))));

    // ── The document, both ways ───────────────────────────────────────────────

    [TestMethod]
    public void The_document_validates()
    {
        var issues = new MessageCodec(Ask()).Validate();
        Assert.AreEqual(0, issues.Count, string.Join("\n", issues));
    }

    [TestMethod]
    public void A_target_is_read_as_what_it_stands_for_and_written_as_what_arrived()
    {
        var codec = new MessageCodec(Ask());

        // The value is the target, not its spelling. A reader that had to know about escapes to compare
        // two paths is a reader that will eventually compare them wrong.
        Assert.AreEqual("/a b", codec.Decode(Octets)["target"].AsText());

        // And byte-exact the other way, which is the half that decides whether a signature over the
        // request line survives a decode.
        CollectionAssert.AreEqual(Octets, codec.Encode(Inputs()));
    }

    [TestMethod]
    public void Escaping_is_what_lets_a_span_that_ends_at_a_separator_carry_that_separator()
    {
        // The reason this is a converter on the field and not something done to the value beforehand. The
        // target runs up to a space; the engine refuses to write a span containing its own terminator,
        // because what was written would not read back as what was meant. With the escaping declared, the
        // space never reaches the wire as a space and the refusal never arises.
        var written = new MessageCodec(Ask()).Encode(Inputs());

        StringAssert.Contains(new string([.. written.Select(b => (char)b)]), "GET /a%20b HTTP/1.1");

        var unescaped = Ask() with
        {
            Id = "unescaped",
            Fields = [.. Ask().Fields.Select(f => f.Id != "target" ? f : new Field
            {
                Id = "target",
                Pattern = Pattern.Opaque.Before(" "),
                Value = Expr.Parse("inputs.target"),
                Via = "unascii",
            })],
        };

        var ex = Assert.ThrowsExactly<ProtoTypeException>(
            () => new MessageCodec(unescaped).Encode(Inputs()));

        StringAssert.Contains(ex.Message, "'target' runs up to 20 and the value contains it");
    }

    [TestMethod]
    public void Basic_credentials_travel_as_base64_and_come_back_as_the_octets_they_were()
    {
        var codec = new MessageCodec(Ask());

        var decoded = codec.Decode(Octets);

        // Octets, not text: a credential is a byte string and the alphabet on the wire is transport. The
        // field never sees the base64 and nothing downstream has to know it happened.
        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("user:pass"), decoded["credentials"].AsBytes());

        CollectionAssert.AreEqual(Octets, codec.Encode(new EvalScope().Set("inputs", EvalScope.Record(
            ("method", decoded["method"]),
            ("target", decoded["target"]),
            ("version", decoded["version"]),
            ("host", decoded["hostValue"]),
            ("credentials", decoded["credentials"]),
            ("headers", decoded["headers"])))));
    }

    /// <summary>
    /// What a converter producing text cannot be, and how late the engine says so.
    ///
    /// <para>
    /// The obvious spelling of the credential field is <c>Via = "base64"</c>, and it cannot work: a span
    /// writes octets and base64 produces text. Fair enough. What is worth pinning is <b>when</b> that is
    /// discovered. A converter declares the kinds it accepts and produces, and the declaration says the
    /// point of doing so is a validate-time message naming the field — but nothing in the engine reads
    /// either declaration, so this is a run-time complaint from the octet writer that names a CLR-shaped
    /// mismatch instead of the two things that disagree.
    /// </para>
    /// </summary>
    [TestMethod]
    public void A_converter_whose_result_is_text_cannot_be_the_last_step_before_an_octet_span()
    {
        var direct = Ask() with
        {
            Id = "direct",
            Fields = [.. Ask().Fields.Select(f => f.Id != "headers" ? f : new Field
            {
                Id = "headers",
                Value = Expr.Parse("inputs.headers"),
                Pattern = ((Pattern.Assorted)f.Pattern) with
                {
                    Sorts = [.. ((Pattern.Assorted)f.Pattern).Sorts.Select(s => s.Name != "authorization" ? s
                        : Arm.On("authorization", ProtoValue.Of("Authorization"),
                        [
                            Literal("authorizationColon", ": "),
                            Literal("authorizationScheme", "Basic "),
                            new Field
                            {
                                Id = "credentials",
                                Pattern = Pattern.Opaque.Before(LineEnd),
                                Value = Expr.Parse("inputs.credentials"),
                                Via = "base64",
                            },
                            Literal("authorizationEnd", "\r\n"),
                        ]))],
                },
            })],
        };

        // Not caught here, which is the finding: the declared kinds are the material for the check and the
        // check is not written.
        Assert.AreEqual(0, new MessageCodec(direct).Validate().Count);

        var ex = Assert.ThrowsExactly<ProtoTypeException>(() => new MessageCodec(direct).Encode(Inputs()));

        StringAssert.Contains(ex.Message, "expected Bytes, got Text");
    }

    // ── The converter pair on its own ─────────────────────────────────────────

    [TestMethod]
    public void Which_characters_a_component_leaves_alone_is_declared_and_not_decided_by_the_engine()
    {
        // Same text, two components, two right answers. A target keeps the separator between its segments;
        // something carrying a whole target inside a query does not, and the difference is a property of
        // where the value sits rather than of percent-encoding.
        Assert.AreEqual("/a%20b", Run("'/a b' |> percent('/') |> ascii()").AsText());
        Assert.AreEqual("%2Fa%20b", Run("'/a b' |> percent('') |> ascii()").AsText());

        var ex = Assert.ThrowsExactly<ProtoTypeException>(() => Run("'/a b' |> percent()"));

        StringAssert.Contains(ex.Message, "no default");
        StringAssert.Contains(ex.Message, "the characters this component leaves alone");
    }

    [TestMethod]
    public void The_pair_round_trips_over_the_text_a_target_can_hold()
    {
        // The forward law, over the declared domain: text, escaped and read back. The non-ASCII case is
        // there because percent-encoding is defined over octets and the octets have to come from
        // somewhere — this pair says UTF-8 and the round trip is what makes that claim checkable.
        Assert.AreEqual("/a b", Run("'/a b' |> percent('/') |> unpercent()").AsText());
        Assert.AreEqual("/a+b?c=d", Run("'/a+b?c=d' |> percent('/') |> unpercent()").AsText());
        Assert.AreEqual("café", Run("'café' |> percent('') |> unpercent()").AsText());
        Assert.AreEqual("%C3%A9", Run("'é' |> percent('') |> ascii()").AsText());
        Assert.AreEqual("", Run("'' |> percent('') |> unpercent()").AsText());
    }

    [TestMethod]
    public void The_backward_law_holds_only_up_to_a_spelling_and_the_pair_says_which_one()
    {
        // A quotient, like `unhex` accepting separators. Two spellings of one escape decode to the same
        // character and re-encode to the upper-case one, so a message that arrived spelled the other way
        // does NOT come back byte-exact — which is legitimate, and is exactly the sort of thing that turns
        // a byte-exact claim quietly false when nobody writes it down.
        Assert.AreEqual("%2F", Run("'%2f' |> unascii() |> unpercent() |> percent('') |> ascii()").AsText());
        Assert.AreEqual("%2F", Run("'%2F' |> unascii() |> unpercent() |> percent('') |> ascii()").AsText());

        // …and over-escaping is the other half of the same quotient: a peer may escape a character this
        // component would have left alone, and the way back is the shortest form.
        Assert.AreEqual("/", Run("'%2f' |> unascii() |> unpercent() |> percent('/') |> ascii()").AsText());

        Assert.AreEqual("percent", ConverterTable.Default.All.Single(c => c.Name == "unpercent").Inverse);
        Assert.AreEqual(ConverterRole.Bijection,
            ConverterTable.Default.All.Single(c => c.Name == "percent").Role);
    }

    /// <summary>
    /// Where the quotient stops being tolerable, and what closes it.
    ///
    /// <para>
    /// The converter pair accepts spellings it would not have written, which is right — a peer is entitled
    /// to escape more than the minimum and to write its digits in lower case, and refusing to <i>read</i>
    /// that would be refusing valid HTTP. What is not tolerable is reading it and then writing something
    /// else, because everything downstream believes the octets came back. So the field-level check settles
    /// it: a span whose converter is a bijection must be octets that converter would have produced, and one
    /// that is not is refused with both spellings in the message.
    /// </para>
    /// </summary>
    [TestMethod]
    public void A_target_spelled_a_way_this_document_would_not_have_written_is_refused_rather_than_carried()
    {
        var codec = new MessageCodec(Ask());

        // Same character, the other case of hex digit. It decodes to '?' perfectly well and would go back
        // out as %3F.
        var lowerCase = Assert.ThrowsExactly<ProtoTypeException>(
            () => codec.Decode([.. Capture.Replace("/a%20b", "/a%3fb").Select(c => (byte)c)]));

        StringAssert.Contains(lowerCase.Message, "'target'");
        StringAssert.Contains(lowerCase.Message, "is not what 'percent(/)' produces");
        StringAssert.Contains(lowerCase.Message, "re-encodes to different octets");

        // And the over-escaped case: a separator this component leaves alone, escaped anyway.
        var overEscaped = Assert.ThrowsExactly<ProtoTypeException>(
            () => codec.Decode([.. Capture.Replace("/a%20b", "%2Fab").Select(c => (byte)c)]));

        StringAssert.Contains(overEscaped.Message, "is not what 'percent(/)' produces");
    }

    [TestMethod]
    public void An_escape_that_is_not_one_is_an_error_rather_than_a_literal_per_cent()
    {
        // Reading a truncated escape as three ordinary characters is how a reader and its peer come to
        // disagree about what a value says while both believe they read it — and a target is the last
        // place for that, because it is what an access decision is made against.
        foreach (var bad in (string[])["'%2' |> unascii() |> unpercent()",
                                       "'%zz' |> unascii() |> unpercent()",
                                       "'a%' |> unascii() |> unpercent()"])
        {
            var ex = Assert.ThrowsExactly<ProtoTypeException>(() => Run(bad), bad);
            StringAssert.Contains(ex.Message, "not followed by two hex digits", bad);
        }
    }

    /// <summary>
    /// A transform and a converter on the same field, which is the obvious way to write the credential
    /// above and used not to survive reading.
    ///
    /// <para>
    /// The conversion slot has two halves and they compose in a fixed order — the document transform
    /// further from the wire, the converter nearer it — so encoding runs the transform first and decoding
    /// runs it last. The canonicality check re-derived the wire form from only the converter half, so for
    /// a field carrying both it compared the wrong thing against the octets and died inside the converter
    /// with a type error naming neither the field nor the transform.
    /// </para>
    ///
    /// <para>
    /// Worth a test of its own rather than a note, because the failure looked exactly like the field being
    /// wrong. The workaround — folding the text codec into the transform — is a fine way to write it and
    /// was not the only way it should have been writable.
    /// </para>
    /// </summary>
    [TestMethod]
    public void A_transform_and_a_converter_on_one_field_survive_the_canonicality_check()
    {
        var split = new MessageDef
        {
            Id = "split",
            Context = Context.Given.These("credentials"),
            Fields =
            [
                new Field
                {
                    Id = "credentials",
                    Pattern = Pattern.Opaque.Before(LineEnd),
                    Value = Expr.Parse("inputs.credentials"),

                    // Octets to base64 text, and then text to octets. Two steps in the two slots that
                    // exist for exactly that, rather than one step doing both.
                    Through = new Transform
                    {
                        Name = "asBase64",
                        Forward = Expr.Parse("value |> base64()"),
                        Inverse = Expr.Parse("value |> unbase64()"),
                        Domain = Expr.Parse("true"),
                        Canonical = false,
                        Summary = "octets as base64 text",
                    },
                    Via = "unascii",
                },
                Literal("end", "\r\n"),
            ],
        };

        var issues = new MessageCodec(split).Validate();
        Assert.AreEqual(0, issues.Count, string.Join("\n", issues));

        var codec = new MessageCodec(split);
        var octets = codec.Encode(new EvalScope().Set("inputs", EvalScope.Record(
            ("credentials", ProtoValue.Of(Encoding.ASCII.GetBytes("aladdin:opensesame"))))));

        Assert.AreEqual("YWxhZGRpbjpvcGVuc2VzYW1l\r\n", Encoding.ASCII.GetString(octets));

        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("aladdin:opensesame"),
            codec.Decode(octets)["credentials"].AsBytes());
    }
}
