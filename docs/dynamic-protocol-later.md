# DynamicProtocol — what is not built, and what not to rediscover

Companion to [dynamic-protocol.md](dynamic-protocol.md), which describes what exists. This holds the parts
worth keeping from the five documents deleted on 2026-08-15: things deliberately not built, findings that
still bite, and traps somebody already paid for.

Nothing here is current design. Where it says a thing is missing, check before believing it.

---

## 1. The state model — specified, never built

The largest deliberate omission. A full specification existed (`dynamic-protocol-state.md`, ~630 lines) and
an implementation existed (`State/Standing.cs`, `State/Subject.cs`) that was welded to `MessageDef` and was
deleted with it. Only the `state` node kind survives, holding values between messages.

The specification's shape, condensed to what still looks right:

- **Three spaces as facets, not roots** — what *we* believe, what *they* believe, and what has been
  *confirmed*. "Ours" and "theirs" are queries over one tree, not separate containers.
- **A scope is the unit of correlation**, and the key is not the whole mechanism. Modbus correlates on a
  transaction id; SIP pairs on three things at once.
- **Lifetime is a node**, not a property — phases, anchors, and a cascade when one ends.
- **Epistemics**: confirmed versus presumed. A capability is negotiated, and until the peer answers, what
  you believe about it is a presumption that can be wrong.
- **Message legality reads the tree**: which messages are legal when, mandated chaining, mutual exclusion.

What made it hard was never the state machine. It was that every legality rule wants to name a message and
a field, and both were spelled in a second vocabulary. On the graph model they are nodes, so the rules
become edges — which is the reason to build it *now* rather than then.

**TCP was the case that demanded the first piece of it, and got it.** The handshake test used to add one
to a sequence number after a SYN and the payload length after data — TCP's own arithmetic, living in a
harness. RFC 9293 §3.3.1 is in the description now and `updates` carries the answer out, so a host
acknowledges what it was told rather than what it worked out. What is still in the host is which segments
are *legal* when.

**Two of the three pieces legality needs are now in place, and the third is where it stalled.** A `checks`
edge works on any node, so a message can carry an invariant about the whole segment rather than about one
field; and a state nobody set reads as nothing rather than failing, because a conversation starting is
ordinary. What is missing is not machinery — an attempt at RFC 9293 §3.10 (legal-in-state plus the
transition, both hanging off the message) loaded and refused a plain SYN generated from CLOSED, which
means the check's view of `syn` and `ack` at the message's appearance is not what it looks like. Diagnose
that before assuming the shape is wrong; the shape looked right.

And note what adding it does to everything else: every TCP test that builds a segment out of nowhere now
has to say which state it is in. That is correct — a segment is legal *in a state* — but it means the
legality slice and the tests that drive it land together or not at all.

## 2. What the ten-protocol corpus is for

The corpus was assembled *before* the engine, from ten protocols chosen so each defeats a grammar in a
different way. The first grammar failed all ten — 182 gaps, 52 blocking; it could not encode five and could
not decode nine. Finding that on paper cost a day instead of a rewrite.

| Protocol | What it defeats |
|---|---|
| **NTP** | nothing much — the smallest honest test. Fixed 48 octets, one octet split 2\|3\|3, a trailer present only when the association is authenticated |
| **Modbus TCP** | a fork whose arms are different lengths, inside what a length field measures; and a request and response that carry the same function code, so direction is not on the wire |
| **CoAP** | *(built)* option deltas that accumulate across options, so decoding option *N* depends on 0..*N*−1 |
| **mDNS / DNS-SD** | *(query built)* name compression — pointers are absolute offsets into the whole message. The query has none and round-trips; the response has four, and needs the two things in §5a besides |
| **MQTT** | *(built)* a varint length header; connect-flag bits that decide whether later fields exist at all — and, unforeseen when the corpus was written, the first protocol with several message formats, told apart by a nibble inside the message |
| **DHCP** | *(built)* options including a bare Pad, which the first attempt could not represent — and so mis-read every option after one |
| **SNMPv2c** | nested BER lengths |
| **SSDP** | delimiter-terminated text; no widths and no length prefixes anywhere |
| **TLS 1.2** | three stacked length-prefixed layers, and a hello with no extension block at all |
| **BACnet/IP** | *(built)* three stacked sub-protocols; a discriminator six octets and a nibble in; a value's octet count packed into three bits of the tag before it. Its segmented answers stay opaque — a segment is cut mid-value, so the thing to parse is the concatenation, which is the client's across datagrams |

Three findings reshaped the design and still hold:

1. **Decode is not a pure per-field fold.**
2. **Extent is a function of value** for self-delimiting shapes, so any fixed facet ordering fails.
3. **Presence is structural.** Whether a part exists changes where the path goes, so it must settle before
   the next place is known — not afterwards.

## 3. Traps, learned the hard way

Kept nearly verbatim, because each cost real time.

- **A check that can never fail.** CoAP has an invariant that became unfalsifiable when the width started
  being *written* from the nibble rather than *compared* against it. Keep the sentence if the specification
  says it — but say in the comment that it guards nothing, or the next reader will think it does.
- **A facet declared and never produced.** `Emitted` sat in the facet set, marked not-applicable
  everywhere, for as long as it took somebody to want a checksum. Do not leave a slot carved out and empty.
- **Test corpora are written by the same hand as the engine.** DHCP's Pad defect and CoAP's escape-width
  defect were both invisible to the real captures. Auditing the generated prose against the RFC found
  things the captures structurally could not.
- **Subagents will edit files you told them to read.** One silently changed a BACnet zero rule from `empty`
  to `oneByte`, contradicting the comment directly above it and breaking the round trip. Diff before
  trusting. (Confirmed again on 2026-08-15, on a different task.)
- **A question with two answers in one codebase will be answered differently.** "What names does this
  expression need" was implemented twice; the copy one check used knew about `let` and not about lambda
  parameters, so bounded iteration was unusable at every site that check governed, and had been since the
  day it landed.
- **Identity is the object, not the name.** Three bugs in one session came from minting a fresh node where
  one was meant to be shared.
- **A name that goes missing evaluates to nothing rather than failing.** A set whose id contained a dot
  could not be reached by the expression naming it — `sets.a.b.extent` is nested member access, not one
  key — so the length that measured it computed *nothing*, and the failure surfaced three layers down as
  "expected Int, got Null" inside a base-128 converter. *Fixed 2026-08-15: refused when the protocol
  loads, along with the rest of `Consistency`.*
- **A capability the engine has and the file format cannot reach is worse than one it lacks.** `Field.Via`
  had seven call sites in the codec and no key in the parser; `Coded` was a class with no node kind. Both
  went unnoticed for as long as they did because the descriptions written around them *explained
  themselves* — dhcp.json argued that an address is kept as octets because a number can be added to
  another one, which reads as a principle and was a workaround for a missing two lines. When the file
  cannot say something, the prose justifying the gap is where it hides.
- **Blind line-number edits cut across method boundaries.** Twice. Read, then edit.
- **A presence that waits on a field further along deadlocks, and the message names the wrong thing.** It
  is reported as "these prerequisites name nodes that were never realised", which sounds like a repetition
  that failed to expand and is actually a description asking about a place the walk cannot reach until the
  question is answered. Values may look forward; presence may not.
- **A computation used to be evaluated once per asker.** CoAP's option delta is wanted by four fields and
  ran four times for every option. Nothing was wrong with the answers, since what it reads cannot change
  between asks — but a fact about a message belongs on an appearance, and until it was one a computation
  had no settled moment to hang anything off. *Fixed 2026-08-15; keyed by the round, because a set written
  once per item answers differently each time round.*

## 4. Why the document layer died

Worth recording, because the failure was not that it was broken.

It worked. The problem was that every change to the graph had to be shaped around keeping it green, and the
shape it forced was consistently the wrong one: a bit run could not become a node while a `Pattern` was
what a field carried, and an extent could not be measured in bits while thirty documents read it as octets.

The trigger was HPACK, which read correctly and could not be written back, for two reasons that were the
same sentence — **the thing that should be a node is not one**:

- a chain's carry had its dependencies recovered by *scanning its text* for field references, so a read
  inside a conditional made every branch's fields a prerequisite of every component;
- a bit group settled all at once, so two runs of one octet pointing opposite ways were a cycle, because
  the node was the *group*.

Both are gone by construction now: computations are nodes with edges, and a bit run is a field.

The compensations that went with it — an `ExprSite` enumeration, a `Roles` string table, a `Vocabulary`
table of which roots each site could reach, three separate reference-scanners, an `Optionals` map — were
all standing in for edges. The vocabulary table was **wrong twice in one session**, each time committing the
exact defect it existed to prevent. A structure that keeps committing the error it exists to stop is
standing in for something else.

## 4a. State, and what it is not for

`updates` is built. **What it is not for is an accumulator inside one message.** CoAP's running option number is the case that
looked like state and is not: a protocol that changed its own state mid-message would be a strange
protocol, and the tell was that it wanted writing six times in one message. Reaching for a state node
there would be abusing the concept to get a fold. The caller sums the deltas instead.

That the update happens exactly once is what makes the design small: `Settle` already refuses a second
answer, so "once per message" enforces itself and needs no overwrite, no ordering rule between the two
directions, and no new law.

## 5a. What mDNS found, 2026-08-15

Two, both measured rather than reasoned about. The first is fixed; the second is not, and its first
diagnosis was wrong in a way worth keeping.

**A repetition inside a repetition used to write the wrong message and say nothing.** Not a refusal, not a
throw — wrong octets. `_round` was one counter for the whole walk, and `Item` resolved `item` against the
first repeating set that held the node, so an inner set bound the *outer* set's item and went round the
outer number of times. Two groups of two labels emitted two octets, each holding an outer item coerced to
text.

Fixed by giving the rounds to the repetitions instead of to the walk — see *Repetition* in
[dynamic-protocol.md](dynamic-protocol.md). The shape was anticipated: `RunNode` was already keyed by
`(node, within, index)` and nothing had ever filled `within`. Two things were subtler than they look and
are the parts to be careful of if this is touched again:

- **Which repetition a place is in comes from what holds it, not from the path.** A loop is left and
  re-entered by edges from junctions that belong to no set, so asking the walk says a loop's own fork is
  outside the loop. A junction keeps whatever frame led to it.
- **The outer round has to advance when the walk steps back *into* an inner repetition.** Going out it
  advances on returning to the outer set's first member; coming in, the edge that goes round again points
  straight at the innermost field, and there is nowhere else to notice. Without it every name after the
  first reads on into the first one's labels — with no error, because the appearances are all distinct.

Also: `Produced` memoised by `(computation, index)`, which is one answer for the second label of every
name. It is keyed by the frame as well now.

**A value cannot be derived from where something sits, going out.** `position` is settled in a final pass
after `resolver.Resolve()` — every value is already fixed by then — so an expression requiring the
`position` facet gets `Null` and the message fails with "expected Int, got Null". Coming in it is fine:
reading settles a position as it arrives.

The first diagnosis of this called it a cycle — a compressed name's width depending on its own offset — and
**that was wrong, and the confusion is worth naming because it is easy to repeat.** Whether and where to
compress is the *sender's* choice, an implementation policy; it is not something the message format says.
Once a description states that a name is these labels then a pointer to that node, the width is fixed and
only the pointer's *value* wants a position. Widths never depend on positions, so positions depend on
widths and pointer values depend on positions — acyclic. Position could be an ordinary scheduled facet
(a place's own = the previous one's plus its extent) rather than a stamp applied afterwards, and the
worklist would settle it like anything else; a description that did write a cycle would be reported as one.
What remains genuinely hard is pointing at a *suffix* — RFC 1035 §4.1.4 permits an offset into the middle
of another name, and no edge can say "the third label of that one".

**What went in instead, and it is the better half of the finding:** the description states its own scope in
the octets. QDCOUNT is checked to be one, ANCOUNT/NSCOUNT/ARCOUNT to be zero, QR to be zero — so a
multi-question query, a known-answer-suppression query (§7.1) and a response are each *refused where they
are written*, naming the reason, instead of being read as though the extra parts were not sent. A
description that cannot cover something should say so in a way a capture can trip over, not in prose.

## 5. Known gaps, as of 2026-08-15

- **A fixed-width field holding a NUL-terminated name is one field, not two.** DHCP's `sname` and `file`
  are sixty-four and a hundred and twenty-eight octets holding a name and then fill; where the name ends is
  said by the first NUL, and a span that ends at a value it has to find first is the capability `Pattern`
  took with it. `fit` covers the writing (and is a no-op when handed the full width, which is what makes
  the round trip work); nothing splits it coming back.
- **Six protocols to author.** Hold SSDP back — it is delimiter-terminated text, and `WireForm.Opaque`
  lost the `Until` delimiter when `Pattern` was deleted. That capability needs rebuilding before SSDP or
  HTTP.
- **A discriminator is read but not recorded.** `identifies` answers which message this is and then throws
  the answer away; the message re-reads it, which is right, but nothing keeps a note that the *protocol*
  looked. Harmless today because every message here carries the discriminator itself. A protocol whose
  discriminator is not part of any message would have nowhere to put it.
- **Nothing checks that a discriminator's keys are exhaustive or disjoint.** Two messages keyed on the same
  value load happily and the first one wins.
- **A `via` cannot carry a fixed argument.** `minuint` needs to be told what a zero comes to, so a BACnet
  Unsigned — the fewest octets that hold the value — cannot be a field's own conversion; the field holds
  octets and the caller reads them as a number. The stated reason for the restriction is that "a field has
  nowhere for an argument to come from", which is true of a *computed* one and not of a literal in the
  file. Either admit `"via": {"apply": "minuint", "with": {"zero": "oneByte"}}` or say why not.
- **Nothing checks that a description is reachable in both directions.** Two shapes of the same message
  chosen by a fork are fine, but a presence that depends on a field further along deadlocks the walk on
  the way out and reads perfectly well on the way in — so a description can be half-right and only the
  encode side says so, at run time, naming nodes rather than the rule.
- **A `validator` node's own `because` is parsed and never read.** A `set-of-values` gets its prose into
  the refusal; a `validator` does not — only the `checks` edge's does. So a file can explain a range and
  have the explanation vanish, which is the one thing the format is not supposed to permit. Either surface
  both (the node says what the rule is, the edge says why this field is under it) or stop parsing it.
- **Modbus register values are one opaque span**, not a node each. They are a repetition and the write side
  can now do repetitions, so this is ready to be refined and would be a good first exercise of
  `requires … "each"` against a real capture.
- **Two descriptions can disagree.** A `decode` chain that contradicts the write path is not detected
  structurally; it shows up as a message that will not round-trip. A load-time consistency check — every
  place reachable on one path reachable on the other — would catch it earlier and is not written.
- **A record literal in the expression language.** There is no way to build a `Rec` in an expression, so
  structured items have to be supplied from outside. Worth a constructor.
- **No clock.** SIP is blocked on it, and so is anything with a retransmission timer.
- **A per-connection layering seam.** `Subprotocol` hangs off a field, so "everything after this on this
  connection is another protocol" can only be said by making the switched stream a trailing field. Works;
  wants WebSocket-after-upgrade to show the right shape.
- **Nothing type-checks an expression's INTERIOR.** Every edge into one is checked now, and what it gives
  is declared — but `count + 'x'` inside the body is still a run-time failure. That is a type checker for
  the expression language, which is a different thing from the edge rule and was not what was missing.
- **`Contains` and `allowed` edges are read by almost nothing.** `allowed` in particular means "what may
  turn up in this span" and the walk never consults it — repetition went the `requires … "each"` route
  instead. Either wire it up or delete it.

## 6. Sub-octet alignment — resolved, recorded because the note was wrong

An earlier version of this note listed sub-octet alignment as an open question: whether refusing a bit group
that is not a whole number of octets was a correct guard or a limitation wearing one, and observed that no
protocol on the list would tell us.

It was a limitation. Bit runs are fields now, sets are measured in bits, and a fixed width is fixed *bits* —
in octets, a four-bit field's width appears to depend on its value, and TCP's Data Offset is then a cycle.
The refusal is gone and nothing replaced it: a set whose members do not total whole octets is still an
error, but it is an error about the *set*, which is the thing that occupies octets.
