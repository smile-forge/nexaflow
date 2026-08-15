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

**TCP is the case that will demand it.** Today's handshake test keeps sequence numbers in the *host*
(`state.sequenceNumber` set by the test harness). That is honest for a harness and wrong for a protocol:
which segments are legal in SYN-SENT is a fact about TCP.

## 2. What the ten-protocol corpus is for

The corpus was assembled *before* the engine, from ten protocols chosen so each defeats a grammar in a
different way. The first grammar failed all ten — 182 gaps, 52 blocking; it could not encode five and could
not decode nine. Finding that on paper cost a day instead of a rewrite.

| Protocol | What it defeats |
|---|---|
| **NTP** | nothing much — the smallest honest test. Fixed 48 octets, one octet split 2\|3\|3, a trailer present only when the association is authenticated |
| **Modbus TCP** | a fork whose arms are different lengths, inside what a length field measures; and a request and response that carry the same function code, so direction is not on the wire |
| **CoAP** | option deltas that accumulate across options, so decoding option *N* depends on 0..*N*−1 |
| **mDNS / DNS-SD** | name compression — pointers are absolute offsets into the whole message |
| **MQTT** | *(built)* a varint length header; connect-flag bits that decide whether later fields exist at all — and, unforeseen when the corpus was written, the first protocol with several message formats, told apart by a nibble inside the message |
| **DHCP** | options including a bare Pad, which the first attempt could not represent — and so mis-read every option after one |
| **SNMPv2c** | nested BER lengths |
| **SSDP** | delimiter-terminated text; no widths and no length prefixes anywhere |
| **TLS 1.2** | three stacked length-prefixed layers, and a hello with no extension block at all |
| **BACnet/IP** | three stacked sub-protocols |

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
  "expected Int, got Null" inside a base-128 converter. Worth fixing at the root: an expression reaching
  under `fields.` or `sets.` for something the computation has no edge to should say so, by name.
- **Blind line-number edits cut across method boundaries.** Twice. Read, then edit.

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

## 5. Known gaps, as of 2026-08-15

- **Seven protocols to author.** Hold SSDP back — it is delimiter-terminated text, and `WireForm.Opaque`
  lost the `Until` delimiter when `Pattern` was deleted. That capability needs rebuilding before SSDP or
  HTTP.
- **A discriminator is read but not recorded.** `identifies` answers which message this is and then throws
  the answer away; the message re-reads it, which is right, but nothing keeps a note that the *protocol*
  looked. Harmless today because every message here carries the discriminator itself. A protocol whose
  discriminator is not part of any message would have nowhere to put it.
- **Nothing checks that a discriminator's keys are exhaustive or disjoint.** Two messages keyed on the same
  value load happily and the first one wins.
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
