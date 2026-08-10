<!-- Generated from a ten-protocol grammar stress test; see docs/dynamic-protocol.md for status,
     provenance and the list of open blockers. Do not edit casually — the corpus in
     src/Nexaflow.Tests/Nexaflow.Tests.Features/Protocol/Corpus/ is what validates every claim here. -->
I have everything I need — the critique, both v2 documents, and the ten captures traced byte by byte.

---

# Final review — v2 grammar + v2 state model

Verified against `critique.md` and all ten captures in `protocol-fixtures.json` (offsets computed, not eyeballed).

---

## 1. The 14 MUST-change items, one at a time

| # | Item | Status | Evidence |
|---|---|---|---|
| **1** | Framing: Modbus-G1 ownership + MQTT-G3 varint framer | **RESOLVED (grammar) / RE-BROKEN (state model)** | Grammar §2.3 states the rule normatively ("the framer never owns wire bytes… hands the *entire* frame, prefix bytes included, as region 0"), enforced by V6. Verified against the capture: `00 01 00 00 00 06 11 03 00 6b 00 03`, `value`=6, `at`=4, `prefixWidth`=2, default `totalIs` → 12 ✓. §2.4 adds `varintPrefixed`/`mqtt7`; MQTT CONNECT `10 8f 01` → 143+3 = 146 ✓. **But §5.14.2's frame stack still writes `{"kind":"lengthPrefixed","prefix":"u16be","at":3,"includesPrefix":false}`** — the exact boolean §2.4 deleted, and the exact thing critique §1.2 said must go. See §2/B2. |
| **2** | Compose layout graph with raw shadow | **GONE with T3** | Grammar §0.1 deletes the mechanism, `@form`, raw-preference, `roundTrip:"byteExact"`, and v1 validator rules 9/10. V34 forbids encode-side reads of `keepRaw`. §5.0.1 concurs. |
| **3** | Position-dependent raw spans go stale | **GONE with T3** | Same. |
| **4** | Operators (`\|>`, `pow`, `Number`, `Bool`) | **RESOLVED** | Grammar §6.1 row 15 `\|>`; `^` is bxor and §6.1.1 *hard-errors* on any surviving `^` rather than guessing; `Number` literals defined by decimal point/exponent; `Bool` in ProtoValue with `Bool→Int` coercion defined. §5.5.1's migration note uses `pow(2, capture.poll)` and integer `* 3 / 4`. Capture C confirms `poll:0x06 = 64 s`, so the fix matters. |
| **5** | Define or delete `tagBefore`/`ignoreIf`/`exitEffects`/`has`/`lookup`/`cmp`/`lexicographicBytes`/`since`; kill the three TLS predicates | **HALF-RESOLVED — the worst survivor** | Deleted cleanly: `tagBefore`, `ignoreIf` (§0.2), `exitEffects` (§5.0.3 → transition with `unless`), `lexicographicBytes`, `isCertBased`/`isEphemeral`/`isStaticRsa` (both docs, → `consts` + `lookup`). **Not resolved: `has()`, `cmp()`, `since()` are deleted by grammar §6.4's "Deleted" line and used 8+ times by §5** — and §5.19 *demands §6 add them*. Detail in §6 below. |
| **6** | Merge duplicate legality mechanisms | **RESOLVED** | Grammar §3.6: "The §5 document's `constraints` key is **deleted**." §5.0.3 and §5.19 agree, with a stated rule of thumb ("only fields of one message → §3 invariant"). `f.*`, `field:X`, `group:X`, bare option codes all gone; one scheme (bare `id` under `capture.*`/`item.*`/`inputs.*`). |
| **7** | Region 0 | **RESOLVED** | §3.2 guarantee 1 pushes `Region{start:0, limit:buf.length, id:"message"}` before the first field, explicitly so `remaining()` works at top level. NTP-G7's 48/52/68/72 discrimination now works — capture C is 68, capture A/B are 48. |
| **8** | Mixed-direction feedback SCCs + honest guarantee | **RESOLVED, and well** | V13 rejects mixed-direction SCCs naming both nodes; the iteration cap is *computed at validate time* as `Σ(maxWidth−minWidth)+1` rather than a magic 8; and §3.3 contains the explicit paragraph withdrawing the "never a runtime failure" claim for homogeneous monotone SCCs. This is the best-executed item in the set. See §3 for the twist: **nothing in the corpus produces an SCC at all.** |
| **9** | `assemble` must be allowed to decode; specify the two-stage contract | **RESOLVED TWICE, INCOMPATIBLY** | Grammar §2.5 explicitly withdraws v1's false justification and specifies stage 1/stage 2 with `hdr.*` scoping. §5.14.1 *also* withdraws it ("~~That reason was false and is withdrawn.~~") and specifies a *different* two-stage contract with `fragmentMessage`/`yields` and `capture.*` re-scoping. Both are coherent; they are not the same design. See B2. |
| **10** | Non-injective converters in the static set | **GONE with T3** | Grammar §0.1 deletes the non-injective-site analysis outright. |
| **11** | One kind key per field | **RESOLVED** | §3.4.9 states it and shows the nesting fix (`lp` hosting a `switch` in `fields`), explicitly calling the v1 TLS example illegal under v1's own rule. |
| **12** | `each.until` evaluation order vs `fold.next` | **RESOLVED** | §3.4.8 gives a numbered 6-step algorithm and works the BACnet `depth` case through it. Unambiguous. |
| **13** | CoAP-G17(a) — presence as a capture | **RESOLVED IN MECHANISM, BROKEN IN SPECIFICATION** | `present:` + `onMismatch:"absent"` + V11/V36 is the right answer. Two defects, traced in §4 below: the encode-side example is illegal under the document's own rules, and *nothing states that `present` binds `false` when `when` is false* — which is precisely the path T1 needs. |
| **14** | Collapse end-of-input spellings and `peek` syntaxes | **RESOLVED** | One spelling `{"when":"remaining() == 0"}`; `endOfRegion`/`endOfInput`/`rest`/`atEnd`/`assertConsumed` deleted and listed in the §6.1.1 migration hard-error set. `peek` is a function only; the object form is deleted. |

**T3 dangling check — clean, with one orphan.** I searched both documents for every T3 artefact. `keepRaw` appears only as diagnostic (grammar §3.1, §0.1, V34; §5.0.1, SM-046/SM-446). No `@form`, no raw-preference, no `roundTrip:"byteExact"`. Two orphans, both minor:

- Grammar §0.1 justifies keeping `keepRaw` "for the state model's transcript accumulators (§5's problem)." **§5 v2 has no transcript accumulator construct** — v1's `confirms:"allSince:clientHello"` was dropped and nothing replaced it. So TLS Finished `verify_data` is unexpressible, and `keepRaw`'s one named consumer does not exist.
- §5.14.2 puts `"keepRaw":"handshakeRaw"` on a **frame entry**. `keepRaw` is a §3.1 *field* common key; framers have no such key.
- SM-046 and SM-446 are **the same rule under two numbers**. In a rule set whose whole purpose is citation in diagnostics, that's worth fixing.

---

## 2. Surviving contradictions between the two documents

Resolved cleanly: operators, field naming, legality mechanism (`invariants` only), `switch` vs `variants` (§5.0.3 deletes `variants`), error vocabularies (§5.7.2's single `disposition` enum with stated deadline effects is a genuine improvement over the four v1 vocabularies).

**Six survivors, in severity order.**

**B1 — The converter/function set is closed in the grammar and violated by the state model in ~12 places.**

| §5 site | §5 uses | Grammar says |
|---|---|---|
| §5.10.3 CoAP Echo, §5.11 `ems`/`ocsp` | `has(capture.options.echo)`, `has(capture.ext.extendedMasterSecret)`, `has(state.sentExt.statusRequest)` | §0.2: "`has()` is superseded by `present:`". §6.4 Deleted: `has`. |
| §5.7 mDNS tiebreak | `cmp(state.myRdata, capture.record.rdata)` | §6.4 Deleted: `cmp`. |
| §5.12, §5.12.1 | `since($lastSeen)`, `since(state.acquireStart)` | §6.4 Deleted: `since`. |
| §5.7, §5.9.3, §5.10.4 | `capture.hdrs \|> key('nts')`, `key('usn')`, `key('bootId')` | §6.4 Deleted: `keys`/`values`/`key`/`all`. Replaced by `lookupBy(member,value)` / `allBy`. |
| §5.11 `dhcpParams` | `capture.options \|> keys` | Same. |
| §5.6, §5.8.6 | `lookup(consts.connackReasons, capture.returnCode)` — **table-second here, table-first in `lookup(consts.cipherProps, session.cipher)`** | §6.4 has `lookup(table)` as a pipelineable list converter, i.e. `x \|> lookup(t)` ≡ `lookup(x, t)` — value-first. §5 uses both orders, neither matching. |

§5.19 makes this explicit and unresolvable: it *requires* §6 to add `has`, `cmp`, `since`, `len(x)`, `lookup`, `startsWith`, `seconds`, "**by deliberate code change** (a closed set that a second document extends in prose is not closed)". The grammar author then deleted three of them. Both authors were right about the principle and neither read the other's answer.

**B2 — Reassembly and framing are specified twice, incompatibly.**

| | Grammar §2.2/§2.4/§2.5 | State model §5.14.2 |
|---|---|---|
| `frame` cardinality | "Exactly one `frame` object" | a **list** of two entries |
| frame entry keys | `kind`, `prefix`, `at`, `totalIs`, `maxPrefixBytes` | `kind`, `prefix`, `at`, **`includesPrefix`**, **`as`**, **`over`**, **`keepRaw`** |
| reassembly home | a `layers` entry, `kind:"reassemble"` | a `model.assemble` declaration |
| stage-1 referent | `"header": "parse.bacnetSegHeader"` (a `parse` block) | `"fragmentMessage": "complexAckSegment"` (a *message*) |
| stage-1 scope | `hdr.*`, visible only inside the layer | `capture.*`, re-scoped to the fragment |
| policy keys | none — "Acking is §5's", emits `assemblyProgress` | `ack`, `continue`, `progress`, `order`, `onGap`, `direction`, `activity` |
| encode | V7: decode-only, encoding is a validation error | `direction:"tx"` for `mdnsKnownAnswer` — i.e. **encode-side** |

That last row is a straight logical conflict: V7 forbids encoding a message with a `reassemble` layer; `mdnsKnownAnswer` declares `direction:"tx"`. The `includesPrefix` survival is a re-break of critique §1.2 (Modbus-G11) inside the document that isn't supposed to own framing.

**B3 — §5.19's "required amendments to §6" were never made.** §5.19 names them; grammar §6.2's path list contains none:

- `machine.<id>.state` — used as the §5.8.5 DHCP `switch` discriminant, the replacement for `variants`. Not a §6.2 path.
- `instance.*` — `instance.window`, `instance.maxApdu`, `instance.maxSegments`. Not a §6.2 path.
- `transport.frameLength` / `transport.sourcePort` / `transport.source` (§5.10.2's NTP crypto-NAK, the flagship "state discovered from a frame size") vs grammar's `meta.frameLen` / `meta.srcPort` / `meta.srcIp`. **Same concept, two names.**
- `$i`, `$v`, `$r`, `$s`, `$vb`, `$ordinal`, `$lastOrdinal`, `$topic`, `$last`, `$lastSeen`, and `$.options[54]` — §5.9.3's `each`/`as` fan-out namespace. Not in §6.2. Note the collision: grammar already uses `$` as a *suffix* (`capture.$` = message root).
- **`{expr}` string interpolation** — `"dev.modbus.u{inputs.unitId}.hr{inputs.start + $i}"`, `"fact:dev.mdns.{$name}.ttl"`, ~15 sites. §6 defines no interpolation syntax at all. SM-302 (namespace check "after interpolation of constant parts") depends on it.
- **`store.*`** — `store.lease.*`, `store.walk.*`, `store.if[{$ifIndex}].descr`, `store.nts.cookies`, `store.snmp.sysUpTime`, `store.sub.*`, `store.upnp.bootId`, `store.ntp.keyValid`. Read and invalidated ~15 times; **no construct anywhere declares a `store` or writes to one.** SM-325 requires every invalidate glob to match "≥1 declared fact key template **or scope path**" — scope paths are never declared, so SM-325 is unimplementable as written.

**B4 — The step graph is referenced by both documents and specified by neither.** Grammar §1 lists `steps` and `edges` as top-level keys and defers them to §5. §5.19 is titled "Required amendments to **§4** and §6" and amends "§4 (step graph)" — but grammar's §4 is *Round-trip tiers*. The step graph document does not exist in v2. Used-but-unspecified: `steps`, `edges`, `expect`, `correlate`, `completion` (`first`/`count:n`/`quiescence:<d>`/`deadline`), `select`/`by:"best:…"`/`bind`, `onEmpty`, `dispatch`, `parallel`, `loop:{max:n}`, `sent.<stepId>`, `activity` regions. **SM-210, SM-214, SM-215, SM-216, SM-217, SM-218 and SM-219 — the entire stated payoff of §5 — quantify over it.** §5.2's whole argument ("the model is primary, a step graph is a plan the validator statically checks against it") is unbuildable until it exists.

**B5 — Grammar §1 still lists `emits` as a top-level key** and says §5 specifies it. §5.9.3: "`emits` from §1 is **retired**." This is the last surviving head of critique §2.11's five-ways-to-write-a-fact hydra, and it's in the document-shape block an implementer reads first.

**B6 — §5's static checks are phrased over a per-message `parse` key that the grammar does not have.** §5.8 shows `"layers": [ … ], "parse": { … }` on one message. Grammar §1: a message has `doc`/`layers`/`invariants`/`limits`/`offsetTables`; `parse` is a *top-level dictionary of reusable field lists* (§3.4.14). SM-326 ("type-checks against that message's `parse` schema"), SM-313 and SM-441 all resolve paths against a key that does not exist. The referent is obvious (`layers[bytes].fields`) — but these are the three checks §5 advertises as its highest-value output.

---

## 3. T2, traced — SNMP GetResponse (337 B) and CoAP

### 3.1 SNMP GetResponse — **T2 holds. Verified byte-exact, and it needs Phase C only.**

The 337-byte capture's nesting, from the actual bytes:

```
30 82 01 4d                          SEQUENCE            len 333   → 4 + 333 = 337 ✓
  02 01 01                           version                    3
  04 06 70 75 62 6c 69 63            community "public"         8
  a2 82 01 3e                        GetResponse PDU     len 318  4 + 318 = 322
    02 03 0c 8a 9b                   request-id 821915          5
    02 01 00  02 01 00               error-status/index       3+3
    30 82 01 2f                      varbind list        len 303  4 + 303 = 307
      30 82 01 04                    vb1                 len 260  4 + 260 = 264
        06 08 2b 06 01 02 01 01 01 00      OID sysDescr.0      10
        04 81 f7  <247 bytes>              OCTET STRING len 247  3 + 247 = 250
      30 10 …                        vb2                 len  16  2 + 16 = 18
      30 13 …                        vb3                 len  19  2 + 19 = 21
```

Arithmetic checks at every level: 260 = 10+3+247 ✓ · 303 = 264+18+21 ✓ · 318 = 5+3+3+307 ✓ · 333 = 3+8+322 ✓ · 337 = 4+333 ✓.

Minimality under `LenSpec {varint:{encoding:"ber", minimal:true}}`: 247 > 127 forces long form, 1 octet suffices → `81 f7` ✓. 260, 303, 318, 333 each need 2 octets → `82 xx xx` ✓. 71 (request) → short form `47` ✓.

**The layout graph.** A BER length's value depends on the span that *follows* it; the body's sizes do not depend on the length field's size. So every edge runs child→parent and the graph is a **pure DAG** — Phase C alone resolves it bottom-up in one topological pass, exactly. `02 03 0c 8a 9b` requires `minint` (BER INTEGER is signed; 0x0C8A9B's top bit is clear so 3 octets suffice — `i32be` would give 4 and also corrupt the enclosing lengths). The OID `2b 06 01 04 01 8f 65 0a 01 03 01` for 1.3.6.1.4.1.2021.10.1.3.1: first-arc merge 40·1+3 = 0x2b ✓, 2021 = 15·128+101 → `8f 65` ✓. Both are named in §6.4 with these exact corpus cases.

**Verdict: T2 for both SNMP captures from one document is real.** §4.3's claim holds and it is v2's strongest result.

**But the document's own explanation of it is wrong, and that matters for the build.** §4.3 says "Phase C resolves the nesting bottom-up as a DAG, **Phase D's Kleene from minimum width converges**." Phase D is never entered — there is no SCC. I checked the whole corpus for one:

- BER nested lengths → child→parent only → DAG.
- MQTT Remaining Length `len(rest)` — `rest` excludes the length field → DAG.
- BACnet BVLC `covers:"fromRegionStart"` (`81 0a 00 11`, 17 = 2+2+13, the length counts itself) — genuine self-inclusion, but the length field is a **fixed-width `u16be`**, so `size` is constant and not in any cycle → DAG.
- mDNS compression — `lookupOffset` reads offsets of *earlier* emitters only, and `off(e)` is a prefix sum of preceding sizes → strictly backward → DAG.
- TLS three-level vectors — fixed-width `u16be`/`u24be` → DAG.

A genuine SCC requires a **variable-width `LenSpec` combined with `covers:"lenAndBody"` or `"fromRegionStart"`** — and §3.4.6's own table marks that row "—" (no corpus user). **Phase D, V12, V13, the `feedback` declaration, and the convergence bound have zero corpus witnesses.** They can be neither validated nor falsified by the fixture suite. That is the one place where v2 violates its own "smallest coherent language" rule, and it's the most intricate machinery in the document. Recommendation in the build order below.

### 3.2 CoAP — **T2 holds, and the encoding is provably forced. Verified on all three captures.**

Capture 1 (81 B), options decoded from `44 01 12 34 | 9a f3 1c 08 | …`, accumulating delta:

| off | header | Δnib | Lnib | ext | opt # | len | value |
|---|---|---|---|---|---|---|---|
| 8 | `b7` | 11 | 7 | — | 11 Uri-Path | 7 | `sensors` |
| 16 | `0b` | 0 | 11 | — | 11 Uri-Path | 11 | `temperature` |
| 28 | `4c` | 4 | 12 | — | 15 Uri-Query | 12 | `unit=celsius` |
| 41 | `0d 0d` | 0 | **13→esc** | `0d`=13 | 15 Uri-Query | **26** | `since=2026-08-09T10:15:00Z` |
| 69 | `21` | 2 | 1 | — | 17 Accept | 1 | `32` = 50 |
| 71 | `d8 de` | **13→esc** | 8 | `de`=222 | **252** Echo | 8 | `a1 b2 c3 d4 e5 f6 07 18` |

71+1+1+8 = 81 ✓, six options ✓. Capture 3 (11 B) exercises the other escape: `e2 06 e9 08 00` → Δnib 14, ext16 = 0x06E9 = 1769, delta = 1769+269 = 2038, option 11+2038 = **2049** (OCF vendor) ✓.

**Encode.** §6.1's `if(len < 13, len, if(len < 269, 13, 14))` gives: 26 → nibble 13 ✓; 235 → nibble 13, ext 235−13 = 222 = `0xde` ✓; 2038 → nibble 14, ext16 2038−269 = 1769 = `0x06e9` ✓. The three ranges are disjoint (0–12 inline, 13–268 via nibble 13, ≥269 via nibble 14) and a negative extension is impossible, so **the encoding is injective — T2 is forced, not merely achievable.** Capture 2's `44 / 81 32 / 21 3c / ff` and `minuint(50)=32`, `minuint(60)=3c` likewise ✓.

**Three specification gaps that stop an engineer from writing the CoAP document:**

1. **The extension *value* expression is nowhere in v2.** §6.1 gives the nibble; `len − 13` / `len − 269` and `delta − 13` / `delta − 269` are never shown, in either direction. Derivable, but the "worked reference" claim doesn't hold.
2. **`each.fold` on encode is never specified.** The running option number is encoder-carried state. §3.2 says "`registers` are the whole of **decoder**-carried state"; §3.4.8's evaluation-order algorithm is written entirely in decode terms; the encode bullet says only "iterate `over`'s list; `item.*` is the element." There is no `item.@prev`. One engineer implements `fold` bidirectionally and writes `next: "num + item.delta"`; the other implements it decode-only, discovers CoAP can't encode, and works around it with `sortedOpts[item.@index - 1].number` — which needs a message-scope `let` holding a list plus expression indexing. Both are consistent with the text.
3. **`len` means three different things.** §6.3's layout function `len(id)`; §6.4's bytes-group converter `len`; and §5.19's demanded function `len(x)`. This is load-bearing: V10 forbids an encode-side `switch.on`/`when` from touching layout functions, so the CoAP length nibble is legal *only* if `item.value |> len` is read as the pure converter and not as `len(id)`. Neither document draws the distinction.

---

## 4. T1, traced

### 4.1 CoAP empty-vs-absent payload — **the mechanism is right; the specification of it fails T1.**

The construct (§3.1) is correct in principle. Two concrete defects:

**(a) The encode-side example is illegal under v2's own rules.**

```jsonc
{ "bytes": {"len":"remaining"}, "as":"payload", "when": "inputs.hasPayload" }   // §3.1, encode column
```

There is no `value`. §3.4.1's validator note requires `value` on encode unless the message is decode-only, and §3.4.3's encode bullet writes "the octets of `value`" — there are none. It must read `"value": "inputs.payload"`. `"len":"remaining"` also survives only because `"remaining"` is a §3.4.0 LenSpec *sentinel* rather than the `remaining()` function that V37 forbids on encode — a coincidence the document doesn't acknowledge, and one that is meaningless on encode anyway.

**(b) The T1 path itself is unspecified.** Trace `hasPayload = false`:

- **Encode:** both fields have false `when` → zero bytes ✓.
- **Decode:** `remaining() > 0` is false at the marker, so `when` is false → field not present. **Does `present` bind `false`, or stay unbound?** §3.1 defines `present` as "was this field actually present?" and defines a false `when` only as "consumes zero bytes." It never says `present` is written on the `when`-false path.
- If unbound, `capture.hasPayload` is `Null`. §6.2 rescues the *next* field ("Null suppresses an enclosing `when`"), so the parse survives — but the **capture** is `Null`, and the input declared `captures:"hasPayload"` with value `false`. T1 compares `Null == false` → **fails**.

Meanwhile the `onMismatch:"absent"` path *does* produce a definite `false`. So the same semantic yields two different capture values depending on which route decode took. **One missing sentence — "when `when` evaluates false, a declared `present` binds `false`" — and MUST-13 is genuinely closed.** Without it, the construct introduced to close CoAP-G17(a) fails the tier it was introduced to satisfy.

With that sentence added, T1 holds cleanly: `(hasPayload=true, payload="")` → `…ff` (marker, nothing after); `(hasPayload=false)` → `…` (nothing). Two byte strings, two capture values ✓. Note this makes an RFC-illegal CoAP message representable (7252 forbids the marker with an empty payload) — which is correct for a *decoder* and should be paired with an `invariant` on the encode side.

### 4.2 DHCP option list — **T1 holds; two authoring traps and one unhomed converter.**

DISCOVER options after the cookie: `35 01 01` · `3d 07 …` · `0c 06 "nexa01"` · `37 04 01 03 06 2a` · `ff` · 33 × `00` = 27 + 33 = 60 = 300 − 240 ✓.

Round trip is sound: `each` binds `List<Record>{code,value}`, order is a property of the list, `tlv.bare = ["0x00","0xff"]` handles PAD/END, and `pad{to:300, as:"padLen"}` captures the 33 ✓. §4.3's "T2 only with declared order" is the honest disposition.

Three notes:

1. **The OFFER is 306 bytes with no padding.** With `{"pad":{"to":300,…}}` and the **default `onOverflow:"error"`**, encoding the OFFER *fails*. The author must write `onOverflow:"ignore"` (§3.4.12 has the value). A default that breaks one of the two corpus captures is a trap worth a line in §3.4.12.
2. **The RFC 3396 decode-side merge has no syntactic home.** §3.4.8 says "merge decoded items with `groupBy` after (`capture.options |> mergeBy('code','value')`)" — **naming two different converters in one sentence**, and §6.4's table has `mergeBy` but no `groupBy`. More importantly: applied *where*? `via` is defined (§3.1) as post-read on a field; §3.4.8 never says `via` is legal on an `each` or what it transforms. Encode's `chunk(255)` has the same problem in reverse. The mechanism `coalesce` was deleted *into* has no place to be written.
3. Ambiguity, not a defect: a document may terminate the option `each` at `ff` and let `pad` eat the 33 zeros, or run `until remaining()==0` and bind 33 PAD records. Both are T1- and T2-clean; they produce different capture models. Fine — but the fixture suite must pin one.

---

## 5. Still defined only by example

Everything below is *used* in a normative example or a validator rule and *specified* nowhere.

**Structural (blocking):**
- The entire step graph — `steps`, `edges`, `expect`, `correlate`, `completion`, `select`/`by:"best:…"`, `onEmpty`, `dispatch`, `parallel`, `loop:{max:n}`, `sent.<stepId>` (B4).
- `store.*` — read/invalidated ~15×, declared by nothing (B3).
- `appliesTo` — grammar §1 assigns it to §5; §5 never mentions it.
- `{expr}` string interpolation — ~15 uses in §5.9/§5.10, no syntax anywhere (B3).
- `$var` fan-out namespace (B3).

**Semantic (each is a real fork):**
- **`span(a, b)` endpoints.** `[start(a), start(b))` or `[start(a), end(b))`? §6.3 says only "length between two ids." The NTP `checksum` example depends on it — and **the example is wrong either way.** The fixture is explicit: `MD5( ascii("secret") || bytes[0..48) )`, digest input 6+48 = 54 bytes, i.e. the 48-byte header only, **excluding** the 4-byte key id. §3.4.13 writes `"over": "span(message, here)"` on the `mac` field, which sits *after* the key id — that digests 52 bytes. The flagship example of the flagship keyed-digest construct is off by 4 bytes against the capture it cites.
- **Does `group`/`lp` create a capture frame?** §3.2 guarantee 5 says frames are message → group → item; §4.1 says "`group`/`lp` do not create a capture frame **unless** they contain `as`-bound fields." So the number of `^` in `capture.^^` depends on whether an intermediate group happens to be empty. Two engineers, two answers.
- **Is `each.until` evaluated on encode?** §3.4.8 says encode "iterates `over`'s list" — no mention of `until`. mDNS T2 depends on the answer (§7 below).
- **Encode-side captures.** `as` is defined as decode-only (§3.1), yet §3.5.3's encode expansion reads `item.hit` and `capture.emitted`, both bound by encode-side `let`. Where does encode-side `let` bind?
- **`switch` case-key matching.** Keys are strings; discriminants are Int (`"0x04"`), Bool (`"false"`/`"true"` in §3.5.3), and masked Int (`"0xc0"`). No coercion rule.
- `classify` — a bare string in most sites, an *expression* in §5.6 (`"lookup(consts.connackReasons, capture.returnCode)"`). Two forms, neither typed.
- `decodeFail` — §5.7.2 routes field mismatches into `on:"decodeFail"`, but grammar's `onMismatch` values are `error|warn|absent` and the grammar never mentions `decodeFail`. The seam §5 leans on is asserted from one side only.
- `dedup` names two unrelated constructs: a responder MID cache (§5.13) and expect-step collect-dedup (§5.19).
- `trust: builtin|user-reviewed|ai-draft` (§1) — no consumer.
- `roundTrip` — §0.1 says `"byteExact"` is not a legal *value*, implying the key still exists with other values. §1 doesn't list it; §4 doesn't define it.
- Whether an `at:"transmit"` field's actually-emitted value reaches `sent.<stepId>.<id>` — §5.12's `ntpOrigin` nonce `verify:{echoAt:…}` needs exactly that (critique §3.17, never a MUST item, still open).

---

## 6. Is the converter set closed?

**In the grammar alone: yes.** §6.4 is a genuinely good table — accepted/produced kinds per row, an inverse declared for every `via`-legal converter, purity stated per row with no impure row surviving (`delta32`/`delta64`/`rate` cut; `unntp64(eraHint)` made pure by promoting the era to an argument). The deliberate over-population ("cheaper to over-populate once than to ship a code change per protocol") is the right call and is stated as such.

**Across both documents: no.** Grammar §6.4 *deletes* `has`, `cmp`, `since`, `key`, `keys`, `values`, `all`; §5 uses all of `has`/`cmp`/`since`/`key`/`keys` and §5.19 demands they be added. `lookup` exists in the grammar but §5 calls it with two argument orders, neither of which matches the pipeline desugaring. §5.0.3's own deletion table says `Map` was replaced by "`List<Record>` + **`key(n)` / `all(n)` accessors**"; grammar §6.4 says the replacement is `lookupBy`/`allBy` and lists `key`/`all` as deleted. **Both documents wrote a deletion table for the same cut and reached opposite conclusions about what survives it.**

---

## 7. What two engineers would still build differently

Beyond the forks in §5 above:

1. **The published `labelSeq` expansion does not produce the corpus bytes.** V40 makes the expansion a test artefact checked byte-for-byte, so this is a day-one build failure. I traced the 127-byte mDNS response with offsets computed, not assumed: pointers are `c0 0c`→12, `c0 28`→40, `c0 17`→23, and **`c0 46`→70, which holds `0x09` — the label length of `nexaprint` inside the SRV record's *target* field, i.e. inside another record's RDATA**, exactly as §3.5.3 claims. The *design* reproduces this: encoding the SRV target registers `nexaprint.local`@70, and the A record's name then hits it. **The published expansion does not**, for three independent reasons:
   - **Scoping.** `{"let":"emitted","value":"true"}` sits inside the `each` body, so per §3.2 guarantee 5 it binds into the **item** frame. The terminator reads `capture.emitted` — the *enclosing* frame — which is never written. The loop runs to `max:128`. Identical defect on the decode side with `jumped` (both in `until` and in the trailing `{"const":"00","when":"!capture.jumped"}`). The correct mechanism is `each.fold`, which §3.4.8 explicitly says `until` may read.
   - **V24.** The encode expansion writes `"switch": "item.hit == null"` with no `on:`. V24: "A `switch` in an encodable message declares `on`." The expansion of an encode-side construct violates it.
   - **Encode-side `until`.** Even with scoping fixed, if `until` is decode-only (§3.4.8's encode bullet is silent), then for the A record's name — `suffixes('.')` = `['nexaprint.local','local']` — iteration 0 emits `c0 46` and iteration 1 *also* emits `c0 17`, producing 4 malformed bytes where the capture has 2.
   
   Also `{"ref":"parse.labelSeq"}` inside the decode expansion's `seek` is self-recursive with **no `maxDepth`**, violating V38.

2. **`fold` on encode** — bidirectional or decoder-only (§3.2 says "decoder-carried"). Decides whether CoAP is encodable at all.

3. **Phase D** — one engineer builds the SCC condensation, `feedback` declarations, direction homogeneity (V13) and the computed iteration bound; the other builds Phase C only and errors on any SCC. **Nothing in the corpus distinguishes them** (§3.1 above). Both pass every fixture.

4. **`minuint` vs `minint`** — §6.4's bullet cites SNMP request-id 821915 → `0c 8a 9b` under the heading "`minuint`/`minint`" collectively. BER INTEGER is signed; a value with the top bit set needs a leading `00`. One engineer picks `minuint` and ships an encoder that corrupts every request-id ≥ 0x80 *and* the four enclosing lengths.

5. **Region 0's id in the group namespace** — V25 requires `checksum.over` to name "a group id or `span(a,b)`". §3.2 gives region 0 the id `"message"`, and §3.4.13 writes `span(message, here)`. Whether a region id is addressable as a group id is unstated.

6. **`present` on the `when`-false path** (§4.1b) — the difference between T1 passing and failing.

7. **`reassemble` vs `assemble`** (B2) — two engineers build two different subsystems from two different sections.

---

## BUILD ORDER

Each step is independently testable against a named corpus capture, ordered so the earliest steps can falsify the most design. Everything up to step 12 is **grammar-only** and unblocked by the §5 problems.

> **Step 0 — spike first.** Before writing production code, prototype the Phase A/B/C layout graph against the SNMP pair (step 5). It is the single hardest T2 claim and the one that invalidates the most work if it is wrong. My trace says it holds — verify it on a throwaway before building steps 1–4 on top of it.

| # | Build | Validated by | Falsifies |
|---|---|---|---|
| **1** | Expression core: precedence table, `\|>`, `pow`, `Number`/`Int`/`Bool` + `Bool→Int`, paths, `if`. Validator skeleton V1–V5, V39. | Unit: `pow(2,6)=64` (capture C's `poll:0x06`), `depth + (lvt==6) - (lvt==7)`, `keepAlive*3/4`, `fc \|> band(0x80) != 0`. | MUST-4. No I/O yet. |
| **2** | `bytes` layer, `scalar`/`const`/`bits`/`skip`/`let`/`assert`, **region 0**, `remaining()`/`offset()`, capture frames. | **NTP A + B (48 B each)** — decode, T1. `bits` for LI/VN/Mode; `remaining()==0` at top level. | MUST-7. `bits` crossing octet boundaries. |
| **3** | `udp` layer; converters batch 1 (`fixed(16,16)`, `ntp32/64`, `ipv4`, `ascii`, `minuint`/`minint`). Encode path, Phase A + F (no graph yet). | **NTP A + B, T2 byte-exact.** Root delay `0x0000028f` = 0.0099945 s. | First end-to-end T2. |
| **4** | `tcp` + framers `fixed`/`lengthPrefixed` + `totalIs` + **§2.3 framer-owns-nothing** + V6. `switch`. | **Modbus 12 / 15 / 9 B, all three T2.** Prefix decoded twice; FC `0x03` vs `0x83` via `switch`. | MUST-1a. Smallest protocol that exercises ownership. |
| **5** | `varintPrefixed` framer + `varint` field + `minimal` + the shared LenSpec varint set. | **MQTT 146 / 4 / 56 / 7 / 2 / 2 B.** `8f 01` = 143; reject `8f 80 00`. Feed a SUBACK+PUBLISH in one segment and a CONNECT split across two. | MUST-1b. One varint implementation, two callers. |
| **6** | **`lp` + `LenSpec` + `group{extent}` + region stack + `each` + layout Phases A/B/C (DAG only, hard-error on any SCC).** `ref`+`maxDepth`, `base128`/`oid`, `ber` varint. | **SNMP 73 B + 337 B from ONE document, T2 byte-exact.** Nine payload sizes in 100..300; `47` / `81 f7` / `82 01 4d`. | The whole layout design. **Go/no-go gate.** |
| **7** | `each.fold` **in both directions**, `present`, `onMismatch:"absent"`, nibble escapes via `bits`+`switch`+`LenSpec{expr}`, `sortBy` before `each`. | **CoAP 81 / 42 / 11 B, T2** (Δ 235 → `d8 de`; Δ 2038 → `e2 06 e9`; len 26 → `0d 0d`) **and the empty-vs-absent T1 pair.** | MUST-12, MUST-13, encode-side fold. |
| **8** | `tlv`/`tlvList` sugar + `--desugar` + V40 harness; `pad`; `seek` + hops; `chunk`/`mergeBy`. | **DHCP 300 / 306 B, T2 with declared order.** 33-byte pad; option-52 overload `seek` to offset 108/44; `ff` END as a `bare` code. | The sugar-expansion contract, for the easy case. |
| **9** | `offsetTables` + `register` + `lookupOffset`; `suffixes`; **`labelSeq` sugar (rewritten with `fold`, `on:`, and encode-side `until`)**. | **mDNS 34 / 127 B, T2 under `compress:"auto"`** — must emit `c0 0c`, `c0 28`, `c0 17` and **`c0 46` pointing at offset 70, inside the SRV RDATA.** | The published expansion (currently wrong three ways). Do **not** start this until §3.5.3 is corrected. |
| **10** | Text path: `bytes{until,consume}` / `whileAnyOf` incl. zero-length runs; `decimal`/`undecimal`, `httpDate`/`unhttpDate`; `lookupBy`/`allBy` with ASCII-case-insensitive Text compare. | **SSDP 142 / 315 B, T2 with declared header order** — `EXT:` must be exactly 6 octets (`45 58 54 3a 0d 0a`). | The `Map`→`List<Record>` decision. |
| **11** | `checksum` with `prefix`/`suffix`, `span(a,b)` **with endpoint semantics pinned**; `at:"transmit"` + V30/V31/V32/V33. | **NTP capture C, 68 B.** Digest must be `MD5(ascii("secret") ‖ bytes[0..48))` = `7f7655604e4230124aa5396825317fe0` — **48 bytes, not 52.** V33 must reject C + `at:"transmit"`. | §3.4.13's example (currently over-digests by 4). |
| **12** | Three-deep `lp` nesting + `each` over a length-prefixed extension list; `ref` with `params`; `covers:"fromRegionStart"`; `reassemble` (grammar §2.5 form) + V7. | **TLS 196 / 113 B** (98-byte extension block = 20+6+14+18+4+5+4+9+18 from nine records) and **BACnet C1/C2/C4/C5 T2** (`81 0a 00 11` = 17 counting itself) with **C3 decode-only** via `reassemble`. | `covers`, `params`, the two-stage parse. |
| **13** | Phase D: SCC condensation, `feedback`, V12/V13, the computed iteration bound. | **No corpus witness exists.** Either write a synthetic fixture (`lp` with `covers:"lenAndBody"` + a `ber` LenSpec) or **cut Phase D from the prototype and make V12 reject every SCC.** | Nothing, today — which is the point. |
| **14** | **Specify the step graph** (the missing document), then §5 in order: machines/states/transitions → observers + `disposition` → `facts` (one `FactRule`, `ProtocolFact` mirroring `DeviceFact`) → `negotiate`/`counters`/`rates`/`health` → the abstract interpreter (SM-210, SM-214). | MQTT (SUBSCRIBE-before-CONNACK, reconnect `requires`), SNMP (`iterate.mibWalk` progress), CoAP (SM-312 granted-vs-requested block size). | Blocked on B1–B4. |

**Where the two-document seams land:** steps 1–13 need only the grammar plus five fixes (§3.5.3's expansion, §3.4.13's `span`, `present`-on-false-`when`, `fold`/`until` on encode, the `len` triple). Step 12's `reassemble` and all of step 14 are blocked on the reconciliation.

---

**NOT READY** — blockers: (1) `has`/`cmp`/`since`/`key`/`keys` deleted by grammar §6.4 but used in ~12 §5 sites, plus `lookup`'s argument order; (2) reassembly and framing specified twice and incompatibly (grammar §2.5 `reassemble` vs §5.14 `assemble`; `totalIs` vs surviving `includesPrefix`; single `frame` object vs a list; V7 decode-only vs `direction:"tx"`); (3) §5.19's required §6 amendments never applied — `machine.<id>.state`, `instance.*`, `transport.*` vs `meta.*`, `$var`, `{expr}` interpolation; (4) the step graph and `store.*` are referenced by both documents, specified by neither, and SM-210/214/215/216/217/218/219 quantify over them; (5) the published `labelSeq` expansion — V40's own test artefact — is wrong in three ways and does not produce the mDNS bytes; (6) grammar §1 still lists `emits`, which §5.9.3 retires, and §5's per-message `parse` key does not exist in the grammar's message shape; (7) `present` is not stated to bind `false` when `when` is false, which fails the T1 case it was added for.

