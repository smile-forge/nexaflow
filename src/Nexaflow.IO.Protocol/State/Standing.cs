using Nexaflow.IO.Protocol.Converters;
using Nexaflow.IO.Protocol.Expressions;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;

namespace Nexaflow.IO.Protocol.State;

/// <summary>What one message did to whatever it was about.</summary>
/// <param name="Which">Which instance, where a subject has more than one.</param>
/// <param name="Moves">Each view that changed, what it changed to, and how sure that is.</param>
public readonly record struct Progress(
    ProtoValue Which,
    IReadOnlyList<(Party Whose, Phase To, Confidence How)> Moves,
    IReadOnlyList<(Recall Where, ProtoValue What)> Kept,
    bool Ended);

/// <summary>
/// How things currently stand: what each party believes about each of a subject's instances.
///
/// <para>
/// <b>There is no conversation object, and that is deliberate.</b> A conversation is not a thing the engine
/// holds — it is a reading someone applies afterwards to a sequence of messages. What exists is the current
/// belief and a message that moves it, which is the whole of it. Anything that looked like a conversation
/// would have to be born, live and die, and none of those events are on the wire.
/// </para>
///
/// <para>
/// Per <b>instance</b> and per <b>party</b>. Up to 255 BACnet transactions share a socket and a peer, so a
/// single state for the run would have them overwrite each other, and a state per connection is no help to
/// a protocol that has no connection. Where a subject has only one instance — a light's power, a device's
/// mode — there is nothing to tell apart and the identity is simply absent.
/// </para>
/// </summary>
public sealed class Standing(Subject subject, ConverterTable? converters = null)
{
    private readonly ConverterTable _converters = converters ?? ConverterTable.Default;

    private readonly Evaluator _evaluator = new(converters ?? ConverterTable.Default);

    private readonly Dictionary<ProtoValue, Dictionary<Party, Phase>> _views = [];

    /// <summary>What has been learned, per instance. Opaque to the engine on purpose — it moves these
    /// where the protocol said to and forms no view about what any of them mean.</summary>
    private readonly Dictionary<ProtoValue, Dictionary<Recall, ProtoValue>> _kept = [];

    public Subject Subject { get; } = subject;

    /// <summary>Stands in for an identity where a subject has only one instance.</summary>
    public static readonly ProtoValue Only = ProtoValue.Of("the one");

    /// <summary>Every instance being tracked.</summary>
    public IReadOnlyCollection<ProtoValue> Tracked => _views.Keys;

    /// <summary>What one party believes. Untouched instances are at the beginning rather than absent — a
    /// peer that has never spoken is not in an unknown state.</summary>
    public Phase PhaseOf(ProtoValue which, Party party)
        => _views.TryGetValue(which, out var views) && views.TryGetValue(party, out var phase)
            ? phase
            : Subject.Start;

    /// <summary>What one party believes, where the subject has only one instance.</summary>
    public Phase PhaseOf(Party party) => PhaseOf(Only, party);

    /// <summary>What is in a slot, or nothing if no message has put anything there.</summary>
    public ProtoValue Recalled(ProtoValue which, Recall slot)
        => _kept.TryGetValue(which, out var kept) && kept.TryGetValue(slot, out var value)
            ? value
            : ProtoValue.Nothing;

    public ProtoValue Recalled(Recall slot) => Recalled(Only, slot);

    /// <summary>
    /// Everything learned about one instance, as outside values a message can be built from.
    ///
    /// <para>
    /// The same shape a caller's inputs have, because that is what they are: a value this message does not
    /// carry. A document declaring <see cref="Context.Remembered"/> reads one exactly as it reads a value
    /// someone typed, and nothing in the codec had to learn that state exists.
    /// </para>
    /// </summary>
    public ProtoValue Learned(ProtoValue which)
    {
        Dictionary<string, ProtoValue> known = new(StringComparer.Ordinal);

        if (_kept.TryGetValue(which, out var kept))
            foreach (var (slot, value) in kept) known[slot.Id] = value;

        return new ProtoValue.Rec(known);
    }

    public ProtoValue Learned() => Learned(Only);

    /// <summary>
    /// Takes a message and moves whatever it moves.
    /// </summary>
    /// <exception cref="ProtoTypeException">
    /// When nothing this message could do is legal from where things stand. That is the refusal the
    /// illegals taxonomy called <i>stateful denial</i>: the message is well formed and every value in it is
    /// legal, and it is still wrong, because of what has and has not already happened.
    /// </exception>
    public Progress Observe(MessageDef message, DecodeResult decoded, Bearing way)
    {
        var which = Which(message, decoded);
        var scope = Scope(decoded);

        List<Transition> possible = [.. Subject.Transitions.Where(
            t => ReferenceEquals(t.On, message) && t.Way == way && Holds(t.When, scope))];

        if (possible.Count == 0)
            throw new ProtoTypeException(
                $"subject '{Subject.Id}': nothing about it describes a message like this being "
              + way.ToString().ToLowerInvariant());

        var taken = possible.Where(t => Applies(t, which)).ToList();

        if (taken.Count == 0)
            throw new ProtoTypeException(
                $"subject '{Subject.Id}': this message is legal in itself and not here. "
              + string.Join(" ", possible.Select(t =>
                    $"{t.Whose.Id} would have to be at '{t.From!.Id}' and is at "
                  + $"'{PhaseOf(which, t.Whose).Id}' — {t.Because}")));

        // Two moves for one party off one message is a document that has not decided what this message
        // means. Silently taking the last would make the answer depend on the order they were written in,
        // which is the sort of thing that works until someone reorders a list.
        foreach (var contested in taken.GroupBy(t => t.Whose).Where(g => g.Count() > 1))
            throw new ProtoTypeException(
                $"subject '{Subject.Id}': {contested.Count()} moves for '{contested.Key.Id}' all match this "
              + "message from where it stands, so which one happens is not decided: "
              + string.Join("; ", contested.Select(t => $"→ '{t.To.Id}' ({t.Because})")));

        List<(Party, Phase, Confidence)> moves = [];
        List<(Recall, ProtoValue)> kept = [];
        bool ended = false;

        foreach (var transition in taken)
        {
            if (!_views.TryGetValue(which, out var views)) _views[which] = views = [];

            views[transition.Whose] = transition.To;
            moves.Add((transition.Whose, transition.To, transition.Confidence));

            // What the protocol said to keep out of this message, converted the way it said to convert it.
            foreach (var recording in transition.Records)
            {
                var value = _evaluator.Eval(recording.From, scope);

                if (recording.Through is { } transform) value = transform.Apply(value, null, _evaluator);

                if (recording.Via is { } via)
                    value = _converters.TryGet(via.Name, out var converter) && converter is not null
                        ? converter.Apply(value, via.Args)
                        : throw new ProtoTypeException(
                              $"subject '{Subject.Id}': '{via.Name}' is not a converter");

                if (!_kept.TryGetValue(which, out var slots)) _kept[which] = slots = [];

                slots[recording.Into] = value;
                kept.Add((recording.Into, value));
            }

            ended |= transition.Ends;
        }

        // The protocol says when an instance is over, not the engine and not the caller. An invoke id is
        // free again the moment the transaction has an outcome; a device mode is never over at all.
        if (ended) Forget(which);

        return new Progress(which, moves, kept, ended);
    }

    /// <summary>Whether a message would be accepted, without accepting it. What a caller needs before
    /// sending one, and what a reviewer needs to see the shape of what was described.</summary>
    public bool WouldAccept(MessageDef message, DecodeResult decoded, Bearing way)
    {
        var which = Which(message, decoded);
        var scope = Scope(decoded);

        return Subject.Transitions.Any(
            t => ReferenceEquals(t.On, message) && t.Way == way && Holds(t.When, scope) && Applies(t, which));
    }

    /// <summary>
    /// Forgets an instance.
    ///
    /// <para>
    /// Called by the engine when a move says it ends, and available to the host for the lifetime it owns
    /// and the engine cannot see — a socket closing, a device going away. Which values outlive which event
    /// is a protocol's business, and the two ends of that are declared and external respectively.
    /// </para>
    /// </summary>
    public bool Forget(ProtoValue which) => _views.Remove(which) | _kept.Remove(which);

    /// <summary>Forgets everything. What a host calls when the lifetime it owns has ended.</summary>
    public void Forget()
    {
        _views.Clear();
        _kept.Clear();
    }

    private bool Applies(Transition transition, ProtoValue which)
        => transition.From is null || ReferenceEquals(PhaseOf(which, transition.Whose), transition.From);

    /// <summary>
    /// Which instance this message is about.
    ///
    /// <para>
    /// Through the concept, which names a different field in each packing that carries it. Only one of
    /// them is ever present, which is why the concept names them all rather than the document naming one.
    /// </para>
    /// </summary>
    private ProtoValue Which(MessageDef message, DecodeResult decoded)
    {
        if (Subject.Distinguishes is not { } concept) return Only;

        var named = message.NamedAll(concept).OfType<Field>().ToList();

        if (named.Count == 0)
            throw new ProtoTypeException(
                $"subject '{Subject.Id}': '{message.Id}' has no '{concept.Id}', so there is nothing to say "
              + "which of these the message is about");

        foreach (var field in named)
            if (decoded[field.CaptureName] is { IsNull: false } value)
                return value;

        throw new ProtoTypeException(
            $"subject '{Subject.Id}': this message carries no '{concept.Id}' — none of "
          + $"{string.Join(", ", named.Select(f => f.CaptureName))} is in the packing that arrived");
    }

    private static EvalScope Scope(DecodeResult decoded)
    {
        Dictionary<string, ProtoValue> byField = new(StringComparer.Ordinal);
        foreach (var (name, value) in decoded.Captures) byField[name] = EvalScope.Record(("value", value));

        return new EvalScope().Set("fields", new ProtoValue.Rec(byField));
    }

    private bool Holds(Expr condition, EvalScope scope)
    {
        var answer = _evaluator.Eval(condition, scope);

        return answer is ProtoValue.Bool b ? b.Value
             : throw new ProtoTypeException(
                   $"a transition's condition `{condition.Render()}` must answer true or false, "
                 + $"got {answer.Kind}");
    }
}
