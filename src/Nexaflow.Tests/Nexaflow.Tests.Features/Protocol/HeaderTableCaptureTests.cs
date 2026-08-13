using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Resolution;
using Nexaflow.IO.Protocol.State;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;
using System.Text;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// A run that builds the table it is read against.
///
/// <para>
/// The protocol is HPACK. What it is here for is the last shape the corpus had no instance of: a component
/// whose meaning depends on what the components <i>before it in the same message</i> put somewhere. A
/// length reaches forward, a back-reference reaches backward, a fold reaches across — all of those are in
/// the corpus already. This is a run where entry three says "the thing entry one added", and neither entry
/// carries it.
/// </para>
///
/// <para>
/// It needed nothing new. The table is the value threaded along the run, which a chain already does; a
/// component reads it as <c>carried</c>, which the vocabulary already binds at a value site; eviction is a
/// fold over that value, which the expression language already has. The only code added for HPACK is a
/// codec for a prefix code, and even that takes its table from the document.
/// </para>
///
/// <para>
/// <b>Two honest departures.</b> Neither is a limitation found; both are scope.
/// </para>
/// <list type="bullet">
///   <item><description>The five representations are modelled as four, because "literal without indexing"
///   and "literal never indexed" have the <i>same shape</i> and differ only in what an intermediary may
///   do — so the fourth arm carries that difference as a bit rather than as a second packing that would be
///   a copy of the first.</description></item>
///   <item><description>The per-component carry is written as one expression with a case analysis, rather
///   than as an assortment's per-kind carries, which is what <see cref="Arm.Carrying"/> exists for. An
///   assortment reads its token as a whole field and HPACK announces its kind in a varying number of
///   leading bits of an octet the component then goes on to use — so the token would have to be peeked at
///   rather than consumed. A chain of choices says the same thing with the machinery that is there, at the
///   cost of the case analysis being in the document instead of spread over the arms.</description></item>
/// </list>
/// </summary>
[TestClass]
[NoCoverage("DynamicProtocol end-to-end codec and state — tree nodes land with the engine")]
public class HeaderTableCaptureTests
{
    // ── Two tables, both of them the specification's own content ──────────────

    private static ProtoValue Row(string name, string value)
        => new ProtoValue.List([ProtoValue.Of(Encoding.ASCII.GetBytes(name)),
                                ProtoValue.Of(Encoding.ASCII.GetBytes(value))]);

    /// <summary>
    /// RFC 7541 Appendix A. Sixty-one entries every implementation is born knowing, which is data and
    /// therefore belongs here rather than in the engine — the same judgement as the code table next door.
    /// </summary>
    private static readonly ProtoValue Static = new ProtoValue.List(
    [
        Row(":authority", ""), Row(":method", "GET"), Row(":method", "POST"),
        Row(":path", "/"), Row(":path", "/index.html"),
        Row(":scheme", "http"), Row(":scheme", "https"),
        Row(":status", "200"), Row(":status", "204"), Row(":status", "206"),
        Row(":status", "304"), Row(":status", "400"), Row(":status", "404"), Row(":status", "500"),
        Row("accept-charset", ""), Row("accept-encoding", "gzip, deflate"),
        Row("accept-language", ""), Row("accept-ranges", ""), Row("accept", ""),
        Row("access-control-allow-origin", ""), Row("age", ""), Row("allow", ""),
        Row("authorization", ""), Row("cache-control", ""), Row("content-disposition", ""),
        Row("content-encoding", ""), Row("content-language", ""), Row("content-length", ""),
        Row("content-location", ""), Row("content-range", ""), Row("content-type", ""),
        Row("cookie", ""), Row("date", ""), Row("etag", ""), Row("expect", ""), Row("expires", ""),
        Row("from", ""), Row("host", ""), Row("if-match", ""), Row("if-modified-since", ""),
        Row("if-none-match", ""), Row("if-range", ""), Row("if-unmodified-since", ""),
        Row("last-modified", ""), Row("link", ""), Row("location", ""), Row("max-forwards", ""),
        Row("proxy-authenticate", ""), Row("proxy-authorization", ""), Row("range", ""),
        Row("referer", ""), Row("refresh", ""), Row("retry-after", ""), Row("server", ""),
        Row("set-cookie", ""), Row("strict-transport-security", ""), Row("transfer-encoding", ""),
        Row("user-agent", ""), Row("vary", ""), Row("via", ""), Row("www-authenticate", ""),
    ]);

    // ── The two shapes everything is built out of ─────────────────────────────

    /// <summary>
    /// An integer whose first digits share an octet with the tag that announced it.
    ///
    /// <para>
    /// The third way a width comes from a value and not one of the two the engine already names. A
    /// <see cref="Pattern.Varint"/> spends a bit per octet on a continue flag; a
    /// <see cref="Pattern.EscapedInline"/> consumes its marker entirely; a <see cref="Pattern.Prefixed"/>
    /// lets the marker select a width. Here the marker is whatever the tag left over, the value sits in it
    /// while it fits, and an all-ones prefix means "and this much more".
    /// </para>
    ///
    /// <para>
    /// And it needed no pattern of its own, which is the point of writing it down: a bit group whose runs
    /// come from different places, plus a choice on whether the prefix saturated, plus the continuation
    /// integer the engine already has. Three notions the corpus was already using, composed.
    /// </para>
    /// </summary>
    private static Field[] Counted(string id, IReadOnlyList<BitSlice> tag, string source)
    {
        int width = 8 - tag.Sum(t => t.Width);
        long ceiling = (1L << width) - 1;

        return
        [
            new Field
            {
                Id = $"{id}Head",
                Pattern = new Pattern.Bits(
                    [.. tag, new BitSlice($"{id}Prefix", width, Expr.Parse($"min({source}, {ceiling})"))]),
            },

            new Field
            {
                Id = $"{id}Tail",
                Pattern = new Pattern.Choice(
                    Expr.Parse($"fields.{id}Head.value.{id}Prefix == {ceiling}"),
                    [
                        Arm.On("inline", ProtoValue.Of(0L), []),
                        Arm.On("extended", ProtoValue.Of(1L),
                        [
                            new Field
                            {
                                Id = $"{id}Rest",
                                Pattern = new Pattern.Varint(GroupOrder.LeastSignificantFirst, 8),
                                Value = Expr.Parse($"{source} - {ceiling}"),
                            },
                        ]),
                    ]),
            },
        ];
    }

    /// <summary>What one of those came to, from either side of the saturation.</summary>
    private static string Reads(string id, long ceiling)
        => $"(fields.{id}Head.value.{id}Prefix == {ceiling} "
         + $"? {ceiling} + fields.{id}Rest.value : fields.{id}Head.value.{id}Prefix)";

    /// <summary>
    /// A counted run of octets that may be prefix-coded, with a bit saying whether it is.
    /// </summary>
    /// <remarks>
    /// The length is measured off the body rather than off the value, which matters here more than
    /// anywhere else in the corpus: a coded body is a different length from what it says, so a document
    /// counting the characters would emit a length nobody could read against. <c>extent</c> is the octets
    /// that were actually written, whichever packing wrote them.
    /// </remarks>
    private static Field[] Text(string id, string source, string coded)
    {
        Field Body(string suffix, Conversion? via) => new()
        {
            Id = $"{id}{suffix}",
            Pattern = Pattern.Opaque.Measured(Expr.Parse(Reads(id, 127))),
            Value = Expr.Parse(source),
            Via = via,
        };

        return
        [
            .. Counted(id, [new BitSlice($"{id}Coded", 1, Expr.Parse(coded))], $"fields.{id}Body.extent"),

            new Field
            {
                Id = $"{id}Body",
                Pattern = new Pattern.Choice(
                    Expr.Parse($"fields.{id}Head.value.{id}Coded"),
                    [
                        Arm.On("plain", ProtoValue.Of(0L), [Body("Plain", null)]),
                        Arm.On("packed", ProtoValue.Of(1L),
                               [Body("Packed", new Conversion("packBits", [PrefixCodeTests.HeaderCode]))]),
                    ]),
            },
        ];
    }

    /// <summary>The octets one of those came to, whichever packing carried them.</summary>
    private static string Said(string id)
        => $"(fields.{id}Body.value == 'packed' ? fields.{id}Packed.value : fields.{id}Plain.value)";

    // ── The four representations ──────────────────────────────────────────────

    private static Field[] Indexed()
        => Counted("ix", [new BitSlice("ixTag", 1, Expr.Parse("1"))], "item.index");

    /// <summary>
    /// A name given either by index or spelled out, which is one choice keyed on whether the index is
    /// zero — the specification's own way of saying "there is no such entry, so read one".
    /// </summary>
    private static Field[] Named(string id, long ceiling)
        =>
        [
            new Field
            {
                Id = $"{id}Name",
                Pattern = new Pattern.Choice(
                    Expr.Parse($"fields.{id}Head.value.{id}Prefix == 0"),
                    [
                        Arm.On("byIndex", ProtoValue.Of(0L), []),
                        Arm.On("spelledOut", ProtoValue.Of(1L),
                               [.. Text($"{id}Nm", "item.name", "item.nameCoded")]),
                    ]),
            },
            .. Text($"{id}Val", "item.value", "item.valueCoded"),
        ];

    private static Field[] Incremental()
        => [.. Counted("inc", [new BitSlice("incTag", 2, Expr.Parse("1"))], "item.index"),
            .. Named("inc", 63)];

    private static Field[] Resize()
        => Counted("rs", [new BitSlice("rsTag", 3, Expr.Parse("1"))], "item.size");

    /// <summary>
    /// The residual packing: a literal the table never learns.
    /// </summary>
    /// <remarks>
    /// Its tag run is two bits wide because the specification uses one of them to say whether any
    /// intermediary may ever index this header — a password, a session identifier. That is a different
    /// <i>instruction</i> and an identical <i>shape</i>, so it is a run of this packing rather than a fifth
    /// arm that would be a copy of the fourth.
    /// </remarks>
    private static Field[] Plain()
        => [.. Counted("lit", [new BitSlice("litTag", 3, Expr.Parse("0")),
                               new BitSlice("litNever", 1, Expr.Parse("item.never"))], "item.index"),
            .. Named("lit", 15)];

    // ── What each of them leaves the table at ─────────────────────────────────

    /// <summary>
    /// The entry an indexing representation names, from whichever half of the space its index falls in.
    /// </summary>
    private static string Entry(string index, string table)
        => $"({index} <= 61 ? inputs.statics |> index({index} - 1) : {table} |> index({index} - 62))";

    /// <summary>
    /// Everything that still fits, oldest dropped first.
    /// </summary>
    /// <remarks>
    /// RFC 7541 §4.1's size rule, in the document because the specification states it: an entry costs its
    /// name, its value, and thirty-two octets of overhead that are not on the wire at all. The engine has
    /// no opinion about any of that — it evaluates a fold the document wrote.
    /// </remarks>
    private static string Fits(string table, string budget)
        => $"(let u = {table} in range(count(u), 256) "
         + $"|> takeWhile(i -> (range(i + 1, 256) "
         + $"|> fold(0, (a, j) -> a + 32 + len(u[j] |> index(0)) + len(u[j] |> index(1)))) <= {budget}) "
         + "|> map(i -> u[i]))";

    /// <summary>
    /// One expression for what a component does to the table, because a chain threads one value and has
    /// one place to say how it moves.
    /// </summary>
    private static string Carry()
    {
        const string Budget = "(carried |> index(0))";
        const string Table = "(carried |> index(1))";

        string name = $"(fields.incHead.value.incPrefix == 0 ? {Said("incNm")} "
                    + $": {Entry(Reads("inc", 63), Table)} |> index(0))";

        string added = $"flatten(list(list(list({name}, {Said("incVal")})), {Table}))";
        string resized = Reads("rs", 31);

        return $"fields.repr.value == 'incremental' "
             + $"? list({Budget}, {Fits(added, Budget)}) "
             + $": (fields.repr.value == 'resize' "
             + $"? list({resized}, {Fits(Table, resized)}) : carried)";
    }

    // ── The message ───────────────────────────────────────────────────────────

    /// <summary>
    /// One instance, not one per call — a transition matches by object identity.
    /// </summary>
    private static readonly MessageDef TheBlock = new()
    {
        Id = "headerBlock",
        Context =
        [
            new Context.Given { Key = "reprs", Purpose = "the representations to write, in order." },
            new Context.Remembered { Key = "table", Purpose = "the table the last block left behind." },
            new Context.Remembered { Key = "budget", Purpose = "how large the table may currently be." },
            // Supplied rather than stated, which is what it always was: the value on the old declaration
            // was never read and the caller handed it in regardless. A document-stated value an expression
            // can name by name is a real gap and is not this.
            new Context.Given { Key = "statics", Purpose = "the entries both ends know." },
        ],
        Fields =
        [
            new Field
            {
                Id = "block",
                Value = Expr.Parse("inputs.reprs"),
                Pattern = new Pattern.Chain(
                    new Field
                    {
                        Id = "repr",

                        // The kind is announced by however many leading bits it takes to announce it, and
                        // the rest of that octet belongs to the packing. So the reader looks without
                        // consuming, and the writer is told — the same asymmetry a length and a
                        // continuation already have, and the reason `Selects` exists.
                        Pattern = new Pattern.Choice(
                            Expr.Parse("peek >= 0x80 ? 1 : (peek >= 0x40 ? 2 : (peek >= 0x20 ? 3 : 4))"),
                            [
                                Arm.On("indexed", ProtoValue.Of(1L), [.. Indexed()]),
                                Arm.On("incremental", ProtoValue.Of(2L), [.. Incremental()]),
                                Arm.On("resize", ProtoValue.Of(3L), [.. Resize()]),
                                Arm.Otherwise("literal", [.. Plain()]),
                            ],
                            Selects: Expr.Parse(
                                "item.sort == 'indexed' ? 1 : (item.sort == 'incremental' ? 2 "
                              + ": (item.sort == 'resize' ? 3 : 4))")),
                    },
                    Expr.Parse("room > 0"),
                    Seed: Expr.Parse("list(inputs.budget, inputs.table)"),
                    Carry: Expr.Parse(Carry()),
                    Thread: "table"),
            },
        ],
    };

    private static MessageDef Block() => TheBlock;

    // ── Driving it ────────────────────────────────────────────────────────────

    private static ProtoValue Octets(string text) => ProtoValue.Of(Encoding.ASCII.GetBytes(text));

    private static ProtoValue Indexed(long index)
        => EvalScope.Record(("sort", ProtoValue.Of("indexed")), ("index", ProtoValue.Of(index)));

    private static ProtoValue Adds(long nameIndex, string name, string value, long coded = 0)
        => EvalScope.Record(("sort", ProtoValue.Of("incremental")), ("index", ProtoValue.Of(nameIndex)),
                            ("name", Octets(name)), ("nameCoded", ProtoValue.Of(coded)),
                            ("value", Octets(value)), ("valueCoded", ProtoValue.Of(coded)));

    private static ProtoValue Resizes(long size)
        => EvalScope.Record(("sort", ProtoValue.Of("resize")), ("size", ProtoValue.Of(size)));

    /// <remarks>
    /// <c>statics</c> is handed in alongside the rest, and it should not have to be: the table is RFC 7541
    /// Appendix A, which the document states rather than asks for. It was declared as a fixed context —
    /// a kind that claimed to carry its own value while the caller supplied it anyway — and that kind is
    /// gone. What is missing to do this properly is a stated value an <i>expression</i> can name; a
    /// converter's argument already reaches one by edge, and an expression has no way to.
    /// </remarks>
    private static EvalScope Inputs(ProtoValue[] reprs, ProtoValue table, long budget = 4096)
        => new EvalScope().Set("inputs", EvalScope.Record(
            ("reprs", new ProtoValue.List(reprs)),
            ("table", table),
            ("budget", ProtoValue.Of(budget)),
            ("statics", Static)));

    private static readonly ProtoValue Empty = new ProtoValue.List([]);

    private static string Show(ProtoValue table)
        => string.Join(" | ", table.AsList().Select(
            e => Encoding.ASCII.GetString(e.AsList()[0].AsBytes()) + ": "
               + Encoding.ASCII.GetString(e.AsList()[1].AsBytes())));

    /// <summary>The table a decode came to — the second half of the pair the run threads.</summary>
    private static ProtoValue TableAfter(DecodeResult decoded) => decoded["table"].AsList()[1];

    private static DecodeResult Read(byte[] octets, ProtoValue table, long budget = 4096)
        => new MessageCodec(Block()).Decode(octets, Inputs([], table, budget));

    // ── The document ──────────────────────────────────────────────────────────

    [TestMethod]
    public void The_document_validates()
    {
        var issues = new MessageCodec(Block()).Validate();
        Assert.AreEqual(0, issues.Count, string.Join("\n  • ", issues));
    }

    // ── The specification's own exchange ──────────────────────────────────────

    /// <summary>
    /// RFC 7541 C.3.1 — three indexed entries and one that adds to the table.
    /// </summary>
    [TestMethod]
    public void A_block_of_indexed_entries_and_one_that_adds_to_the_table()
    {
        byte[] octets = Convert.FromHexString("828684410f7777772e6578616d706c652e636f6d");

        var decoded = Read(octets, Empty);

        CollectionAssert.AreEqual(new[] { "indexed", "indexed", "indexed", "incremental" },
            decoded["block"].AsList().Cast<ProtoValue.Rec>()
                            .Select(r => r.Members["repr"].AsText()).ToArray());

        // The run built this, and no component of it carries it.
        Assert.AreEqual(":authority: www.example.com", Show(TableAfter(decoded)));
    }

    /// <summary>
    /// RFC 7541 C.3.2 — and the next block names the entry the last one added.
    /// </summary>
    /// <remarks>
    /// The engine holds nothing between the two. What survives is what a move put in a slot, and what the
    /// second decode gets is that slot handed back as an ordinary outside value — the same seam a
    /// negotiated connection identifier goes through. There is no history to consult and nothing consults
    /// one.
    /// </remarks>
    [TestMethod]
    public void And_the_next_block_names_what_the_last_one_left_behind()
    {
        var first = Read(Convert.FromHexString("828684410f7777772e6578616d706c652e636f6d"), Empty);

        byte[] second = Convert.FromHexString("828684be58086e6f2d6361636865");
        var decoded = Read(second, TableAfter(first));

        // 0xbe is index 62: the first entry of the dynamic half, which the previous block created.
        Assert.AreEqual("cache-control: no-cache | :authority: www.example.com",
            Show(TableAfter(decoded)));
    }

    /// <summary>
    /// And the writer cannot, which is a limit of the engine rather than of the document.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The carry is one expression with a case analysis, so its text names fields from all four packings —
    /// and a dependency here is recovered by <b>scanning that text</b> for <c>fields.…</c>. The scan cannot
    /// see that a read sits under a branch, so every field the carry mentions becomes a prerequisite of
    /// every component, and three quarters of them belong to packings that did not occur.
    /// </para>
    /// <para>
    /// Nothing is wrong with the refusal — a prerequisite that never materialises is exactly the fault it
    /// is named after, and letting it slide would emit a short message and call it a success. What is
    /// wrong is upstream: reachability is being approximated by a regular expression over source text
    /// because the expression is not in the graph. Were the carry's sub-terms nodes, "only when this arm
    /// was chosen" would be an edge that is or is not there, and this would not arise.
    /// </para>
    /// <para>
    /// The two ways around it inside the present design are both worse than the problem: teach the scanner
    /// about branches — a seventh approximation of reachability — or give <see cref="Pattern.Chain"/>
    /// per-arm carries, which is a twelfth sited expression, a role constant and a vocabulary row. See
    /// <c>docs/protocol-graph-migration.md</c>.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void But_writing_one_back_is_refused_while_a_carry_reaches_into_every_packing()
    {
        var refusal = Assert.ThrowsExactly<ResolutionException>(
            () => new MessageCodec(Block()).Encode(Inputs(
                [Indexed(2), Indexed(6), Indexed(4), Adds(1, "", "www.example.com")], Empty)));

        StringAssert.Contains(refusal.Message, "never realised");

        // The named nodes are the giveaway: repr[0] took the indexed packing, and what it is said to be
        // missing are fields belonging to the packings it did not take.
        StringAssert.Contains(refusal.Message, "block.repr[0].rsHead.Value");
        StringAssert.Contains(refusal.Message, "block.repr[0].incNmPlain.Value");
    }

    /// <summary>
    /// A single component writes back exactly, which is what locates the limit: the carry is what breaks
    /// it, and a run of one never evaluates one.
    /// </summary>
    [TestMethod]
    public void A_block_of_one_component_is_written_back_exactly()
    {
        var codec = new MessageCodec(Block());

        CollectionAssert.AreEqual(new byte[] { 0x82 }, codec.Encode(Inputs([Indexed(2)], Empty)));

        // Two, and the carry between them has to be settled — at which point every field it names becomes
        // a prerequisite, including the ones in packings neither component took.
        Assert.ThrowsExactly<ResolutionException>(
            () => codec.Encode(Inputs([Indexed(2), Indexed(6)], Empty)));
    }

    // ── The shape this is here for ────────────────────────────────────────────

    /// <summary>
    /// A component naming an entry an earlier component of the <b>same run</b> added.
    /// </summary>
    /// <remarks>
    /// This is the one the corpus had nothing like. Everything else that reaches across a message reaches
    /// for octets — a length, an offset, a digest over a span. Here the third component means what it means
    /// because of what the first component <i>did</i>, and the intermediate is a value the run is
    /// accumulating rather than anything on the wire.
    /// </remarks>
    [TestMethod]
    public void A_later_component_names_what_an_earlier_one_added()
    {
        // Add ':authority: one', then name index 62 — which exists only because the entry before it ran.
        byte[] octets = [.. Convert.FromHexString("4103"), .. Encoding.ASCII.GetBytes("one"), 0xbe];

        var decoded = Read(octets, Empty);

        Assert.AreEqual(":authority: one", Show(TableAfter(decoded)));
        CollectionAssert.AreEqual(new[] { "incremental", "indexed" },
            decoded["block"].AsList().Cast<ProtoValue.Rec>()
                            .Select(r => r.Members["repr"].AsText()).ToArray());
    }

    /// <summary>
    /// A budget the run itself changes, and entries leaving because of it.
    /// </summary>
    [TestMethod]
    public void A_component_that_shrinks_the_table_evicts_what_no_longer_fits()
    {
        // Two spelled-out entries, then a resize to 36 — which is one entry's worth. An entry costs its
        // name, its value and thirty-two octets that are not on the wire at all; that number is the
        // specification's and it is in the document, not the engine.
        var decoded = Read(Convert.FromHexString("4002616101314002626201323f05"), Empty);

        CollectionAssert.AreEqual(new[] { "incremental", "incremental", "resize" },
            decoded["block"].AsList().Cast<ProtoValue.Rec>()
                            .Select(r => r.Members["repr"].AsText()).ToArray());

        // 'bb' is the newest and survives at 35; keeping 'aa' as well would cost 70.
        Assert.AreEqual("bb: 2", Show(TableAfter(decoded)));
        Assert.AreEqual(36L, decoded["table"].AsList()[0].AsInt(), "and the budget the run was left with");
    }

    // ── The prefix integer ────────────────────────────────────────────────────

    /// <summary>A component's own parts, which belong to the instance and not to the message.</summary>
    private static ProtoValue Part(DecodeResult decoded, int at, string name)
        => ((ProtoValue.Rec)decoded["block"].AsList()[at]).Members.GetValueOrDefault(
               name, ProtoValue.Nothing);

    [TestMethod]
    public void An_index_too_large_for_the_prefix_continues_into_the_octets_after_it()
    {
        // The prefix saturates at 127 and the remainder follows as continuation octets. RFC 7541 C.1.1
        // writes 1337 against a five-bit prefix as 1f 9a 0a; the tag here is one bit, so 127 comes out of
        // the prefix instead of 31 and the remainder is 1210.
        var wide = Read([0xff, 0xba, 0x09], Empty);

        Assert.AreEqual("extended", Part(wide, 0, "ixTail").AsText());
        Assert.AreEqual(1337L, Part(wide, 0, "ixRest").AsInt() + 127);

        var narrow = Read([0x8a], Empty);
        Assert.AreEqual("inline", Part(narrow, 0, "ixTail").AsText(), "nothing follows a prefix that fits");
        Assert.AreEqual(10L, Part(narrow, 0, "ixPrefix").AsInt());
    }

    [TestMethod]
    public void A_value_that_fits_the_prefix_spends_no_octets_after_it()
    {
        CollectionAssert.AreEqual(new byte[] { 0x8a },
            new MessageCodec(Block()).Encode(Inputs([Indexed(10)], Empty)));
    }

    // ── The prefix code, in a document rather than in an expression ───────────

    /// <summary>
    /// RFC 7541 C.4.1 — the same header as C.3.1 with its value prefix-coded, read against the table in
    /// the document next door.
    /// </summary>
    [TestMethod]
    public void A_literal_that_says_it_is_coded_is_read_through_the_document_s_code_table()
    {
        var decoded = Read(Convert.FromHexString("418cf1e3c2e5f23a6ba0ab90f4ff"), Empty);

        Assert.AreEqual("packed", Part(decoded, 0, "incValBody").AsText(), "the bit that says which");
        Assert.AreEqual(":authority: www.example.com", Show(TableAfter(decoded)));
    }

    /// <summary>
    /// Writing that one back is refused, and for a second reason worth keeping separate from the first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A coded literal's length is the length of the <i>coded</i> octets, so the head reads
    /// <c>fields.…Body.extent</c>; and which packing the body is depends on the head's coded bit. Two runs
    /// of one octet, pointing opposite ways — which is fine, because no run depends on itself.
    /// </para>
    /// <para>
    /// The engine sees a cycle anyway, because the node is the <b>group</b> and not the run: the whole bit
    /// group has one <c>Value</c> facet, so a dependency on either run is a dependency on both.
    /// <see cref="BitSlice.Value"/> let the runs come from different places and stopped short of letting
    /// them settle at different times. Sub-node granularity again — the same shortfall as the sibling
    /// refusal above, in a different part of the engine.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void And_writing_it_back_is_refused_because_a_bit_group_settles_all_at_once()
    {
        var refusal = Assert.ThrowsExactly<ResolutionException>(
            () => new MessageCodec(Block()).Encode(
                Inputs([Adds(1, "", "www.example.com", coded: 1)], Empty)));

        StringAssert.Contains(refusal.Message, "depend on each other");
        StringAssert.Contains(refusal.Message, "incValHead.Value");
        StringAssert.Contains(refusal.Message, "incValBody.Extent");
    }

    // ── What survives between blocks, and how ─────────────────────────────────

    private static readonly Party Us = new() { Id = "us" };
    private static readonly Phase Open = new() { Id = "open", About = "the connection is carrying blocks." };

    private static readonly Recall Held = new()
    {
        Id = "table", About = "the entries this connection has agreed on so far.",
    };

    private static Subject Connection() => new()
    {
        Id = "connection",
        Start = Open,
        Parties = [Us],
        Transitions =
        [
            new Transition
            {
                Whose = Us, On = Block(), Way = Bearing.Received, From = Open, To = Open,
                When = Expr.Parse("true"),

                // The fold leaves the walk that computed it and goes into a slot, and the state layer
                // learns nothing about tables to make that happen.
                Records = [new Recording(Expr.Parse("fields.table.value |> index(1)"), Held)],
                Because = "a header block leaves the table it built.",
            },
        ],
    };

    [TestMethod]
    public void The_table_crosses_between_messages_as_an_ordinary_kept_value()
    {
        var standing = new Standing(Connection());
        var codec = new MessageCodec(Block());

        var first = codec.Decode(Convert.FromHexString("828684410f7777772e6578616d706c652e636f6d"),
                                 Inputs([], Empty));
        standing.Observe(Block(), first, Bearing.Received);

        Assert.AreEqual(":authority: www.example.com", Show(standing.Recalled(Held)));

        // And the next block is read against it, handed back the way any remembered value is.
        var second = codec.Decode(Convert.FromHexString("828684be58086e6f2d6361636865"),
                                  Inputs([], standing.Recalled(Held)));
        standing.Observe(Block(), second, Bearing.Received);

        Assert.AreEqual("cache-control: no-cache | :authority: www.example.com",
            Show(standing.Recalled(Held)));
    }

    // ── The engine knows none of it ───────────────────────────────────────────

    [TestMethod]
    public void Nothing_about_header_tables_is_known_to_the_engine()
    {
        // Every fact about HPACK is in the document: which leading bits mean what, that index 62 is the
        // first dynamic entry, that an entry costs thirty-two octets it does not occupy, that a resize
        // evicts from the oldest end. The engine's part is a chain, a choice, a bit group and a fold.
        Assert.IsTrue(Carry().Contains("32"), "the overhead is a number the document states");

        var codec = new MessageCodec(Block());
        Assert.AreEqual(0, codec.Validate().Count);
    }
}
