using Nexaflow.IO.Protocol.Values;

namespace Nexaflow.IO.Protocol.Wire;

/// <summary>
/// A value the message does not contain.
///
/// <para>
/// These were a bag of names in a dictionary, resolved at evaluation time, declared nowhere. That is the
/// same shape the field references and the rules both had before they became edges, and it fails the same
/// way: <c>inputs.clientIdentifer</c> evaluated to nothing and surfaced three layers down as a converter
/// complaining about a type. Nothing could say what a document needed in order to run, because nothing had
/// been told.
/// </para>
///
/// <para>
/// <b>Not an encode-side concept.</b> Reading a packet is not understanding it: four octets are an address
/// either way, but deciding whether they mean <i>this machine</i> needs the protocol's own named constants
/// and the environment's actual addresses, and neither is in the packet. A protocol with negotiated
/// options is sharper still — the preferred order is an input, it shapes what gets sent, and it is what
/// makes the peer's answer legible. Context feeds the layer that says what octets mean, and that layer is
/// there in both directions.
/// </para>
///
/// <para>
/// The kinds differ in ways that matter, for the same reason the kinds of illegal do. Where a value comes
/// from decides who can be asked for it, whether it may be shown in a dry run, when it stops being true,
/// and what a guard should make of a document that wants it.
/// </para>
/// </summary>
public abstract class Context : Node
{
    /// <summary>How expressions name it — <c>inputs.&lt;key&gt;</c>. A name for writing with, not how
    /// anything finds it.</summary>
    public required string Key { get; init; }

    public override string Name => Key;

    /// <summary>What this is, in words. Not decoration: for anything a person supplies, this is the
    /// question they get asked, and a document that cannot say why it wants a value has no business
    /// asking for one.</summary>
    public string Purpose { get; init; } = "";

    /// <summary>Whether a person has to be asked, which is what makes the prompt list computable rather
    /// than something maintained alongside the document and forgotten.</summary>
    public virtual bool Asked => false;

    /// <summary>Whether its value may appear in an explanation or a dry run.</summary>
    public virtual bool Showable => true;

    /// <summary>
    /// Supplied by whatever is driving the engine, with no claim about where it came from.
    ///
    /// <para>
    /// Honest for a caller that already has the value in hand, and deliberately the least useful kind: a
    /// document made entirely of these has declared its inputs and said nothing about them.
    /// </para>
    /// </summary>
    public sealed class Given : Context
    {
        public static IReadOnlyList<Context> These(params string[] keys)
            => [.. keys.Select(k => new Given { Key = k })];
    }

    /// <summary>Asked of a person, so it carries what to ask and what would be a sensible answer.</summary>
    public sealed class Prompted : Context
    {
        public ValueRange? Within { get; init; }
        public string? Example { get; init; }

        public override bool Asked => true;
    }

    /// <summary>Asked of a person and never shown again — a credential.</summary>
    public sealed class Secret : Context
    {
        public override bool Asked => true;
        public override bool Showable => false;
    }

    /// <summary>Taken from the machine this is running on: an adapter's address, the clock, a local
    /// prefix. Discovered rather than asked.</summary>
    /// <summary>
    /// A value learned from an earlier message.
    ///
    /// <para>
    /// Reading it needs nothing new, which is the point of it being context: a field names it the way it
    /// names any other outside value, the graph records the dependency, and a document that reads one it
    /// never declared is refused as usual. Only the <i>writing</i> side is new — a transition puts it
    /// there. What a caller supplies and what an earlier message left behind are both things this message
    /// did not carry, and the difference between them is where they came from, not what they are.
    /// </para>
    /// </summary>
    public sealed class Remembered : Context;

    public sealed class Ambient : Context;

    /// <summary>Already known about the device being spoken to — something a previous exchange taught.
    /// The kind whose validity a later observation can revoke.</summary>
    public sealed class Known : Context;

    /// <summary>A number that advances on its own, with the bounds it wraps within.</summary>
    public sealed class Counter : Context
    {
        public long Low { get; init; }
        public long High { get; init; } = long.MaxValue;
        public bool Wraps { get; init; } = true;
    }

    /// <summary>The document's own constant. Distinct from a field the document fixes: this one is a value
    /// several fields can be written from, not an octet on the wire.</summary>
    public sealed class Fixed : Context
    {
        public required ProtoValue Value { get; init; }
    }

    public override string ToString() => $"{GetType().Name.ToLowerInvariant()} {Key}";
}
