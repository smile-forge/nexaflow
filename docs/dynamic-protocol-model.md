# DynamicProtocol — the graph model

Supersedes the message/state/fact split in [dynamic-protocol-grammar.md](dynamic-protocol-grammar.md) and
[dynamic-protocol-state.md](dynamic-protocol-state.md). Those described three mechanisms — a message
template language, a state machine, and a fact list — where there is **one graph with several projections**.

Status: **under falsification.** Nothing here is settled until all ten corpus protocols are expressed in it.

---

## 0. Principles

**P1 — One graph.** Messages, wire segments, semantic entities, states and observations are nodes in a
single graph. Ordering, containment, derivation and provenance are edges. A "message definition", a
"state machine" and a "fact table" are *queries* over that graph, not separate artefacts.

**P2 — Notions only, never protocol specifics.** The model may name *ideas* — length-prefixed,
interchangeable, escaped-inline, back-reference — and may never name a protocol. If a `DHCP`, `CoAP` or
`BER` element ever appears in the engine, the generalisation has failed. This is enforced mechanically
(§9), not by convention.

**P3 — Entities are shared; segments are per-occurrence.** Two messages carrying the same device id
reference **one** entity node through **two** segment nodes. This is the difference between a graph and a
pile of templates, and it is what lets the AI discuss the protocol with a user rather than recite fields.

**P4 — Ordering is explicit and partial.** A packing is a partial order with constraints, not a list. Some
segments are strictly sequenced, some are interchangeable, some optional, some repeated.

**P5 — Structure is direction-neutral.** Decode is pattern-matching against the graph; encode is
resolution over it. One structure, both directions — never two descriptions that must be kept in step.

**P6 — Two state spaces.** Local (what we intend, what we hold) and remote (what we believe the peer
believes). A transition in one can drive the other. Epistemic status — confirmed vs presumed — is
*orthogonal* to which space a state lives in; the earlier design collapsed those two axes into one.

**P7 — The run graph and the protocol graph are different artefacts over the same primitives.** A run is
what happened once. A protocol is what can happen. Induction from the first to the second is deferred, but
the model must never make it impossible by conflating them.

---

## 1. Nodes

| Node | What it is |
|---|---|
| `Entity` | A semantic thing the protocol talks about — a device id, a relay state, a session token, a lease time. Carries an ontology reference and a value domain. This is the AI's vocabulary. |
| `Segment` | A region of wire. Has an extent, a resolution state, and exactly one shaping `Pattern`. Occurs in one packing at one place. |
| `Pattern` | A generic shape (§3). Shared across every protocol that exhibits it. |
| `Message` | A named exchange unit. Owns no fields — it is the head of one or more `Packing`s. |
| `Packing` | One concrete arrangement of segments for a message, guarded by a state predicate (§4). |
| `Slot` | A position within a packing: one segment, an interchangeable set, an optional, a repetition, or a choice. |
| `State` | A node in either the local or the remote state space (§5). |
| `Transform` | A named, pure conversion between an entity value and a segment value, or between entity values. The `MapRoomToId` in the worked example is one. |
| `Observation` | A reified assertion: this entity had this value, per this source, at this time, with this confidence. Provenance lives here (§6). |

A node never encodes a protocol name. `Pattern` instances are shared: the same length-prefixed pattern
node serves every protocol that length-prefixes something.

## 2. Edges

Edges carry properties; parallel edges are meaningful (that is how multi-source provenance works).

| Edge | From → To | Properties |
|---|---|---|
| `packs` | Message → Packing | guard (state predicate) |
| `slot` | Packing → Slot | ordinal |
| `holds` | Slot → Segment | optionality, repetition source |
| `shapedBy` | Segment → Pattern | pattern parameters |
| `carries` | Segment → Entity | transform, direction |
| `precedes` | Slot → Slot | strict ordering |
| `interchangeableWith` | Slot ↔ Slot | symmetric; order is free within the group |
| `derives` | Segment → Segment | kind: `lengthOf`, `countOf`, `checksumOf`, `offsetOf`, `presenceOf` — **the resolver's dependency edges** (§7) |
| `contains` | Segment → Segment | sub-segment (a bit slice, a nested structure) |
| `transitions` | State → State | trigger (message sent/received/timeout), guard, effect |
| `influences` | State → State | cross-space: a local transition drives a remote belief, or vice versa |
| `assertedBy` | Observation → Entity | source, confidence, observedUtc, ttl, supersededUtc |
| `sourcedFrom` | Entity → Message | which exchange yields this entity's value |

## 3. Patterns — the recogniser library

Patterns are **notions**, and they are the prior that makes induction possible later. Each is a
recogniser (decode) and a producer (encode) for the same shape.

| Pattern | Parameters | Notion |
|---|---|---|
| `Scalar` | width, endianness, signedness | a fixed-width number |
| `BitSlice` | offset, width, within | a run of bits inside an octet group |
| `LengthPrefixed` | length pattern, covers (`bodyOnly` / `selfAndBody` / `fromRegionStart`) | a length that governs a following region |
| `EscapedInline` | inline max, escape table (sentinel → wider pattern, bias) | a small value inline, escaping to a wider encoding past a threshold |
| `Continuation` | bits per unit, order | a value spread across octets with a continue flag |
| `TypeLengthValue` | tag pattern, length pattern, bare tags | tag-then-length-then-value, nestable |
| `Delimited` | delimiter, consume | a region ending at a marker |
| `Terminated` | terminator | a sequence ending at a sentinel element |
| `FixedWidth` | width, fill, alignment | padded to a constant size |
| `Repeated` | element, count source | n occurrences, count from anywhere resolvable |
| `BackReference` | target resolution, direction | a pointer to an earlier region, resolved by offset or identity |
| `Checksum` | algorithm, span | a digest over a declared span |
| `Enumerated` | value → meaning map | a coded value with named meanings |
| `Padding` | to, fill, side | alignment filler |
| `Opaque` | — | bytes we can carry but not interpret |

Two consequences worth noting, both evidence the abstraction is at the right level:

- **`EscapedInline` covers both a nibble that escapes to a wider field and a length with short/long
  forms.** Two protocols that look unrelated exhibit one notion.
- **`BackReference` covers name compression without knowing what a name is** — the notion is "a pointer
  to an earlier region", and whether that region is a domain name is the entity's business.

## 4. Packing — ordering as a partial order

A `Packing` is selected by a guard over state, and arranges slots. Slot kinds:

```
Slot := One(segment)
      | Group(slots…, order: strict | free)      // free = interchangeable
      | Optional(slot, presence)                 // presence resolvable from the graph
      | Repetition(slot, count)                  // count resolvable from the graph
      | Choice(slots…, discriminator)            // exactly one, selected by a resolvable value
```

The user's case is direct: message `A` has two packings, guarded on state.

```
A @ state1 := Group[ One(s1), One(s2), One(s3) ], order: strict
A @ state2 := Group[ One(s1), Group[ One(s2), One(s3) ] order: free, One(s4) ], order: strict
```

**Encode** picks the packing whose guard holds, then linearises: strict groups in ordinal order, free
groups in any order the resolver finds convenient (canonically, ordinal, so encoding is deterministic).
**Decode** matches: strict groups in sequence, free groups as a set — a segment matched out of ordinal
order is legal and records which order was seen, so re-encode can reproduce it if asked.

A free group is precisely where byte-exact re-encode is *not* guaranteed and semantic round-trip is. That
distinction now falls out of the model instead of being a hand-maintained list of exceptions.

## 5. State — a tree for addressing, a DAG for lifetime

Full specification: [dynamic-protocol-state.md](dynamic-protocol-state.md). This is the summary.

**State is a tree of scopes for *addressing* and a DAG of anchors for *lifetime*.** Those were one edge in
the first version of this section, and that conflation is the defect all six stress protocols broke on —
three of them declaring the model unusable as written. One edge was being asked to carry naming, lifetime,
key uniqueness and gating at once.

Each scope holds up to one machine per state space; a scope may be *keyed*, in which case many instances are
live at once; an instance's death propagates along its **anchors**, and an instance's default anchor is its
containing scope — which reproduces the cascade guarantee exactly, while making every exception **declared
and validated** rather than impossible.

The tree itself survived: six of ten protocols need state keyed finer than a connection, one peer can hold
255 concurrent transactions, and a single machine per peer expresses none of it.

Falsification against the corpus killed the previous flat design outright: six of ten protocols need state
keyed by something finer than a connection. BACnet can have **255 live transactions against one peer**;
Modbus needs `(connection, unitId)`; SNMP correlates per request-id with several outstanding; CoAP matches
on message-id *and* token at different times; MQTT has a packet-identifier space; NTP echoes an origin
timestamp. A single machine per peer cannot express any of them.

### 5.1 Attribution — "ours" and "theirs" are queries, not containers

A state value is a **proposition**, and what makes it ours or theirs is **who holds it**:

```
Assertion { proposition, holder, confidence, since, ttl?, supersededBy? }
holder := Us | Peer(id) | ThirdParty(id) | Unheld
```

The three "spaces" are then projections over attribution, not places to put things:

- **Local** — assertions held by `Us`.
- **Remote** — assertions we record as held by `Peer`. We change them only by inference or by being told.
- **World** — `Unheld` or `ThirdParty`: a proposition the protocol *causes* that no participant observes —
  a notification the peer emits to others on our behalf, a physical outcome. Two corpus protocols hold one
  that decides their next action and belongs to neither party; with only two spaces it has nowhere to live,
  and because guards read the configuration rather than the observation store it is not merely unrecorded,
  it is **unguardable**.

The same variable can carry concurrent assertions from different holders, and their disagreement is the
information:

```
transport.state:  Us  → open           (confidence: asserted)
                  Peer → presumedDead  (confidence: likely, since: …)
```

That is the half-open transport, and expressing it is the most valuable thing this part of the model does.

**Two consequences worth stating.** First, this is the *same* mechanism as an `Observation` (§6) — holder,
confidence, freshness, supersession — so state and facts stop being two systems that must be kept in step.
Second, it kills the duplication that one-tree-per-space forced: every object with a local face and a
believed-remote face previously had to be declared twice and kept aligned by convention, and four of six
stress tests independently proposed the same `mirrorOf` patch, which is the tell that the shape was wrong.

**The asymmetry claim is withdrawn.** "Only Remote carries epistemics" was wrong in both directions. Local
means *ours*, and ours is not the same as *known*: our accumulated results, our retained history, the
resources we hold, and our outcome after giving up are all held by us and all genuinely uncertain. Only our
intent at the instant we form it is certain — one transition, not a subtree.

### 5.2 A scope

```
Scope {
  id
  contains?  : Scope        // ADDRESSING parent. Absent ⇒ root. Forms a tree.
  identity?  : Identity     // present ⇒ keyed; many instances live at once
  begins     : [ Trigger… ]
  anchors    : [ Anchor… ]  // LIFETIME. Defaults to [{ on: contains, then: retire }]
  local? remote? world? : Machine
  frame      : [ Binding… ] // entity values this instance holds
}
```

`contains` gives naming, path addressing and the key-uniqueness domain, and nothing else.

### 5.3 Lifetime is a node, not a property

There is no single notion of "lifetime". A message has one, a transaction has one, a connection has one,
and an *engagement* — a user or application's participation — has one that appears in no message and spans
many connections. These are different objects that **bound** one another: when a connection ends a
transaction under it usually ends too, but not definitively, and "usually" is exactly the thing a built-in
cascade rule cannot express.

So `Lifetime` is a first-class node, and bounding is an edge with **declared** closure semantics:

```
Lifetime {
  id
  opens   : [ Trigger… ]      // a message edge is the first-class case; local intent and timers also
  closes  : [ Trigger… ]      // empty ⇒ perpetual, and that must be written, not defaulted into
  bounds  : [ Bound… ]
  lingers?: Duration          // identity stays reserved after close (§5.3a)
}

Bound { of: Lifetime, onClose: ends | suspends | detaches | independent, when?: Guard }
Binds { lifetime: Lifetime, target: Scope | Machine | Entity, onClose: [ Transition… ] }
```

**A message opens a lifetime, as an edge.** That is better than a trigger buried in a scope for two
reasons: "what does this message open?" becomes a graph query, and — the reason it matters later — a run
graph can *observe* a message and record which lifetime it opened, which is the raw material induction
needs. Closure is symmetric but not required to be a message: timers and local decisions close lifetimes too.

**Closure cascade is graph-driven, not built in.** `onClose` on each bound says what this particular
bounding relationship does, so a protocol declares its own semantics rather than inheriting a rule:

- `ends` — the default, and what reproduces the previous cascade guarantee exactly.
- `suspends` — revocable. The only honest response to a cascade triggered by a *presumed* state: a belief
  must not irreversibly demolish confirmed state, and a peer that was merely slow has to be recoverable.
- `detaches` — survives, and may be re-bound later. A counterparty replaced under a live allocation is not
  a lapse and must not read as one.
- `independent` — the bound is informational; closure does not propagate.

`when` guards make the runtime-variable case a handler rather than a second tree: a lifetime whose
relationship to the transport is decided by one negotiated bit declares two guarded bounds, and its
addressing position never moves.

**Closure is a trigger, and `Binds.onClose` carries the transitions.** Ending a peer's lifetime while 255
exchanges are bound to it is 255 *outcomes*, not 255 deletions — and they differ: a read becomes `unknown`,
a write becomes `possiblyApplied`, because the peer may have executed it and lost the acknowledgement.
Deleting both identically destroys exactly the distinction the model exists to preserve.

**What this buys beyond the previous shape:** a protocol may define whatever lifetime wrappers it needs —
an engagement, a lease period, a subscription epoch — with its own opening and closing semantics, without
any of them having to become a node in the addressing tree. Scope keeps naming; Lifetime keeps time.

**Totality is validated.** Every bound declares an `onClose`; every lifetime either declares a closing
trigger or is explicitly perpetual. The guarantee the tree previously got from its shape — nothing is
silently forgotten — now comes from validation, which is strictly better because the exceptions are visible
rather than unrepresentable.

> Not yet falsified. The tree-with-anchors design this replaces was broken by all six stress protocols;
> this generalises it rather than contradicting it, so the risk is lower — but "lower" is not "checked".

### 5.3a Phases

A lifetime is in exactly one phase, orthogonal to any state bound to it:

```
pending → open → lingering → closed
            ↘ suspended ↗
```

`lingering` needs no new object — it is `closed` with a duration. Identity stays reserved against
reallocation, correlation still resolves to it, and declared transitions still fire. A bound closing does
**not** cascade a lingering lifetime away, because the reason it lingers is precisely that late traffic can
still arrive.

That single phase closes, with no per-protocol rule: identifier reuse after wrap, non-idempotent request
replay, "a response with an unrecognised identifier must be silently discarded" (implementable only if
retired identifiers stay recognisable), and the reconnect hazard where a cascade frees a whole identifier
space at once and a straggler is accepted as the answer to a different question.

### 5.4 Configuration, and the extractable subgraph

The **configuration** is the set of `(live scope instance → current state)` across both trees. It is what a
`Packing` guard is evaluated against, and it is what makes *"a unique subgraph is extractable from a given
state"* well defined: given a configuration, the reachable subgraph is exactly the messages whose packings
have a satisfied guard.

Addressing is by path, with the current frame implicit:

```
remote.connection.authenticated                       // absolute
transaction.awaiting                                  // relative to the frame being resolved
remote.connection.transaction[capture.invokeId]       // an explicit instance
```

### 5.5 Message legality reads the tree

The three legality relations all take a **scope**, which is the thing they previously lacked:

- `validIn(paths…)` — legal only when the configuration satisfies these.
- `requires(message, within: scope)` — mandated chaining, *within this transaction* rather than
  ever-at-all. Previously unscoped and therefore useless for any protocol with concurrent exchanges.
- `excludes(message, within: scope)` — never together, within a named scope and window.

### 5.6 Correlation — the scope is the unit, the key is not the whole mechanism

The scope is the right unit and that survives. **The claim that the key alone is the whole of correlation
is withdrawn** — it is roughly 60% of it, and the missing part matters:

- A key needs a **namespace and a direction**. Two identifier spaces can carry the same numeric value at
  the same time for unrelated exchanges, and direction is frequently in no field of any message.
- A key needs a **reservation window** past completion, or a straggler is matched to the wrong exchange
  (§5.3a).
- Some correlation is **two independent keys at different times**, not nested keys.

That last point is a correction to this section, not a refinement of it. The previous text told a document
author to nest one identifier scope inside the other — and the stress test traced the consequence: the
inner exchange completes, cascade retires the outer, and a response that legitimately arrives later has
nothing to match against. **The model instructed the author to build a bug.** Two identifiers with
independent lifetimes are *siblings*, both anchored to the exchange they serve, not one inside the other.

| Protocol | Correlated by | Shape |
|---|---|---|
| SNMP | request-id | one keyed scope |
| Modbus | transaction id under unit id | genuinely nested |
| CoAP | message-id **and** token | **siblings**, different lifetimes — not nested |
| MQTT | packet identifier | keyed, plus a direction facet the wire never carries |
| BACnet | invoke id | keyed, with a lingering window for late replies |
| NTP | echoed origin timestamp | keyed on a generated nonce |

Still one notion across six protocols, which is the evidence the unit is right. What changed is that the
notion is *the scope with its identity, namespace, direction and reservation window* — not a bare key.

### 5.7 Cross-tree influence

`influences` edges cross the two roots and are directional:

- Local → Remote: we sent credentials, so we now *presume* the peer considers us authenticating.
- Remote → Local: a rejection tells us the peer's belief, and we move our own state to match.

A Remote transition driven by inference is `presumed`; one driven by an actual message is `confirmed`. That
distinction is carried, not computed, and it is what lets a guard say *"only if we have actually been told"*.

## 6. Observations — provenance on edges

An `Observation` is a reified edge: `Observation --assertedBy--> Entity`, carrying source, confidence,
timestamp, TTL and supersession. Parallel observations on one entity **are** the provenance; conflict is
visible rather than squashed, and "3 sources" is a graph query, not a bookkeeping field.

This replaces the flat fact list. It is strictly more expressive and it keeps the claim attached to the
thing claimed, in context, rather than in a side table keyed by a string.

## 7. Resolution — n-stage, driven by the graph

Encode is a worklist over segment resolution state:

```
Unresolved → Sized → Positioned → Valued → Emitted
```

A segment advances when its prerequisites — read from `derives` and `contains` edges — have advanced far
enough. A checksum needs its span *valued*; a length needs its body *sized*; a back-reference needs its
target *positioned*.

```
while (progress) {
    progress = false
    for each segment not Emitted
        if prerequisites satisfied { advance(segment); progress = true }
}
if any segment not Emitted → cycle, naming the unresolved set
```

Three properties this buys over the two-pass-plus-capped-iteration scheme it replaces:

1. **No pass count to tune, and no runtime iteration cap.** Completion is resolution, not a budget.
2. **A cycle is a validation error again**, detected as "a pass resolved nothing new", and reportable as
   the exact set of mutually-blocked segments.
3. **"Is this evaluable on the encode side?" becomes a graph reachability question** rather than a list of
   hand-written rules — which is what dissolves the contradictions the review found in V9/V10/V11.

Decode is the same worklist with the prerequisite relation reversed: a segment is matchable when its
position and extent are determined.

## 8. Run graph vs protocol graph

Same node and edge kinds; different granularity and use.

| | Run graph | Protocol graph |
|---|---|---|
| Scope | one exchange that happened | everything that can happen |
| Segments | concrete, with observed bytes | shaped by patterns, extents possibly symbolic |
| Packings | the one that occurred | all variants with guards |
| States | the path taken | the full space |
| Entities | bound values | domains, with external inputs and outputs |

A protocol graph reaches **outside** the protocol at both ends: an `Entity` may be an *input* (a room name
the user supplies) or an *output* (a light is on). Neither appears in any message; both are linked through
`Transform` and `sourcedFrom` edges. That chain is the worked example:

```
Entity "room name"            (input, from the user)
  --transform MapRoomToId-->  Entity "room id"
      --sourcedFrom-->        Message GetInformation
  --carries-->                Segment s1 of Message ChangeState   (octet 0)

Entity "power state"          (output, observable)
  --carries-->                Segment s2.bit4 of Message ChangeState
      --contains-->           Segment s2                          (octet 1)
```

Neither entity is a field. Both are reachable from the graph, which is what lets the assistant say
*"turning on the kitchen light means setting bit 4 of the second octet, and I need to look the room id up
first"* — and what a template representation cannot say at all.

Induction (run graph → protocol graph) is **deferred**. The model exists so that it stays possible:
generalising requires the general to be representable separately from the specific.

## 9. The generalisation guard

P2 is enforced, not trusted. An architecture test scans the engine assembly — type names, member names,
string literals, enum members — for any protocol name, and fails naming the offender. The corpus supplies
the vocabulary to look for.

A protocol's specifics live in **documents and pattern parameters**, never in the engine. If a notion can
only be described by naming a protocol, it is not yet a notion, and the right response is to find the
general shape or leave the protocol unexpressible and say so.

---

## Falsification

The model is not settled. It is settled when all ten corpus protocols in
[`Protocol/Corpus/`](../src/Nexaflow.Tests/Nexaflow.Tests.Features/Protocol/Corpus) are expressed in it —
every capture decoded and re-encoded — and the generalisation guard passes. The byte grammar failed 10/10
on its first attempt; the honest expectation is that this does too, and the point is to find out cheaply.
