# DynamicProtocol — the note before the graph rewrite

Written 2026-08-13, at 362 green tests, by the person who put the compensations in.

This is guidance for turning `Nexaflow.IO.Protocol` into a model where **everything is a typed node with typed
edges**, including computation. It is not a plan to start tomorrow. It is what to read first when the rope
runs out, and how to tell that it has.

## Where things stand

The engine encodes and decodes from a declarative graph, byte-exactly in both directions, against a corpus
of BACnet, CoAP, DHCP, DNS name compression, HTTP/1.1, mDNS, Modbus, MQTT, NTP, SNMPv2c, SSDP, TCP, TLS and
WebSocket, plus shape-only entries for layering and prefixed integers.

**The corpus documents are shape demonstrations, not faithful protocol descriptions.** This is deliberate and
was decided explicitly. An audit will report that eleven of them state almost none of their protocol's value
constraints; that finding is correct and is not a defect. Only convert a document to fuller fidelity if a
*shape* is wrong, as DHCP's was — its options could not represent a bare Pad and so mis-read every option
after one.

## What is already right, and must survive

- **The facet model.** Demand-driven over `Realised`/`Present`/`Extent`/`Position`/`Value`/`Emitted`, no
  passes, no fixed points, no back-patching. Ordering `Sized → Positioned → Valued → Emitted` failed on all
  ten original stress protocols because extent is a function of value for self-delimiting shapes. Do not
  reintroduce passes.
- **Identity is the object.** Not a name. Three bugs this session came from minting a fresh node where one
  was meant to be shared: `Root` and `Beside` not carried by the record copy constructor, and a test factory
  returning a new `MessageDef` per call so no transition matched by reference. **Any node a record owns must
  be copied explicitly in its copy constructor.**
- **One declaration, both directions.** Asymmetries are real but must be *sited* and declared —
  `Choice.Selects`, `Opaque.Length`, `Chain.Continues`. A second parallel path would let the two drift.
- **The RFC/engine rule.** If a specification would state it, it belongs in a document; if it is about the
  correctness of handling the graph, it belongs in code. This resolved several arguments cleanly and should
  be the first question asked of any new feature.
- **Canonicality.** One legal encoding of a value, others refused, in both directions. Currently three
  instances: minimal varints, a required letter case, and a value the document derives being compared with
  what arrived. A fourth is coming (omit-if-equal-to-default, which DER and protobuf require).

## Why the rewrite

Structure is a typed graph. **Computation is not** — it is `Expr` trees parsed from strings, opaque to the
graph, with dependency edges *recovered by pattern-matching the text*. Everything below exists only to bridge
that gap:

| compensation | what it stands in for |
|---|---|
| `Vocabulary` table (`ExprSite` → allowed roots) | "can this node reach that one" — a graph question |
| `ExprSite`, twelve hand-enumerated sites in four places | where an expression sits, which an edge would carry |
| `Roles` string constants | telling one node's several expressions apart |
| `Roles.Carrying(sort)` | telling one node's several *carries* apart |
| `FieldReferences` / `ContextReferences` / `PresenceReferences` | edge traversal, done by scanning text |
| `Refs`'s "`carried` is a root so add the dependency by hand" | a flow edge |
| `Optionals` map, for resolving `present.x` to a different node | an edge |
| `Named` / `FacetNamed` | one relation, spelled twice; they disagreed once |
| `Chain.Seed/Carry` **and** `Assorted.Seed/Arm.Carry` | one flow notion, spelled twice |
| the addressability exemption for a kind's carry | an edge that starts in the wrong place |

### Where it actually stopped

HPACK was attempted on 2026-08-13 and is the first thing the corpus could not finish. It reads
correctly — the RFC 7541 C.3 exchange decodes, the table is built across the run, a later component names
what an earlier one added, a resize evicts — and **it cannot be written back**. Two independent refusals,
both pinned by tests in `HeaderTableCaptureTests`:

- **A carry that branches declares every branch.** The chain's carry is one expression with a case
  analysis over four packings, and its dependencies are recovered by *scanning its text* for `fields.…`.
  The scan cannot see that a read sits under a conditional, so every field named becomes a prerequisite of
  every component and three quarters of them belong to packings that did not occur. The refusal is
  correct; what is wrong is that reachability is being approximated by a regular expression over source
  text. **Any chain carry that reads inside a choice hits this** — it is not specific to HPACK, and the
  corpus had simply never written one.
- **A bit group settles all at once.** A coded literal's length is measured off its body, and which
  packing the body is depends on the head's coded bit. Two runs of one octet pointing opposite ways, no
  run depending on itself — and a cycle, because the node is the *group*. `BitSlice.Value` let the runs
  come from different places and stopped short of letting them settle at different times.

Both are the same sentence: the thing that should be a node is not one. Neither has a fix inside the
present design that is not a new compensation — teaching the scanner about branches is a seventh
approximation of reachability, and per-arm carries on `Chain` is a twelfth sited expression plus a role
constant plus a vocabulary row.

**This is the trigger.** The note existed to say when the rope runs out; it has.

### The evidence, not the aesthetics

The vocabulary table has been **wrong twice in one session**, both times committing the exact defect class it
was built to prevent — a name bound at one scope-construction site and not another, reading as nothing and
making every comparison quietly false. Once by banning `inputs` from reader sites (the reasoning sounded like
the `room` ban and was its opposite: `Decode` takes a scope and always did), once by not covering
`Transition.When` at all. A structure that keeps committing the error it exists to stop is standing in for
something else.

Threading now needs its notion spelled in five places. Two containers is coincidence; the third will not be.

"What names does this expression need" was implemented **twice** — `Vocabulary.RootsOf` and
`Expr.FreeRootNames` — and the copy the vocabulary check used knew about `let` and not about lambda
parameters. So bounded iteration was unusable at every site the table governs, and had been since the day
the table landed: an ordinary fold reported three unknown roots. Nothing caught it because `map`/`fold`
had only ever been written inside a transform, which uses the other one. `RootsOf` now delegates. The
lesson is not "fix the copy" — it is that a question with two answers in one codebase will be answered
differently, and this one is *exactly* the question the rewrite makes structural.

## What the target looks like

**Keep `Expr.Parse`. Change what it produces.** The string syntax is good and authoring a graph by hand would
be miserable. The change is that parsing yields *nodes in the graph* rather than a tree the evaluator walks
privately. Then:

- A dependency is an edge, not a regex over text.
- Availability is "can this edge be drawn", not a table.
- An intermediate has an identity, so `.As("name")` is free and the chain-thread naming hack disappears.
- Flow between components is one edge type that does not care whether the container repeats one shape or
  several kinds.

`ExprSite`, `Roles`, `Vocabulary`, the three `*References` scanners and both threading parameter sets all
delete.

### Order

1. **Give `Expr` sub-terms node identity** and register them in `ProtocolGraph`, with the evaluator still
   walking them. Pure representation change; all 362 tests should stay green. If they do not, stop — the
   failure is telling you something about identity.
2. **Replace the scanners with edge queries.** `LinkReads` becomes traversal.
3. **Retire the vocabulary table**, deriving availability from reachability. Do this *after* step 2 so the
   edges exist to reason about.
4. **Collapse threading into flow edges**, deleting `Chain.Seed/Carry`, `Assorted.Seed`, `Arm.Carry`,
   `Roles.Carrying` and the addressability exemption.
5. **Delete `ExprSite` and `Roles`.**

### Cheap thing to do first, whether or not the rewrite happens

Derive the vocabulary table's claims from the scopes instead of asserting them: build each scope, compare
what it actually binds against what the table says. Both of this session's failures would have been caught.
It is small, it does not presuppose the rewrite, and it protects step 3.

## Traps, learned the hard way

- **A check that can never fail.** CoAP has an invariant that became unfalsifiable when the width started
  being *written* from the nibble rather than *compared* against it. Kept, because the sentence is still one
  the specification says — but say so in the comment, or the next reader will think it guards something.
- **A facet declared and never produced.** `Emitted` sat in `AllFacets`, marked not-applicable everywhere,
  for as long as it took someone to want a checksum. Do not leave a slot carved out and empty.
- **Captures are not fields.** A chain's thread and a bit run bind as captures; a field expression naming one
  is refused as out of scope, which is correct. Do not "fix" it by making them fields.
- **Test corpora are written by the same hand as the engine.** DHCP's Pad defect and CoAP's escape-width
  defect were both invisible to the real captures. The prose-versus-RFC audits found things the captures
  structurally could not.
- **Subagents will edit files you told them to read.** One silently changed a BACnet zero rule from `empty`
  to `oneByte`, contradicting the comment directly above it and breaking the round trip. Diff before trusting.

## What is left undone

- **HPACK's encode side**, blocked as above. The decode side, both tables, the prefix integer and the
  eviction fold are done and green; `HeaderTableCaptureTests` carries the working half and pins the two
  refusals so they flip when the rewrite lands. The forecast in the previous version of this note was
  wrong in two ways worth recording: the N-bit prefix integer needed **no new pattern** (a bit group whose
  runs come from different places, a choice on whether the prefix saturated, and the existing continuation
  integer), and eviction needed **no fold converter** — `fold`, `scan` and `takeWhile` were already in the
  expression language. What HPACK actually cost the engine was one converter pair, `packBits`/`unpackBits`,
  and even that takes its 257-row table from the document.
- **A record literal in the expression language.** There is still no way to build a `Rec`, so HPACK's table
  entries are two-element lists. Fine, but it is why `flatten(list(list(…), carried))` is how a document
  prepends to a list — worth a `prepend` or a record constructor either way.
- **`Context.Fixed` declares a value nothing reads.** The document states the constant and the caller
  supplies it anyway. Either the codec should bind it or the kind should stop carrying a value.
- **SIP**, blocked on there being no clock. Also wants composite instance identity — `Subject.Distinguishes`
  names one concept and SIP pairs on three.
- **Sub-octet alignment.** `Pattern.Bits` refuses a group that is not a whole number of octets. Nothing in
  the corpus is bit-oriented, so we have never learned whether that refusal is a correct guard or a
  limitation wearing one. No protocol on the current list would tell us.
- **A per-connection layering seam.** `Subprotocol` hangs off a field, so "everything after this on this
  connection is another protocol" can only be said by making the switched stream a trailing field. Works;
  wants WebSocket-after-upgrade to show the right shape.
- **USB descriptors and QUIC** were on the list and are now largely subsumed. Drop unless wanted for coverage.
