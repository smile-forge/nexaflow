# DynamicProtocol

A declarative protocol engine. A protocol is a **graph**, written down as JSON — not code. The graph
describes how values become octets and back, and the engine contains no protocol specifics at all.

The point is that an AI can *author* a protocol description, a person can *review* it, and a description
that is trusted becomes a button on a device's page. Smart plugs through to HVAC, without a build.

> **Rewritten 2026-08-15.** Everything before this described a *document* format that has since been
> deleted, along with `MessageDef`, `MessageCodec` and `Pattern` — about 20,000 lines. Read
> [dynamic-protocol-later.md](dynamic-protocol-later.md) for what that layer taught, what is deliberately
> not built, and the traps worth not rediscovering.

## Status, honestly

| | |
|---|---|
| **Round-trips against real captures** | TCP (RFC 9293), NTP (RFC 5905), Modbus TCP, MQTT 3.1.1, DHCP (RFC 2131/2132), CoAP (RFC 7252), mDNS query (RFC 6762), BACnet/IP (ASHRAE 135 Annex J) |
| **Authored but not yet built** | SNMP, SSDP, TLS — and the mDNS *response*, which needs a name to be able to end in a compression pointer |
| **Engine** | `src/Nexaflow.IO.Protocol` — no protocol name appears in it |
| **Definitions** | `src/Nexaflow.Tests/Nexaflow.Tests.Features/Protocol/Definitions/*.json` |
| **Captures** | `src/Nexaflow.Tests/Nexaflow.Tests.Features/Protocol/Corpus/*.json` — 30 packets, every octet accounted for by exactly one field |

The corpus predates the engine and was assembled to defeat it: ten protocols chosen so each breaks a
grammar in a different way. It is evidence, not description — which is why it survived the deletion of
everything that *described* a protocol.

---

# Part 1 — the model

## Two graphs

The **protocol graph** is the static description. It is complete before anything reads it: no part of it
depends on a message.

The **run graph** is made per message. Computed values live **on appearances** in it, so anything pointing
at a node is handed the value rather than recomputing it. An appearance is keyed by `(node, within, index)`
— the index is which time round, for the parts that repeat.

> **The invariant.** Once a build starts, information comes from the protocol graph or the run graph and
> nowhere else. No ambient scope, no field on the codec, no dictionary threaded through a call. A
> computation's inputs are assembled from *its own* edges immediately before it runs, so what an expression
> can see is exactly what the graph says it may.

## Facets

Every appearance answers up to six questions, settled by a demand-driven worklist — no passes, no fixed
points, no back-patching.

| Facet | The question |
|---|---|
| `Realised` | has the walk got here |
| `Present` | is it here at all |
| `Extent` | how many octets |
| `Position` | where it starts |
| `Value` | what it holds |
| `Emitted` | what octets it came to |

Ordering `Sized → Positioned → Valued → Emitted` fails on real protocols, because for a self-delimiting
shape the extent is a *function of* the value. Do not reintroduce passes.

**Direction is asymmetric and that is not a defect.** Writing schedules facets — a length can measure a
span that has not been laid down. Reading settles what it finds as it finds it, because nothing later can
inform anything earlier.

## The chain

    protocol → message → packing → what follows what

One edge (`then`) at every step. A message's ways on are its arrangements, keyed and decided exactly as an
alternation's arms are — four scales, one mechanism.

The root is the **protocol**, not its first message. Rooting at a message works for exactly as long as
there is one and silently privileges the first the moment there are two.

**Which message this is, is usually something the message says.** Going out there is a caller to ask;
coming in there is not, and nothing has been read at the moment the choice has to be made. So the reading
*looks* — see `identifies` below. What that buys is worth stating plainly: every message stays describable
on its own, as though it were the only one in the protocol.

---

# Part 2 — how to build a protocol graph

A definition is one JSON object: `protocol`, `title`, `source`, `about`, `nodes`, `edges`.

```json
{
  "protocol": "tcp",
  "source": "RFC 9293",
  "nodes": [ { "id": "tcp", "kind": "protocol" } ],
  "edges": [ { "kind": "then", "from": "tcp", "to": "segment" } ]
}
```

**Ends are named, never numbered.** An edge says `"from": "dataOffset"`. Naming ends by array position is
smaller, unreadable, and makes every insertion a silent re-pointing of everything after it — the one class
of corruption that still loads.

**Unknown kinds are refused by name**, never skipped. A protocol that loses a part on the way in reads back
as one that is almost right, and every capture that happens not to exercise the missing part still passes.

`about` may be a string or an array of lines; they are joined. Write prose — a protocol is meant to be read
and argued with.

## Node kinds

| `kind` | What it is |
|---|---|
| `protocol` | Where a walk begins. Its ways on are the message formats. |
| `message` | One message *format*. As many as the specification has formats — not one per flag combination. |
| `packing` | One arrangement of a message. More than one only when the structure decisively changes **on state**. |
| `set` | A container the specification names: a header, a group of bits, a pseudo-header. **It holds; it does not produce.** |
| `junction` | A place that makes no octets and holds nothing — a fork, a place arms meet, an arm that writes nothing. |
| `field` | Something that occupies octets. Carries a `form`. |
| `end-parse` | Where a reading is allowed to stop. |
| `input` | A value the caller provides. Read as `inputs.<id>`. |
| `state` | A value the protocol keeps between messages. Read as `state.<id>`. |
| `constant` | A value fixed by the description. |
| `evaluated` | An expression. |
| `converted` | A member of the closed converter set, with its value **and its arguments** on edges. |
| `coded` | Code behind a name the host registered. Its inputs are edges like anything else. |
| `default` | What a part means when absent, and the policy around that. |
| `validator` | Ranges a value must fall in. |
| `set-of-values` | A named value set, open or closed. |

`input`, `state` and `constant` are **source nodes**: computations whose work is to return what they were
given. So a field fed from outside is fed by one edge, with no expression in between reading it back out by
name — that intermediate step was the same fact written twice.

They have **no key**. A node has an id, and a second name is another pair of things that can drift. What
they carry is `as` — the specification's own term, which is what somebody setting a value will say
("Source Port", not `input.sourcePort`).

## Field forms

A field has a **form**, not a shape. Shapes were instructions for building a graph, and the graph is now
the thing that is written down.

| `of` | Parameters | Notes |
|---|---|---|
| `run` | `bits` | A run of bits. **This is what a bit field is** — its own node, not a slice of an octet some other node owns. |
| `scalar` | `octets`, `big`, `signed` | Fixed-width integer. |
| `opaque` | `octets` (optional) | Octets carried uninterpreted. With no width, something else says how far it runs — or it takes what is left. |
| `varint` | `order`, `max`, `minimal` | Continuation-encoded integer. |
| `escaped` | `limit`, `max`, `minimal` | A small value inline, escaping to a counted run. |
| `prefixed` | `marker`, `widths`, `minimal` | Leading bits select the width; the rest is the value. |

## Edge kinds

| `kind` | Direction | What it says |
|---|---|---|
| `then` | from → to | What follows what. `key`, `otherwise`, `optional`. |
| `decode` | from → to | The same, for a reading that is not the writing backwards. May loop. |
| `holds` | set → member | Membership and contiguity, with `order`. **Not a path.** |
| `computes` | **owner → computation** | What produces one of the owner's facets. `facet`, and `reading` where the two directions differ. |
| `requires` | **computation → input** | What it needs, with `sequence` and `facet`. `parameter` names which argument it feeds. |
| `decides` | forking node → computation | What picks the way on. `reading`. |
| `identifies` | forking node → field → … → field | A path read **ahead** of choosing, ending at the discriminator. Reading only. |
| `checks` | node → validator | What has to hold. |
| `assumes` | field → default | What it means when absent. |
| `updates` | place → state | What a message leaves behind. `facet`, and `parameter` where a calculation is on the way. |
| `allowed` | span → definition | What may turn up in a span. |

The two arrow directions are worth reading twice: **`computes` points from the thing being computed to the
thing computing it**, and `requires` points the other way.

## Choosing a message, when the wire says which

`decides` answers a fork with a value something already has, and that works everywhere the deciding fact
arrives before the fork. At the top of a protocol it is exactly wrong: which message this is, is written
*inside* the message.

`identifies` is a **path, like `then`**, laid from the forking place through however many fields it takes
to arrive at the one that discriminates. The last field on it is the discriminator; its value is matched
against the `key` on each way on. So a discriminator that is not the first thing in a message needs nothing
special — the path weaves through whatever precedes it.

```json
{ "kind": "identifies", "from": "mqtt", "to": "probe.packetType" },
{ "kind": "then", "from": "mqtt", "to": "connect", "key": { "int": 1 } },
{ "kind": "then", "from": "mqtt", "to": "connack", "key": { "int": 2 } }
```

**Nothing is consumed.** Those fields are read from a copy of the position, and the message that gets
chosen reads its own from its own first octet. That is what keeps a message from having to know it has
siblings, or that something looked before choosing it — enter a message part-read and its description
depends on how it came to be selected.

And because nothing is consumed, **these need not be the fields the message uses.** A discriminator may
read one octet as an integer where the message reads it as two runs of four bits. Both describe the same
octet and neither owes the other anything.

Going out, the same fork is decided by `decides` in the ordinary way — the caller says which message it
wants. Two directions, two sources, one fact.

**So a protocol with several messages needs no fork inside any of them.** MQTT has six formats and every
one is a single path from its first octet to its last: what varies between them is which path, not what
happens partway along one.

The same edge answers a fork *inside* a message where the octets are what decide. CoAP's options end at a
`0xFF` that would otherwise read as an option header, so the reading looks at the next octet before
committing to another option.

## Sets, and what a length measures

A set holds; it does not produce. Its extent is a fact about its members rather than something it computed
— which is exactly why a length can measure a header while the header writes nothing itself.

**A set is measured in bits.** In octets a four-bit field has extent zero, so summing octets makes TCP's
header eighteen long instead of twenty; only the *answer* is in octets, and a set ending mid-octet is an
error rather than something rounded.

A set's **octets** come from its members' *values*, laid down by their forms. That is what makes it work
for a pseudo-header, which is never transmitted and so has no stretch of wire to point at. A set nothing
walks to is laid out because something requires it.

A set's octets are worked out **only when something requires them** — computing them regardless makes every
container of an absent part unanswerable, for an answer nobody wanted.

## The checksum shape

The pattern is worth copying, because it removes a notion that is not worth supporting: a field needing two
different values at two moments.

    checksum ←computes← onesComplementSum
                          ←requires← pseudoHeader        (octets)
                          ←requires← concat              (value)
                                       ←requires← header-before-checksum (octets)
                                       ←requires← constant 0x0000
                                       ←requires← header-after-checksum  (octets)
                          ←requires← payload             (value)

The octets summed hold a zero where the checksum goes, and the field itself only ever holds its answer.
A computation may require another computation; the join is nobody's field, and inventing one would put a
value on the wire the protocol does not have.

## Absence — four separate questions

A `default` answers them, and a protocol picks its own combination.

| Key | Question |
|---|---|
| `is` | what the part means when it is missing |
| `written` | whether it is written anyway (a reserved octet that must be present holding zero) |
| `omitted` | whether it is left out when it would only repeat the default |
| `missing` | `Assumed`, or `Malformed` — being missing was not allowed |

`omitted` is half a rule and the engine supplies the other half: if a writer omits the default and a reader
also accepts it spelled out, one value has two encodings and every message taking the long form comes back
different. So the long form is **refused** coming in — the same law as a padded varint.

**An optional part should be gated on state**, not on an input. An NTP association is authenticated or it
is not; every packet on it carries a MAC or none of them do. And gating TCP's options on the SYN *flag*
made the arrangement depend on a field of the very message it was arranging.

## Repetition

Going out, a set may `require` a list with `"facet": "each"`, and is written **once per item**.

```json
{ "kind": "requires", "from": "options", "to": "input.options", "facet": "each", "sequence": 0 }
```

Nothing about the repetition is in the graph's structure. The description says *which list*; it says nothing
about how many, because that number belongs to a message — and in other protocols the list is computed, or
read off a field a moment earlier. A count in the description could be none of those.

Each pass binds `item` and `ordinal`, so fields read their own by name: `item.kind`, not index 0 of a list
dug out again in every field.

Coming in, the reading goes round on a `decode` edge and finds out how many there were — by looking for a
sentinel, by running out of octets, or by **counting**, where the wire says how many and nothing else does:

```json
{ "id": "evaluated.moreQuestions", "kind": "evaluated", "runs": "ordinal + 1 < qdcount",
  "gives": "Bool", "takes": { "qdcount": "Int" } }
```

`ordinal` is which time round *this* repetition is on. Going out the count is `count(<the list>)`, so the
header and the body cannot disagree: one fact, read from whichever side knows it. DNS needs this and a
sentinel will not do — a question carries no terminator and what follows the last one begins exactly as
another question would.

**A reading's rounds carry on past the repetition**, because nothing drives them but the edge that points
back — so the part after a run of options is settled on the walk's fifth appearance rather than its first.
Harmless (reaching for a node from a later round falls back to its only appearance), but a test looking for
that part by index will not find it. Resetting the round on the way out is *not* the fix: a loop's control
junctions are not members of the set they drive, so the walk is standing on one exactly when it is deciding
whether to go round again.

**A repetition may hold another one.** A run of names, each of which is a run of labels — which is what
every DNS message is made of, and what SNMP's varbinds, TLS's extensions and BACnet's properties are too.
The rounds belong to the repetition rather than to the walk: an appearance is keyed by which repetition
encloses it as well as by which time round, so the third label of the second name is a different node from
the second label of the third.

Which repetition a place is in comes from what **holds** it, not from where the walk has been — a loop is
left and re-entered by edges from junctions that belong to no set, and a junction keeps whatever frame led
to it. Going out, returning to the outer set's first member says a new round started; coming in, the edge
that goes round again may point straight at the innermost field, so stepping back *into* a repetition the
walk had left is what says so.

And the inner list is the outer item's: `"runs": "item.labels"` on the computation a nested set repeats
over is asked of the name being written, so a description says what an inner run is *of* rather than
receiving one flattened list and an index scheme to take it apart.

## When the reading is not the writing backwards

Most of a protocol is read by walking what it writes, and there one description serves both directions and
they cannot drift. `decode` edges are for the rest — and the rest is not rare, because protocols are built
for the wire: what can be inferred is omitted, what repeats has no count, and what is malformed has rules
only a reader can apply.

Opt-in per place. A message that declares no reading is read the way it is written.

An `end-parse` node says where a reading may stop. A walk that runs out of edges has finished; whether it
was *entitled* to finish there is a different question, and until something says so the two are the same
answer — which is exactly what a truncated message looks like from inside. A reading that ends anywhere
else is refused, and so is one that leaves octets in hand.

Bound while reading, and only while reading: `remaining` (octets left) and `position` (how far in). They are
the two ends of one question — a trailer is there when there is room for it; a run of options ends where
the header ends, which is an offset.

## Expressions

`Expr.Parse`, evaluated against a scope built from the computation's own `requires` edges. Roots:
`fields.<id>.{value,extent,octets}`, `sets.<id>.{extent,octets}`, `inputs.<id>`, `state.<id>`, plus
`remaining`, `position`, `item` and `ordinal` where they apply.

Converters are a **closed set** — adding one is a deliberate code change, not something a description can
do. `ConverterTable` has 87, including `concat`, `onesComplementSum`, `crc16`, `crc32`, `md5`, `sha256`,
`hmacSha256`, `base64`, `utf8`, `hex`, `packBits`, `repeat` and `index`.

Higher-order forms are in the expression language rather than the table: `map`, `filter`, `findFirst`,
`takeWhile`, `any`, `all`, `fold`, `scan`. So an accumulating read needs no converter — HPACK's eviction
was written with `fold` before anybody thought to add one.

## Values, kinds, and what the file may say

**The file says everything the engine does.** Where it did not, the gap showed up as prose: a description
justifying octets where the real reason was that no key existed to write `"via": "ipv4"`. Three sources of
a value — `input`, `state`, `constant` — and three ways to change one:

| | |
|---|---|
| `converted` | a member of the closed set. **Most of the time this is what you want.** |
| `evaluated` | an expression, for arithmetic the converters do not have |
| `coded` | code behind a name the host registered, for a library nothing else reaches |

All three take their inputs from `requires` edges and can reach nothing else, and **every input is a named
parameter with a declared kind**:

```json
{ "id": "evaluated.padding", "kind": "evaluated", "runs": "(4 - options % 4) % 4",
  "takes": { "options": "Int" }, "gives": "Int" },
{ "kind": "requires", "from": "evaluated.padding", "to": "options",
  "facet": "extent", "parameter": "options", "sequence": 0 }
```

**An expression never names a node id.** It reads its parameters; the edge says which node fills each and
which fact about that node is wanted. Where a value comes from is stated once — the older form said it in
the expression's path *and* on the edge, in two spellings that could drift and needed a check to keep
agreeing. Which is also why a dotted id is no longer a hazard: there is nothing inside an expression that
could spell one.

A converter takes its **value** on unnamed edges (several gather into a list, in `sequence` order) and its
**arguments** on named ones — `repeat` takes a `count`, `fit` a `width` and a `fill`. So an argument may be
a constant, a field read a moment ago, or another calculation, and one call can collect several values
while a parameter stays fixed.

Four names an expression may read that no edge supplies, because they are facts about where the walk is
rather than about any node: `item`, `ordinal`, `remaining`, `position`.

**A list says what its items are**, so `item` is answerable too:

```json
{ "id": "input.subscriptions", "kind": "input", "gives": "List",
  "of": { "filter": "Text", "qos": "Int" } }
{ "id": "input.returnCodes", "kind": "input", "gives": "List", "of": "Int" }
```

A record shape types `item.filter`; a bare kind means `item` *is* the value, and asking it for a member is
then the mistake.

**A field may convert on its own**, with `"via"`, applied on the way out and inverted on the way in:
DHCP's `yiaddr` is four octets on the wire and `192.168.0.10` to whoever sets it. `via` takes no arguments
— a field has nowhere for one to come from — so a conversion that needs a width is a `converted`
computation instead, and saying otherwise is refused when the protocol loads.

**Every computation says what kind of value it gives**: `"gives": "Bytes"`, `Int`, `Number`, `Bool`,
`Text`, `Instant`, `Duration`, `List`, `Record`. A constant knows its own. Required rather than defaulted,
because a kind nobody stated agrees with everything, and a check that runs only where somebody remembered
reads as safety and is not.

## What a message leaves behind

A `state` slot could only be read until now. `updates` is the other direction — a field, a set or a
calculation says which slot it fills, optionally through calculations that work on the value on the way:

```json
{ "kind": "updates", "from": "sequenceNumber", "to": "advance", "parameter": "sent" },
{ "kind": "updates", "from": "advance", "to": "state.nextSequence" }
```

`facet` because a set has no value — a slot fed from one wants its extent. `parameter` names where the
value lands when the next thing is a calculation, exactly as a requirement does; pointing straight at a
slot needs nothing, because a slot holds one value.

**It happens when the message is complete**, not at the moment the source settles — a calculation on the
way may need facts the source's own settling does not wait for, and an expression handed nothing comes to
nothing rather than waiting.

**Once per message, and nothing enforces that separately.** A slot settles like anything else and settling
twice is already an error naming two producers. So a running total across a repetition is not this: that
is a fold over whatever repeats, and reaching for state to hold it is using a fact about a conversation to
carry a fact about a message. CoAP's option numbering is the case that looks like state and is not.

## Refused when it loads

Each of these was a real failure that surfaced somewhere else entirely, so each is now a sentence naming
the node. `ConsistencyTests` pins them.

- **A node with no edges.** Two value sets shipped in MQTT this way, documenting return codes nothing
  checked.
- **An expression reading a name it does not take**, a parameter nothing fills, an edge that does not say
  which parameter it fills, and a parameter the expression never reads.
- **`item.<member>` where the list says items have no such member**, or `item` read whole where an item is
  a record — the last name that was answerable to nothing.
- **A kind that cannot go where it is put** — `Text` into a field that lays down an integer.
- **A converter given a parameter it does not take**, or a `via` that needs one, or a `via` with no
  inverse.
- **A kind that disagrees across a `requires` edge**, for expressions, code and converters alike.

## Rules of thumb when authoring

- **Every field is a node**, including the four-bit and one-bit ones.
- **Sets follow the specification's own headings.** If the RFC names a group, it is a set.
- **Fields identical between messages are one node**; fields named alike that mean different things are
  not. TCP's Acknowledgment Number and its ACK flag share a syllable and nothing else. The test that
  settles the hard cases is **the legal values**: MQTT defines its packet type once, but a CONNECT's is
  the constant 1 and a CONNACK's is the constant 2, so those are two nodes — and the concept they are both
  instances of is a third, the one `identifies` reads.
- **What two messages genuinely share is often a value, not a place.** A SUBSCRIBE's Packet Identifier and
  the SUBACK's that echoes it are two positions in two messages holding one token: two fields, one input.
- **Describe the shape, not the catalogue.** RFC 9293 gives two option shapes — single octet, or
  kind-length-data — so a TCP option this file has never heard of round-trips. Enumerating known kinds
  refuses it.
- **If a specification would state it, it belongs in the description**; if it is about the correctness of
  handling the graph, it belongs in code. Ask this first of any new feature.
- **Nothing whose PRESENCE another part depends on may be worked out from a field further along.** A value
  may — a length measures a span not yet laid down, which is the ordinary case. Presence may not, because
  the walk cannot go on until it settles, so it would be waiting on a place nothing has arrived at.
  CoAP's option length is taken from the item rather than from the field it becomes, for exactly this
  reason, and so is BACnet's: whether a tag carries an extra length octet is asked of the *value* being
  written, not of the extent of the span it will occupy. The two look identical in the file and only one
  of them terminates.

---

## Reading the tests

| File | What it pins |
|---|---|
| `TcpDefinitionTests` | the description says what RFC 9293 §3.1 says |
| `TcpHandshakeTests` | two hosts, five segments, `nexaflow` across — each end reads what the other sent |
| `NtpCaptureTests` | three real captures decode and re-encode identically |
| `ModbusCaptureTests` | a fork whose arms are different lengths, inside what a length measures |
| `MqttCaptureTests` | six message formats, chosen by looking ahead; a varint at two widths |
| `DhcpCaptureTests` | a repetition ended by a sentinel, with bare codes and fill after it |
| `CoapCaptureTests` | options that only mean something in sequence, and both nibble escapes |
| `MdnsCaptureTests` | a name — labels ending at a label of length zero — inside a run of questions ended by a count, and a description that says in its octets what it does not cover |
| `BacnetCaptureTests` | three layers, a discriminator six octets in, and a value whose length is three bits of the octet before it |
| `DecodePathTests` | a reading that branches, loops, and stops where it may |
| `RepetitionTests` | written once per item, including a run inside a run |
| `AbsenceTests` | the four questions |
| `ConsistencyTests` | what a description has to be true of itself, refused when it loads |
| `StateUpdateTests` | what a message leaves behind, and what state is not for |

A host may set values and ask for a message, and that is the whole of its vocabulary. It cannot reach into
the run, place a field, or fix up octets — so anything that comes out right came out of the description.
