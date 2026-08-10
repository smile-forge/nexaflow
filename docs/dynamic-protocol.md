# DynamicProtocol

A declarative protocol engine. A protocol is a **JSON document**, not code: the document describes how a
method call with parameters becomes bytes on the wire, and the engine has no per-protocol code path at all.

The point is that the AI can *learn* how to talk to a device — draft a document, see the exact bytes it
would emit without sending anything, refine, test once under approval — and a document the user then trusts
becomes a **button on that device's page**. Smart plugs through to HVAC, without a build.

## Status

**Prototype, under construction.** Expression core, transform language, facet resolver and the field codec
are built; two of the ten corpus protocols round-trip byte-exactly. Variable-width fields (`varint`, `lp`)
are next, and step 6 is the go/no-go gate.

| Document | What it is |
|---|---|
| [dynamic-protocol-grammar.md](dynamic-protocol-grammar.md) | §1–§4, §6, §7 — document shape, layers and framing, the `bytes` field grammar, the layout algorithm, round-trip tiers, expressions and the closed converter set, validator rules |
| [dynamic-protocol-state.md](dynamic-protocol-state.md) | §5 — the protocol state machine: which messages are legal when, mandated chaining, mutual exclusion, confirmed vs presumed state, negotiated capability, counters, reassembly, retry |
| [dynamic-protocol-review.md](dynamic-protocol-review.md) | The adversarial review of both, with the build order and the open blockers |

## Why it looks like this

The first version of the grammar was written from first principles and looked reasonable. It was then
stress-tested against **ten real protocols** — SNMPv2c, mDNS/DNS-SD, Modbus TCP, CoAP, DHCP, MQTT 3.1.1,
NTPv4, SSDP, TLS 1.2 and BACnet/IP — chosen so that each defeats a grammar in a different way.

It failed all ten. 182 gaps, 52 of them blocking; it could not *encode* five and could not *decode* nine.
The captures are checked in at
[`src/Nexaflow.Tests/Nexaflow.Tests.Features/Protocol/Corpus/`](../src/Nexaflow.Tests/Nexaflow.Tests.Features/Protocol/Corpus)
— 30 packets, 2,603 bytes, every byte accounted for by exactly one field, guarded by
`ProtocolCorpusIntegrityTests`.

The three findings that reshaped the design:

1. **Decode is not a pure per-field fold.** CoAP option deltas accumulate across options, so decoding
   option *N* depends on 0..*N*−1. DNS name-compression pointers are absolute offsets into the whole
   message. MQTT connect-flag bits decide whether later fields exist at all. The grammar needed a decode
   *context* with a region stack and fold registers, not a cursor.
2. **Encoding needs a layout graph, not two passes.** One SNMP document must emit both a 73-byte
   all-short-form request and a 337-byte response with mixed `81`/`82` long-form lengths. Nested BER
   lengths make widths mutually dependent; the resolution is a dependency graph over spans with
   topological ordering, and a cycle is a *validation* error.
3. **Byte-exact re-encode of arbitrary non-canonical wire bytes does not compose.** It was specified,
   reviewed, and cut — it conflicts with the layout graph and silently corrupts position-dependent forms
   (a traced example emitted a structurally valid, semantically wrong mDNS packet). See §0.1 of the
   grammar for what it would cost.

## Round-trip tiers

| Tier | Guarantee | In v2 |
|---|---|---|
| **T1** semantic | `decode(encode(x)) ≡ x` on the capture model | required for every message |
| **T2** canonical | `encode(inputs)` produces the exact bytes of a canonically-encoded capture | required for every canonical corpus capture |
| **T3** byte-exact replay | re-emit arbitrary non-canonical wire bytes unchanged | **out of scope** — see above |

## Safety

The engine assembly ([`Nexaflow.IO.Protocol`](../src/Nexaflow.IO.Protocol)) has **zero project references
and no socket API in scope**. It produces `byte[]` plus a send *intent* and consumes bytes from a transport
it is handed. A protocol document — including one an LLM wrote — cannot reach the network from here even by
accident, because there is nothing to reach. Containment is structural, not procedural.

Everything that actually touches a socket goes through `NetworkGuard` in
[`Nexaflow.IO.Network`](../src/Nexaflow.IO.Network): target allow-list (only devices on a locally attached
prefix, or an address the user typed), volume ceilings the document cannot raise, trust states bound to
content hash, and an append-only audit log. An unreviewed AI draft never broadcasts, never elevates, gets
one target and a handful of packets.

## Open blockers

The review returned **NOT READY** on the full spec. The blockers are almost all *seams* between the two
documents, which were authored in parallel:

1. Converters used by §5 that §6.4 deletes (`has`, `cmp`, `since`, `key`, `keys`), plus `lookup`'s
   argument order.
2. Reassembly and framing specified twice, incompatibly (grammar §2.5 `reassemble` vs state §5.14
   `assemble`).
3. §5.19's required amendments to §6 never applied.
4. The **step graph** — the run-level document — is referenced by both and specified by neither.
5. The published `labelSeq` expansion is wrong in three ways and does not produce the mDNS bytes.
6. `emits` survives in grammar §1 but is retired by state §5.9.3.
7. `present` is not stated to bind `false` when `when` is false, which fails the T1 case it exists for.

**Steps 1–13 of the build order are grammar-only and unblocked by all of these.** Only step 14 (the state
model) is gated. The five grammar-side corrections needed along the way are listed at the end of the review.

## Build order

Each step is independently testable against a named capture, earliest-falsifying first. Full table in the
[review](dynamic-protocol-review.md).

| # | Build | Validated by |
|---|---|---|
| 1 ✅ | Expression core: precedence, `\|>`, `pow`, Int/Number/Bool + coercion, paths, converters | unit, against real capture values |
| — ✅ | **Transform language**: `let`, lambdas, `map`/`fold`/`scan`/`filter`, `range(n, max)`, declared domains | first-arc merge + base-128 as documents, agreeing byte-for-byte with the engine |
| — ✅ | **Facet resolver**: `Realised`/`Present`/`Extent`/`Value`/`Emitted`, demand-driven waiters, five diagnostics | synthetic hard cases incl. under-expansion |
| 2 ✅ | Scalars, signed scalars, bit groups, opaque spans; decode | **NTP A+B decode, T1** |
| 3 ✅ | Encode path through the resolver; dependencies derived from expressions | **NTP A+B re-encode, byte-exact T2** |
| 4 ✅ | Regions, `Choice` with masked keysets, `Repeat`; realisation-driven encode | **Modbus ×3, T2** |
| 5 | varint framer + field | MQTT ×6 |
| 6 | `lp` + `LenSpec` + `group` + nested lengths | **SNMP 73 B + 337 B from one document — go/no-go gate** |
| 7 | `fold` on the decode side, `present` | CoAP ×3, T2 + the empty-vs-absent T1 pair |
| 8–13 | sugar, offset tables, text path, checksums, deep nesting | DHCP, mDNS, SSDP, NTP-C, TLS, BACnet |
| 14 | the step graph, then the state model | needs the seams closed first |

**First real capture through the whole stack** (2026-08-10): both 48-octet NTP captures decode into named
values and re-encode to the exact original octets, through document → expression core → converters →
pattern library → facet resolver → codec. Nothing in that path names a protocol.

**First branching, framed and repeating message** (2026-08-10, step 4): all three Modbus captures — a
12-octet request, a 15-octet success carrying three registers, and a 9-octet exception — decode and
re-encode byte-exactly, with `length` and `byteCount` **withheld from the encode inputs** so a value that
was echoed rather than derived would fail. The two responses come from one declaration.

## What building step 4 settled

Four things the specification left open, or got the emphasis wrong on, became decidable once there was a
codec to try them in.

**A framer is not a declaration.** §2.3 already says the field template owns every byte and a framer is a
decode-side boundary oracle; the implementation goes one step further and has no framer construct at all.
A length prefix *is* an ordinary field whose value is a region's extent (`fields.body.extent`), and finding
where a message ends in a byte stream is a transport concern that never reaches the codec. So the question
Modbus-G1 asked — do the template and the frame object both own offsets 4–5, and does encode emit 10, 12 or
14 octets? — cannot be asked, rather than being answered.

**Exhaustiveness is decidable, not a matter of declaring a default.** Where a discriminator is a mask
(`fields.functionCode.value band 0x80` — how a flag bit packed into a type octet is almost always written),
the reachable keyset is computable: `{0, 0x80}`. So "you have not handled 0x80" and "this arm can never be
selected" and "this fallback is unreachable" are all *validate-time* errors. A fallback is required only
where the keyset cannot be computed, and is rejected as dead where it can. The paired-guard emulation could
never do this: nothing checks complementary `when`s for overlap or coverage, which is how a message with an
unanticipated discriminator binds no fields and reports no error.

**The repetition count's direction-asymmetry is forced, not a wart.** The corpus recorded it as a defect —
`each` taking a collection on encode and a count on decode, "same keyword, opposite meaning". It is neither.
On encode the collection exists and its length *is* the count; on decode the count must be recovered from
something already read. Making the count the single shared truth produces a genuine cycle: the preceding
byte-count field reads `fields.registers.extent`, so it would depend on the repetition while the repetition
depended on it. One declaration names the collection; each direction derives what only it has to.

**Ids are flat across regions and arms, and that has a price.** It is what lets an expression say
`fields.byteCount.extent` from anywhere in a message instead of spelling a path through the nesting. The
cost is that a field reaching into an arm that was not selected passes the document check — the id really
does exist — and fails at encode as `Unrealised`. That is the correct failure and it names the field, but
it is a run-time one. A repeated **composite** element is refused outright for the same reason: its fields
would need per-element names (the spec's `item.*` frame chain), and without them an expression inside the
element resolves against whichever iteration ran last. Left unexpressible rather than guessed at; SNMP's
varbind list at step 6 is what will force the naming.

## A note on the pipeline operator

The grammar's precedence table lists `|>` as loosest, but its own worked example
(`capture.fc |> band(0x80) != 0` meaning `(fc & 0x80) != 0`) requires it to bind tighter than `!=` — read
literally as loosest, that example is a syntax error. The implementation follows the example: `|>` binds
looser than arithmetic and bitwise, tighter than any comparison. Recorded here because it is a spec
correction, not an implementation choice.
