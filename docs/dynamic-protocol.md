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
| **Round-trips against real captures** | TCP (RFC 9293), NTP (RFC 5905), Modbus TCP, MQTT 3.1.1, DHCP (RFC 2131/2132) |
| **Authored but not yet built** | BACnet, CoAP, mDNS, SNMP, SSDP, TLS |
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
| `converted` | A member of the closed converter set, with its inputs on edges. |
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
| `requires` | **computation → input** | What it needs, with `sequence` and `facet`. |
| `decides` | forking node → computation | What picks the way on. `reading`. |
| `identifies` | forking node → field → … → field | A path read **ahead** of choosing, ending at the discriminator. Reading only. |
| `checks` | node → validator | What has to hold. |
| `assumes` | field → default | What it means when absent. |
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

Coming in, the reading goes round on a `decode` edge and finds out how many there were. Neither direction
has a count and neither needs one.

**A reading's rounds carry on past the repetition**, because nothing drives them but the edge that points
back — so the part after a run of options is settled on the walk's fifth appearance rather than its first.
Harmless (reaching for a node from a later round falls back to its only appearance), but a test looking for
that part by index will not find it. Resetting the round on the way out is *not* the fix: a loop's control
junctions are not members of the set they drive, so the walk is standing on one exactly when it is deciding
whether to go round again.

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
- **A field or set named in an expression must have a dot-free id.** `sets.connack.variableHeader.extent`
  parses as nested member access, so a set whose id contains a dot is silently not found and the
  expression quietly yields nothing. Ids of nodes nothing names in an expression — messages, packings,
  junctions, constants — may carry dots freely.
- **Describe the shape, not the catalogue.** RFC 9293 gives two option shapes — single octet, or
  kind-length-data — so a TCP option this file has never heard of round-trips. Enumerating known kinds
  refuses it.
- **If a specification would state it, it belongs in the description**; if it is about the correctness of
  handling the graph, it belongs in code. Ask this first of any new feature.

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
| `DecodePathTests` | a reading that branches, loops, and stops where it may |
| `RepetitionTests` | written once per item |
| `AbsenceTests` | the four questions |

A host may set values and ask for a message, and that is the whole of its vocabulary. It cannot reach into
the run, place a field, or fix up octets — so anything that comes out right came out of the description.
