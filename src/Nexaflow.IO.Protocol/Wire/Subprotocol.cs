using Nexaflow.IO.Protocol.Transforms;
using Nexaflow.IO.Protocol.Values;

namespace Nexaflow.IO.Protocol.Wire;

/// <summary>
/// A protocol implemented outside the document model, plugged in where a document would otherwise be.
///
/// <para>
/// The reason the seam exists. Most of what a device conversation sits on is solved and uninteresting —
/// nobody needs a drafted description of TLS, and a wrong one is worse than none. Letting a
/// <see cref="Subprotocol"/> resolve to a known-good implementation means a document can describe the part
/// that is actually novel and stack it on something already trusted.
/// </para>
/// </summary>
public interface ISubprotocol
{
    string Name { get; }

    byte[] Encode(ProtoValue inputs);

    ProtoValue Decode(ReadOnlySpan<byte> octets);
}

/// <summary>What is inside a carried span.</summary>
public abstract record Carriage
{
    /// <summary>Another protocol, described by a graph of its own.</summary>
    public sealed record Described(ProtocolGraph Protocol) : Carriage;

    /// <summary>An implementation the host supplies by name. The seam.</summary>
    public sealed record Provided(string Implementation) : Carriage;
}

/// <summary>
/// One place where a message carries another protocol.
///
/// <para>
/// <b>A node rather than a region naming a document directly.</b> Three things need somewhere to live and
/// a direct nest has room for none of them. A protocol can carry the same inner protocol in more than one
/// place — one in the header, two more as separate parts of the body — and those are different carriers
/// with different rules, which a shared reference could not tell apart. The octets may not go in verbatim:
/// compressed, encrypted, escaped, base64'd, and the transform belongs on the seam. And the seam is where
/// an implementation can be swapped for the description, which is only possible if there is something in
/// between to swap.
/// </para>
///
/// <para>
/// It also settles what position means across a layer, without a rule about it: an inner document builds
/// its own octets, so offsets inside it are relative to its own beginning because there is nothing else
/// for them to be relative to.
/// </para>
/// </summary>
public sealed class Subprotocol : Node
{
    public required string Id { get; init; }

    /// <summary>
    /// The place this occupies, as the document names it.
    /// </summary>
    /// <remarks>
    /// A carrier replaces the span a document declared, and that span had a name things refer to. Two
    /// names for one place is how a reference to it resolves to a node nobody meant — so the carrier takes
    /// the name of what it stands in for, and <see cref="Id"/> stays what it is: the seam, which is what a
    /// refusal about a missing implementation should say.
    /// </remarks>
    public string? Occupies { get; internal set; }

    public override string Name => Occupies ?? Id;

    public required Carriage Carries { get; init; }

    /// <summary>
    /// What happens to the inner octets between being built and being embedded, applied in order and
    /// undone in reverse on the way in.
    ///
    /// <para>
    /// Empty is the ordinary case and means the octets go in as they are. Where it is not empty it is
    /// usually the whole reason the layer is interesting — a payload that is compressed, or wrapped, or
    /// escaped so that a delimiter in the inner message does not end the outer one.
    /// </para>
    /// </summary>
    public IReadOnlyList<Conversion> Via { get; init; } = [];

    /// <summary>A document-authored transform on the same seam, for a rule that belongs to this protocol
    /// rather than to any general notion.</summary>
    public Transform? Through { get; init; }

    /// <summary>
    /// What the protocol beneath is given, said in <i>this</i> document's terms: its input key, and the
    /// expression that supplies it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The alternative is to hand the inner run whatever outside values the outer run happens to hold and
    /// match them by name. That works wherever two independently-written documents agree on a spelling and
    /// is silently wrong everywhere else — one protocol feeding another by coincidence, with no edge saying
    /// so and nothing able to check it. It is the ambient scope this engine has removed twice.
    /// </para>
    /// <para>
    /// So each entry becomes a computation on a <see cref="Requires"/> edge, exactly as a converter's
    /// arguments do. A layer is then fed the way everything else is, the graph records which outer values
    /// a carried protocol depends on, and a document that fails to feed one can be told at authoring time.
    /// </para>
    /// </remarks>
    public IReadOnlyDictionary<string, Expressions.Expr> Feeds { get; init; }
        = new Dictionary<string, Expressions.Expr>();

    public string About { get; init; } = "";

    public override string ToString() => Carries switch
    {
        Carriage.Described d => $"{Id} → {d.Protocol.Id}",
        Carriage.Provided p => $"{Id} → {p.Implementation} (provided)",
        _ => Id,
    };
}

/// <summary>
/// The implementations a host is willing to stand behind.
///
/// <para>
/// Passed in rather than discovered, and empty by default. A document naming an implementation that was
/// not offered is refused — the alternative is an engine that quietly resolves a name to whatever it can
/// find, which for something sitting under an unreviewed draft is the wrong default by a distance.
/// </para>
/// </summary>
public sealed class Implementations
{
    private readonly Dictionary<string, ISubprotocol> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ICalculation> _code = new(StringComparer.Ordinal);

    public static Implementations None { get; } = new();

    public Implementations Offer(ISubprotocol implementation)
    {
        _byName[implementation.Name] = implementation;
        return this;
    }

    public ISubprotocol Get(string name, string wanted)
        => _byName.TryGetValue(name, out var found)
            ? found
            : throw new ProtoTypeException(
                  $"'{wanted}' carries '{name}', which this host does not provide. Offered: "
                + (_byName.Count == 0 ? "nothing" : string.Join(", ", _byName.Keys.Order())));

    public Implementations Offer(ICalculation calculation)
    {
        _code[calculation.Name] = calculation;
        return this;
    }

    public ICalculation Code(string name, string wanted)
        => _code.TryGetValue(name, out var found)
            ? found
            : throw new ProtoTypeException(
                  $"'{wanted}' names the implementation '{name}', which this host does not provide. Code a "
                + "description names has to be supplied rather than looked up by name in whatever happens "
                + "to be loaded. Offered: "
                + (_code.Count == 0 ? "nothing" : string.Join(", ", _code.Keys.Order())));
}

/// <summary>
/// Code behind a name, for the calculations neither a converter nor an expression can state.
/// </summary>
/// <remarks>
/// <para>
/// The same bargain a subprotocol implementation makes: the host offers it, a description may name it, and
/// what it is handed comes from the naming node's own edges. It cannot reach into the message, ask where
/// it is, or read anything nobody gave it — so what it can do is bounded by the graph rather than by
/// whoever wrote the code.
/// </para>
/// <para>
/// A description naming one that was not offered is refused. Looking the name up in whatever is loaded
/// would make "a protocol file may run any code it can name" true, and that is not something review
/// catches.
/// </para>
/// </remarks>
public interface ICalculation
{
    /// <summary>The name a description reaches it by.</summary>
    string Name { get; }

    /// <summary>What it makes of what its edges gave it.</summary>
    Values.ProtoValue Work(Values.ProtoValue given);
}
