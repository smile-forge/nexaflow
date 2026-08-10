using Nexaflow.IO.Protocol.Values;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Nexaflow.IO.Protocol.Converters;

/// <summary>
/// The closed converter set.
///
/// <para>
/// Each entry names the corpus protocol that forced it, because a converter with no forcing case is
/// surface area with no evidence behind it. A handful are retained without a forcing case
/// (<c>base64</c>, <c>sha1</c>, <c>crc32</c>, <c>hmacSha256</c>) on the grounds that the set is closed and
/// it is cheaper to over-populate once than to ship a code change per new protocol.
/// </para>
/// </summary>
public sealed class ConverterTable
{
    private readonly Dictionary<string, Converter> _byName = new(StringComparer.Ordinal);

    public static ConverterTable Default { get; } = Build();

    public bool TryGet(string name, out Converter? converter)
    {
        var ok = _byName.TryGetValue(name, out var c);
        converter = c;
        return ok;
    }

    public IReadOnlyCollection<Converter> All => _byName.Values;

    private ConverterTable Add(Converter c)
    {
        _byName[c.Name] = c;
        return this;
    }

    private ConverterTable Add(string name, ValueKinds accepts, ValueKinds produces, string? inverse,
                               Func<ProtoValue, IReadOnlyList<ProtoValue>, ProtoValue> apply, string summary)
        => Add(new Converter(name, accepts, produces, inverse, apply, summary));

    private static ConverterTable Build()
    {
        var t = new ConverterTable();

        // ── text / encode ────────────────────────────────────────────────────
        t.Add("hex", ValueKinds.Bytes, ValueKinds.Text, "unhex",
            (v, _) => ProtoValue.Of(Convert.ToHexString(v.AsBytes()).ToLowerInvariant()),
            "octets to lower-case hex text");

        t.Add("unhex", ValueKinds.Text, ValueKinds.Bytes, "hex",
            (v, _) => ProtoValue.Of(ParseHex(v.AsText())),
            "hex text to octets; whitespace and ':' separators are ignored");

        t.Add("ascii", ValueKinds.Bytes, ValueKinds.Text, "unascii",
            (v, _) => ProtoValue.Of(Encoding.ASCII.GetString(v.AsBytes())),
            "octets to ASCII text");

        t.Add("unascii", ValueKinds.Text, ValueKinds.Bytes, "ascii",
            (v, _) => ProtoValue.Of(Encoding.ASCII.GetBytes(v.AsText())),
            "ASCII text to octets");

        // Strict in both directions, and that is the entire point of it. `Encoding.UTF8` replaces anything
        // ill-formed with U+FFFD and carries on, so a truncated sequence, an overlong form or a raw
        // surrogate decodes to a plausible string and then re-encodes to different octets — silently, and
        // in the exact place a specification says the receiver must reject the message. Substituting a
        // character for a fault is the failure mode, not the recovery.
        t.Add("utf8", ValueKinds.Bytes, ValueKinds.Text, "unutf8",
            (v, _) => ProtoValue.Of(StrictUtf8.GetString(v.AsBytes())),
            "octets to UTF-8 text; ill-formed input is an error, never a replacement character");
        t.Add("unutf8", ValueKinds.Text, ValueKinds.Bytes, "utf8",
            (v, _) => ProtoValue.Of(StrictUtf8.GetBytes(v.AsText())),
            "UTF-8 text to octets; an unpaired surrogate is an error");

        t.Add("latin1", ValueKinds.Bytes, ValueKinds.Text, "unlatin1",
            (v, _) => ProtoValue.Of(Encoding.Latin1.GetString(v.AsBytes())),
            "octets to Latin-1 text — HTTP-style header bytes, which are not UTF-8");
        t.Add("unlatin1", ValueKinds.Text, ValueKinds.Bytes, "latin1",
            (v, _) => ProtoValue.Of(Encoding.Latin1.GetBytes(v.AsText())), "Latin-1 text to octets");

        t.Add("base64", ValueKinds.Bytes, ValueKinds.Text, "unbase64",
            (v, _) => ProtoValue.Of(Convert.ToBase64String(v.AsBytes())), "octets to base64");
        t.Add("unbase64", ValueKinds.Text, ValueKinds.Bytes, "base64",
            (v, _) => ProtoValue.Of(Convert.FromBase64String(v.AsText())), "base64 to octets");

        t.Add("mac", ValueKinds.Text, ValueKinds.Bytes, "unmac",
            (v, _) => ProtoValue.Of(ParseHex(v.AsText())),
            "'aa:bb:cc:dd:ee:ff' to 6 octets — Wake-on-LAN's magic packet");
        t.Add("unmac", ValueKinds.Bytes, ValueKinds.Text, "mac",
            (v, _) => ProtoValue.Of(string.Join(':', v.AsBytes().Select(b => b.ToString("x2")))),
            "6 octets to 'aa:bb:cc:dd:ee:ff'");

        t.Add("ipv4", ValueKinds.Text, ValueKinds.Bytes, "unipv4",
            (v, _) => ProtoValue.Of(IPAddress.Parse(v.AsText()).GetAddressBytes()),
            "dotted-quad to 4 octets — DHCP yiaddr/siaddr");
        t.Add("unipv4", ValueKinds.Bytes, ValueKinds.Text, "ipv4",
            (v, _) => ProtoValue.Of(new IPAddress(v.AsBytes()).ToString()), "4 octets to dotted-quad");

        // Partial isomorphism with `fit`, on the domain "text containing no NUL and no longer than the
        // width". The arities differ — `fit` takes the width — which is exactly why the relation has to be
        // declared on both sides rather than inferred.
        t.Add("cstr", ValueKinds.Bytes, ValueKinds.Text, "fit",
            (v, _) =>
            {
                var b = v.AsBytes();
                int end = Array.IndexOf(b, (byte)0);
                return ProtoValue.Of(Encoding.ASCII.GetString(b, 0, end < 0 ? b.Length : end));
            },
            "NUL-terminated text out of a fixed-width field — DHCP sname/file");

        // ── numeric ──────────────────────────────────────────────────────────
        t.Add("int", ValueKinds.Any, ValueKinds.Int, null,
            (v, _) => ProtoValue.Of(v.AsInt()), "to Int, applying the Bool coercion");

        // `zero` is REQUIRED, and its absence was a live P2 violation: hard-coding zero → one 0x00 octet
        // is one family's rule, and it made a zero-valued minimal option (which must encode to NO octets)
        // inexpressible while the summary claimed to serve both.
        t.Add("minuint", ValueKinds.Numeric, ValueKinds.Bytes, "unminuint",
            (v, a) => ProtoValue.Of(MinimalUnsigned(v.AsInt(), ZeroRule(a, 0))),
            "minimal-width unsigned octets. zero: 'oneByte' (a single 00) or 'empty' (no octets) — "
          + "required, because which one is correct is a property of the protocol, not of the notion");
        t.Add("unminuint", ValueKinds.Bytes, ValueKinds.Int, "minuint",
            (v, _) =>
            {
                long n = 0;
                foreach (var b in v.AsBytes()) n = (n << 8) | b;
                return ProtoValue.Of(n);
            },
            "big-endian octets to an unsigned value");

        t.Add("minint", ValueKinds.Numeric, ValueKinds.Bytes, "unminint",
            (v, _) => ProtoValue.Of(MinimalSigned(v.AsInt())),
            "minimal two's-complement octets — BER INTEGER; 821915 must be 0c 8a 9b, three octets");
        t.Add("unminint", ValueKinds.Bytes, ValueKinds.Int, "minint",
            (v, _) =>
            {
                var b = v.AsBytes();
                if (b.Length == 0) return ProtoValue.Of(0L);
                long n = (sbyte)b[0];
                for (int i = 1; i < b.Length; i++) n = (n << 8) | b[i];
                return ProtoValue.Of(n);
            },
            "two's-complement octets to a signed value");

        // `order` is REQUIRED. Omitting it was the sharpest violation in the engine: the notion is
        // "a value spread across octets with a continue flag", which two unrelated families exhibit in
        // OPPOSITE group orders. Most-significant-first reads 8f 65 as 2021; least-significant-first reads
        // 8f 01 as 143. With the parameter missing the second family cannot use this at all, so a general
        // name sat over one family's codec — and no name scan can see that.
        t.Add("base128", ValueKinds.Int, ValueKinds.Bytes, "unbase128",
            (v, a) => ProtoValue.Of(Base128(v.AsInt(), MsbFirst(a, 0))),
            "value to 7-bit continuation octets. order: 'msbFirst' or 'lsbFirst' — required");
        t.Add("unbase128", ValueKinds.Bytes, ValueKinds.Int, "base128",
            (v, a) =>
            {
                var bytes = v.AsBytes();
                bool msbFirst = MsbFirst(a, 0);
                long n = 0;

                if (msbFirst)
                    foreach (var b in bytes) n = (n << 7) | (byte)(b & 0x7F);
                else
                    for (int i = bytes.Length - 1; i >= 0; i--) n = (n << 7) | (byte)(bytes[i] & 0x7F);

                return ProtoValue.Of(n);
            },
            "7-bit continuation octets to a value. order: 'msbFirst' or 'lsbFirst' — required");

        // No hierarchical-identifier converter. The leading-pair merge (40x + y with a saturating inverse)
        // is one encoding family's rule, and a converter for it would be a protocol specific with a general
        // name — the exact failure this table is guarded against. It composes from `split`, `undecimal`,
        // `base128`, `flatten` and `scan`, so it lives in a document instead; see the worked transform in
        // TransformLanguageTests. If a notion can only be described by naming a protocol, it is not a notion.

        // Both widths REQUIRED — a default fraction width is one protocol's layout as an engine constant.
        t.Add("fixed", ValueKinds.Numeric, ValueKinds.Int, "unfixed",
            (v, a) => ProtoValue.Of(
                (long)Math.Round(v.AsNumber() * Math.Pow(2, Required(a, 1, "fixed", "fraction bits").AsInt()))),
            "real to fixed-point, e.g. fixed(16, 16) — integer and fraction widths both required");
        t.Add("unfixed", ValueKinds.Int, ValueKinds.Number, "fixed",
            (v, a) => ProtoValue.Of(
                v.AsInt() / Math.Pow(2, Required(a, 1, "unfixed", "fraction bits").AsInt())),
            "fixed-point to real — widths required");

        t.Add("decimal", ValueKinds.Numeric, ValueKinds.Text, "undecimal",
            (v, a) =>
            {
                var s = v.AsInt().ToString(CultureInfo.InvariantCulture);
                if (a.Count > 0) s = s.PadLeft((int)a[0].AsInt(), a.Count > 1 ? a[1].AsText()[0] : '0');
                return ProtoValue.Of(s);
            },
            "value to decimal ASCII — every number in an SSDP header");
        t.Add("undecimal", ValueKinds.Text, ValueKinds.Int, "decimal",
            (v, _) => ProtoValue.Of(long.Parse(v.AsText().Trim(), CultureInfo.InvariantCulture)),
            "decimal ASCII to a value");

        // The bitwise operators are also converters, because the spec writes masking in pipeline form:
        // `capture.fc |> band(0x80) != 0`. Same semantics as the infix spelling, one implementation.
        t.Add("band", ValueKinds.Numeric, ValueKinds.Int, null,
            (v, a) => ProtoValue.Of(v.AsInt() & a[0].AsInt()),
            "bitwise and — Modbus flags an exception response by setting the top bit of the function code");
        t.Add("bor", ValueKinds.Numeric, ValueKinds.Int, null,
            (v, a) => ProtoValue.Of(v.AsInt() | a[0].AsInt()), "bitwise or");
        t.Add("bxor", ValueKinds.Numeric, ValueKinds.Int, null,
            (v, a) => ProtoValue.Of(v.AsInt() ^ a[0].AsInt()), "bitwise xor");
        // NOT inverses of each other, despite the symmetry. `shr(shl(x,k),k)` loses high bits and
        // `shl(shr(x,k),k)` loses low ones — 5 >> 1 << 1 is 4. Declaring the pair as mutual inverses was
        // simply false, and nothing noticed until the round-trip laws were actually checked.
        t.Add("shl", ValueKinds.Numeric, ValueKinds.Int, null,
            (v, a) => ProtoValue.Of(v.AsInt() << (int)a[0].AsInt()), "shift left (lossy; no inverse)");
        t.Add("shr", ValueKinds.Numeric, ValueKinds.Int, null,
            (v, a) => ProtoValue.Of(v.AsInt() >> (int)a[0].AsInt()), "shift right (lossy; no inverse)");

        t.Add("mod", ValueKinds.Int, ValueKinds.Int, null,
            (v, a) => ProtoValue.Of(((v.AsInt() % a[0].AsInt()) + a[0].AsInt()) % a[0].AsInt()),
            "non-negative modulo — sequence counters that wrap");
        t.Add("clamp", ValueKinds.Numeric, ValueKinds.Int, null,
            (v, a) => ProtoValue.Of(Math.Clamp(v.AsInt(), a[0].AsInt(), a[1].AsInt())), "bound a value");

        // ── bytes ────────────────────────────────────────────────────────────
        t.Add("len", ValueKinds.Bytes | ValueKinds.Text | ValueKinds.List, ValueKinds.Int, null,
            (v, _) => ProtoValue.Of((long)(v switch
            {
                ProtoValue.Bytes b => b.Value.Length,
                ProtoValue.Text s => s.Value.Length,
                ProtoValue.List l => l.Items.Count,
                _ => 0,
            })),
            "length of a value — NOT the layout function len(id), which measures an emitted span");

        t.Add("concat", ValueKinds.Bytes | ValueKinds.List, ValueKinds.Bytes, null,
            (v, a) =>
            {
                var parts = new List<byte[]>();
                if (v is ProtoValue.List l) parts.AddRange(l.Items.Select(i => i.AsBytes()));
                else parts.Add(v.AsBytes());
                parts.AddRange(a.Select(x => x.AsBytes()));
                return ProtoValue.Of(parts.SelectMany(p => p).ToArray());
            },
            "join octet runs");

        t.Add("repeat", ValueKinds.Bytes, ValueKinds.Bytes, null,
            (v, a) =>
            {
                var b = v.AsBytes();
                int n = (int)a[0].AsInt();
                if (n < 0) throw new ProtoTypeException("repeat count must not be negative");
                var outp = new byte[b.Length * n];
                for (int i = 0; i < n; i++) b.CopyTo(outp, i * b.Length);
                return ProtoValue.Of(outp);
            },
            "repeat octets — Wake-on-LAN's 16 copies of the target MAC");

        t.Add("slice", ValueKinds.Bytes, ValueKinds.Bytes, null,
            (v, a) =>
            {
                var b = v.AsBytes();
                int off = (int)a[0].AsInt();
                int len = a.Count > 1 ? (int)a[1].AsInt() : b.Length - off;
                if (off < 0 || len < 0 || off + len > b.Length)
                    throw new ProtoTypeException($"slice({off},{len}) is outside a {b.Length}-octet value");
                return ProtoValue.Of(b[off..(off + len)]);
            },
            "a sub-range of octets");

        t.Add("take", ValueKinds.Bytes, ValueKinds.Bytes, null,
            (v, a) => ProtoValue.Of(v.AsBytes().Take((int)a[0].AsInt()).ToArray()), "leading octets");
        t.Add("drop", ValueKinds.Bytes, ValueKinds.Bytes, null,
            (v, a) => ProtoValue.Of(v.AsBytes().Skip((int)a[0].AsInt()).ToArray()), "all but the leading octets");
        t.Add("takeLast", ValueKinds.Bytes, ValueKinds.Bytes, null,
            (v, a) => ProtoValue.Of(v.AsBytes().TakeLast((int)a[0].AsInt()).ToArray()), "trailing octets");
        t.Add("dropLast", ValueKinds.Bytes, ValueKinds.Bytes, null,
            (v, a) => ProtoValue.Of(v.AsBytes().SkipLast((int)a[0].AsInt()).ToArray()), "all but the trailing octets");
        t.Add("reverse", ValueKinds.Bytes, ValueKinds.Bytes, "reverse",
            (v, _) => ProtoValue.Of(v.AsBytes().Reverse().ToArray()), "reverse octet order");
        t.Add("xor", ValueKinds.Bytes, ValueKinds.Bytes, "xor",
            (v, a) =>
            {
                var b = (byte[])v.AsBytes().Clone();
                var k = a[0].AsBytes();
                if (k.Length == 0) return ProtoValue.Of(b);
                for (int i = 0; i < b.Length; i++) b[i] ^= k[i % k.Length];
                return ProtoValue.Of(b);
            },
            "xor with a repeating key");

        t.Add("fit", ValueKinds.Bytes | ValueKinds.Text, ValueKinds.Bytes, "cstr",
            (v, a) =>
            {
                var b = v is ProtoValue.Text s ? Encoding.ASCII.GetBytes(s.Value) : v.AsBytes();
                int n = (int)a[0].AsInt();
                byte fill = a.Count > 1 ? (byte)a[1].AsInt() : (byte)0;
                if (b.Length > n)
                    throw new ProtoTypeException($"fit({n}) received {b.Length} octets — over-length input "
                                               + "is truncation, which a fixed-width field must never do silently");
                var outp = new byte[n];
                Array.Fill(outp, fill);
                b.CopyTo(outp, 0);
                return ProtoValue.Of(outp);
            },
            "pad to a fixed width, erroring on over-length — DHCP sname (64) and file (128)");

        t.Add("chunk", ValueKinds.Bytes, ValueKinds.List, null,
            (v, a) =>
            {
                var b = v.AsBytes();
                int n = (int)a[0].AsInt();
                List<ProtoValue> parts = [];
                for (int i = 0; i < b.Length; i += n)
                    parts.Add(ProtoValue.Of(b[i..Math.Min(i + n, b.Length)]));
                return new ProtoValue.List(parts);
            },
            "split octets into runs — RFC 3396 splits a long DHCP option across repeats of its code");

        // ── digest ───────────────────────────────────────────────────────────
        t.Add("md5", ValueKinds.Bytes, ValueKinds.Bytes, null,
            (v, _) => ProtoValue.Of(MD5.HashData(v.AsBytes())), "MD5 — NTP symmetric-key authentication");
        t.Add("sha1", ValueKinds.Bytes, ValueKinds.Bytes, null,
            (v, _) => ProtoValue.Of(SHA1.HashData(v.AsBytes())), "SHA-1");
        t.Add("sha256", ValueKinds.Bytes, ValueKinds.Bytes, null,
            (v, _) => ProtoValue.Of(SHA256.HashData(v.AsBytes())), "SHA-256");
        t.Add("hmacSha256", ValueKinds.Bytes, ValueKinds.Bytes, null,
            (v, a) => ProtoValue.Of(HMACSHA256.HashData(a[0].AsBytes(), v.AsBytes())), "keyed HMAC-SHA-256");
        // The polynomial is a PARAMETER, not a name. A `crc16modbus` converter would be one protocol's
        // mechanism baked into the engine; `crc16(0xa001)` is the notion, and the document supplies which
        // polynomial it needs.
        // The polynomial is REQUIRED, with no default. Defaulting it to any real polynomial puts that
        // protocol's constant in the engine while the name stays clean — the same violation the rename
        // from a protocol-named converter was supposed to fix.
        t.Add("crc16", ValueKinds.Bytes, ValueKinds.Int, null,
            (v, a) => ProtoValue.Of(Crc16(v.AsBytes(), (ushort)Required(a, 0, "crc16", "polynomial").AsInt())),
            "reflected CRC-16 with a caller-supplied polynomial — required");
        t.Add("crc32", ValueKinds.Bytes, ValueKinds.Int, null,
            (v, _) => ProtoValue.Of(Crc32(v.AsBytes())), "CRC-32 (IEEE)");
        t.Add("onesComplementSum", ValueKinds.Bytes, ValueKinds.Int, null,
            (v, _) => ProtoValue.Of(OnesComplementSum(v.AsBytes())),
            "16-bit one's-complement sum of one's-complement — the checksum shape used across the IP family");

        // ── text ops ─────────────────────────────────────────────────────────
        t.Add("upper", ValueKinds.Text, ValueKinds.Text, null,
            (v, _) => ProtoValue.Of(v.AsText().ToUpperInvariant()), "upper-case");
        t.Add("lower", ValueKinds.Text, ValueKinds.Text, null,
            (v, _) => ProtoValue.Of(v.AsText().ToLowerInvariant()), "lower-case");
        t.Add("trim", ValueKinds.Text, ValueKinds.Text, null,
            (v, _) => ProtoValue.Of(v.AsText().Trim()), "strip surrounding whitespace");
        t.Add("split", ValueKinds.Text, ValueKinds.List, "join",
            (v, a) => new ProtoValue.List(v.AsText().Split(a[0].AsText()).Select(ProtoValue.Of).ToList()),
            "split text");
        t.Add("join", ValueKinds.List, ValueKinds.Text, "split",
            (v, a) => ProtoValue.Of(string.Join(a[0].AsText(), v.AsList().Select(x => x.AsText()))), "join text");
        t.Add("startsWith", ValueKinds.Text, ValueKinds.Bool, null,
            (v, a) => ProtoValue.Of(v.AsText().StartsWith(a[0].AsText(), StringComparison.Ordinal)), "prefix test");
        t.Add("contains", ValueKinds.Text, ValueKinds.Bool, null,
            (v, a) => ProtoValue.Of(v.AsText().Contains(a[0].AsText(), StringComparison.Ordinal)), "substring test");
        t.Add("suffixes", ValueKinds.Text, ValueKinds.List, null,
            (v, a) =>
            {
                var sep = a[0].AsText();
                var parts = v.AsText().Split(sep);
                List<ProtoValue> outp = [];
                for (int i = 0; i < parts.Length; i++) outp.Add(ProtoValue.Of(string.Join(sep, parts.Skip(i))));
                return new ProtoValue.List(outp);
            },
            "'a.b.c' to ['a.b.c','b.c','c'] — drives DNS name-compression suffix sharing");

        // ── list ─────────────────────────────────────────────────────────────
        // The bridge between the list world (where bounded iteration lives) and the octet world. Without
        // it, a transform can compute the digits of an encoding but cannot hand them back as bytes.
        t.Add("octets", ValueKinds.List, ValueKinds.Bytes, "unoctets",
            (v, _) => ProtoValue.Of(v.AsList().Select(x =>
            {
                long b = x.AsInt();
                if (b is < 0 or > 255) throw new ProtoTypeException($"{b} is not an octet value");
                return (byte)b;
            }).ToArray()),
            "a list of 0..255 values to octets");
        t.Add("unoctets", ValueKinds.Bytes, ValueKinds.List, "octets",
            (v, _) => new ProtoValue.List([.. v.AsBytes().Select(b => ProtoValue.Of((long)b))]),
            "octets to a list of values");

        t.Add("flatten", ValueKinds.List, ValueKinds.List, null,
            (v, _) => new ProtoValue.List([.. v.AsList().SelectMany(x => x is ProtoValue.List l ? l.Items : [x])]),
            "one level of list nesting removed — what `map` over a list-producing lambda leaves behind");

        t.Add("count", ValueKinds.List, ValueKinds.Int, null,
            (v, _) => ProtoValue.Of((long)v.AsList().Count),
            "item count — a DNS section count derived from the record list rather than hand-maintained");
        t.Add("first", ValueKinds.List, ValueKinds.Any, null,
            (v, _) => v.AsList().Count > 0 ? v.AsList()[0] : ProtoValue.Nothing, "first item or null");
        t.Add("last", ValueKinds.List, ValueKinds.Any, null,
            (v, _) => v.AsList().Count > 0 ? v.AsList()[^1] : ProtoValue.Nothing, "last item or null");
        t.Add("index", ValueKinds.List, ValueKinds.Any, null,
            (v, a) =>
            {
                var items = v.AsList();
                long i = a[0].AsInt();
                if (i < 0) i += items.Count;
                return i >= 0 && i < items.Count ? items[(int)i] : ProtoValue.Nothing;
            },
            "item at an index, or null");
        t.Add("sortBy", ValueKinds.List, ValueKinds.List, null,
            (v, a) => new ProtoValue.List(v.AsList()
                .OrderBy(x => x is ProtoValue.Rec r && r.Members.TryGetValue(a[0].AsText(), out var m)
                              ? m.AsNumber() : 0)
                .ToList()),
            "order a list by a member BEFORE encoding — stops an out-of-order CoAP option list wrapping "
          + "to a negative delta");
        // `equivalence` defaults to 'exact' because exact IS the identity comparison; case-folding must be
        // asked for. Hard-wiring case-insensitivity here was a violation no name scan could ever see: it
        // installed one protocol family's equivalence rule in the engine's comparison operator, and it is
        // wrong for every case-sensitive keyed lookup (a path segment, a negotiated protocol name).
        t.Add("lookupBy", ValueKinds.List, ValueKinds.Any, null,
            (v, a) => Match(v, a).FirstOrDefault() ?? ProtoValue.Nothing,
            "first record whose member matches, or null. equivalence: 'exact' (default) or 'caseFold'");
        t.Add("allBy", ValueKinds.List, ValueKinds.List, null,
            (v, a) => new ProtoValue.List(Match(v, a).ToList()),
            "every matching record in wire order. equivalence: 'exact' (default) or 'caseFold'");
        t.Add("lookup", ValueKinds.Any, ValueKinds.Any, null,
            (v, a) => a[0] is ProtoValue.Rec table && table.Members.TryGetValue(v.AsText(), out var hit)
                      ? hit : ProtoValue.Nothing,
            "table lookup against a consts record — a cipher-suite or reason-code classification is data, "
          + "not three engine predicates");
        t.Add("mergeBy", ValueKinds.List, ValueKinds.List, null,
            (v, a) =>
            {
                string keyName = a[0].AsText(), valName = a[1].AsText();
                List<ProtoValue> merged = [];
                Dictionary<string, int> seen = new(StringComparer.Ordinal);

                foreach (var item in v.AsList())
                {
                    if (item is not ProtoValue.Rec rec
                        || !rec.Members.TryGetValue(keyName, out var k)
                        || !rec.Members.TryGetValue(valName, out var val))
                    { merged.Add(item); continue; }

                    var key = k.ToString();
                    if (seen.TryGetValue(key, out var at) && merged[at] is ProtoValue.Rec prev)
                    {
                        var members = new Dictionary<string, ProtoValue>(prev.Members, StringComparer.Ordinal);
                        members[valName] = ProtoValue.Of(
                            prev.Members[valName].AsBytes().Concat(val.AsBytes()).ToArray());
                        merged[at] = new ProtoValue.Rec(members);
                    }
                    else { seen[key] = merged.Count; merged.Add(item); }
                }
                return new ProtoValue.List(merged);
            },
            "concatenate repeats of the same key — RFC 3396 requires a long DHCP option split across "
          + "repeated codes be rejoined on receipt");

        // ── time ─────────────────────────────────────────────────────────────
        // An epoch-based fixed-point instant. The epoch and the fraction width are PARAMETERS — naming a
        // converter after the protocol that popularised this shape would put that protocol in the engine.
        // A fixed-point *duration* needs nothing new: that is already `fixed`/`unfixed`.
        t.Add("epochFixed", ValueKinds.Instant, ValueKinds.Int, "unepochFixed",
            (v, a) => ProtoValue.Of(ToEpochFixed(((ProtoValue.Instant)v).Value,
                                                 Epoch(a, 0), a.Count > 1 ? (int)a[1].AsInt() : 32)),
            "instant to a fixed-point count since an epoch, e.g. epochFixed('1900-01-01', 32)");
        t.Add("unepochFixed", ValueKinds.Int, ValueKinds.Instant, "epochFixed",
            (v, a) => new ProtoValue.Instant(FromEpochFixed(v.AsInt(), Epoch(a, 0),
                                                            a.Count > 1 ? (int)a[1].AsInt() : 32,
                                                            a.Count > 2 ? (int)a[2].AsInt() : 0)),
            "fixed-point count since an epoch to an instant; the wrap count is an explicit argument, "
          + "which is what keeps this pure rather than reading a clock");
        t.Add("epochSeconds", ValueKinds.Instant, ValueKinds.Int, "unepochSeconds",
            (v, _) => ProtoValue.Of(((ProtoValue.Instant)v).Value.ToUnixTimeSeconds()), "instant to Unix seconds");
        t.Add("unepochSeconds", ValueKinds.Int, ValueKinds.Instant, "epochSeconds",
            (v, _) => new ProtoValue.Instant(DateTimeOffset.FromUnixTimeSeconds(v.AsInt())), "Unix seconds to instant");
        t.Add("seconds", ValueKinds.Numeric, ValueKinds.Duration, null,
            (v, _) => new ProtoValue.Duration(TimeSpan.FromSeconds(v.AsNumber())), "a count of seconds as a duration");

        return t;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IEnumerable<ProtoValue> Match(ProtoValue list, IReadOnlyList<ProtoValue> args)
    {
        string member = args[0].AsText();
        var wanted = args[1];
        var comparison = Equivalence(args, 2);

        foreach (var item in list.AsList())
        {
            if (item is not ProtoValue.Rec rec || !rec.Members.TryGetValue(member, out var got)) continue;

            bool hit = got is ProtoValue.Text a && wanted is ProtoValue.Text b
                ? string.Equals(a.Value, b.Value, comparison)
                : got.Equals(wanted);

            if (hit) yield return item;
        }
    }

    /// <summary>
    /// The text equivalence relation, taken from a declared argument rather than assumed. Exact is the
    /// identity and so may default; anything looser is a property of the protocol and must be asked for.
    /// </summary>
    private static StringComparison Equivalence(IReadOnlyList<ProtoValue> args, int at)
        => args.Count > at
            ? args[at].AsText() switch
            {
                "exact" => StringComparison.Ordinal,
                "caseFold" => StringComparison.OrdinalIgnoreCase,
                var other => throw new ProtoTypeException(
                    $"equivalence must be 'exact' or 'caseFold', got '{other}'"),
            }
            : StringComparison.Ordinal;

    /// <summary>A required converter argument. Missing it is an authoring error, never a silent default —
    /// a default that is not the identity is one protocol's constant living in the engine.</summary>
    private static ProtoValue Required(IReadOnlyList<ProtoValue> args, int at, string converter, string what)
        => args.Count > at
            ? args[at]
            : throw new ProtoTypeException(
                $"{converter}() requires {what} as argument {at + 1}. It has no default: which value is "
              + "correct is a property of the protocol, and defaulting it would put that protocol's "
              + "constant in the engine.");

    private static bool MsbFirst(IReadOnlyList<ProtoValue> args, int at)
        => Required(args, at, "base128", "order ('msbFirst' or 'lsbFirst')").AsText() switch
        {
            "msbFirst" => true,
            "lsbFirst" => false,
            var other => throw new ProtoTypeException($"order must be 'msbFirst' or 'lsbFirst', got '{other}'"),
        };

    private static bool ZeroIsEmpty(IReadOnlyList<ProtoValue> args, int at)
        => Required(args, at, "minuint", "zero rule ('oneByte' or 'empty')").AsText() switch
        {
            "empty" => true,
            "oneByte" => false,
            var other => throw new ProtoTypeException($"zero must be 'oneByte' or 'empty', got '{other}'"),
        };

    private static bool ZeroRule(IReadOnlyList<ProtoValue> args, int at) => ZeroIsEmpty(args, at);

    /// <summary>UTF-8 that refuses rather than substitutes, in both directions.</summary>
    private static readonly Encoding StrictUtf8 =
        Encoding.GetEncoding("utf-8", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

    private static byte[] ParseHex(string s)
    {
        Span<char> buf = stackalloc char[s.Length];
        int n = 0;
        foreach (var c in s)
            if (Uri.IsHexDigit(c)) buf[n++] = c;
            else if (c is not (' ' or ':' or '-' or '.' or '\t' or '\r' or '\n'))
                throw new ProtoTypeException($"'{c}' is not a hex digit or a recognised separator");

        if (n % 2 != 0) throw new ProtoTypeException("hex text has an odd number of digits");
        return Convert.FromHexString(buf[..n]);
    }

    /// <summary>Minimal-width unsigned big-endian octets. How zero encodes is the caller's declaration:
    /// some protocols require a single 0x00, others require no octets at all.</summary>
    private static byte[] MinimalUnsigned(long value, bool zeroIsEmpty)
    {
        if (value == 0) return zeroIsEmpty ? [] : [0];
        if (value < 0) throw new ProtoTypeException("minuint received a negative value");

        List<byte> outp = [];
        while (value > 0) { outp.Insert(0, (byte)(value & 0xFF)); value >>= 8; }
        return outp.ToArray();
    }

    /// <summary>
    /// Minimal two's-complement octets. The rule that matters: a leading 0x00 is added only when the top
    /// bit of the first significant octet would otherwise read as a sign bit — which is why SNMP's
    /// request-id 821915 is three octets (<c>0c 8a 9b</c>) and not four.
    /// </summary>
    private static byte[] MinimalSigned(long value)
    {
        List<byte> outp = [];
        long v = value;

        if (v >= 0)
        {
            while (v > 0) { outp.Insert(0, (byte)(v & 0xFF)); v >>= 8; }
            if (outp.Count == 0) outp.Add(0);
            if ((outp[0] & 0x80) != 0) outp.Insert(0, 0x00);
        }
        else
        {
            while (v < -1) { outp.Insert(0, (byte)(v & 0xFF)); v >>= 8; }
            if (outp.Count == 0) outp.Add(0xFF);
            if ((outp[0] & 0x80) == 0) outp.Insert(0, 0xFF);
        }
        return outp.ToArray();
    }

    /// <summary>
    /// A value spread across octets with a continue flag. Group order is a parameter: two unrelated
    /// families exhibit this notion in opposite orders, and the continue flag follows the order — it marks
    /// "another group follows", which is the leading groups when most-significant is first and the
    /// trailing ones when least-significant is.
    /// </summary>
    private static byte[] Base128(long value, bool msbFirst)
    {
        if (value == 0) return [0];

        List<byte> groups = [];
        while (value > 0) { groups.Add((byte)(value & 0x7F)); value >>= 7; }   // least significant first

        if (msbFirst)
        {
            groups.Reverse();
            for (int i = 0; i < groups.Count - 1; i++) groups[i] |= 0x80;
        }
        else
        {
            for (int i = 0; i < groups.Count - 1; i++) groups[i] |= 0x80;
        }
        return groups.ToArray();
    }

    private static long Crc16(byte[] data, ushort polynomial)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ polynomial) : (ushort)(crc >> 1);
        }
        return crc;
    }

    private static long Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        return ~crc & 0xFFFFFFFF;
    }

    private static long OnesComplementSum(byte[] data)
    {
        long sum = 0;
        for (int i = 0; i + 1 < data.Length; i += 2) sum += (data[i] << 8) | data[i + 1];
        if (data.Length % 2 != 0) sum += data[^1] << 8;
        while ((sum >> 16) != 0) sum = (sum & 0xFFFF) + (sum >> 16);
        return ~sum & 0xFFFF;
    }

    /// <summary>The epoch argument, as an ISO date. A document supplies it; the engine knows no epochs.</summary>
    private static DateTimeOffset Epoch(IReadOnlyList<ProtoValue> args, int at)
        => args.Count > at
            ? DateTimeOffset.Parse(args[at].AsText(), CultureInfo.InvariantCulture,
                                   DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
            : DateTimeOffset.UnixEpoch;

    private static long ToEpochFixed(DateTimeOffset t, DateTimeOffset epoch, int fracBits)
    {
        double scale = Math.Pow(2, fracBits);
        var delta = t - epoch;
        long whole = (long)delta.TotalSeconds;
        double frac = delta.TotalSeconds - whole;
        return (whole << fracBits) | (long)Math.Round(frac * scale);
    }

    private static DateTimeOffset FromEpochFixed(long raw, DateTimeOffset epoch, int fracBits, int wraps)
    {
        double scale = Math.Pow(2, fracBits);
        long whole = raw >> fracBits;
        double frac = (raw & ((1L << fracBits) - 1)) / scale;
        return epoch.AddSeconds(whole + frac + wraps * Math.Pow(2, 64 - fracBits));
    }
}
