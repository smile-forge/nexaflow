using Nexaflow.IO.Protocol.Values;

namespace Nexaflow.IO.Protocol.Wire;

/// <summary>
/// What a check actually does, as a node between the thing checked and the data it checks against.
///
/// <para>
/// The distinction it exists for: <b>a set is data, a validator is a check.</b> Pointing a
/// <see cref="Checks"/> edge straight at a <see cref="ValueSet"/> collapsed the two, and that reads fine
/// right up to the first constraint a set cannot hold. A bound is the plain case — "no greater than the
/// maximum the peer advertised" has no enumeration behind it, and writing one would mean listing every
/// legal value of a thirty-two bit field.
/// </para>
///
/// <para>
/// So what it admits comes from two places at once, and both are needed. <b>Ranges</b> are written on the
/// validator, because a run of legal values is not a list and turning it into one is absurd at any
/// interesting width. <b>Requirement edges</b> supply the rest — a registry node, a constant, or anything
/// that computes one — which is what makes "whatever the handshake advertised" expressible. Neither can do
/// the other's job: an edge cannot carry a run without enumerating it, and a written range cannot depend
/// on a value that does not exist until the conversation has happened.
/// </para>
///
/// <para>
/// It was briefly two kinds, one per source. That was the same over-splitting this model keeps deleting:
/// they answer one question — is this value among the ones allowed here — and differ only in where the
/// allowed ones came from. Mixing them is ordinary, and a document that writes a reserved run <i>and</i>
/// points at a registry is describing one constraint.
/// </para>
/// </summary>
public sealed class Validator : Node
{
    public required string Id { get; init; }

    public override string Name => Id;

    /// <summary>Why, in the author's words — carried into the refusal, because the sentence the author
    /// would have written beats a rendering of the comparison that failed.</summary>
    public string Because { get; init; } = "";

    /// <summary>
    /// Runs of legal values, written here.
    /// </summary>
    /// <remarks>
    /// On the node rather than on an edge because a range is not data that can arrive as a value — it is
    /// two ends and a rule about what lies between, and the whole point of it is that the values in the
    /// middle were never listed.
    /// </remarks>
    public IReadOnlyList<ValueRange> Ranges { get; init; } = [];

    /// <summary>
    /// A verdict, given the value and whatever the requirement edges supplied.
    /// </summary>
    /// <param name="against">
    /// What arrived on the edges, in order: a <see cref="ValueSet"/> to draw members from, or a plain value
    /// that is legal in itself. Mixed freely — a value either matches something admitted or it does not,
    /// and which source admitted it changes nothing about the answer.
    /// </param>
    public Verdict Judge(ProtoValue value, IReadOnlyList<object?> against)
    {
        if (Ranges.Any(r => r.Admits(value))) return Verdict.Fine;

        bool anyOpen = false;
        List<string> sources = [.. Ranges.Select(r => r.ToString())];

        foreach (var source in against)
            switch (source)
            {
                case ValueSet set:
                    if (set.Admits(value)) return Verdict.Fine;

                    anyOpen |= set.Bounding == Bounding.Open;
                    sources.Add(set.Id);
                    break;

                case ProtoValue allowed:
                    if (ProtoValue.Alike(allowed, value)) return Verdict.Fine;

                    sources.Add(allowed.ToString());
                    break;
            }

        if (sources.Count == 0)
            return Verdict.Wrong($"'{Id}' admits nothing at all, so no value can satisfy it");

        // An open source says its list is not the whole story, so a value nothing admitted is one this
        // document has not been told about rather than an illegal one — and refusing it would call
        // tomorrow's assignments malformed. Where every source is closed there is no such doubt.
        return anyOpen
            ? Verdict.Unknown($"{value} is not among {string.Join(", ", sources)}, but those are not the "
                            + "whole story — this is a value this document has not been told about")
            : Verdict.Wrong($"{value} is not among {string.Join(", ", sources)}");
    }

    public override string ToString() => Id;
}

/// <summary>
/// What a check came to.
/// </summary>
/// <remarks>
/// Three answers rather than two, because "not admitted" is not one outcome. A value nothing closed admits
/// is malformed; the same value where a source is open is one this document has not been told about, and a
/// receiver has to be able to carry it, log it or pass it on rather than reject the message. A boolean
/// throws that distinction away at the point where it is still known.
/// </remarks>
public readonly record struct Verdict(bool Passed, bool Recognised, string Why)
{
    public static Verdict Fine { get; } = new(true, true, "");

    /// <summary>It does not hold, and that is an error.</summary>
    public static Verdict Wrong(string why) => new(false, true, why);

    /// <summary>It is outside what this document knows, which is not the same as illegal.</summary>
    public static Verdict Unknown(string why) => new(true, false, why);
}
