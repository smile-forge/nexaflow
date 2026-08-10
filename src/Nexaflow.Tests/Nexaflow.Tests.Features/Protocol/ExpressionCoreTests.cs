using Nexaflow.IO.Protocol.Converters;
using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// The expression core — build step 1.
///
/// <para>
/// Every case here is drawn from a real capture in the protocol corpus, because the first grammar's
/// arithmetic bugs were not caught by plausible-looking unit tests: <c>2 ^ poll</c> looked like
/// exponentiation and silently computed a xor, giving an NTP poll interval of 4 seconds instead of 64,
/// and <c>keepAlive * 0.75</c> truncated to zero because a decimal literal was an integer. Both would
/// have passed any test written from the same misunderstanding, and both are pinned below against the
/// value the wire actually carries.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("DynamicProtocol expression core — tree nodes land with the engine")]
public class ExpressionCoreTests
{
    private static readonly Evaluator Eval = new();

    private static ProtoValue Run(string expr, EvalScope? scope = null)
        => Eval.Eval(expr, scope ?? new EvalScope());

    private static long Int(string expr, EvalScope? scope = null) => Run(expr, scope).AsInt();

    // ── Operators: the two arithmetic bugs the first grammar shipped ──────────

    [TestMethod]
    public void Caret_is_bitwise_xor_and_exponentiation_is_pow()
    {
        // NTP capture C carries poll = 0x06, meaning a 64-second interval. Under the old reading,
        // "2 ^ poll" computed 2 xor 6 = 4 — a plausible number, four seconds, and completely wrong.
        Assert.AreEqual(4, Int("2 ^ 6"), "'^' is bitwise xor");
        Assert.AreEqual(64, Int("pow(2, 6)"), "the NTP poll interval for poll=6 is 64 seconds");
    }

    [TestMethod]
    public void Decimal_literals_are_real_numbers_not_truncated_integers()
    {
        // MQTT's keepalive obligation is three-quarters of the negotiated interval. As an Int literal,
        // 0.75 was 0, and the obligation fired every zero seconds.
        var scope = new EvalScope().Set("session", EvalScope.Record(("keepAlive", ProtoValue.Of(60L))));

        Assert.AreEqual(45.0, Run("session.keepAlive * 0.75", scope).AsNumber(), 1e-9);
        Assert.AreEqual(90.0, Run("session.keepAlive * 1.5", scope).AsNumber(), 1e-9);
        Assert.AreEqual(45, Int("session.keepAlive * 3 / 4", scope), "the integer route agrees");
    }

    [TestMethod]
    public void Comparisons_coerce_to_int_under_arithmetic()
    {
        // BACnet's tag-depth fold: opening tag (LVT 6) descends, closing tag (LVT 7) ascends. Without a
        // Bool → Int coercion this expression does not type-check at all.
        var open = new EvalScope().Set("item", EvalScope.Record(("lvt", ProtoValue.Of(6L))));
        var close = new EvalScope().Set("item", EvalScope.Record(("lvt", ProtoValue.Of(7L))));
        var plain = new EvalScope().Set("item", EvalScope.Record(("lvt", ProtoValue.Of(2L))));

        const string fold = "1 + (item.lvt == 6) - (item.lvt == 7)";
        Assert.AreEqual(2, Int(fold, open), "an opening tag descends a level");
        Assert.AreEqual(0, Int(fold, close), "a closing tag ascends");
        Assert.AreEqual(1, Int(fold, plain), "any other tag leaves depth alone");
    }

    [TestMethod]
    public void There_is_no_implicit_int_to_bool_coercion()
    {
        // Silent truthiness is how a comparison ends up used as a quantity. A document must write x != 0.
        Assert.ThrowsExactly<ProtoTypeException>(() => Run("1 && 1"));
        Assert.IsTrue(Run("1 != 0 && 2 != 0").AsBool());
    }

    [TestMethod]
    public void The_pipeline_binds_loosest_so_a_masked_comparison_reads_naturally()
    {
        // Modbus signals an exception response by setting the top bit of the function code: 0x03 -> 0x83.
        var normal = new EvalScope().Set("capture", EvalScope.Record(("fc", ProtoValue.Of(0x03L))));
        var error = new EvalScope().Set("capture", EvalScope.Record(("fc", ProtoValue.Of(0x83L))));

        Assert.IsFalse(Run("capture.fc |> band(0x80) != 0", normal).AsBool());
        Assert.IsTrue(Run("capture.fc |> band(0x80) != 0", error).AsBool(),
            "must parse as (fc & 0x80) != 0, not fc & (0x80 != 0)");
    }

    [TestMethod]
    public void Precedence_follows_the_declared_table()
    {
        Assert.AreEqual(7, Int("1 + 2 * 3"));
        Assert.AreEqual(9, Int("(1 + 2) * 3"));
        Assert.AreEqual(1, Int("5 & 3"));
        Assert.AreEqual(6, Int("5 ^ 3"));
        Assert.AreEqual(7, Int("5 | 3"));
        Assert.AreEqual(8, Int("1 << 3"));
        Assert.IsTrue(Run("1 + 1 == 2").AsBool(), "arithmetic binds tighter than equality");
        Assert.AreEqual(-5, Int("0 - 5"));
        Assert.AreEqual(20, Int("if(1 < 2, 20, 30)"));
    }

    [TestMethod]
    public void Word_forms_of_the_bitwise_operators_are_accepted()
    {
        Assert.AreEqual(Int("5 & 3"), Int("5 band 3"));
        Assert.AreEqual(Int("5 ^ 3"), Int("5 bxor 3"));
        Assert.AreEqual(Int("5 | 3"), Int("5 bor 3"));
    }

    [TestMethod]
    public void Null_propagates_through_access_rather_than_throwing()
    {
        // An absent optional value must suppress its enclosing `when`, not abort the parse.
        Assert.IsTrue(Run("capture.missing").IsNull);
        Assert.IsTrue(Run("capture.missing.deeper").IsNull);
        Assert.IsTrue(Run("capture.missing[3]").IsNull);
        Assert.AreEqual(9, Int("capture.missing ?? 9"));
        Assert.IsFalse(Run("capture.missing").AsBool(), "null reads false in a boolean position");
    }

    [TestMethod]
    public void Syntax_errors_are_reported_with_a_position()
    {
        var ex = Assert.ThrowsExactly<ProtoSyntaxException>(() => Run("1 + + )"));
        Assert.IsTrue(ex.Position > 0);
    }

    [TestMethod]
    public void An_unknown_converter_says_the_set_is_closed()
    {
        var ex = Assert.ThrowsExactly<ProtoTypeException>(() => Run("5 |> notAConverter()"));
        StringAssert.Contains(ex.Message, "closed",
            "the message must explain that extending the set is a code change, not an authoring mistake");
    }

    // ── Converters, against real capture values ───────────────────────────────

    [TestMethod]
    public void Continuation_encoding_serves_both_group_orders()
    {
        // The parameter that makes this a notion rather than one family's codec. Two unrelated protocols
        // in the corpus spread a value across octets with a continue flag, in OPPOSITE group orders:
        // most-significant-first reads 8f 65 as 2021; least-significant-first reads 8f 01 as 143.
        // Without `order`, the second is decoded as 1921 and the protocol is silently unusable.
        Assert.AreEqual("8f65", Run("2021 |> base128('msbFirst')").ToString());
        Assert.AreEqual(2021, Int("'8f65' |> unhex() |> unbase128('msbFirst')"));

        Assert.AreEqual("8f01", Run("143 |> base128('lsbFirst')").ToString());
        Assert.AreEqual(143, Int("'8f01' |> unhex() |> unbase128('lsbFirst')"));

        Assert.AreEqual(1921, Int("'8f01' |> unhex() |> unbase128('msbFirst')"),
            "…and reading it in the wrong order silently yields a plausible wrong number, "
          + "which is exactly why the parameter is required rather than defaulted");
    }

    [TestMethod]
    public void A_parameter_whose_value_is_a_protocol_property_has_no_default()
    {
        // A default that is not the identity is one protocol's constant living in the engine. These four
        // were all defaulted at one point, and each default was a different protocol's choice.
        foreach (var missing in (string[])["2021 |> base128()", "0 |> minuint()",
                                           "'aabb' |> unhex() |> crc16()", "1.5 |> fixed(16)"])
        {
            var ex = Assert.ThrowsExactly<ProtoTypeException>(() => Run(missing), missing);
            StringAssert.Contains(ex.Message, "no default", missing);
        }
    }

    [TestMethod]
    public void Minimal_width_zero_encodes_per_the_declared_rule()
    {
        // One family requires a single 00 for a zero value; another requires no octets at all. Hard-coding
        // either makes the other protocol inexpressible while the summary claims to serve both.
        Assert.AreEqual("00", Run("0 |> minuint('oneByte')").ToString());
        Assert.AreEqual("", Run("0 |> minuint('empty')").ToString(), "a zero-valued minimal option is empty");
        Assert.AreEqual("32", Run("50 |> minuint('oneByte')").ToString(), "non-zero is unaffected");
        Assert.AreEqual("32", Run("50 |> minuint('empty')").ToString());
    }

    [TestMethod]
    public void Text_equivalence_is_declared_rather_than_assumed()
    {
        var headers = new ProtoValue.List(
        [
            EvalScope.Record(("name", ProtoValue.Of("Location")), ("value", ProtoValue.Of("http://x"))),
        ]);
        var scope = new EvalScope().Set("capture", EvalScope.Record(("headers", headers)));

        // Case-folding must be asked for. Assuming it installed one protocol family's equivalence rule in
        // the engine's comparison operator — invisible to any name scan, and wrong for a case-sensitive
        // keyed lookup such as a path segment or a negotiated protocol name.
        Assert.IsTrue(Run("capture.headers |> lookupBy('name', 'LOCATION')", scope).IsNull,
            "exact is the default, because exact is the identity comparison");
        Assert.IsFalse(Run("capture.headers |> lookupBy('name', 'LOCATION', 'caseFold')", scope).IsNull);
        Assert.IsFalse(Run("capture.headers |> lookupBy('name', 'Location')", scope).IsNull);
    }

    [TestMethod]
    public void Minint_produces_the_three_octet_snmp_request_id()
    {
        // The SNMP captures carry request-id 821915 as 0c 8a 9b. A fixed-width i32be gives four octets,
        // which is both the wrong bytes and the wrong declared length — the length field would disagree.
        Assert.AreEqual("0c8a9b", Run("821915 |> minint()").ToString());
        Assert.AreEqual(821915, Int("'0c8a9b' |> unhex() |> unminint()"));
    }

    [TestMethod]
    public void Minint_inserts_a_sign_pad_only_when_the_top_bit_would_read_as_negative()
    {
        Assert.AreEqual("00ff", Run("255 |> minint()").ToString(), "0xff alone would decode as -1");
        Assert.AreEqual("7f", Run("127 |> minint()").ToString(), "0x7f is already positive");
        Assert.AreEqual("00", Run("0 |> minint()").ToString());
        Assert.AreEqual("ff", Run("0 - 1 |> minint()").ToString());
    }

    [TestMethod]
    public void Minuint_is_used_where_the_wire_forbids_a_leading_zero()
    {
        // SNMP TimeTicks 9740722 is an UNSIGNED type, so it needs the 0x00 pad the signed form supplies —
        // but a CoAP option value must be minimal-unsigned, where that pad would be a different encoding.
        Assert.AreEqual("94a1b2", Run("9740722 |> minuint('oneByte')").ToString());
        Assert.AreEqual("32", Run("50 |> minuint('empty')").ToString(), "CoAP Accept 50");
    }

    [TestMethod]
    public void There_is_no_hierarchical_identifier_converter_in_the_engine()
    {
        // The leading-pair merge is one encoding family's rule. A converter for it would be a protocol
        // specific wearing a general name — the failure mode this table is guarded against — so it lives
        // in a document instead, built from the notions below. See TransformLanguageTests.
        foreach (var name in (string[])["oid", "unoid"])
            Assert.IsFalse(ConverterTable.Default.TryGet(name, out _),
                $"'{name}' encodes one family's rule and belongs in a document, not the engine");
    }

    [TestMethod]
    public void The_notions_a_hierarchical_identifier_is_built_from_are_all_present()
    {
        // Each of these is exhibited by more than one protocol, which is what makes it a notion rather
        // than a disguise. Composed, they are the whole encoding.
        Assert.AreEqual("[1, 3, 6]", Run("'1.3.6' |> split('.') |> map(a -> a |> undecimal())").ToString());
        Assert.AreEqual("8f65", Run("2021 |> base128('msbFirst')").ToString(),
            "2021 = 15*128 + 101, so 0x8f 0x65");
        Assert.AreEqual(2021, Int("'8f65' |> unhex() |> unbase128('msbFirst')"));
        Assert.AreEqual("[1, 2, 3]", Run("[[1], [2, 3]] |> flatten()").ToString());
        Assert.AreEqual("2b", Run("[1 * 40 + 3] |> octets()").ToString(), "the leading pair merges to 0x2b");
    }

    [TestMethod]
    public void Fixed_point_reproduces_the_ntp_root_delay()
    {
        // NTP's root delay field in the capture is 0x0000028f — 655 in 16.16, i.e. 0.0099945 s.
        Assert.AreEqual(655, Int("0.0099945 |> fixed(16, 16)"));
        Assert.AreEqual(0.0099945, Run("655 |> unfixed(16, 16)").AsNumber(), 1e-7);
    }

    [TestMethod]
    public void The_wake_on_lan_payload_is_one_expression()
    {
        // Six 0xff octets then the target MAC sixteen times: 102 octets.
        var scope = new EvalScope().Set("device", EvalScope.Record(("mac", ProtoValue.Of("aa:bb:cc:dd:ee:ff"))));
        var magic = Run("'ffffffffffff' |> unhex() |> concat(device.mac |> mac() |> repeat(16))", scope);

        var bytes = magic.AsBytes();
        Assert.AreEqual(102, bytes.Length, "6 + 16 * 6");
        CollectionAssert.AreEqual(new byte[] { 0xff, 0xff, 0xff, 0xff, 0xff, 0xff }, bytes[..6]);
        CollectionAssert.AreEqual(new byte[] { 0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff }, bytes[6..12]);
        CollectionAssert.AreEqual(new byte[] { 0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff }, bytes[^6..]);
    }

    [TestMethod]
    public void Fit_refuses_to_truncate_a_fixed_width_field()
    {
        // DHCP sname is 64 octets and file is 128. Silently truncating an over-long value is how a
        // boot filename becomes a different, valid-looking filename.
        Assert.AreEqual(64, Run("'boot' |> fit(64)").AsBytes().Length);
        Assert.AreEqual("boot", Run("'boot' |> fit(64) |> cstr()").ToString());
        Assert.ThrowsExactly<ProtoTypeException>(() => Run("'0123456789' |> fit(4)"));
    }

    [TestMethod]
    public void Ascii_case_insensitive_lookup_is_what_http_style_headers_need()
    {
        var headers = new ProtoValue.List(
        [
            EvalScope.Record(("name", ProtoValue.Of("HOST")), ("value", ProtoValue.Of("239.255.255.250:1900"))),
            EvalScope.Record(("name", ProtoValue.Of("Location")), ("value", ProtoValue.Of("http://x/desc.xml"))),
        ]);
        var scope = new EvalScope().Set("capture", EvalScope.Record(("headers", headers)));

        Assert.AreEqual("http://x/desc.xml",
            Run("capture.headers |> lookupBy('name', 'LOCATION', 'caseFold')", scope) is ProtoValue.Rec r
                ? r.Members["value"].ToString() : "<not found>");
        Assert.IsTrue(Run("capture.headers |> lookupBy('name', 'nope', 'caseFold')", scope).IsNull);
    }

    [TestMethod]
    public void MergeBy_rejoins_an_option_split_across_repeated_codes()
    {
        // RFC 3396: a DHCP option longer than 255 octets is split across repeats of the same code and
        // MUST be concatenated on receipt.
        var opts = new ProtoValue.List(
        [
            EvalScope.Record(("code", ProtoValue.Of(43L)), ("value", ProtoValue.Of(new byte[] { 1, 2 }))),
            EvalScope.Record(("code", ProtoValue.Of(43L)), ("value", ProtoValue.Of(new byte[] { 3, 4 }))),
            EvalScope.Record(("code", ProtoValue.Of(53L)), ("value", ProtoValue.Of(new byte[] { 1 }))),
        ]);
        var scope = new EvalScope().Set("capture", EvalScope.Record(("options", opts)));

        var merged = Run("capture.options |> mergeBy('code', 'value')", scope).AsList();

        Assert.AreEqual(2, merged.Count, "the two code-43 records become one");
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 },
            ((ProtoValue.Rec)merged[0]).Members["value"].AsBytes());
    }

    [TestMethod]
    public void Suffixes_drives_dns_name_compression_sharing()
    {
        var parts = Run("'nexaprint._http._tcp.local' |> suffixes('.')").AsList();

        Assert.AreEqual(4, parts.Count);
        Assert.AreEqual("nexaprint._http._tcp.local", parts[0].ToString());
        Assert.AreEqual("local", parts[^1].ToString());
    }

    [TestMethod]
    public void Lookup_replaces_what_would_otherwise_be_protocol_specific_engine_predicates()
    {
        // A cipher-suite or reason-code classification is data in a consts table, not three hard-coded
        // predicates inside the engine.
        var table = EvalScope.Record(
            ("0", ProtoValue.Of("accepted")),
            ("5", ProtoValue.Of("not authorized")));
        var scope = new EvalScope().Set("consts", EvalScope.Record(("connack", table)));

        Assert.AreEqual("not authorized", Run("'5' |> lookup(consts.connack)", scope).ToString());
        Assert.IsTrue(Run("'99' |> lookup(consts.connack)", scope).IsNull);
    }
}
