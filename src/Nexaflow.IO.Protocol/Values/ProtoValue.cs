using System.Globalization;

namespace Nexaflow.IO.Protocol.Values;

/// <summary>
/// Every value the expression language can produce. A closed union, deliberately: a protocol document must
/// not be able to smuggle an arbitrary CLR type through the engine, and an exhaustive switch is what makes
/// converter type-checking a validate-time property rather than a runtime surprise.
/// </summary>
public abstract record ProtoValue
{
    /// <summary>Raw octets — what every field ultimately encodes to.</summary>
    public sealed record Bytes(byte[] Value) : ProtoValue
    {
        public bool Equals(Bytes? other) => other is not null && Value.AsSpan().SequenceEqual(other.Value);
        public override int GetHashCode()
        {
            var h = new HashCode();
            h.AddBytes(Value);
            return h.ToHashCode();
        }
    }

    /// <summary>i64. Integer literals and <c>0x…</c> land here; <c>/</c> truncates.</summary>
    public sealed record Int(long Value) : ProtoValue;

    /// <summary>f64. A decimal literal containing <c>.</c> or an exponent lands here — without which
    /// <c>keepAlive * 0.75</c> is <c>keepAlive * 0</c> and an MQTT keepalive fires every zero seconds.</summary>
    public sealed record Num(double Value) : ProtoValue;

    /// <summary>Produced by every comparison and by <c>&amp;&amp;</c>/<c>||</c>/<c>!</c>. Coerces to
    /// <see cref="Int"/> implicitly under arithmetic, which is what lets a tag-depth fold write
    /// <c>depth + (lvt == 6) - (lvt == 7)</c>.</summary>
    public sealed record Bool(bool Value) : ProtoValue;

    public sealed record Text(string Value) : ProtoValue;
    public sealed record Instant(DateTimeOffset Value) : ProtoValue;
    public sealed record Duration(TimeSpan Value) : ProtoValue;

    public sealed record List(IReadOnlyList<ProtoValue> Items) : ProtoValue
    {
        public bool Equals(List? other) => other is not null && Items.SequenceEqual(other.Items);
        public override int GetHashCode() => Items.Count;
    }

    /// <summary>Named members — what <c>each … yields:"record"</c> produces. There is no map type;
    /// repeated HTTP headers need list order anyway, so one kind serves both.</summary>
    public sealed record Rec(IReadOnlyDictionary<string, ProtoValue> Members) : ProtoValue
    {
        public bool Equals(Rec? other)
            => other is not null
               && Members.Count == other.Members.Count
               && Members.All(kv => other.Members.TryGetValue(kv.Key, out var v) && Equals(kv.Value, v));
        public override int GetHashCode() => Members.Count;
    }

    /// <summary>Absent. Out-of-range indexing yields this, and it <b>suppresses</b> an enclosing
    /// <c>when</c> rather than erroring — so an optional field that isn't there reads as "not there".</summary>
    public sealed record Null : ProtoValue
    {
        public static readonly Null Instance = new();
    }

    // ── Construction ──────────────────────────────────────────────────────────

    public static ProtoValue Of(long v) => new Int(v);
    public static ProtoValue Of(double v) => new Num(v);
    public static ProtoValue Of(bool v) => new Bool(v);
    public static ProtoValue Of(string v) => new Text(v);
    public static ProtoValue Of(byte[] v) => new Bytes(v);
    public static ProtoValue Nothing => Null.Instance;

    // ── Interrogation ─────────────────────────────────────────────────────────

    public bool IsNull => this is Null;

    /// <summary>Short kind name, used verbatim in validator and runtime messages so a type error reads
    /// "expects Bytes, got Int" rather than naming a CLR type the document author never wrote.</summary>
    public string Kind => this switch
    {
        Bytes => "Bytes", Int => "Int", Num => "Number", Bool => "Bool", Text => "Text",
        Instant => "Instant", Duration => "Duration", List => "List", Rec => "Record",
        _ => "Null",
    };

    /// <summary>
    /// The integer view, applying the one defined coercion: <c>Bool → Int</c> (false 0, true 1). There is
    /// deliberately no <c>Int → Bool</c> — write <c>x != 0</c> — because implicit truthiness is how a
    /// comparison silently becomes an arithmetic operand in the wrong direction.
    /// </summary>
    public long AsInt() => this switch
    {
        Int i => i.Value,
        Bool b => b.Value ? 1 : 0,
        Num n when n.Value == Math.Floor(n.Value) && !double.IsInfinity(n.Value) => (long)n.Value,
        _ => throw new ProtoTypeException($"expected Int, got {Kind}"),
    };

    public double AsNumber() => this switch
    {
        Num n => n.Value,
        Int i => i.Value,
        Bool b => b.Value ? 1 : 0,
        _ => throw new ProtoTypeException($"expected Number, got {Kind}"),
    };

    /// <summary>The boolean view. Only <see cref="Bool"/> is truthy, plus <see cref="Null"/> which reads
    /// false so that a missing optional value suppresses its <c>when</c> instead of throwing.</summary>
    public bool AsBool() => this switch
    {
        Bool b => b.Value,
        Null => false,
        _ => throw new ProtoTypeException($"expected Bool, got {Kind}"),
    };

    public string AsText() => this switch
    {
        Text t => t.Value,
        Int i => i.Value.ToString(CultureInfo.InvariantCulture),
        Num n => n.Value.ToString(CultureInfo.InvariantCulture),
        Bool b => b.Value ? "true" : "false",
        _ => throw new ProtoTypeException($"expected Text, got {Kind}"),
    };

    public byte[] AsBytes() => this switch
    {
        Bytes b => b.Value,
        _ => throw new ProtoTypeException($"expected Bytes, got {Kind}"),
    };

    public IReadOnlyList<ProtoValue> AsList() => this switch
    {
        List l => l.Items,
        _ => throw new ProtoTypeException($"expected List, got {Kind}"),
    };

    /// <summary>
    /// Rendering for messages, tests and the dry-run breakdown. <b>Sealed</b> on purpose: a derived
    /// <c>record</c> would otherwise synthesise its own <c>ToString</c> and every value would print as
    /// <c>Text { Value = … }</c>, which turns a readable hex dump into noise.
    /// </summary>
    public sealed override string ToString() => this switch
    {
        Bytes b => Convert.ToHexString(b.Value).ToLowerInvariant(),
        Int i => i.Value.ToString(CultureInfo.InvariantCulture),
        Num n => n.Value.ToString("R", CultureInfo.InvariantCulture),
        Bool b => b.Value ? "true" : "false",
        Text t => t.Value,
        Instant t => t.Value.ToString("O"),
        Duration d => d.Value.ToString(),
        List l => "[" + string.Join(", ", l.Items) + "]",
        Rec r => "{" + string.Join(", ", r.Members.Select(kv => $"{kv.Key}: {kv.Value}")) + "}",
        _ => "null",
    };
}

/// <summary>A type error in a protocol document. Carries document-author vocabulary, never CLR types —
/// these surface to the user reviewing an AI-authored protocol, and to the model correcting one.</summary>
public sealed class ProtoTypeException(string message) : Exception(message);

/// <summary>A malformed expression. Reported at validate time, before anything can run.</summary>
public sealed class ProtoSyntaxException(string message, int position) : Exception($"{message} (at offset {position})")
{
    public int Position { get; } = position;
}
