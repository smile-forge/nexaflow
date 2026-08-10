<!-- Generated from a ten-protocol grammar stress test; see docs/dynamic-protocol.md for status,
     provenance and the list of open blockers. Do not edit casually — the corpus in
     src/Nexaflow.Tests/Nexaflow.Tests.Features/Protocol/Corpus/ is what validates every claim here. -->
# DynamicProtocol grammar — **v2 (implementable)**

Target assembly: `Nexaflow.IO.Protocol` (`net10.0`, zero project references, no socket API in scope).
Status: **prototype spec, frozen for implementation.** Two engineers should build the same thing from this.

This document owns **§1, §2, §3, §4, §6, §7**. **§5 (protocol state model, run graph, `emits`, correlation, reassembly *policy*)** is a separate document by a separate author; every seam with it is named explicitly below.

---

## 0. Scope decisions taken before anything else

These are binding. Do not reintroduce what is cut here.

### 0.1 Round-trip: v2 implements T1 and T2. T3 is out.

* **T1 semantic round-trip** — `decode(encode(x)) ≡ x` on the capture model. Required for every message.
* **T2 canonical encode** — `encode(inputs)` produces the exact bytes of a canonically-encoded capture. Required for every corpus capture that is canonically encoded (§4.3 enumerates which are).
* **T3 byte-exact re-encode of arbitrary non-canonical wire bytes — EXPLICITLY OUT OF SCOPE.**

The hostile review demonstrated two independent, unrepaired failures in the v1 T3 mechanism:

1. **It does not compose with the layout graph.** Preferring a retained raw span changes `size(e)`, but `size(e)` is fixed in layout Phase C, and the preference decision depends on values Phase C computes. Nesting a non-minimal BER length inside two minimal ones has no fixed point under the v1 rules. Same root cause for MQTT's `8f 80 00`.
2. **It goes stale for position-dependent forms.** A retained `c0 46` DNS back-reference is a pointer into a layout, not a value. Edit an unrelated earlier field and the retained pointer silently addresses the wrong byte. v1 emits a structurally valid, semantically corrupt packet with no error.

Therefore v2 **deletes the raw-shadow mechanism, the `@form` discriminator, and raw-preference-on-encode entirely**. `roundTrip: "byteExact"` is not a legal value. The non-injective-site analysis and validator rules 9 and 10 of v1 are deleted.

`keepRaw` survives **only** as an opaque diagnostic capture: it binds the exact consumed span for logging and for the state model's transcript accumulators (§5's problem). **Encode never reads it.** Referencing a `keepRaw` name from any encode-side expression is a validation error (V34).

**What T3 would cost, stated plainly.** Roughly: (a) a fourth layout phase in which every non-injective emitter carries a *discrete form variable* alongside its size, with the SCC solver working over the product lattice `size × form`; (b) `register`/`lookupOffset` back-references retaining *suffix identity* rather than an offset, re-resolved at emit; (c) a per-capture provenance graph so a mutated field invalidates every retained span whose position or content depends on it. That is a second solver and a dataflow analysis, not a feature. It is the right thing for a wire-fuzzing or MITM-replay tool. It is not the right thing for a device-discovery prototype whose encodes originate from user inputs, not from decoded packets.

### 0.2 Cut for over-engineering

| Cut | Replaced by | Rationale |
|---|---|---|
| `Map` in ProtoValue | `List<Record>` + `lookupBy`/`allBy` converters | Nothing produced a `Map`. Repeated HTTP headers need the list anyway. |
| `parseAs` field kind | `group` with `over:` | Differed from `group` only in reading a captured `Bytes` instead of `buf`. |
| `coalesce`, `sortBy` on `each` | list converters applied to the source list *before* `each` | Each served exactly one protocol (DHCP RFC 3396; CoAP option order) and introduced an encode-only asymmetry into a bidirectional kind. |
| `anchors` (decode-context field) | region 0 (§3.2) + `id` + `offsetOf(id)` | One naming scheme for positions, not two. |
| `exitEffects`, `budgetCounter`, `isCertBased`/`isEphemeral`/`isStaticRsa` | §5's transitions; `consts` table + `lookup()` | The three cipher predicates were a TLS-specific engine path wearing converter syntax — the exact thing rule V1-of-purpose forbids. |
| `labelSeq` as a primitive | **sugar** over `each` + `lp` + `switch` + `seek` + the general `register`/`lookupOffset` offset-table facility (§3.5.3) | The dedupe rule that demoted `tlv` applies to itself. |
| `tagBefore`, `ignoreIf`, `has()`, `cmp`, `lexicographicBytes`, `since` | deleted; see §3.1 note and §6.4 | Introduced by example only. `has()` is superseded by `present:` (§3.1). |
| `delta32`/`delta64`/`rate` converters | §5's fact layer | Wrap-corrected counter deltas are a property of the *fact*, not of the wire. Removing them makes the converter table fully pure. |
| `dnsName`/`undnsName` converters | the `labelSeq` sugar expansion | A converter cannot see offsets, so it can never express compression. |
| `jsonPath`, `regex`, `xpath`, `bcd`/`unbcd`, `sha512` | — | Zero corpus users. |
| `canonical` message block | field declarations (`minimal`, `compress`) | With T3 gone, every `asObserved` option vanished and the block became a restatement of the fields. |

### 0.3 Sugar layers

**v2 ships exactly three layers: `bytes`, `udp`, `tcp`.** The `http`, `coap`, `mqtt`, `modbus`, `snmp`, `dns`, `tls` sugar layers named in v0 §2 are **dropped**. The corpus proves the primitives suffice — all ten protocols are expressible in `bytes` alone, and each protocol's expression is exhibited in §3.4/§3.5 by the field kind it forced.

Consequently **rule V40 ("every sugar construct ships a published, dumpable expansion") holds over v2's three *field-level* sugar constructs only — `tlv`, `tlvList`, `labelSeq` — all three of which are expanded in this document (§3.5).** It no longer fails sevenfold over unwritten layer macros.

### 0.4 Deferred, named with cost

| Deferred | Consequence | Cost to land |
|---|---|---|
| **Transport configuration** — multicast join/TTL/interface, `bind: "unbound"`, `acceptForeignDst`, bound source port, SO_REUSEADDR | **DHCP, mDNS and SSDP cannot SHIP.** They decode and encode correctly; they cannot be *sent*. | Small for mDNS/SSDP/NTP (socket options). **Not small for DHCP:** `bind:"unbound"` + `acceptForeignDst` needs a raw/`IP_HDRINCL` socket, which crosses the elevation boundary and must route through `IShellServices.RunElevatedAsync`. It is a privilege-boundary change, not a config key. |
| **Raw `ip` / `ethernet` layers** | ARP, raw ICMP, custom L2 unexpressible. | Elevation-gated transport + two more framers. |
| **`http` chunked transfer-encoding** | Nothing in v2 expresses it (the chunk list is length-prefixed items terminated by a zero-length chunk *whose own length field is hex ASCII*). SSDP does not use it; a real HTTP client would. | A `bytes.until` variant with a hex-ASCII `LenSpec`, plus a reassembling `each`. Small but not free. |
| **Encode-side fragmentation** (splitting a large APDU into segments) | CoAP Block1 uploads and BACnet segmented *requests* cannot be sent. Decode-side reassembly ships (§2.5). | The inverse of §2.5: a splitter driven by a peer-advertised window, which is state-model work. |
| **GREASE-insensitive matching** (TLS-G16) | TLS fingerprint comparison unexpressible. `ignoreIf` is cut. | An equivalence relation on the capture model; belongs with `emits`, not with fields. |
| **NTP `at:"transmit"` under symmetric auth** | Capture C (68 B, MD5 MAC over the whole packet) must sample T1 at encode time, not socket-write time, and absorbs the construction latency. | Hardware/kernel timestamping. Genuinely hard; correctly refused (V33). |

---

## §1 Document shape

A protocol is a single JSON document.

```jsonc
{
  "protocol": 2,                       // grammar version. 1 is accepted and migrated (§6.1.1).
  "id": "…", "name": "…", "description": "…", "version": "…",
  "trust": "builtin | user-reviewed | ai-draft",

  "appliesTo": { … },                  // predicate over device facts        — §5
  "inputs":   [ … ],                   // injectable fields                  — §6.5
  "consts":   { … },                   // Record; any ProtoValue             — §6.2
  "limits":   { … },                   // document-level defaults            — §3.7
  "messages": { … },                   // named message templates            — §2, §3
  "parse":    { … },                   // named reusable field lists         — §3.4.14

  "model":    { … },                   // state machine                      — §5
  "steps":    [ … ], "edges": [ … ],   // run graph                          — §5
  "emits":    [ … ],                   // capture → DeviceFact bindings      — §5
  "summary":  "…"
}
```

**Ownership seam.** `appliesTo`, `model`, `steps`, `edges`, `emits` are specified by the §5 document. This document defines the *values* they consume (§6 paths, types, converters, and the capture model of §4.1) and nothing else about them.

**A message:**

```jsonc
"readHoldingRegisters": {
  "doc": "…",
  "layers": [ … ],                     // outermost-first stack             — §2
  "invariants": [ … ],                 // THE single legality mechanism     — §3.6
  "limits": { … },                     // overrides document limits         — §3.7
  "offsetTables": { … }                // encode-side back-reference tables — §3.4.16
}
```

**There is no `constraints` key and no `validIn`/`requires`/`conflicts` on a message in this document.** Message-level *legality with respect to protocol state* is §5's; message-level legality with respect to *its own fields* is `invariants`, here, and nowhere else (§3.6). This closes the duplicate-mechanism finding.

---

## §2 Layers and framing

A message's `layers` is a **stack, outermost-first — the order the bytes appear on the wire.**

| kind | what it contributes | below it |
|---|---|---|
| `udp` | a datagram; ports | OS |
| `tcp` | a byte stream; **a framer** (§2.3) | OS |
| `reassemble` | joins N framed PDUs into one buffer (§2.5) | udp / tcp |
| `bytes` | the octet template — the universal escape, and the only content layer | whatever is beneath |

`serial`, `ble`, `tls`, `ip`, `ethernet` and every protocol-named sugar layer are **not in v2**.

### 2.1 `udp`

```jsonc
{ "kind": "udp", "dstPort": 161, "srcPort": 0 }
```

`srcPort: 0` = ephemeral. **Nothing else is configurable in v2** — see §0.4. A UDP datagram **is** the frame: no framer, no framing ambiguity.

### 2.2 `tcp`

```jsonc
{ "kind": "tcp", "dstPort": 502, "frame": { … } }
```

Exactly one `frame` object. Framing is a property of the **connection**, not of the message.

### 2.3 Framing — **the framer never owns wire bytes** *(closes Modbus-G1)*

This is the rule both prior documents skipped, and it is stated once, normatively:

> **The field template is the sole and complete description of every byte of a frame. A framer is a decode-side boundary oracle only. It reads bytes without consuming them, computes where the frame ends, and hands the *entire* frame — prefix bytes included — to the `bytes` layer as region 0. On encode a framer contributes nothing: the template emits the prefix, exactly as it emits everything else.**

Consequences, all mandatory:

* **Encode.** The framer is not invoked. `encode(msg)` is exactly what the template emits. Modbus's 12-byte request is 12 bytes because the template contains `{"u16be":"len(mbapBody)"}`; there is no second writer of offsets 4–5 and no 10-byte or 14-byte reading.
* **Decode.** The framer determines the frame extent, then the template re-reads the prefix as an ordinary field. The prefix is decoded twice (once by the framer, once by the template) and this is **intentional**: the template's copy is the one that binds a capture and the one an `assert` can cross-check.
* **Validator (V6).** A framer's declared prefix span must be *covered* by some template field. The validator locates the byte range `[at, at+prefixWidth)` in the emission tree and errors if no field emits it, or if a field straddles the boundary.
* There is no `owns` key and no `includesPrefix` boolean. Ownership is not configurable.

### 2.4 Frame kinds

```jsonc
{ "kind": "none" }
{ "kind": "fixed", "size": 12 }
{ "kind": "delimited", "delimiter": "0d0a", "max": 8192 }
{ "kind": "lengthPrefixed", "prefix": "u16be", "at": 4, "totalIs": "at + prefixWidth + value" }
{ "kind": "varintPrefixed", "encoding": "mqtt7", "at": 1, "maxPrefixBytes": 4,
  "totalIs": "at + prefixWidth + value" }
```

**`totalIs` replaces `includesPrefix`** *(closes Modbus-G11)*. It is an expression over three bound names — `at` (declared), `prefixWidth` (the decoded prefix's own octet count; constant for `lengthPrefixed`, 1–`maxPrefixBytes` for `varintPrefixed`), and `value` (the decoded prefix value) — evaluating to the **total octet count of the frame**. The default is `at + prefixWidth + value`, which is the "counts neither itself nor the header before it" case. The three cases a boolean could not distinguish:

| protocol shape | `totalIs` |
|---|---|
| Modbus MBAP: 6-byte value covers only what follows the prefix, prefix at offset 4 | `at + prefixWidth + value` (default) |
| length counts itself but not the header | `at + value` |
| length counts the whole datagram | `value` |

**`varintPrefixed` is new and is why MQTT is framable on TCP at all** *(closes MQTT-G3)*. Its `encoding` is drawn from the same closed `LenSpec` varint set as §3.4.0, so there is one varint implementation, not two. `at: 1, encoding: "mqtt7"` frames MQTT: header byte, then a 1–4 byte base-128 little-endian length, then that many bytes. A broker that puts a 7-byte SUBACK and a PUBLISH in one TCP segment, or splits a 146-byte CONNECT across two, is framed correctly.

**Framer bounds.** Every framer is bounded: `fixed` by `size`, `delimited` by `max`, `lengthPrefixed`/`varintPrefixed` by `limits.maxFrameBytes` (default 65535). A `totalIs` result outside `[at + prefixWidth, maxFrameBytes]` is a decode error, not a buffer allocation.

### 2.5 `reassemble` — decode-side only, one parameterised assembler *(resolves MUST-9)*

Fragmentation is not a field-grammar problem, so it lives here. **v1's justification that `parse` cannot see fragment content was false** — CoAP Block2 numbers and BACnet sequence numbers are *decoded fields*, so reassembly is inherently a two-stage parse. v2 specifies the two stages.

```jsonc
{ "kind": "reassemble",
  "header": "parse.bacnetSegHeader",   // stage 1: runs against EACH fragment
  "key":    "hdr.invokeId",            // correlation key across fragments
  "ordinal":"hdr.sequenceNumber",      // position; Int
  "modulus": 256,                      // optional: ordinal wraps (BACnet); omit for CoAP Block
  "more":   "hdr.moreFollows",         // Bool: another fragment is coming
  "body":   "hdr.payloadSpan",         // the Bytes of THIS fragment that get concatenated
  "limits": { "maxFragments": 64, "maxAssembledBytes": 65535, "timeoutMs": 5000 } }
```

**The two-stage contract, normatively:**

1. **Stage 1 (per fragment).** The `header` parse block runs over the fragment's own octets. Its region 0 is *the fragment*. Its captures are bound into a scope reachable as **`hdr.*`** — and `hdr.*` is visible **only** inside this `reassemble` layer's own keys. It is not visible to the message's field list.
2. **Stage 2 (per assembly).** The `body` spans are concatenated in `ordinal` order (modulo `modulus` if declared, with the assembler tracking the unwrapped sequence). When `more` is false on the highest-ordinal fragment and the ordinal set is gapless, the assembly is complete and the concatenation is handed to the `bytes` layer as **region 0 of the message**. `capture.*` in the message's field list scopes to that assembled buffer and to nothing else — a field cannot see fragment boundaries, which is the point.
3. `meta.fragmentCount` and `meta.assembledLen` are available to the message (§6.2).
4. **Acking is §5's.** `reassemble` emits an `assemblyProgress` event; whether that produces a BACnet SegmentACK or a CoAP Block2 continuation request is a state-model decision.

There are **no `indexed`/`sequenced`/`continuation` assembler kinds.** They differed only in whether the ordinal wraps, which is the `modulus` key. One construct.

**Encode-side fragmentation is deferred** (§0.4). A message whose layer stack contains `reassemble` is decode-only in v2; encoding it is a validation error (V7).

*Forced by:* BACnet-G4 (capture C3 stops inside a 4-octet object identifier — no segment is independently parseable), CoAP blockwise, TLS-G8 (record framer ↔ handshake framer are unaligned: `frame` handles the record layer, `reassemble` joins handshake messages).

### 2.6 `bytes`

```jsonc
{ "kind": "bytes", "fields": [ … ] }
```

The content layer. Its field list is §3. There is exactly one encoder and one decoder in the engine, and they consume this.

---

## §3 The `bytes` field grammar

### 3.1 Field objects: common keys

Every field is one JSON object with **exactly one kind key** (§3.4) plus any of these common keys, all legal on every kind, in both modes:

```jsonc
{
  "<kind>": …,
  "id":     "name",        // THE name of this field. Unique within the message.
  "as":     "name",        // decode: bind the decoded value into the current capture frame
  "value":  "<expr>",      // encode: the value to emit
  "via":    "converter",   // decode: applied after reading. encode: the inverse, before writing.
  "when":   "<expr>",      // conditional presence; false ⇒ zero bytes emitted / consumed
  "present":"name",        // bind a Bool capture: was this field actually present?   ← NEW
  "expect": <literal|expr>,// assert the decoded value equals this
  "onMismatch": "error",   // error | warn | absent          (default: error)
  "size":   <expr>,        // assert the emitted/consumed octet count equals this
  "keepRaw":"name",        // diagnostic only; encode never reads it (§0.1)
  "at":     "encode",      // encode | transmit              (§3.4.17)
  "doc":    "…"
}
```

**Naming scheme — one, and only one** *(resolves MUST-6's second half)*:

> A field's name is its **`id`**. If `as` is present and `id` is absent, `id` defaults to `as`. Ids are unique within a message (V8). **Every reference to a field anywhere in the document is by bare id**: `capture.<id>` / `item.<id>` for its value, `len(<id>)` / `offsetOf(<id>)` / `octets(<id>)` / `bytes(<id>)` / `count(<id>)` for its layout. There is no `f.*`, no `field:X`, no `group:X`, and no bare-option-code form. The §5 author binds to the same ids.

**`when` is normative in both modes.**
*Decode:* a false `when` consumes zero bytes. *Encode:* a false `when` emits zero bytes.
A `when` expression may reference **only** captures/items bound strictly earlier in the same field list or in an enclosing frame. Forward references are a validate-time error, decidable by field index (V9).
An **encode-side** `when` may not reference any layout function (`len`, `offsetOf`, `octets`, `bytes`, `count`), because the emission tree must be known before layout (V10). The single declared exception is the compression feedback node of §3.3 Phase D.

**`present` closes CoAP-G17(a)** *(resolves MUST-13)*. It binds a `Bool` into the capture frame recording whether the field was present. Combined with `onMismatch: "absent"` it makes optional-marker protocols T1-clean:

```jsonc
// decode                                          // encode
{ "const": "ff", "id": "payloadMarker",            { "const": "ff", "id": "payloadMarker",
  "when": "remaining() > 0",                         "when": "inputs.hasPayload",
  "onMismatch": "absent",                            "present": "hasPayload" },
  "present": "hasPayload" },                       { "bytes": {"len":"remaining"}, "as":"payload",
{ "bytes": {"len":"remaining"}, "as":"payload",      "when": "inputs.hasPayload" }
  "when": "capture.hasPayload" }
```

`decode(encode(payload="", hasPayload=true))` yields `hasPayload=true, payload=<empty>`; `decode(encode(hasPayload=false))` yields `hasPayload=false`. Two distinct capture-model values, two distinct byte strings. T1 holds.

**`onMismatch: "absent"`** — the decoded value did not equal `expect`/`const`, so **rewind the cursor to where the field started and treat the field as not present.** It is legal only when `present` is also declared (V11), because otherwise it is silent corruption. This is the general "optional marker / probe a discriminator" mechanism (CoAP payload marker, DHCP magic cookie when used to *select* a parse rather than *require* one, TLS optional trailing region).

**`via` is the decode-side converter pipeline.** v0 decode had no slot for one, so a `u32be` `yiaddr` could never become `192.168.0.10`. Every converter usable in `via` must declare an inverse (V2). *Forced by:* DHCP-G3, NTP-G2/G5.

**Deleted common keys.** `tagBefore` (write the tag as a preceding scalar — the BER canonical form in §3.5.1 already does), `ignoreIf` (§0.4).

---

### 3.2 The decode context — decode is a fold over a context, not over a cursor

Decode is a **deterministic single left-to-right traversal of the field list**, folding over a decode context `DC`:

```
DC = {
  buf       : immutable octets of the layer payload (the frame, or the assembly of §2.5)
  cursor    : absolute offset into buf
  regions   : stack of Region { start, limit, id, onUnderrun, onOverrun }
  frames    : capture scope chain (message → group → iteration item)
  registers : named mutable cells (each.fold vars) — the ONLY mutable state
  hops      : remaining back-reference budget
  depth     : remaining ref/group-over recursion budget
  meta      : transport metadata (§6.2)
}
```

Six normative guarantees:

1. **Region 0 is the message frame** *(resolves MUST-7)*. Before the first field is read, the engine pushes `Region { start: 0, limit: buf.length, id: "message", onUnderrun: "skip", onOverrun: "error" }`. Therefore `remaining()` and `offset()` are **defined at top level**, with no enclosing `group`. This is what makes NTP's 48/52/68/72-byte trailer disambiguation expressible — `remaining()` after the 48-byte header is `0`, `4`, `20` or `24`, and a `switch` on it selects the trailer shape. Without this statement NTP-G7 is not closed.
2. **Regions are a stack, not a cursor limit.** Entering pushes `(start=cursor, limit=cursor+extent)`. A read past `limit` is an **overrun error**. Leaving with `cursor < limit` is an **underrun error** by default. This is what makes "the varbind list declares 303 octets, the three varbinds must consume exactly 303" checkable. *Closes SNMP-G7, TLS-G12, BACnet-G10, Modbus-G8, DHCP-G7, mDNS-G4.*
3. **`registers` are the whole of decoder-carried state**, each scoped to its declaring `each.fold`. There is no global mutable decode state. CoAP's running option number, BACnet's tag depth, DHCP's accumulator are all registers. *Closes CoAP-G1, BACnet-G3, DHCP-G6.*
4. **Backward-only random access.** The cursor advances monotonically except inside `seek`, which jumps to an absolute offset **strictly less than the offset of the `seek` field itself**, reads, and restores the cursor. A forward or equal target is a decode error. Combined with `hops` this makes DNS compression terminating *by construction* — a `c0 0c` pointer sitting at offset 12 is a self-loop and is **rejected, not hung**. There is no second, weaker statement of this rule anywhere in v2 (v1 stated it twice, differently).
5. **Capture scoping is a chain.** Inside `each`, `item.*` is the current iteration frame; `capture.*` is the enclosing frame; `capture.^` walks one frame outward (repeatable: `capture.^^`); `capture.$` is the message root. *Closes CoAP-G9, mDNS-G11, Modbus-G12, TLS-G2.*
6. **Everything is bounded** by `limits` (§3.7): region depth, hops, iterations, recursion, scan length, total decoded bytes. A malformed packet fails; it never spins.

---

### 3.3 Encode: the layout dependency graph

Two-pass back-patching is insufficient and is replaced. Three corpus facts kill it: BER length octets are counted by every enclosing length (SNMP — nine measured payload sizes in 100..300 where +1 payload byte grows the packet by +2); a DNS name is 12 bytes compressed or 17 uncompressed depending on its own offset (mDNS — 127 vs 215 bytes for the same response); BACnet's BVLC length counts itself.

**Phase A — build the emission tree.** Walk the field list expanding `ref`, `switch` (case chosen by its encode-side `on` expression), `each` (source list known from inputs), and `when` (predicates over `inputs`/`consts`/`params`/`state` — known before layout by V10). Result: an ordered tree of **emitters**, each with a `SizeExpr` and a `ValueExpr`.

**Phase B — build the dependency graph `G`.** Nodes:

* `size(e)` — octet count of emitter `e`
* `off(e)` — absolute offset of emitter `e`
* `len(S)` — length of every named span `S` (a `group`, an `lp` body, `span(a,b)`)
* **`val(e)` — the *value* of emitter `e`, when that value is layout-dependent** *(closes critique 3.10)*

Edges:

* `len(S) → size(e)` for each `e ∈ S`
* `off(e) → size(p)` for every preceding sibling `p` and every enclosing prefix
* `size(e) → x` for each span/position term `x` in `e`'s `SizeExpr`
* **`val(e) → x` for each span/position term `x` in `e`'s `ValueExpr`**
* **`size(f) → val(e)` where `f`'s width is determined by `e`'s value** (a `LenSpec: {"expr": …}`; BACnet's extended-length octet governed by the LVT bit-slice)

Without `val` nodes, BACnet-G5 — writing `octets(propVal)` into a **`bits` slice** of an already-emitted tag byte — is invisible to the cycle checker. With them, BACnet's chain is `size(extLenOctets) → val(lvtSlice) → len(propVal)`, and `len(propVal)` depends on leaves only: a DAG, resolved exactly.

**Phase C — condense to SCCs, topologically order.** A singleton SCC with no self-loop resolves directly. **Nested BER, three-level TLS vectors, and BVLC→NPDU→APDU→tag are trees, therefore DAGs**, and are exact in one pass. This is the common case.

**Phase D — feedback SCCs.** An SCC of size > 1, or with a self-loop, is legal **only if every node in it carries a `feedback` declaration and all declarations share one direction.** Exactly three constructs may declare it:

| Construct | Declaration | Seed | Direction | Bounded by |
|---|---|---|---|---|
| `varint` with a variable-width encoding | implicit | minimum width | non-decreasing | `maxBytes` |
| `lp` with a variable-width `LenSpec`, or `covers` including itself | implicit | minimum width | non-decreasing | `maxBytes` |
| `switch` whose `on` expression contains `lookupOffset(…)` | **explicit** `"feedback": {"seed":"<caseName>", "direction":"nonIncreasing"}` | the named case | non-increasing | the seed case's size |

**Mixed-direction SCCs are a validation error** *(resolves MUST-8)*. An SCC containing both a non-decreasing and a non-increasing node — a compressed name inside a variable-width-length-prefixed region whose length precedes it — is not monotone in any single direction, Kleene iteration need not converge, and v2 refuses it at validate time naming both nodes (V13). No corpus protocol produces one: mDNS's compressed names sit inside a fixed-width `u16be` RDLENGTH, whose `size` is constant and therefore not in the SCC at all.

**Convergence, and the honest restatement of the guarantee** *(resolves MUST-8's second half)*:

* *Non-decreasing SCC.* Seed every feedback width at its minimum. Each iteration recomputes all sizes/offsets, then recomputes each feedback node's required width. Widths only grow ⇒ measured lengths only grow ⇒ required widths only grow. The sequence is monotone in ℤⁿ and bounded above by `Σ maxBytes`. **It converges in at most `Σ_nodes (maxWidth − minWidth) + 1` iterations.**
* *Non-increasing SCC.* Seed fully uncompressed. Content is unchanged between iterations (only offsets shift), so a suffix that matched last pass still matches; widths only shrink, bounded below by the fully-compressed width. Same bound.

The engine therefore computes `layoutIterations` **at validate time** as that sum, rather than defaulting it to a magic 8. Exceeding it is an **engine defect (internal error)**, not an authoring error and not an expected runtime path.

> **The guarantee, stated honestly.** v0 promised: *"a cyclic span dependency is a validation error, never a runtime failure."* v2 keeps that promise for every cycle it can classify — non-monotone cycles, mixed-direction cycles, and genuine forward references are all rejected at validate time (V12, V13). It **weakens** the promise in exactly one place: a homogeneous monotone SCC iterates at run time. It is *proved* to converge within a *statically computed* bound, so no legal document can hit the cap; but the cap exists, and if it is ever hit that is a bug in this engine, not in the document. Saying "preserved" without this paragraph, as v1 did, is false.

**Phase E — cycle errors.** Any SCC containing an undeclared node, or a mixed direction, or an `off(e) → size(f)` edge where `f` follows `e`, is a validation error naming the full cycle.

**Phase F — emit.** All sizes, offsets and layout-dependent values are known. Write straight through, including back-patches into `bits` slices via `octets(id)` / `offsetOf(id)`.

*Closes:* SNMP-G1, mDNS-G3, BACnet-G5/G7, MQTT-G1, CoAP-G5, TLS-G6, Modbus-G11.

---

### 3.4 Field kinds

Each entry: **syntax · encode · decode · validator · forced by.**

#### 3.4.0 `LenSpec` — one concept, reused by `lp`, `bytes`, `varint`, `each`

```jsonc
LenSpec :=
    "u8" | "u16be" | "u16le" | "u24be" | "u32be" | "u32le"
  | { "varint": { "encoding": "ber"|"mqtt7"|"leb128u"|"vlq", "maxBytes": 4, "minimal": true } }
  | { "expr": "<expression>" }   // length comes from an already-bound capture
  | "remaining"                  // to the limit of the innermost region
```

Named varint encodings — **a closed set; adding one is a deliberate code change**:

| name | form | corpus user |
|---|---|---|
| `ber` | `<128` short form; else `0x80\|n` then n big-endian octets | SNMP |
| `mqtt7` | base-128 **little-endian**, bit 7 = continue, ≤ 4 bytes | MQTT (field *and* framer, §2.4) |
| `leb128u` | base-128 little-endian, unbounded | — (retained: the closed set is cheaper to over-populate once than to extend later) |
| `vlq` | base-128 **big-endian**, bit 7 = continue | ASN.1 subidentifiers, reached via `base128` (§6.4) |

`minimal: true` (default): **on decode, a non-shortest representation is a decode error.** This makes value→bytes injective and is why MQTT's `8f 80 00` is *rejected* rather than requiring shadow state. `minimal: false` is legal and means the decoder accepts non-shortest forms; **the encoder still emits the shortest** — with T3 gone there is no mechanism, and no promise, to reproduce the original width.

**CoAP's 4-bit-nibble-plus-escape and BACnet's LVT-plus-escape are *not* varints.** They are packed into a tag byte and are expressed with `bits` + `switch` + a `LenSpec {"expr": …}`. No special case. *(CoAP-G5, BACnet-G9.)*

---

#### 3.4.1 `scalar` — fixed-width numbers

```jsonc
{ "scalar": "u16be", "value": "len(pdu)" }        // canonical form
{ "u16be": "len(pdu)" }                           // shorthand ≡ above
{ "u16be": null, "as": "txn" }                    // shorthand, decode
```

**Desugaring rule (one rule, applied first):** `{"<scalarName>": E, …}` ≡ `{"scalar":"<scalarName>", "value": E, …}`; `E = null` means no `value`.

Widths: `u8 u16be u16le u24be u32be u32le u64be u64le i8 i16be i16le i32be i32le i64be f32be f64be`.

* **encode** — evaluate `value` (inverse-`via` first if `via` present), range-check against the width, write.
* **decode** — read the width, apply `via`, bind `as`, check `expect`.
* **validator** — `value` required on encode unless the message is decode-only; overflow of the declared width is a validate-time error when the value is constant, a runtime error otherwise.
* *Forced by:* every protocol.

---

#### 3.4.2 `const` — literal octets, bidirectional

```jsonc
{ "const": "63 82 53 63", "id": "magicCookie" }
{ "const": "ff", "onMismatch": "absent", "present": "hasPayload" }
```

* **encode** — write the literal.
* **decode** — read that many octets and compare. On inequality apply `onMismatch`: `error` (corrupt packet), `warn` (bind and continue), `absent` (rewind, field not present — requires `present`).
* **validator** — hex literal must be an even number of hex digits; `absent` requires `present` (V11).
* *Forced by:* DHCP-G7 (magic cookie), CoAP-G17a (payload marker), NTP, TLS.

---

#### 3.4.3 `bytes` — **LOAD-BEARING.** The universal variable-length octet field

```jsonc
// decode
{ "bytes": { "len": "capture.commLen" },              "as": "community", "via": "latin1" }
{ "bytes": { "len": "remaining" },                    "as": "payload" }
{ "bytes": { "until": "0d0a", "consume": true },      "as": "line", "via": "latin1" }
{ "bytes": { "whileAnyOf": "20 09" },                 "as": "ows" }
// encode
{ "bytes": { "value": "inputs.community |> ascii" }, "size": 6 }
{ "bytes": { "until": "0d0a" }, "value": "inputs.line |> ascii" }
```

Exactly one of `len` / `until` / `whileAnyOf` (or none, for a plain `value` write).

* **encode.**
  * `len` / no extent key: write the octets of `value` (after inverse-`via`). `size`, if present, is asserted — a 31-byte NTP random or TLS `client_random` fails at encode rather than producing a self-consistently wrong packet *(TLS-G13)*.
  * **`until`: the encoder writes `value` and then writes the delimiter, iff `consume` is true (default true).** `consume: false` means the delimiter is left to a following field on decode and is *not* emitted on encode. This is stated here because v1 never stated it and the SSDP expansion is unbuildable without it.
  * **If the emitted `value` contains the delimiter, encode fails** with a named error citing the field id. v1's validator rule ("the delimiter must be provably absent from the value") is **deleted** — it is unsatisfiable, because nothing in the type system can forbid CRLF inside a `text` input. Delimiter collision is a runtime encode error, and that is the correct place for it.
  * `whileAnyOf` on encode writes `value` verbatim and asserts every octet is a member of the set.
* **decode.**
  * `len` consumes exactly that many octets; overrun against the innermost region is an error. `"remaining"` consumes to the region limit.
  * `until` scans forward for the delimiter, binds the span **before** it, and consumes the delimiter iff `consume`. Scanning is bounded by `max` (default `limits.maxScanBytes`, default 8192); hitting the bound or the region limit without a match is a decode error.
  * `whileAnyOf` consumes a maximal run of the listed octets. **A zero-length run is legal and binds empty `Bytes`** — this is what makes SSDP's `EXT:` (colon, no OWS, no value) decode and re-encode as exactly 6 octets.
  * Result kind is `Bytes`; `via` converts.
* **validator** — exactly one extent key (V14). `max` is optional (defaulted); v1's rule 11 in both halves is deleted.
* *Forced by:* SNMP-G4, MQTT-G2, CoAP-G2, TLS-G3, NTP-G5, DHCP-G4, SSDP-G1, mDNS-G1, BACnet-G16.

---

#### 3.4.4 `bits` — **LOAD-BEARING.** Multi-octet, bidirectional

```jsonc
{ "bits": [ {"n":2,"value":"0"}, {"n":3,"value":"consts.vn"}, {"n":3,"value":"consts.mode"} ] }
{ "bits": [ {"n":2,"as":"li"},   {"n":3,"as":"vn"},           {"n":3,"as":"mode"} ] }
{ "bits": [ {"n":10,"as":"objectType"}, {"n":22,"as":"instance"} ] }              // BACnet objid
{ "bits": [ {"n":4,"value":"1"}, {"n":1,"value":"1"},
            {"n":3,"value":"octets(propVal)"} ], "id": "lvtTag" }                 // BACnet LVT
{ "bits": [ {"n":3,"as":"codeClass"}, {"n":5,"as":"codeDetail"} ] }               // CoAP code
```

* **encode** — pack slices MSB-first into a big-endian bit stream, most-significant slice first. `"order": "lsbFirst"` on the field reverses slice order within the group. A slice's `value` may be a layout function, making it a Phase F back-patch target and a `val(e)` node in Phase B.
* **decode** — unpack the same way; each slice with `as` binds an `Int`.
* **validator** — `Σ n` must be a multiple of 8 (V15). A slice is 1..32 bits (V16). A slice may carry `value`, `as`, `expect`; not `via`, not `when`.
* *Forced by:* NTP-G1 (LI/VN/Mode), CoAP-G4 + CoAP-G17b (code class/detail), MQTT-G8/G20, mDNS-G10 (DNS flags), DHCP-G9, BACnet-G1 (10+22 bit object id, crosses octet boundaries three times).

---

#### 3.4.5 `varint` — **LOAD-BEARING.** Standalone variable-width integer

```jsonc
{ "varint": { "encoding": "mqtt7", "maxBytes": 4, "minimal": true }, "value": "len(rest)" }
{ "varint": { "encoding": "ber" }, "as": "berLength" }
```

* **encode** — write the shortest representation. A Phase D non-decreasing feedback node when its value depends on a span containing it.
* **decode** — read; a non-minimal encoding is an error when `minimal` (default).
* **validator** — `maxBytes` required for unbounded encodings; participates in the SCC direction check (V13).
* *Forced by:* MQTT-G1 (Remaining Length: `8f 01` = 143, `02` = 2, same field), SNMP-G1 (BER length as a standalone field).

---

#### 3.4.6 `lp` — **LOAD-BEARING.** Length-prefixed nesting. *The* structural primitive

Replaces hand-written `{"u16be":"len(X)"} + {"group":"X"}` pairs, TLS `vector<T>`, MQTT/DNS/BACnet length-prefixed strings, DHCP option bodies, and BER/SNMP value regions. **Its span is anonymous**, which is what makes it safe inside `each`.

```jsonc
// leaf value
{ "lp": { "len": "u16be" }, "value": "inputs.clientId |> utf8", "as": "clientId", "via": "utf8" }

// nested field list — on decode this is a bounded region
{ "lp": { "len": { "varint": { "encoding": "ber" } } },
  "fields": [ … ], "onUnderrun": "error", "onOverrun": "error" }

// self-inclusive length (BACnet BVLC: the u16be counts itself and the two octets before it)
{ "lp": { "len": "u16be", "covers": "fromRegionStart" }, "fields": [ … ] }
```

**`covers`** ∈ `"body"` (default) | `"lenAndBody"` | `"fromRegionStart"` | `{"expr": "bodyLen + <k>"}`, where the bound names are `bodyLen`, `lenWidth`, and `preLen` (the octets from the start of the enclosing region to the start of the length field):

| value | wire number | corpus |
|---|---|---|
| `body` | `bodyLen` | TLS vectors, MQTT strings, BER |
| `lenAndBody` | `bodyLen + lenWidth` | — |
| `fromRegionStart` | `preLen + lenWidth + bodyLen` | BACnet BVLC |

* **encode** — the body is laid out first (Phase C/D), then the length is written per `covers`. A variable-width `LenSpec` makes this a feedback node.
* **decode** — read the length, invert `covers` to recover `bodyLen`, **push a region** of exactly that extent, parse the body, assert exact consumption. `onUnderrun`/`onOverrun` ∈ `error` | `warn` | `skip` (advance to limit) | `capture` (bind the remainder), per field.
* **validator** — `covers.expr` must be affine in `bodyLen` with coefficient 1, so the decode-side inversion is exact (V17). Exactly one of `fields` / `value` (V18).
* *Forced by:* SNMP-G8, TLS-G6, MQTT-G4, BACnet-G7/G10, mDNS-G4, DHCP-G7.

---

#### 3.4.7 `group` — **LOAD-BEARING.** Named span, decode region, and sub-parse

```jsonc
{ "group": "vbl", "extent": "capture.vblLen", "onOverrun": "error", "onUnderrun": "error",
  "fields": [ … ] }
{ "group": "innerOpaque", "over": "capture.vbRaw", "maxDepth": 8, "fields": [ … ] }
```

Three roles, one kind:

1. **No `extent`, no `over`** — a pure naming device for `len(id)` / `bytes(id)` / `offsetOf(id)`, as in v0.
2. **`extent`** (decode-only) — `group` **is** the region primitive. There is no separate `region` kind.
3. **`over`** — re-enter this field list against an already-captured `Bytes` value instead of `buf`. **This absorbs `parseAs`**, which differed from `group` in nothing else. Region 0 for the sub-parse is the captured value; `capture.^` still reaches the outer frame; `maxDepth` is mandatory and ≤ 32.

* **encode** — `extent` and `over` are ignored (encode-only-meaningful keys are `id` and the field list); the group contributes its body.
* **decode** — per role above.
* **validator** — `extent` and `over` are mutually exclusive (V19); `over` requires `maxDepth` (V20).
* *Forced by:* SNMP-G7/G8, mDNS-G4, TLS-G12, BACnet-G10, DHCP-G7.

---

#### 3.4.8 `each` — **LOAD-BEARING.** Unified, bidirectional, stateful iteration

v0's `each` meant two incompatible things in the two directions, took only a count, had no per-iteration scope and no accumulator.

```jsonc
{ "each": {
    "over":  "inputs.subscriptions",   // encode source (omit for decode-only lists)
    "bind":  "subscriptions",          // capture name, BOTH directions
    "until": { … },                    // decode termination — see below
    "max":   4096,                     // bound; defaults from limits.maxIterations
    "fold":  { "var": "num", "init": "0", "next": "num + item.delta" },
    "yields":"record"                  // record | scalar
  },
  "fields": [ … ] }
```

**Termination — `until` is a single key taking exactly one of these object forms** *(resolves critique 3.4: `count` is a **value of** `until`, never a sibling)*:

| form | meaning |
|---|---|
| `{"count": "<expr>"}` | fixed iteration count |
| `{"when": "<expr>"}` | stop when the predicate is true; may read `fold` registers and `remaining()` |
| `{"peek": "u8", "equals": "0xff"}` | *deleted* — write `{"when": "peek('u8') == 0xff"}` |

Two forms, one syntax for `peek` *(resolves MUST-14)*. "Until the region ends" is `{"when": "remaining() == 0"}`. There is no `endOfRegion`, no `endOfInput`, no `rest`, no `atEnd`.

**Evaluation order — normative, and it is the difference between a hang and a parse** *(resolves MUST-12)*:

```
apply fold.init to registers
loop:
  1. evaluate until  (registers hold the values written by the PREVIOUS iteration's next)
  2. if true -> stop, WITHOUT running the body
  3. if iterationCount == max -> error
  4. push item frame; run body fields; pop
  5. evaluate fold.next; write registers
  6. goto 1
```

BACnet's depth fold works under exactly this order: `{"var":"depth","init":"1","next":"depth + (item.lvt==6) - (item.lvt==7)"}` with `"until":{"when":"depth == 0"}`. `init` is 1, so test 1 is false; after the closing-tag iteration `next` writes 0; the following test stops the loop. Testing *before* `init` or against pre-advance values inside the body loops to `max`.

* **encode** — iterate `over`'s list; `item.*` is the element. `over` must be a `List`.
* **decode** — iterate until `until`; each iteration's captures form an item frame; `bind` binds a `List<Record>` (or `List<scalar>` if `yields: "scalar"`).
* **Scoping (normative).** `item.*` is the current frame, plus `item.@index` (0-based) and `item.@ordinal` (1-based). `capture.^` reaches the parent frame, `capture.$` the root. A `group` declared inside the body creates **one anonymous span per iteration**; `len(X)` inside the body binds *this* iteration's instance; `len(X@all)` is the concatenation of all of them.
* **No `sortBy`, no `coalesce`.** Sort the source list with a converter before `each` sees it (`inputs.options |> sortBy('number')`); merge decoded items with `groupBy` after (`capture.options |> mergeBy('code','value')`). See §6.4. This removes the encode-only asymmetry from a bidirectional kind.
* **validator** — `max` mandatory or defaulted (V21); `over` required if the message is encodable (V22); a `fold.next` may reference only `item.*` and its own register (V23).
* *Forced by:* SNMP-G5/G9/G10, DHCP-G1, CoAP-G1/G3/G6, TLS-G1/G2 (98 bytes = 9 extensions of widths 20,6,14,18,4,5,4,9,18 — no expression yields 9 from 98), MQTT-G4/G5/G10, mDNS-G2/G9, SSDP-G2, BACnet-G3, Modbus-G4.

---

#### 3.4.9 `switch` — **LOAD-BEARING.** Discriminated union. Needed by all ten protocols

```jsonc
{ "switch": "capture.valueTag",          // decode discriminant
  "on":     "inputs.valueKind",          // encode discriminant
  "as":     "valueKind",
  "cases": {
    "0x04": [ { "bytes": {"len":"remaining"}, "as":"str",   "via":"latin1"    } ],
    "0x43": [ { "bytes": {"len":"remaining"}, "as":"ticks", "via":"unminuint" } ],
    "0x05": [ ]
  },
  "default": [ { "bytes": {"len":"remaining"}, "as":"opaque" } ] }
```

* **decode** — evaluate `switch` (a capture bound earlier, or `peek(...)`), run the matching case's field list. No match and no `default` is a decode error. The matched case key is bound to `as`, so the state model and `emits` can branch on it structurally.
* **encode** — evaluate `on`, emit that case. If `on` is absent, `switch` is used for decode; encoding is a validation error (V24).
* **`default` is what makes forward compatibility expressible** — RFC 3597 opaque RDATA, unknown TLS extensions, vendor DHCP options.
* **`exhaustive` is deleted.** v1 required a validate-time proof over "the discriminant's declared enum", but a decode discriminant is a `capture.*` and captures have no enum declaration mechanism. Unimplementable as written; cut rather than half-specified.
* **`feedback`** may be declared here for the compression node (§3.3 Phase D).
* **One kind key per field** *(resolves MUST-11)*. `lp` and `switch` are both kind keys and **may not appear on the same object**. The v1 TLS example was illegal under v1's own rule; the fix is nesting, not a rule change:

```jsonc
{ "lp": { "len": "u16be" }, "id": "extBody", "fields": [
    { "switch": "item.type",
      "cases": { "0x0000": [ … ], "0x0010": [ … ] },
      "default": [ { "bytes": {"len":"remaining"}, "as":"opaque" } ] } ] }
```

* *Forced by:* SNMP-G6, Modbus-G3 (FC 0x03 vs 0x83 — one request, two structurally different responses), mDNS-G5, CoAP value types, DHCP-G7, TLS-G4 (record content_type *and* extension_type), NTP-G10, BACnet-G2, MQTT packet-type dispatch.

---

#### 3.4.10 `let` — **LOAD-BEARING.** Zero-width derived binding

```jsonc
{ "let": "delta", "value": "if(item.dNib < 13, item.dNib, if(item.dNib == 13, item.dE + 13, item.dE + 269))" }
{ "let": "upstreamIp", "value": "capture.refIdRaw |> unipv4", "when": "capture.stratum >= 2" }
```

Consumes and emits zero octets; binds into the current frame. This is how a field is **reinterpreted** without re-reading it: NTP's stratum-discriminated Reference ID, CoAP's delta/length arithmetic, BACnet's tag decomposition.

* **validator** — `value` required; may not reference layout functions on encode (V10).
* *Forced by:* NTP-G8, CoAP-G1/G5, BACnet-G5.

---

#### 3.4.11 `assert` — **LOAD-BEARING.** Cross-field invariants at parse position

```jsonc
{ "assert": "capture.length == capture.byteCount + 3",
  "onFail": "error", "message": "MBAP length disagrees with PDU byte count" }
{ "assert": "remaining() == 0", "message": "trailing octets after PDU" }
```

* **decode** — evaluate at this position; on false apply `onFail` ∈ `error` | `warn`.
* **encode** — evaluated after layout; `error` aborts the encode.
* `assertConsumed` is deleted — it is `{"assert":"remaining() == 0"}`, and one spelling is enough.
* **`onFail` has two values, not three.** `tolerate` existed only to feed the T3 raw shadow, which is gone.
* *Forced by:* Modbus-G8, TLS-G12, BACnet-G10, SSDP-G17, DHCP-G7.

---

#### 3.4.12 `pad`

```jsonc
{ "pad": { "to": 300, "of": "message", "with": "00", "side": "right",
           "onOverflow": "error" }, "as": "padLen" }
```

`of` ∈ `message` | `region` | `<groupId>`. `onOverflow` ∈ `error` (default) | `ignore` | `truncate`.

* **encode** — write fill until the named frame reaches `to`.
* **decode** — consume fill to the target; bind the observed fill length to `as`.
* **T1 note.** Pad *quantity* is a free wire choice for DHCP. Under T1 the padding length is a capture (`as`) and round-trips; under T2 the encoder emits exactly `to`. Reproducing a *different* server's pad quantity is T3 and is out of scope.
* *Forced by:* DHCP-G18/G21 (300-byte BOOTP minimum), NTP.

---

#### 3.4.13 `checksum` — keyed digests, byte-width results, anchored spans

```jsonc
{ "checksum": { "alg": "md5",
                "prefix": "consts.macKey |> unhex",
                "over": "span(message, here)",
                "as": "bytes:16" },
  "id": "mac" }
```

* `prefix` / `suffix` — key material affixed to the digest input that is **not on the wire**. NTP's symmetric MAC is `MD5(key ‖ message)`, a prefix-MAC, not HMAC. This is the general keyed-digest facility; it is not NTP-specific.
* `as` accepts a scalar name (`"u16le"`) or `"bytes:N"`.
* `over` must name a resolved span — `<groupId>` or `span(a, b)` with `here` legal as the second argument. A bare `start` is a validation error: three-deep nesting made it ambiguous (V25).
* **decode** — recompute and compare; `onMismatch` applies.
* *Forced by:* NTP-G6 (unreachable by any other construct — capture C's 68 bytes are 48 header + 4 key id + 16 MD5 over key‖header).

---

#### 3.4.14 `ref` — parameterised, boundedly recursive

```jsonc
"parse": { "ctxTag": { "params": ["num","lvt"],
                       "fields": [ { "bits": [ {"n":4,"value":"params.num"},
                                               {"n":1,"value":"1"},
                                               {"n":3,"value":"params.lvt"} ] } ] } }
…
{ "ref": "parse.ctxTag", "with": { "num": 0, "lvt": 4 } }
{ "ref": "parse.berValue", "maxDepth": 16 }        // self-recursion, bounded
```

Self-reference is legal **only** with an explicit `maxDepth` ≤ 32, so the work bound stays computable. This is what makes the ASN.1/BER family and BACnet constructed data expressible without writing every shape longhand.

* *Forced by:* SNMP-G8, BACnet-G11.

---

#### 3.4.15 `seek` — zero-width backward positioning

```jsonc
{ "seek": { "to": "108", "len": 128 },
  "when": "capture.optionOverload band 1 != 0",
  "fields": [ { "each": { "until": {"when":"remaining() == 0"}, "max": 64 }, "fields": [ … ] } ],
  "as": "fileOptions" }
```

Opens a region at an absolute offset **without moving the outer cursor**, parses a field list inside it, restores. Region 0 is the reference frame for `to`.

* **decode** — `to` must resolve **strictly backward** of this field's own offset (§3.2 guarantee 4). No second, weaker statement of the rule exists.
* **encode** — legal only into a span already reserved by a preceding fixed-size field; back-patched in Phase F.
* **validator** — `to` must be an expression over region-0 offsets; `hops` decrements per `seek` (V26).
* *Forced by:* DHCP-G5 (option 52 Option Overload redirects the option parse backward into `file` at 108 and `sname` at 44), and it is the mechanism the `labelSeq` sugar is built on.

---

#### 3.4.16 `register` — **the general encode-side offset table** *(replaces `labelSeq` primitive)*

```jsonc
"offsetTables": { "names": { "scope": "message" } }      // message-level declaration
…
{ "register": { "table": "names", "key": "item.suffix" } }   // encode-only, zero-width
```

Records `(key → currentAbsoluteOffset)` in the named table at this point in the emission. The companion expression function is **`lookupOffset(table, key)`** → `Int` or `Null` (§6.3).

This is the only genuinely new machinery `labelSeq` had, and it is now a general facility: any protocol with back-referenced strings (DNS, mDNS, LLMNR, NBNS, and the NBNS-family second-level encoding) uses it. `labelSeq` itself is demoted to dumpable sugar (§3.5.3) — **the dedupe rule that demoted `tlv` applies to itself.**

* **encode** — zero octets; writes the table entry. A `switch` reading `lookupOffset` is the declared compression feedback node (§3.3 Phase D).
* **decode** — no-op.
* **validator** — `table` must be declared in `offsetTables`; `key` must be pure (V27).

---

#### 3.4.17 `skip` — decode-only advance

```jsonc
{ "skip": "16 - capture.hlen" }
```

Consumes N octets and binds nothing.

* **validator** — decode-only; illegal on encode (V28). **`skip` destroys information and therefore breaks T1** for any input that maps into the skipped span; the validator emits a **warning** naming the field, and `skip` inside a message with any `captures`-declared input covering that span is an error (V29). Prefer `{"bytes":{"len":…},"as":…}`.

---

#### 3.4.18 `at: "transmit"` — late-bound fields

```jsonc
{ "bytes": { "value": "now.utc |> ntp64" }, "size": 8, "at": "transmit", "id": "t1" }
```

Deferred to the socket-write path. Constraints, all validate-time:

* must be **fixed-width** (V30);
* **may participate in a `len` span** — its width is fixed, so its contribution to any length is known at layout time. (v1's blanket "must not participate in any `len`/`checksum` span" forbade the construct in every protocol with an outer length field. Relaxed.)
* **must not participate in any `checksum` span** (V31) — which correctly *rejects* an NTP packet that both carries a symmetric MAC and late-binds T1. That combination is refused, not faked; see §0.4.
* illegal in decode mode (V32).

The receive counterpart is `meta.rxUtc` (§6.2), which is NTP's T4 and appears nowhere in the 48 octets.

* *Forced by:* NTP-G9. Without it an NTP encoder folds its own packet-construction latency into the offset it exists to measure.

---

### 3.5 Sugar — three constructs, all with published, dumpable expansions

`--desugar` emits the expansion; the expansion is a **test artefact**, checked byte-for-byte against a hand-written equivalent in the fixture suite (V40).

#### 3.5.1 `tlv` / `tlvList`

```jsonc
{ "tlv": { "t": "u8", "l": "u8", "bare": ["0x00", "0xff"] }, "fields": [ … ] }
```
expands to
```jsonc
[ { "u8": null, "as": "type" },
  { "switch": "capture.type",
    "cases": { "0x00": [], "0xff": [ { "let": "end", "value": "true" } ] },
    "default": [ { "lp": { "len": "u8" }, "fields": [ … ] } ] } ]
```

`tlvList` = `each` with `until` over `tlv`. The `bare` list (length-less codes — DHCP PAD `0x00`, END `0xff`) is what a fixed T-L-V walker could never express *(DHCP-G2)*.

#### 3.5.2 BER (SNMP) — no BER-specific construct exists

```jsonc
{ "u8": null, "as": "tag", "expect": "0x30" },
{ "lp": { "len": { "varint": { "encoding": "ber" } } }, "id": "msg", "fields": [ … ] }
```

That is the whole of it. Nested BER is `lp` inside `lp`; recursion is `ref … maxDepth`. Object identifiers are the `oid` converter (§6.4). *Closes SNMP-G1..G10.*

#### 3.5.3 `labelSeq` — **demoted to sugar** *(critique 5.1)*

```jsonc
{ "labelSeq": { "unit": {"len":"u8","max":63}, "terminator": "00",
                "separator": ".", "table": "names",
                "backref": { "mask":"c0", "match":"c0", "offsetBits":14, "maxHops":16 },
                "maxDecodedLen": 255, "compress": "auto" },
  "value": "inputs.service", "as": "qname" }
```

**Decode expansion:**
```jsonc
[ { "each": { "bind": "labels", "max": 128,
              "until": { "when": "peek('u8') == 0 || capture.jumped" } },
    "fields": [
      { "switch": "peek('u8') band 0xc0",
        "cases": {
          "0xc0": [ { "bits": [ {"n":2}, {"n":14,"as":"ptr"} ] },
                    { "seek": { "to": "item.ptr" }, "fields": [ { "ref": "parse.labelSeq" } ],
                      "as": "tail" },
                    { "let": "jumped", "value": "true" } ] },
        "default": [ { "lp": { "len": "u8" }, "as": "label", "via": "latin1" } ] } ] },
  { "const": "00", "when": "!capture.jumped" } ]
```

**Encode expansion (`compress: "auto"`):**
```jsonc
[ { "each": { "over": "inputs.service |> suffixes('.')", "bind": "labels", "max": 128,
              "until": { "when": "capture.emitted" } },
    "fields": [
      { "let": "hit", "value": "lookupOffset('names', item.suffix)" },
      { "switch": "item.hit == null",
        "feedback": { "seed": "false", "direction": "nonIncreasing" },
        "cases": {
          "false": [ { "bits": [ {"n":2,"value":"3"}, {"n":14,"value":"item.hit"} ] },
                     { "let": "emitted", "value": "true" } ],
          "true":  [ { "register": { "table": "names", "key": "item.suffix" } },
                     { "lp": { "len": "u8" }, "value": "item.label |> ascii" } ] } } ] },
  { "const": "00", "when": "!capture.emitted" } ]
```

`compress: "never"` omits the `switch` and always takes the `true` arm — the correct setting for RFC 2782 unicast SRV targets *(mDNS-G7)*. There is no `dnsName` converter: a converter cannot see offsets.

*Forced by:* mDNS-G1/G2/G3/G7. The `c0 46` in the corpus response points **into another record's RDATA** and `c0 17` points at a mid-name suffix, so the decoder must hold the whole `buf` — which §3.2 already guarantees.

---

### 3.6 `invariants` — the single legality mechanism *(resolves MUST-6)*

```jsonc
"invariants": [
  "inputs.hasPassword == 0 || inputs.hasUsername == 1",
  { "expr": "!(capture.proxyUri != null && (capture.uriHost != null || capture.uriPath != null))",
    "message": "Proxy-Uri cannot combine with Uri-* options" },
  "inputs.startAddress + inputs.quantity <= 0x10000"
]
```

* **One mechanism, one namespace.** The §5 document's `constraints` key is **deleted**. A rule about a message's own fields lives here. A rule about *which messages are legal in which protocol state* lives in §5's `model` and refers to nothing inside a message except captures by bare id.
* **One naming scheme**, as §3.1: bare ids under `capture.*` / `item.*` / `inputs.*`. No `f.*`, no `field:X`, no bare option codes.
* **`exclusive` sugar is deleted** — write the boolean. One form.
* Evaluated at **validate** time where every operand is constant, at **encode** time otherwise, and asserted on **decode**.
* *Forced by:* MQTT-G15 (`connectFlags = 0x4a` is illegal and v0 emitted it happily), CoAP-G14, Modbus-G14, SSDP-G19.

---

### 3.7 `limits`

```jsonc
"limits": { "maxIterations": 4096, "maxRegionDepth": 32, "maxRefDepth": 16,
            "maxHops": 16, "maxScanBytes": 8192, "maxDecodedBytes": 65535,
            "maxFrameBytes": 65535, "maxCaptureBytes": 262144 }
```

Document-level defaults; a message may override. `layoutIterations` is **not** an author-settable limit — it is computed at validate time from the SCC bound (§3.3 Phase D).

`maxCaptureBytes` bounds the *memory* of the capture tree, not just the work — v1 bounded work and left memory open for accumulating decoders.

---

## §4 Round-trip tiers

### 4.1 The capture model

A message's **capture model** is the set of names bound by `as` and `present`, with their frame structure (`each` produces `List<Record>`; `group`/`lp` do not create a capture frame unless they contain `as`-bound fields).

The correspondence between inputs and captures is **declared, not inferred**:

```jsonc
{ "name": "quantity", "kind": "prompt", "type": "u16", "min": 1, "max": 125,
  "captures": "quantity",
  "description": "How many consecutive holding registers to read (1-125)." }
```

`captures` names the capture path that must equal this input after a round trip. Inputs without `captures` are excluded from T1 and the validator warns (V35).

### 4.2 The tiers

| Tier | Statement | Required of | How it is established |
|---|---|---|---|
| **T1 — semantic** | `decode(encode(x)) ≡ x` restricted to inputs declaring `captures` | **every** message | Static validator rules (V29, V35, V36) **plus** generated property tests over the declared input space |
| **T2 — canonical bytes** | `encode(x) == b` for the corpus captures listed in §4.3 | every canonically-encoded corpus capture | The fixture suite. Failure is a build failure. |
| **T3 — byte-exact for arbitrary `b`** | — | **nothing. Out of scope.** | §0.1 |

**T1 is not claimed to be statically decidable, and v1's claim that it was is withdrawn.** `prompt` inputs of `type: "text"` are unbounded, so a randomized property test finds counterexamples over a *sampled* space. What v2 *does* guarantee statically is the set of conditions under which T1 is *known to fail*: a `skip` overlapping a `captures`-declared input (V29); an input with no `captures` and no `when` guard consuming it (V35); a `when`-guarded field whose presence is not recoverable because it lacks `present` and its value domain includes the empty value (V36 — this is CoAP-G17a generalised). Everything else is property-tested, honestly labelled as such.

### 4.3 Which corpus captures are T2, and which are not

| Protocol | Captures | T2? | Why |
|---|---|---|---|
| SNMPv2c | 73 B request, 337 B response | **yes** | All lengths minimal (`47`; `82 01 4d` for 333; `81 f7` for 247). One document emits both byte-exactly — Phase C resolves the nesting bottom-up as a DAG, Phase D's Kleene from minimum width converges. This was the single hardest claim in the corpus and it holds. |
| Modbus TCP | 12 / 15 / 9 B | **yes** | No encoding freedom anywhere. Requires §2.3's ownership rule to be true. |
| CoAP | 81 / 42 / 11 B | **yes** | Option order is fixed ascending; the 13/14 escape is forced by magnitude (13 and 268 take the 8-bit form, 269 forces the 16-bit form); repeated-option order is semantic and preserved. |
| MQTT 3.1.1 | 146 / 4 / 56 / 7 / 2 / 2 B | **yes** | Captures use minimal varints; `minimal: true` rejects the alternatives on decode. |
| NTPv4 | 48 / 48 / 68 B | **yes**, with a caveat | Fixed layout. Capture C's MAC forbids `at:"transmit"` (V31), so T1 is sampled at encode. |
| BACnet/IP | 17 / 32 / 37 / 10 / 13 B | **yes** | Captures use minimal LVT and minimal integers. Non-minimal LVT on the wire decodes but re-encodes minimally — that is T3. |
| mDNS | 34 / 127 B | **yes, under `compress:"auto"`** | The 127-byte response is maximally compressed; `compress:"auto"` reproduces it. `compress:"never"` gives 215 bytes and a different, also-legal packet. |
| DHCP | 300 / 306 B | **only with declared order** | Option order and pad quantity are free on the wire. A document that lists the options in the captured order and pads to 300 emits both captures exactly. A *different* server's ordering is T3. |
| SSDP | 142 / 315 B | **only with declared order** | Header order and OWS runs are free. A document declaring the exact header sequence emits both exactly — including `EXT:` as six octets, via `whileAnyOf` binding a zero-length run. A general SSDP parser gets T1 only. |
| TLS 1.2 | 196 / 113 B | **only with declared order** | Extension order is free (and is itself a fingerprint). Same disposition as SSDP. |

This table is the T2 contract. Seven of ten are unconditionally T2; three are T2 for a document that declares the observed ordering as document structure, which is exactly what the corpus fixtures do.

---

## §6 Expressions, types, converters

### 6.1 Operators

Tightest binding first:

| prec | operators | assoc |
|---|---|---|
| 1 | `x[i]` `x.y` `f(…)` | left |
| 2 | `!` `~` unary `-` | right |
| 3 | `*` `/` `%` | left |
| 4 | `+` `-` | left |
| 5 | `<<` `>>` | left |
| 6 | `band` / `&` | left |
| 7 | **`bxor` / `^`** | left |
| 8 | `bor` / `\|` | left |
| 9 | `<` `<=` `>` `>=` | left |
| 10 | `==` `!=` | left |
| 11 | `&&` | left |
| 12 | `\|\|` | left |
| 13 | `??` | right |
| 14 | `? :` | right |
| 15 | **`\|>`** pipeline: `a \|> f(b)` ≡ `f(a, b)` | left |

**The pipeline is `|>` and binds loosest**, so `capture.fc |> band(0x80) != 0` parses as `(fc & 0x80) != 0`.

**`^` is bitwise xor. Exponentiation is `pow(a, b)`.** Any expression of the form `2 ^ x` in a v1-era document is a bug: with NTP's `poll = 6` it computes `4`, not `64`. The migrator (§6.1.1) flags every `^` in a v1 document as a hard error requiring author review; it does **not** guess.

**Functions** (multi-argument, not pipelineable): `if(c,a,b)` · `min(a,b)` · `max(a,b)` · `abs(x)` · `pow(a,b)` · `peek(scalarName, offset?)` · `lookupOffset(table, key)` · `now()` · plus the layout functions of §6.3.

`if()` is load-bearing: CoAP's nibble is `if(len < 13, len, if(len < 269, 13, 14))` and there is no way to conditionally emit half a byte with `when`.

**`peek` has exactly one syntax**: the function `peek('u8')`, `peek('u16be', 2)`. The `{"peek":…,"equals":…}` object form is deleted *(resolves MUST-14)*.

#### 6.1.1 v1 → v2 migration

The validator accepts `"protocol": 1` and mechanically rewrites: any `|` whose right operand is a closed-set converter name becomes `|>`. It then **hard-errors**, without guessing, on: any remaining `^`; any `|` whose right operand is an unknown identifier; `roundTrip`, `canonical`, `keepRaw` read by an encode expression, `tagBefore`, `ignoreIf`, `parseAs`, `labelSeq` as a primitive with `anchor`, `each.sortBy`, `each.coalesce`, `assertConsumed`, `endOfRegion`, `endOfInput`, `rest`, `atEnd`, `exhaustive`. Each error names the v2 replacement.

### 6.2 Types

```
ProtoValue = Bytes | Int | Number | Bool | Text | Instant | Duration | List<T> | Record | Null
```

* **`Int` is i64.** Overflow is a runtime error. `/` on two `Int`s is truncating integer division.
* **`Number` is f64.** **Bare decimal literals containing a `.` or an exponent are `Number`** (`0.75`, `1.5`, `1e3`). Integer literals and `0x…` are `Int`. Without `Number` literals, `keepAlive * 0.75` is `keepAlive * 0` and the MQTT keepalive obligation fires every zero seconds.
* **`Bool` is a ProtoValue.** All six comparisons, `&&`, `||`, `!` produce `Bool`. **Coercion `Bool → Int` is defined: `false → 0`, `true → 1`, applied implicitly wherever an arithmetic or bitwise operator receives a `Bool`.** There is no `Int → Bool` coercion; write `x != 0`. Without this, `depth + (item.lvt == 6) - (item.lvt == 7)` — v1's own answer to BACnet-G3 — does not type-check under v1's own type list.
* **`Record`** has named members and is what `each … yields:"record"` produces. **There is no `Map`.**
* **Indexing**: `x[0]`, `x[-1]` (last), `x[expr]`. Out-of-range yields `Null`, and `Null` **suppresses** an enclosing `when` (treated false) and an enclosing `emits`, rather than erroring.
* Literals: `0x…`/decimal `Int`; decimal-with-point/exponent `Number`; `'…'` `Text`; `"aa bb"` in a `const` slot `Bytes`; `true`/`false`/`null`.

**Paths:**

```
inputs.*   consts.*   params.*                      // params.* = ref arguments
device.*   (mac, ip, hostname, fact("…"))
adapter.*  (ip, mac, broadcast, prefix, gateway, name)
dest.*     (ip, port, hostport)                     // bracket-correct for IPv6: [FF02::C]:5353
meta.*     (rxUtc, txUtc, srcIp, srcPort, dstPort, datagramLen, frameLen,
            fragmentCount, assembledLen)
capture.*  capture.^…  capture.$                    // frame chain (§3.2)
item.*     item.@index  item.@ordinal
hdr.*                                               // ONLY inside a reassemble layer (§2.5)
state.*  session.*  store.*  now.*  random.*  seq.*  // scopes owned by §5
sent.<stepId>.<id>                                  // resolved field values of an already-sent message
```

* **`dest.*`** removes the silent duplication where SSDP's `HOST:` header and the udp layer both spell the destination with nothing keeping them in sync *(SSDP-G8)*.
* **`meta.rxUtc`** is NTP's T4, which appears nowhere in the 48 octets *(NTP-G9)*.
* **`sent.<stepId>.<id>`** gives correlation a field-level handle. v0's only route to a sent message was `msg.<name>`, which yields *bytes*, forcing a hard-coded `slice(0,2)` inside correlation logic *(Modbus-G5, NTP-G11, CoAP-G11)*.
* **Setting a transport destination from a capture (SSDP-G15) is §5's `variants.transport`, not §6's.** `dest.*` is the read direction only. Named here so the seam is not silently crossed twice.

### 6.3 Layout functions — one naming scheme, bare ids

| function | encode | decode |
|---|---|---|
| `len(id)` | span length | span length |
| `len(id@i)` / `len(id@all)` | i-th / all iterations of a group inside `each` | same |
| `span(a, b)` | length between two ids; `here` legal as `b` | same |
| `bytes(id)` | span octets | span octets |
| `offsetOf(id)` | absolute offset | absolute offset |
| `octets(id)` | emitted octet count (Phase F back-patch target) | consumed octet count |
| `count(id)` | number of `each` iterations | number of items |
| `remaining()` · `consumed()` · `offset()` | **validation error** | relative to the innermost region |
| `peek(scalar, off?)` | **validation error** | non-consuming read |
| `lookupOffset(table, key)` | registered offset or `Null` | **validation error** |

`bytes(id)` is what makes NTP's keyed digest over the message writable at all. `octets(id)` is a legal `bits` slice value and is BACnet's LVT back-patch.

**`len(after:X)` and `len(from:A to:B)` are deleted** in favour of `span(a, b)`. **`group:`/`field:` prefixes are deleted.** One scheme.

### 6.4 Converters — closed set, purity declared per converter

**Rules.** Every converter declares accepted and produced kinds, so type errors are validate-time (V1). Every converter usable in `via` declares an inverse (V2). **Every converter in v2 is pure** — a deterministic function of its arguments — and the table says so explicitly per row. There is no impure row: v1's `delta32`/`delta64`/`rate` are cut (§0.2) and `unntp64`'s era ambiguity is resolved by making the era an **explicit argument**, `unntp64(eraHint)`, rather than a hidden clock read.

| group | converters | pure | inverse |
|---|---|---|---|
| **text/encode** | `hex unhex` · `ascii unascii` · `utf8 unutf8` · `latin1 unlatin1` · `base64 unbase64` · `mac unmac` · `ipv4 unipv4` · `ipv6 unipv6` · `cstr` | yes | paired |
| **numeric** | `u8 … f64be` · `int hexint be le` · `minuint unminuint` · `minint unminint` · `base128 unbase128` · `oid unoid` · `fixed(i,f,signed) unfixed(…)` · `decimal(w?,pad?) undecimal` · `mod(n)` · `clamp(lo,hi)` | yes | paired |
| **bytes** | `concat repeat(n) slice(off,len) pad(n,fill,side) fit(n,fill,side) reverse xor(k) take(n) drop(n) takeLast(n) dropLast(n) chunk(n) len` | yes | `fit`/`pad` ↔ `cstr`; others one-way, flagged decode-hostile |
| **digest** | `md5 sha1 sha256 crc16(poly) crc16modbus crc32 hmacSha256(k) internetChecksum` | yes | none (one-way by nature; legal only in `checksum`) |
| **text ops** | `upper lower trim split(sep) join(sep) format(fmt) startsWith(s) contains(s) suffixes(sep)` | yes | `split`↔`join` |
| **list** | `count first last index(n) find(pred) filter(pred) map(f) any(pred) all(pred) sortBy(member) mergeBy(keyMember, valueMember) lookupBy(member, value) allBy(member, value) lookup(table)` | yes | none |
| **time** | `ntp32 unntp32` · `ntp64 unntp64(eraHint)` · `ntp128 unntp128(eraHint)` · `epochSeconds unepochSeconds` · `epochMillis unepochMillis` · `httpDate unhttpDate` · `iso8601 uniso8601` · `seconds` (Int→Duration) | yes | paired |

**Why each non-obvious one is load-bearing:**

* `minuint`/`minint` — SNMP request-id 821915 must be `0c 8a 9b` (3 octets); `i32be` gives 4, which is the wrong bytes *and* the wrong declared length. CoAP Content-Format 0 must be a **zero-octet** value. BACnet property-id 512 must be `1a 02 00`. *(SNMP-G3, CoAP-G7, BACnet-G5)*
* `base128` / `oid` — `1.3.6.1.4.1.2021.10.1.3.1` → `2b 06 01 04 01 8f 65 0a 01 03 01`: first-arc merge `40x+y` plus per-arc base-128, arcs 1–5 octets. Unreachable from `split`/`join`. Lossless both ways because BER forbids non-minimal arcs. *(SNMP-G2)*
* `fixed`/`unfixed` — NTP root delay `0x0000028f` is 0.0099945 s in 16.16. Subsumes Modbus scaled registers and BACnet. *(NTP-G3)*
* `decimal`/`undecimal` — every number in SSDP is decimal ASCII. Without it `MX` must be typed `text`, killing its 1–5 bound and the `mx * 1000` listen window. *(SSDP-G5)*
* `httpDate`/`unhttpDate` — `Sun, 09 Aug 2026 12:00:00 GMT`; `iso8601` produces the wrong 20 octets. `unhttpDate` accepts the two obsolete RFC 850 / asctime forms; `httpDate` emits only IMF-fixdate. **Converters may declare asymmetric accept/emit sets, and this is the only one that does.** *(SSDP-G6)*
* `ntp32/64/128` — NTP has three timestamp widths; v0 had one unwidthed name with no inverse. `unntp64(eraHint)` takes the era disambiguator as an argument, making it pure. *(NTP-G2, NTP-G15)*
* `cstr` / `fit` — DHCP `sname`/`file` are fixed-width NUL-padded; `pad` was silent on over-length input and had no decode inverse. *(DHCP-G20)*
* `chunk(n)` / `mergeBy` — RFC 3396 splits an option longer than 255 octets into repeated instances of the same code that MUST be concatenated on receipt. `chunk` on encode, `mergeBy('code','value')` on the decoded list. This is where `coalesce` went. *(DHCP-G6)*
* `sortBy(member)` — applied to the *source list* before `each`, preventing an out-of-order CoAP option list from silently wrapping to a negative delta. This is where `each.sortBy` went. *(CoAP-G15)*
* **`lookupBy` / `allBy` — how a `List<Record>` is keyed** *(closes the open question)*. `capture.headers |> lookupBy('name', 'LOCATION')` returns the first `Record` whose `name` member matches, or `Null`; the value is `(… ).value`. **Comparison is ASCII-case-insensitive when both operands are `Text`**, which is what HTTP header names need. `allBy` returns every match in wire order, which is what repeated headers need. *(SSDP-G4/G20)*
* `lookup(table)` — `capture.returnCode |> lookup(consts.connackReasons)`, where the table is a `consts` `Record`. **This is the declarative replacement for `isCertBased`/`isEphemeral`/`isStaticRsa`**: a cipher-suite classification is a `consts` table, not three engine predicates. *(critique 6.2)*
* `suffixes(sep)` — `'a.b.c' |> suffixes('.')` → `['a.b.c','b.c','c']`. Drives the `labelSeq` encode expansion; also serves any suffix-shared-string protocol.
* `find`/`any`/`filter` — TLS extension lookup. *(TLS-G11)*
* `count` — DNS section counts QDCOUNT/ANCOUNT/NSCOUNT/ARCOUNT from a data-driven record list. *(mDNS-G8)*

**Deleted:** `dnsName`/`undnsName` (§3.5.3), `bcd`/`unbcd`, `jsonPath`, `regex`, `xpath`, `sha512`, `delta32`/`delta64`/`rate`, `keys`/`values`/`key`/`all` (the `Map` accessors), `cmp`, `lexicographicBytes`, `since`, `has`.

**Not corpus-forced, retained deliberately:** `base64`, `leb128u`, `hmacSha256`, `sha1`, `crc32`, `internetChecksum`. The set is closed and extending it is a code change, so it is cheaper to over-populate once than to ship a code change per new protocol. Everything else in the table has a named corpus forcing case.

### 6.5 Injectable fields

```jsonc
{ "name": "quantity", "kind": "prompt", "type": "u16", "min": 1, "max": 125,
  "captures": "quantity",
  "description": "How many consecutive holding registers to read (1-125)." }

{ "name": "subscriptions", "kind": "prompt", "type": "list<record>",
  "schema": { "topic": "text", "qos": { "type": "u8", "min": 0, "max": 2 } },
  "captures": "subscriptions",
  "description": "Topic filters and the QoS to request for each." }

{ "name": "options", "kind": "internal", "type": "list<record>",
  "schema": { "code": "u8", "value": "bytes" }, "captures": "options" }
```

* `kind` ∈ `internal` | `prompt` | `constant` | `capture`.
* `type` ∈ `text int u8..u64 i8..i64 number bool bytes hex mac ipv4 ipv6 port enum secret instant duration list<T> record list<record>`. **No `map<K,V>`.**
* **`min` / `max` / `pattern` / `enumValues`** are checked at input-binding time, before a byte is emitted — Modbus's 125-register cap, SSDP's MX 1–5, DHCP unit ranges, all previously discoverable only from the device's error response one round trip later. *(Modbus-G14, SSDP-G19)*
* **`record` / `list<record>` with `schema`** is what makes CoAP option sets, MQTT subscriptions, SNMP varbinds, DHCP options and TLS extensions **data rather than document structure**. Field access inside `each` is `item.<member>`. Six of ten agents were forced to hand-unroll their documents without it.
* **`captures`** declares the T1 correspondence (§4.1).
* `description` is **mandatory** on every `prompt` input.
* `seq` gains a shape, referenced as `seq.<id>.next`:
  ```jsonc
  { "id": "mqttPacketId", "width": "u16", "min": 1, "max": 65535, "wrap": true, "scope": "session" }
  ```
  A bare `seq.next` with no modulus silently breaks Modbus after 65 536 transactions and MQTT after 65 535 packets. *(Modbus-G10, MQTT-G16)*

---

## §7 Validator rules

All checked before a byte moves. Numbered for citation in diagnostics.

**Types and converters**

1. **V1** Every converter's accepted/produced kinds type-check across each pipeline; `Bool → Int` coercion is applied where §6.2 defines it, and nowhere else.
2. **V2** Every converter used in `via` declares an inverse. A one-way converter in `via` is an error naming the converter.
3. **V3** Every operator's operand kinds type-check. `/` on `Int`,`Int` is truncating; on any `Number` it is float division.
4. **V4** An `Int` literal or constant-folded expression that overflows i64 is an error.
5. **V5** A converter argument that must be constant (`repeat(n)`, `chunk(n)`, `decimal(w)`) is constant-folded or errors.

**Layers and framing**

6. **V6** A framer's prefix span `[at, at + prefixWidth)` must be covered by exactly one template field, which may not straddle the boundary. *(§2.3 — the framer never owns wire bytes.)*
7. **V7** A message whose layer stack contains `reassemble` is decode-only; any encode reference to it is an error.
8. **V8** Field `id`s are unique within a message. `id` defaults to `as`. Both `id` and `as` present with different values is legal; both defaulting to the same name in two fields is an error.

**Field-level**

9. **V9** A decode expression may not reference a capture bound later in the same field list. Decidable by field index.
10. **V10** An **encode-side** `when`, `let.value` or `switch.on` may not reference `len`/`offsetOf`/`octets`/`bytes`/`count`. Single exception: a `switch` carrying an explicit `feedback` declaration and reading `lookupOffset`.
11. **V11** `onMismatch: "absent"` requires `present`.
12. **V12** The layout dependency graph must be acyclic, **or** every SCC must consist solely of declared feedback nodes.
13. **V13** Every SCC must be **homogeneous in direction**. A mixed non-decreasing / non-increasing SCC is an error naming both nodes. *(Resolves MUST-8.)*
14. **V14** A `bytes` field carries at most one of `len` / `until` / `whileAnyOf`.
15. **V15** `bits` slices sum to a multiple of 8.
16. **V16** A `bits` slice is 1..32 bits and carries no `via` and no `when`.
17. **V17** `lp.covers.expr` is affine in `bodyLen` with coefficient 1.
18. **V18** `lp` carries exactly one of `fields` / `value`.
19. **V19** `group.extent` and `group.over` are mutually exclusive.
20. **V20** `group.over` requires `maxDepth` ≤ 32.
21. **V21** `each` declares `max`, or inherits `limits.maxIterations`.
22. **V22** `each` in an encodable message declares `over`.
23. **V23** `each.fold.next` references only `item.*` and its own register.
24. **V24** A `switch` in an encodable message declares `on`.
25. **V25** `checksum.over` names a group id or `span(a, b)`. A bare `start` is an error.
26. **V26** `seek.to` resolves relative to region 0 and, on decode, must be provably or dynamically strictly backward of the `seek` field's own offset.
27. **V27** `register.table` is declared in `offsetTables`; `register.key` is a pure expression over `item.*`/`inputs.*`.
28. **V28** `skip` is illegal on encode.
29. **V29** `skip` overlapping the span of any input declaring `captures` is an error; any other `skip` is a T1 warning naming the field.
30. **V30** `at:"transmit"` fields are fixed-width.
31. **V31** `at:"transmit"` fields do not participate in any `checksum` span. (They **may** participate in a `len` span.)
32. **V32** `at:"transmit"` is illegal in decode mode.
33. **V33** A message containing both a `checksum` over the whole frame and an `at:"transmit"` field is an error, with the diagnostic naming NTP symmetric auth as the known case.
34. **V34** No encode-side expression references a `keepRaw` name.
35. **V35** Every input without `captures` is a T1 warning naming the input.
36. **V36** A `when`-guarded field whose value domain includes the empty value and which does not declare `present` is an error. *(CoAP-G17a generalised.)*

**Expressions and mode**

37. **V37** `remaining()`, `consumed()`, `offset()`, `peek()` are illegal in encode mode; `lookupOffset()` is illegal in decode mode.
38. **V38** Self-recursive `ref` declares `maxDepth` ≤ 32.
39. **V39** Every `invariants` entry type-checks to `Bool`; entries whose operands are all constant are evaluated at validate time and must hold.

**Sugar**

40. **V40** Every sugar construct (`tlv`, `tlvList`, `labelSeq`) has a published `bytes` expansion, dumpable via `--desugar`, and the fixture suite compares the expansion's output byte-for-byte against a hand-written equivalent. **This is v2's complete sugar set** — there are no sugar *layers*, so the rule holds over three constructs, all expanded in §3.5, rather than failing over seven unwritten layer macros.

**Round-trip**

41. **V41** For every corpus capture listed as T2 in §4.3, `encode(inputs) == captureBytes`. Failure is a build failure.
42. **V42** For every message, the generated T1 property test over the declared input space passes. Failure names the input, the differing capture path, and the byte offset.

