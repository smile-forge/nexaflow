using System.Text;
using Nexaflow.IO.Protocol.Expressions;

namespace Nexaflow.IO.Protocol.Wire;

/// <summary>
/// A message definition written out as English.
///
/// <para>
/// Not a debugging aid. The whole point of the engine is that a model can draft a protocol document and a
/// <b>person then decides whether to trust it</b> — and nobody reviews trust by reading a field tree. This
/// is the artefact that review happens against, so it has to state the things a reader would otherwise
/// have to infer: which values the caller supplies and which the engine derives, what decides a branch,
/// what ends a repetition, and what the engine will refuse.
/// </para>
///
/// <para>
/// It is also the only way to check the model against a specification written by someone else. A corpus
/// assembled by the same hand that wrote the engine can only find the gaps that hand thought of; prose
/// that can be laid beside an RFC can be found wrong by the RFC.
/// </para>
/// </summary>
public static class MessageProse
{
    public static string Describe(MessageDef message)
    {
        var text = new StringBuilder();
        var fields = message.AllFields.ToList();

        text.AppendLine($"# {message.Id}");
        text.AppendLine();
        text.AppendLine($"{Count(message.Fields.Count, "field")} at the top level, "
                      + $"{fields.Count} in the message altogether.");
        text.AppendLine();

        Supplied(message, text);
        Derived(message, text);

        text.AppendLine("## Layout");
        text.AppendLine();
        text.AppendLine("Fields appear on the wire in the order given. Nesting is shown by indentation; a");
        text.AppendLine("region contributes no octets of its own, only those of what it contains.");
        text.AppendLine();

        foreach (var field in message.Fields) Walk(field, 0, text, message);

        Refusals(message, text);
        return text.ToString();
    }

    // ── Which way each value flows ────────────────────────────────────────────

    private static void Supplied(MessageDef message, StringBuilder text)
    {
        var roots = message.AllFields
            .Where(f => f.Value is not null)
            .SelectMany(f => f.Value!.Descendants().OfType<Expr.Member>()
                              .Where(m => m.Target is Expr.Root { Name: "inputs" or "item" })
                              .Select(m => $"{((Expr.Root)m.Target).Name}.{m.Name}"))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        if (roots.Count == 0) return;

        text.AppendLine("## Supplied by the caller");
        text.AppendLine();
        text.AppendLine("These are the only values that have to come from outside. Anything named");
        text.AppendLine("`item.…` belongs to one structure of a chain rather than to the message.");
        text.AppendLine();
        foreach (var root in roots) text.AppendLine($"- `{root}`");
        text.AppendLine();
    }

    private static void Derived(MessageDef message, StringBuilder text)
    {
        var derived = message.AllFields
            .Where(f => f.Value is not null && f.Value!.RootNames().Contains("fields"))
            .Select(f => (f.Id, Rule: f.Value!.Render()))
            .ToList();

        if (derived.Count == 0) return;

        text.AppendLine("## Derived by the engine");
        text.AppendLine();
        text.AppendLine("Supplying any of these would be a mistake: they are computed from the message as it");
        text.AppendLine("is being built, and a supplied value that disagreed would be silently wrong.");
        text.AppendLine();
        foreach (var (id, rule) in derived) text.AppendLine($"- `{id}` — {rule}");
        text.AppendLine();
    }

    // ── The walk ──────────────────────────────────────────────────────────────

    private static void Walk(Field field, int depth, StringBuilder text, MessageDef message)
    {
        var pad = new string(' ', depth * 2);
        text.AppendLine($"{pad}- **{field.Id}** — {Shape(field)}{Source(field)}");

        switch (field.Pattern)
        {
            case Pattern.Group group:
                foreach (var child in group.Fields) Walk(child, depth + 1, text, message);
                break;

            case Pattern.Choice:
                // Through the edges: the key that selects a packing is carried by the offer, not by the
                // packing, so this is where it has to be read from.
                foreach (var offer in message.Offered(field))
                {
                    var arm = (Arm)offer.To;

                    text.AppendLine($"{pad}  - *{arm.Name}* — {Selected(offer)}, "
                                  + $"{(arm.Fields.Count == 0 ? "contributing nothing" : Count(arm.Fields.Count, "field"))}.");
                    foreach (var child in arm.Fields) Walk(child, depth + 2, text, message);
                }
                break;

            case Pattern.Chain chain:
                Walk(chain.Element, depth + 1, text, message);
                break;

            case Pattern.Assorted assorted:
                Walk(assorted.Token, depth + 1, text, message);

                foreach (var offer in message.Offered(field))
                {
                    var sort = (Arm)offer.To;

                    text.AppendLine($"{pad}  - *{sort.Name}* — {Selected(offer)}, "
                                  + $"{(sort.Fields.Count == 0 ? "contributing nothing" : Count(sort.Fields.Count, "field"))}.");
                    foreach (var child in sort.Fields) Walk(child, depth + 2, text, message);
                }
                break;
        }

        if (depth == 0) text.AppendLine();
    }

    private static string Selected(Offers offer) => (offer.Key, offer.Repeats) switch
    {
        (null, _) => "taken when nothing else matches",
        (Values.ProtoValue.Int i, false) => $"taken when the discriminator is {i.Value} (0x{i.Value:x})",
        (Values.ProtoValue.Int i, true) => $"announced by {i.Value} (0x{i.Value:x}), any number of times",
        (var k, false) => $"announced by '{k}'",
        (var k, true) => $"announced by '{k}', any number of times",
    };

    private static string Shape(Field field) => field.Pattern switch
    {
        Pattern.Scalar s =>
            $"{Count(s.Octets, "octet")}, {(s.Signed ? "two's-complement signed" : "unsigned")}, "
          + $"{(s.BigEndian ? "most" : "least")} significant octet first",

        Pattern.Bits b =>
            $"{Count(b.TotalBits / 8, "octet")} divided into bit runs, most significant first: "
          + string.Join(", ", b.Slices.Select(s => $"`{s.Name}` ({Count(s.Width, "bit")})")),

        Pattern.Opaque { Width: { } w } => $"a run of {Count(w, "octet")} carried without interpretation",

        Pattern.Opaque { Length: { } len } =>
            $"a run of octets carried without interpretation, {Count(len)} of them on the way in; on the "
          + "way out it is as long as the value written",

        Pattern.Varint v =>
            $"an integer spread seven bits at a time over up to {Count(v.MaxOctets, "octet")}, "
          + $"{(v.Order == GroupOrder.MostSignificantFirst ? "most" : "least")} significant group first, "
          + "with the high bit of an octet marking that another follows. **Its width is a function of its "
          + "value**, so it is not known until the value is"
          + (v.Minimal ? ". A chain longer than the shortest encoding of its value is refused" : ""),

        Pattern.EscapedInline e =>
            $"a value carried in one marker octet while it is below {e.InlineLimit} (0x{e.InlineLimit:x}); at "
          + $"or above, the marker records how many of the next octets carry it, up to {e.MaxOctets}. "
          + "**Its width is a function of its value**"
          + (e.Minimal ? ". Escaping when the value would have fitted inline, or using more octets than it "
                       + "needs, is refused" : ""),

        Pattern.Choice { Conditioned: true } c =>
            $"one of {Count(c.Arms.Count, "packing")}, and nothing on the wire says which. Each says when it "
          + "is an option: "
          + string.Join("; ", c.Arms.Select(a => a.Condition is null
                ? $"*{a.Name}* otherwise"
                : $"*{a.Name}* while `{a.Condition.Render()}`"))
          + ". Both directions ask the same question — a reader knows which parts arrived, a writer knows "
          + "which it was asked for — and exactly one packing is an option in every combination of them, "
          + "which is proved rather than trusted",

        Pattern.Choice { Selects: { } selects } c =>
            $"one of {Count(c.Arms.Count, "packing")}. On the way in `{c.Key.Render()}` says which arrived; "
          + $"on the way out `{selects.Render()}` says which to write. Two readings of one discriminator, "
          + "because the question is not answerable the same way in both directions — a reader asks the "
          + "region, a writer asks what it was given. Both land on the same arms, so the cover is proved "
          + "once and holds for both",

        Pattern.Choice c =>
            $"one of {Count(c.Arms.Count, "packing")}, chosen by `{c.Key.Render()}`. The same expression "
          + "decides it in both directions: on the way in it is what has just been read, on the way out "
          + "what is about to be written. Whichever is taken becomes this field's value, so a later step "
          + "branches on the shape that arrived rather than re-testing the discriminator",

        Pattern.Group { Extent: { } extent } =>
            $"a region, running {Count(extent)} on the way in. Its contents must fill it exactly",

        Pattern.Group => "a region; its extent is whatever its contents come to",

        Pattern.Assorted a =>
            $"a run of components, each announcing which of {Count(a.Sorts.Count, "kind")} it is with its "
          + $"`{a.Token.Id}`. Another follows while `{a.Continues.Render()}`. The order is preserved and is "
          + "not significant. **Each kind is separately declared**, so a kind that comes at most once is a "
          + "node something elsewhere can point at — which is what a run of one repeated shape could never "
          + "offer, because there the answer would depend on which component happened to arrive first"
          + (a.Sorts.Any(s => s.Repeats)
                ? $". {Sentence([.. a.Sorts.Where(s => s.Repeats).Select(s => $"`{s.Name}`")])} may come more "
                + "than once, and so cannot be named from outside" : ""),

        Pattern.Chain c =>
            $"zero or more structures of the same shape, one after another. Another follows while "
          + $"`{c.Continues.Render()}` — where `ordinal` counts the structures already read and `room` is "
          + "the octets left in the enclosing region. Each structure has its own field names, so a length "
          + "prefix inside one refers to that structure and no other",

        _ => "an unnamed shape",
    };

    private static string Source(Field field)
    {
        List<string> notes = [];

        if (field.Value is { } value) notes.Add($"written from `{value.Render()}`");
        if (field.Via is { } via) notes.Add($"passed through the converter `{via}` on the way out and its inverse on the way in");

        if (field.Through is { } transform)
        {
            notes.Add($"transformed by the document transform `{transform.Name}`"
                    + (transform.Summary.Length > 0 ? $" ({transform.Summary})" : ""));

            if (transform.Domain is { } domain)
                notes.Add($"which is only reversible where `{domain.Render()}`");
        }

        if (field.As is { } captured && captured != field.Id) notes.Add($"bound as `{captured}`");

        return notes.Count == 0 ? "." : ". " + Sentence(notes) + ".";
    }

    // ── What the engine will not accept ───────────────────────────────────────

    private static void Refusals(MessageDef message, StringBuilder text)
    {
        List<string> refusals = [];
        var all = message.AllFields.ToList();

        foreach (var field in all)
            switch (field.Pattern)
            {
                case Pattern.Choice { Key: null } conditioned:
                    refusals.Add($"`{field.Id}` has no discriminator; its packings say when each is an "
                               + "option. Every combination of the parts they turn on was tried, and "
                               + "exactly one packing applies in each — so a message where none would fit, "
                               + "or two would, is impossible rather than merely unexpected.");
                    break;

                case Pattern.Choice choice:
                {
                    var reachable = Pattern.ReachableKeys(choice.Key!);
                    var fallback = message.Offered(field).FirstOrDefault(o => o.IsFallback);

                    refusals.Add(reachable is not null
                        ? $"`{field.Id}` dispatches on `{choice.Key.Render()}`, which can only take "
                        + $"{reachable.Count} values, and the arms cover all of them — an unhandled value is "
                        + "impossible rather than merely unexpected."
                        : $"`{field.Id}` dispatches on `{choice.Key.Render()}`, whose range cannot be computed, "
                        + $"so the arm *{fallback?.To.Name}* is declared to catch anything unlisted. "
                        + "A message that matches no arm is an error, never a silent skip.");
                    break;
                }

                case Pattern.Group { Extent: not null }:
                    refusals.Add($"`{field.Id}` declares how far it runs, and its contents must consume "
                               + "exactly that. Leaving octets unread would hand them to whatever comes next.");
                    break;

                case Pattern.Varint or Pattern.EscapedInline:
                    refusals.Add($"`{field.Id}` refuses any encoding of its value longer than the shortest "
                               + "one, so a value maps to exactly one sequence of octets.");
                    break;
            }

        refusals.Add("Any field reading past the end of its enclosing region is an error, as is a message "
                   + "with octets left over once every field has been read.");

        refusals.Add("A field the document fixes to a constant must arrive carrying it, and a value whose "
                   + "encoding is not the canonical one is refused — otherwise either would re-encode to "
                   + "different octets than arrived, with nothing saying so.");

        // Rules read differently from structural refusals: these describe messages that are shaped
        // correctly and illegal anyway, which is exactly what a reviewer most needs pointed out.
        foreach (var rule in message.Rules)
            refusals.Add($"**{Kind(rule)}** — {rule}. {rule.Because}");

        text.AppendLine("## What is refused");
        text.AppendLine();
        text.AppendLine("These are checked, not assumed. A message that breaks one of them fails to decode");
        text.AppendLine("rather than decoding into something plausible.");
        text.AppendLine();
        foreach (var refusal in refusals.Distinct(StringComparer.Ordinal)) text.AppendLine($"- {refusal}");
    }

    /// <summary>Which sort of illegal. Naming the kind is the whole reason the kinds are separate — a
    /// reader deciding whether to trust a document needs to know whether a rule is about one value, about
    /// two fields contradicting each other, or about a combination that may never occur.</summary>
    private static string Kind(Rule rule) => rule switch
    {
        Rule.Domain => "a value this field may not take",
        Rule.Requires => "one condition obliging another",
        Rule.Excludes => "a combination that may never occur",
        Rule.Invariant => "something that must always hold",
        Rule.Arrangement => "how one structure may follow another",
        _ => "a rule",
    };

    // ── Wording ───────────────────────────────────────────────────────────────

    private static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";

    private static string Count(Expr expression) => $"`{expression.Render()}`";

    private static string Sentence(List<string> parts)
        => parts.Count == 1 ? Capitalised(parts[0])
         : Capitalised(string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1]);

    private static string Capitalised(string s) => char.ToUpperInvariant(s[0]) + s[1..];
}
