<!-- The protocol state model, v2. Rewritten after all six stress protocols broke the previous "tree carries
     lifetime" design. Superseded content is in git history. The load-bearing change: a tree of scopes for
     ADDRESSING, a DAG of anchors for LIFETIME. See docs/dynamic-protocol-model.md section 5 for the summary. -->
## 5. State — scopes, one identity, three spaces

State is a **tree of scopes for addressing** and a **DAG of anchors for lifetime**. Those were one edge in the
previous draft, and that conflation is the single defect the falsification found in every protocol. Each
scope holds one machine per state space; a scope may be *keyed*, in which case many instances are live at
once; an instance's death propagates along its anchors, and by default an instance's only anchor is its
containing scope — which reproduces the cascade guarantee exactly, while making every exception declared
and validated rather than impossible.

Falsification against the corpus killed the flat design outright, and the tree survived that test: six of
ten protocols need state keyed finer than a connection, one peer can hold 255 concurrent transactions, and a
single machine per peer expresses none of it. What the corpus then killed is the claim that **one** parent
edge can carry addressing, lifetime, key uniqueness and gating at once. Six of six stress tests broke on it;
two declared the model unusable as written. §5.4 is the repair, and it is one notion, not six.

Two claims from the previous draft are withdrawn outright:

- **"Only Remote nodes carry epistemics."** False in both directions, and it contradicts P6 one section
  after P6 states the two axes are orthogonal. Rewritten in §5.1 and §5.8, not patched.
- **"`key` is the whole of correlation."** The *scope* is the right unit; the key alone is roughly 60% of
  the mechanism. §5.3 supplies the rest.

### 5.1 Three spaces, as facets — not as roots

A state's **space** answers *whose state is this*, which is an authority question:

- **Local** — ours. We change it by deciding to. *"I have chosen this offer."*
- **Remote** — the peer's, as we model it. We change it only by inference or by being told. *"It thinks I am
  unauthenticated."*
- **World** — neither party's. An effect the protocol causes that no participant can observe: a
  notification the peer emits to third parties on our behalf, a claim about a shared medium with no
  counterparty, a physical outcome. Written only by effects, never by a received message, and typically
  permanently unsettleable.

The third value is not an indulgence. Two corpus protocols hold a proposition that decides their next
action, is caused by the protocol, and belongs to neither participant; with two spaces it has no home, and
because §5.6 guards read the configuration and not the observation store, it is not merely unrecorded — it
is **unguardable**.

**Spaces are not tree roots.** A scope declares one identity and one lifetime and hosts up to one machine
per space. The previous shape — one tree per space — forced every real object that has both a local face and
a believed-remote face to be declared twice, keyed twice, and kept in step by convention. That is precisely
the duplication P5 forbids for structure, and four of six stress tests independently proposed the same
patch (a `mirrorOf` / `reflects` / mirrored-declaration edge). The patch is unnecessary: make the spaces
facets of one scope and the correspondence is structural.

```
scope connection {
  identity  { … }
  local  ‹opening | open | closing | closed›
  remote ‹believedOpen | believedEstablished | presumedDead | knownClosed›
}
```
One key, one lifetime, one cascade, two machines that may legitimately disagree — which is the point. A
local face reading `open` while the remote face reads `presumedDead` is the half-open transport, and
expressing it is the best thing the two-space model does. It survives; only its position moves.

**The asymmetry argument is withdrawn.** The old claim was that Local is "always known, never qualified".
Local means *ours*, and ours is not the same as known. Our own accumulated results (a traversal that may
have skipped a row deleted behind the cursor), our own retained history (which correlation keys are still
spoken for after a restart), our own resources (a socket we hold that may no longer carry a byte), and our
own outcome after giving up (a write the peer may have applied before it stopped answering) are all Local
and all genuinely uncertain. Only our intent *at the instant we form it* is certain, and that is one
transition, not a subtree. Epistemics therefore attaches to states, wherever they live (§5.8).

### 5.2 A scope

```
Scope {
  id
  contains?    : Scope                  // ADDRESSING parent. Absent ⇒ a root scope. Forms a tree.
  identity?    : Identity               // present ⇒ keyed; many instances live at once (§5.3)
  begins       : [ Trigger… ]           // what creates an instance
  anchors      : [ Anchor… ]            // LIFETIME. Defaults to [{ on: contains, do: retire }] (§5.4)
  local?       : Machine
  remote?      : Machine
  world?       : Machine
  frame        : [ Binding… ]           // entity values this instance holds (§5.5)
  durability   : transient | durable    // default transient (§5.9)
  retention?   : Bound                  // required when durable
}

Machine {
  states  [ State… ]
  initial : State | { State… }          // a set ⇒ the machine starts indeterminate (§5.8)
  admits? : State → { Message… }        // closed admission set: in this state, only these are legal
}

State {
  name
  terminal?  : bool
  lingers?   : Resolvable               // how long the instance stays addressable after this state (§5.4)
  settledBy? : [ Trigger… ]             // an empty list asserts: no evidence can ever settle this
  // every state carries an epistemic stamp (§5.8) in every space
}
```

`identity` present makes the declaration instanced: 255 concurrent transactions are **one declaration and
255 instances**, and that result stands.

### 5.3 Identity and correlation

§5.6's old claim — that six protocols' correlation collapses into the scope key — is half true and
oversold. The *scope* is the right unit and it earns its keep: many live instances with independent
machines, out-of-order completion for free, retransmission landing on the existing instance for free, and
duplicate suppression for free where the duplicate shares the key. What a bare `key: Resolvable` cannot do
is stated here, and it is five things, not one.

```
Identity {
  domain      : { of: <value domain>, ordering: equality | prefix | interval | <partial order> }
  generation  : minted | witnessed(<expr>)
  allocation  : observed | minted { from: <domain>, avoiding: live ∪ lingering }
  reconcile   : adopt | merge | fork | supersede     // when a deferred component is settled
}
```

```
// declared on the MESSAGE, once per scope it binds — not once on the scope
correlates <scope> {
  resolve : [ Rule… ]                   // ordered; first rule that yields an instance wins
  admits? : <predicate over the matched instance>
  onMiss  : create | ignore | <Transition>          // a transition here may emit
  window? : <admission bound>
}

Rule := key(<expr>)                     // an identity value; equality under the domain's ordering
      | match(<predicate>, cardinality: one | many)
```

**(a) The projection belongs to the message, not to the scope.** One scope is routinely reached by
several messages that compute its identity differently: the same logical identifier appears under a
different field name in a continuation message, the originating side is implied by message class in one
message and carried in an explicit bit in another, and one message (an empty acknowledgement) carries no
correlation value at all and must be resolved through a reference stored on a sibling instance at creation.
A single expression declared on the scope cannot be written. Declaring it per message also makes the
degenerate case — every message projecting the same field — a one-line default.

**(b) Identity is composite and ranges over the observation context.** The event supplies an ambient frame
alongside the decoded capture: direction of travel, local endpoint, arrival interface, transport epoch. Two
independent identifier spaces multiplexed over one transport, distinguished only by who allocated the
value, are told apart by an identity component that is in no field of any message. Key expressions read
`capture.*` **and** `context.*`. Where the scope tree already supplies a discriminator — a socket, an
endpoint — path composition supplies it structurally and no tuple syntax is needed; that is the strongest
argument for the tree over a flat scope list, and the old table missed it by presenting every key as a
single wire token.

**(c) A key is for lookup; acceptance needs a separate predicate.** Correlation values are frequently
sequential and guessable, and several protocols require a matched message to also agree on a field that is
*not* part of the key. Folding those fields into the key is not equivalent: a mismatch then **misses** and
mints a second instance, where the protocol requires it to be **rejected**. Three outcomes are needed where
a key gives two — miss, match-and-admit, match-and-reject — and the third is a reportable event, not noise.

**(d) A miss is behaviour, not an error.** An unmatched request may be required to draw a reset; an
unmatched acknowledgement must be silently ignored; an unmatched response must be discarded without
diagnostic; and on the sending side the same expression must *create*. `onMiss` carries it, and because it
may name a transition, the required outbound reply is expressible.

**(e) The domain carries an ordering; equality is the degenerate case.** The exclusions that matter
operationally are over containment, not equality — a region that contains another region, a filter that
matches many names. With `ordering: prefix` (or any declared partial order) the comparison operators are
available to keys, selectors and guards, and `equality` reproduces today's behaviour exactly.

**Aliasing and deferred identity.** An instance's identity is a *set of names*: `resolve` is an ordered
list, and a successful resolve under a name the instance has not yet been seen under adds that name. That
covers a responder which must find one reservation under either of two client-supplied identifiers, and a
peer reachable under both a stable name and a transport address. Where a component of the identity is only
learned from the reply the instance was created to elicit — the responder's identity is a field of its own
answer — the instance is created **unidentified**, is addressable relative to the transition that created
it, and binds its identity on first correlated evidence. `reconcile` then says what happens: `adopt` (no
existing instance holds it), `merge` (one does — fold the provisional instance and its children in),
`fork` (several responders — one keyed instance each), `supersede` (retire the prior generation, open a
new one). Children created under a provisional instance are re-parented by the same operation, never
destroyed.

**Generation separates identity from the key.** Identity is `(names, generation)`. Keys are drawn from
bounded, reusable domains; a message whose key matches but whose generation does not is *stale*, and
classifiable as such rather than misrouted into a live exchange. `generation: witnessed(<expr>)` binds the
generation to an observable that changes when the peer's run changes — the generation is then discovered
rather than minted, and disagreement supersedes the instance (with the retroactive onset of §5.6).
`allocation` is the encode half that the old `Resolvable` never had: to *drive* a protocol you must mint a
key from a domain while avoiding both live and lingering instances, which P5 requires the one structure to
support.

**Correlation is not always one-to-one.** `match(<predicate>, cardinality: many)` covers a broadcast query
answered by zero, one or forty peers over an admission window, where the correlating relation is a range
predicate and the exchange terminates by silence, not by a message. `match` with a declared ordering over
live instances covers the oldest-outstanding queues of protocols whose responses carry no correlation field
at all. Both are linear over a candidate set; a declared index hint on the predicate keeps `key` the
indexable default and confines the scan to the cases that genuinely need it.

**Two things that looked like new mechanisms and are not.** *Positional correlation* — a batch response
whose elements bind to a batch request's elements by ordinal, where the inner scope's key is absent from
the response entirely — is `key(anchor.frame.requests[ordinal])` once instances hold frames (§5.5) and the
packing exposes the repetition ordinal. *Resolution through a sibling* is `key(sibling.frame.pairedWith)`.
Both are key expressions over frames; neither needs its own rule kind.

### 5.4 Lifetime — phases, anchors, cascade

**This is the headline, and the finding is blunt: the tree as written is the wrong shape for lifetime.**
Every one of the six stress tests produced state that must outlive, or survive the replacement of, the
scope the addressing tree makes its parent:

- a session whose subscriptions and unacknowledged transfers must survive the transport and be resumed by a
  later one — where the *direction of containment* is decided by one bit of one field at runtime;
- a completed exchange that must remain addressable to replay its cached answer, and an abandoned one that
  must remain addressable to refuse a late reply;
- durable capability knowledge learned only by being refused, which a heuristic "the peer is probably gone"
  would otherwise delete on a guess — the self-defeating case being deleting the record that explains why
  the peer is deliberately silent;
- a request/response exchange that spans two reliability exchanges in opposite directions in two
  independent key namespaces;
- an allocation whose granting counterparty is replaced mid-life without the allocation itself lapsing;
- a locally-owned multi-exchange operation whose only killer lives in the other space.

The instinct to re-shape the tree per protocol is wrong, and the second instinct — a lifetime qualifier on
the existing edge — is not enough, because one of these needs the containment *direction* to be a runtime
value, which no static edge of either polarity provides. The fix is **one notion: ownership without
containment.**

```
Anchor {
  on : <scope path>                     // ANY scope, any space; need not be the addressing parent
  do : [ { when?: <guard>, then: retire | suspend | detach | <Transition> } … ]
}
```

- `contains` keeps the tree. It gives naming, path addressing, key-uniqueness domain and occupancy domain,
  and nothing else.
- `anchors` carries lifetime. It is a set, it may point anywhere including across spaces, and the graph it
  forms is a DAG. **An instance's default anchor set is `[{ on: contains, then: retire }]`**, so a document
  that says nothing behaves exactly as §5.3 previously promised.
- **Anchor loss is a trigger like any other.** The named handler runs; the descendant reaches a *real*
  terminal state with real epistemics rather than vanishing. Retiring a peer with 255 live exchanges is not
  255 deletions, it is 255 *outcomes*, and they differ: reads become `unknown` while a write becomes
  `possiblyApplied`, because the peer may have executed it and lost the acknowledgement. Deleting both
  identically destroys exactly the distinction the model exists to preserve, and every one of them has a
  caller waiting for an answer.
- **`when` guards make the runtime-variable case a handler, not a second tree.** A scope whose containment
  direction is decided by a negotiated bit anchors to the transport with two guarded handlers — retire when
  the flag was set at creation, suspend otherwise. The addressing position never moves.
- **`suspend` makes cascade revocable.** A suspended instance preserves its state, its frame and its key;
  no transition may fire and no message is legal against it; it resumes into the state it was suspended
  from when the anchor is restored, or when a new instance of the anchor scope binds to it. This is the
  only honest response to a cascade triggered by a *presumed* state — a belief must not irreversibly
  demolish confirmed state, and the peer that was merely slow must be recoverable.
- **`detach` survives the anchor and may be re-anchored** by a later transition. That is a counterparty
  being replaced under a live allocation, which is not a lapse and must not read as one.

**Totality is validated.** Every anchor must have a handler that fires on every path; a scope that
overrides the default must be shown total at document-validation time. The property §5.3 was written to
guarantee — nothing is silently forgotten — is preserved by validation instead of by shape.

**The parent-choice rule, stated normatively, because the previous draft's own worked answer got it
wrong.** The addressing parent is chosen by naming; the anchor is chosen by lifetime — *the scope whose
death genuinely ends this state's meaning*. A scope reached *through* another is not thereby owned by it.
Diagnostic: a scope whose creation trigger is "first reference" and whose only termination is its parent's
is a **state partition**, not a lifecycle scope, and its anchor is almost certainly wrong.

**Phases.** An instance is in exactly one phase, orthogonal to its states:

```
pending → live → lingering → forgotten
             ↘ suspended ↗
```

`lingering` needs no new object: it is a **terminal state with a `lingers` duration**. During it the
instance is not live, its key stays reserved against reallocation, correlation still resolves to it, and
the transitions declared on that terminal state still fire — which is exactly what a completed exchange
needs to replay its cached answer and an abandoned one needs to refuse a late reply. `forgotten` releases
the key. Retiring an anchor does **not** cascade a lingering instance away, because the reason it lingers
is precisely that late traffic can still arrive.

This one phase closes, with no per-protocol rule: identifier reuse after wrap, non-idempotent request
replay, "a response with an unrecognised identifier must be silently discarded" (implementable only if
retired identifiers stay recognisable), and the reconnect hazard where a cascade frees a whole identifier
space at once and a straggler is accepted as the answer to a different question.

### 5.5 Configuration — frames, bindings, selectors

The previous definition — the set of `(live scope instance → current state)` — is too thin in three ways.

**(1) Instances hold values.** Every stress test hit this, and one called it the root of all its other
failures. Reassembly buffers, sequence cursors in cyclic domains, retry counters, negotiated window sizes
and encodings, granted-narrower-than-requested capabilities, the retained request a positional correlation
must join against, competing simultaneous offers each with its own terms — none of them is a state, and
enumerating their domains as states is unbounded. Worse: guards, keys and packing selection all need to
read them, and §6 Observations are explicitly not part of the configuration, so a value held only as an
Observation is invisible to a guard. Two of the model's own features already presume this exists — the
`effect` on a transition has no target namespace without it, and a packing guard cannot consult a
negotiated parameter that decides which packing is legal.

Every instance therefore carries a **frame**: entity bindings, each with its own provenance. This is the
same machinery as §1/§6, not a parallel one — a frame binding *is* an Observation whose subject is scoped to
this instance. Which supplies the missing edge in the other direction too: an Observation may be **held by**
a scope instance, and it cascades with it. Invalidation depth becomes anchor depth — facts about the durable
subject anchor high and survive, facts about the transient run anchor low and die with it — so partial
invalidation needs no glob syntax and no enumeration of fact classes. It falls out of where the fact was
hung.

**(2) Existence is not boolean, and neither is a state.** See §5.8.

```
Configuration := ∀ instance :  ( phase, existence-stamp, state-set per space, frame )
```

**(3) One event binds several instances, and they are not on one path.** A single inbound datagram
routinely binds two to four instances across *sibling* scopes — a reliability exchange and a
request/response exchange; a transaction and a connection and a peer-capability record and a shared-medium
holder — and which ones it binds is decided by fields of the datagram being routed. "The current frame
implicit" is then ambiguous, and relative addressing silently resolves to the wrong sibling.

**Correlation's output is a BINDING**: an ordered map `scope declaration → instance`, computed once per
event, **before any guard is evaluated**, and carried as the evaluation environment for every guard,
transition and effect that event drives. A message declares every scope it binds (§5.3), in order, so a
later projection may read an earlier-bound instance's frame — which is what makes positional and
by-reference correlation expressible. `onMiss: create` executes during binding.

Addressing:

```
local.connection.open                          // absolute: space, scope path, state
remote.peer.exchange[capture.invokeId]         // an explicit instance
exchange.awaiting                              // resolved IN THE BINDING; ambiguous ⇒ validation error
this.frame.grantedWindow                       // a bound value on the current instance
peer.exchange{ where(state = suspended) }      // an instance SET — a selector
```

**Selectors** are one construct with two sugars, and they carry a surprising amount of the corpus's weight:

```
Selector := where(<predicate over phase, state, frame, epistemics, identity>)
          | under(<instance>)            // sugar: descendants by `contains`
          | anchoredTo(<instance>)       // sugar: dependents by `anchors`
```

A selector is an instance set, and the same construct serves three demands that arrived separately: it is
the target of a **quantified effect** (resume every suspended transfer under a resumed session — one
trigger, N instances, N unknown at authoring time); it is the `within:` of the legality relations (§5.7),
where the grouping needed is frequently *semantic* — every instance agreeing on a subject, or on a
configured partition function that appears in no message — and therefore cuts across the addressing tree;
and it is the candidate set of `match` correlation (§5.3). There is no separate "aspect", "resource node"
or "quantified influence" notion; there is one selector.

**The extractable-subgraph claim is restated, and it is weaker than before.** Given a configuration, the
reachable subgraph partitions into the **definite** set (guards evaluate true) and the **contingent** set
(guards evaluate unknown, per §5.8). Both are computable and both are reportable. The previous claim that a
*unique* subgraph is extractable is false whenever a guard reads a state the protocol never confirms; saying
so is better than manufacturing certainty at exactly the point the model claims to have none.

### 5.6 Transitions — triggers and effects

```
Transition {
  trigger : Trigger
  guard?  : <predicate over the configuration and binding>
  effect? : Effect
  onsetAt?: <expr>                       // the instant it actually occurred, if earlier than detection
}

Trigger := sent(<message class>) | received(<message class>)
         | when(<predicate>)
         | anchorLost(<anchor>) | anchorRestored(<anchor>)
         | identified | restored | external(<name>)

Effect := { <target> := <state> | <target>.frame.<entity> := <expr> | <target>.epistemics := <stamp> }…
        + { emit <message> to <Delivery> }…
```

**`when` is the whole of the timing vocabulary**, and it subsumes four things that were requested
separately. A deadline is `when(now ≥ frame.renewAt)` — so a single state may arm any number of named,
independently cancellable deadlines whose instants are *derived from received data* rather than being one
untyped constant. A freshness lapse is `when(stale(remote.registration))` — the case where a peer simply
dies, nothing arrives, no obligation is breached, and the degradation itself is the only event. An upward
aggregate is `when(count(exchange{where(state = failed)}) ≥ k)`, which is the only way a connectionless
peer scope can ever be retired, and evaluating it continuously rather than at child termination is the
difference between suppressing a thousand retransmissions and suppressing none. And a witness disagreement
is already handled by `generation: witnessed(…)` (§5.3), so it needs no trigger of its own.

**A message class, not a message name**, on `sent`/`received`: a name, a set, a direction, or `any`. That
dissolves the requested "witness relation" — an obligation discharged by *any* outbound traffic, a liveness
belief refreshed by *any* completed exchange — into the trigger vocabulary that already existed.

**`onsetAt` separates occurrence from detection.** Some retirements are inferred from evidence that also
dates them, typically tens of seconds before we found out. Cascading from `now` either keeps state recorded
during the lag under an epoch it did not come from — silent corruption — or discards facts unnecessarily. A
trigger with an onset declares the window `[onsetAt, now]`, and effects sweep it: re-attribute to the
successor, or discard. Paired rule: **lifetime effects are applied at delivery-completion granularity,
never mid-delivery**, so an instance can never be retired by the very message it is still carrying — the
re-entrancy that otherwise destroys the evidence of its own cause.

**Effects reach anywhere addressable and may emit.** An effect is a set of assignments to paths or
selectors plus a set of emissions, applied **atomically against the pre-event configuration** (so effects
cannot observe each other's partial results), with retirements applied last. Emission is not optional
sugar: several protocols *mandate* a reply on a state transition — a negative acknowledgement on an
out-of-window segment, a reset on an unmatched request — and without it every "you must answer" rule has to
live outside the model. Assignment to a path that is not the current instance is likewise required: a fact
learned inside one exchange (the peer does not support this capability) must be written to the peer, which
is an ancestor, and a response frequently writes a transaction, a connection, a peer and a capability
record from one datagram.

```
Delivery := <scope instance ref> | group(<selector>) | unidentified
```

A guard currently selects a packing, which is bytes. Some protocols distinguish two exchanges *only* by
whom they address — one unicasts to the counterparty that granted something, the other broadcasts to
whoever will answer — with byte-identical content. Selecting a delivery alongside the packing is what makes
"what can I send, and to whom" answerable; the mechanism is a scope reference, which is why the definition
sits here even though the selection happens in §4.

**Three requested mechanisms dissolve here and are deliberately not added.** An *obligation object* with a
retry schedule is a child scope with a frame, a `when` trigger and an emitting self-loop — the attempt is
already an instance, and modelling it as one gives per-attempt values a home and a static bound on packet
count that a capped back edge does not. An *election* over a bounded candidate set is an admission window
(§5.3) plus a `when` trigger plus a selector plus an `argmax` over frames, with the losers given a declared
terminal state; the protocol graph holds the structure — that there are N mutually exclusive alternatives
which collapse to exactly one — and the document holds the preference. A *refresh-in-place* transition that
re-stamps freshness without changing state is a self-loop whose effect is epistemic only.

### 5.7 Message legality

```
validIn(paths…, onUnknown: refuse | permit)   // default refuse
requires(message, within: <Selector>)
excludes(<discriminator?>, within: <Selector>)
admits: <state> → { message… }                // a CLOSED set, declared on the state
```

`within:` takes a **selector**, not a tree path. The exclusions that matter are frequently over a semantic
grouping rather than a containment one — two individually-legal writes to the same subject at the same
priority under different correlation identifiers are jointly incoherent, and their nearest common ancestor
forbids far too much. A selector says exactly what is contended, including over a partition function
supplied by configuration rather than observed on the wire.

`excludes` takes an optional **discriminator expression**: *at most one distinct value of this expression
per selected set*. Without it, "a mandated retransmission of the same request" and "a second, different
request" are the same message name and the relation must forbid both or neither.

`admits` is the inverse of `validIn` and exists because whitelist-by-omission fails silently: a state that
accepts two messages out of forty should say so once where it is true, not require an annotation on the
other thirty-eight and on every message added later.

A **retirement obligation** is a validation rule, not a relation: a scope declares which terminal states it
may retire from, plus an explicit abandoned terminal, so an operation whose exit is data-driven cannot
silently vanish mid-flight and an unmet obligation is a reportable outcome. That is what `requires` cannot
express for an iteration whose length is unknown when it starts.

Two boundaries, stated so they are not silently absorbed here:

- **Structural legality is not §5's.** Constraints wholly within one message — mutually exclusive option
  groups, a subfield that must be zero when its enabling flag is clear, a reserved pattern, a correlation
  value that must be non-zero — read no scope and belong to §4 as **packing invariants**, checked at
  document validation where operands are constant and at encode otherwise. §5 currently claims all legality
  and covers only the configurational half.
- **Vacuity is assertable.** A protocol with no session and a bearer credential on every message genuinely
  has no state-gated message legality. An empty relation set is indistinguishable from an unfinished
  document; an explicit assertion validates as complete.

### 5.8 Epistemics

One stamp, available on **any** state in any space, on an instance's **existence**, and on any frame
binding — the same machinery as §6, because it is the same idea:

```
Stamp {
  origin     : chosen | observed | inferred | asserted | restored
  confidence : <ordinal>
  asOf       : <instant ± uncertainty>
  decay      : none | <expr>
}
```

**Origin replaces the confirmed/presumed pair**, which cannot name two origins the corpus needs. `chosen` —
we did it (the Local default, and the reason Local documents get no noisier). `observed` — a message said
so. `inferred` — deduced, *including from an absence*; a timeout is a legitimate origin. `asserted` — the
peer told us about a past we never witnessed, restoring state this run never created; as good as the peer's
memory and dying with it. `restored` — loaded from our own durable store, with `asOf` carried forward so
decay is measured against real elapsed time rather than uptime. Each has a different decay profile, which
is why one flag cannot carry them. §5.7's rule survives intact in the new vocabulary: a transition driven by
a message yields `observed`, one driven by inference yields `inferred`, and the distinction is carried, not
computed, so a guard can still say *only if we have actually been told*.

**Settleability is declared, not inferred from a low confidence.** A state whose `settledBy` list is empty
asserts that no evidence can ever settle it — whether a peer released a candidate we declined, whether a
notification we caused reached third parties, whether an unacknowledged-by-design message arrived. Marking
these merely low-confidence falsely implies they could become confirmed, schedules refreshes that can never
resolve, and invites a user-facing suggestion to go and check something uncheckable.

**Existence is epistemic.** Over an unreliable transport, creating a Remote instance on send asserts that a
datagram arrived. It may not have. The old configuration was a crisp set of live instances while §5.1
declared Remote never directly known; a connectionless protocol breaks that pair on packet one. Membership
therefore carries the same stamp, and a guard may test presence with a confidence floor.

**Evaluation is three-valued.** `true | false | unknown`, where unknown arises from a presumed-absent
instance, an indeterminate state set, or a capability learned only by silence. Unknown propagates;
`validIn` defaults to `refuse` on unknown and may declare `permit` (probe-and-see). A guard whose value on
unknown is not determinate is a validation error, so "never seen" can never be silently conflated with
"seen and false" — which is the difference between suppressing first contact with every new peer and making
the guard vacuous.

**A machine's current state may be a set.** Silence is the dominant failure signal in half the corpus and
is byte-identical across causes with entirely different remediations — wrong credential, host down,
filtered, deliberately muted, reply lost, reply too large to route. A scalar confidence on one chosen state
is an unsound assertion, and *we learned nothing* is not representable as a result at all. A candidate set
with **elimination** by later evidence is; a singleton is the normal case and costs nothing.

**Freshness is not a scalar TTL.** It is `(evidence source, renewal trigger, decay)`, and it is legitimately
**absent** with a stated reason: one belief decays because addressing can be reassigned and renews on an
announcement; one decays in seconds and renews on re-read; one — the peer is awaiting our answer — neither
decays nor renews, and demanding a number there gets a fabricated one that a guard then inherits. Where a
scope's key *is* the epoch (`generation: witnessed`), scope liveness is authoritative and freshness is
demoted to a re-acquisition hint; two mechanisms answering "is this still true" must not be able to
disagree.

### 5.9 Durability

**In scope, minimally, as a scope property.** The argument is not convenience: the model claims the
configuration determines what is legal to send, and for at least two corpus protocols the legal *first*
message after a process restart differs precisely because state survived — a reboot-with-a-remembered-
allocation request, a resumed session that must not re-subscribe and must retransmit unacknowledged
transfers with a duplicate flag. Excluding persistence makes the extractable-subgraph claim false at t0.
Knowledge learned only by being refused is the other half: some protocols offer no discovery message, so
each such fact costs a wasted round trip and an error to acquire and is worth caching for days.

```
durability : transient | durable
retention  : <bound>          // required when durable
```

Three obligations, all validated:

1. A durable scope's identity must be **restart-stable** — an endpoint name qualifies, a socket handle does
   not — so it can be rehydrated at all.
2. Restoration is the `restored` trigger, and restored states and frames carry `origin: restored` with
   `asOf` set to the recorded instant, not to now.
3. A durable scope must declare a retention or eviction bound. A scope that never retires and can be
   created by scanning a large identifier space otherwise grows without limit.

What is **out** of scope for the model: the storage mechanism, the file format, and any statement about
where the store lives. The model declares which instances persist and in what epistemic condition they
return; the engine decides how.

### 5.10 Correlation, restated

The old table's convergence claim survives in weakened, more useful form: **the scope is one notion across
the corpus; the key is not.** Corrected:

| Correlated by | Is | What a bare key missed |
|---|---|---|
| a request identifier | a keyed scope | composite with the local endpoint; an admission predicate, because identifiers are guessable |
| a transaction identifier per connection | a keyed scope | the addressed-unit level is a **sibling durable scope**, not a nesting level — nesting widened a uniqueness constraint into legality and cascaded away days of cached knowledge |
| a reliability identifier and a request token | **two sibling scopes, two directions each** | not nested in either direction; one of them is resolved by reference, not by a field |
| a packet identifier | a keyed scope | two disjoint spaces over one transport, told apart by ambient direction; a generation, because identifiers are released and reused |
| an invocation identifier | a keyed scope | composite with role and originating device; a different projection per message |
| an echoed nonce | a keyed scope | a *minted* key — the allocation half `Resolvable` never had |
| an element's ordinal within a batch response | a keyed scope | its key is absent from the response; it joins against the retained request in the correlating instance's frame |
| a range predicate over responders | a **matched** scope, cardinality many | not a key at all; completed by silence, bounded by an admission window |
| position in an outstanding queue | a **matched** scope with an ordering | no correlation field exists on the wire |

### 5.11 Ranking

**Load-bearing** — a corpus protocol is inexpressible or silently wrong without it:

| Change | Driven by | Cost of omission |
|---|---|---|
| `anchors` split from `contains`, with guarded handlers | 6/6 | state that must outlive its parent is destroyed; two protocols unusable |
| lingering phase + generation | 5/6 | late traffic silently answers a different question |
| instance frames (and observations held by instances) | 6/6 | guards cannot read negotiated values; nothing else in this list works |
| binding, computed before guards | 4/6 | relative addressing resolves to the wrong sibling |
| per-message projection, `admits`, `onMiss` | 4/6 | mandated replies inexpressible; forgery indistinguishable from noise |
| epistemics off the root | 6/6 | contradicts P6; our own uncertain history unrecordable |
| three-valued evaluation + presumed existence | 3/6 | manufactures certainty exactly where the model claims none |
| spaces as facets | 4/6 | every dual-faced object declared and keyed twice, kept in step by convention |
| `when` triggers, `onsetAt`, message classes | 5/6 | data-derived deadlines, aggregates and freshness lapse all inexpressible |
| effects: emission, remote targets, selectors | 4/6 | "you must answer" rules leave the model |
| deferred identity + reconcile | 2/6 | discovery has no representable pre-reply state at all |
| durability | 5/6 | the legal first message after restart is wrong |
| ordered key domains | 2/6 | the exclusion that matters cannot find its target |
| selectors on `within:` | 2/6 | exclusion forbids far too much or nothing |

**Load-bearing but narrow** — one or two protocols, and fatal there: the `World` space (an unguardable
proposition otherwise); `onsetAt` sweeps (silent mis-attribution otherwise); aliasing/merge (responder-side
only); delivery selection (one protocol's two modes are otherwise identical).

**Convenience** — real, but a document can work around them: candidate state sets (degrade to a single
`indeterminate` state and lose the discrimination); `admits` closed sets (expressible as N `validIn`
clauses that fail by omission); vacuity assertion; uncertainty intervals on instants; retirement-obligation
validation.

**Deliberately not added** (each was requested and each dissolves): obligation objects, election operators,
refresh-in-place transitions, witness relations, mirroring edges, resource nodes, aspects, ordinal and
by-reference correlation rule kinds, upward evidence-propagation edges, quantified influence edges. Ten
requested mechanisms, zero new notions — that is the dedupe result, and it is the main evidence the cut
above is in the right place.

### 5.12 Purity, and what is not closed

**Purity.** No construct names a protocol. Five resisted and were generalised: a clean-start flag deciding
containment direction became a *guarded anchor handler*; an unobservable third-party notification became the
*World space*; a modal state that answers everything else with silence became an *`admits` set plus an
unsettleable state*; a shared half-duplex medium known only from configuration became a *selector over a
configured expression*; and a segmented transfer that may re-key per segment became *anchors as a DAG plus
deferred identity*. §9's guard applies unchanged.

**Not closed.**

1. **State as an input to decoding.** Some segmented transfers are not independently parseable — the parse
   target is an accumulation that appears on no wire, and the dependency inverts: you must transition
   before you can decode, and only a state predicate says a decodable object exists. §5 supplies the hook
   (a frame region plus a predicate) and nothing more; the two-tier envelope/payload mechanism belongs to
   §4 and §7 and is not specified. Encode inverts identically and is equally unspecified.
2. **Positional correlation depends on §4** exposing a repetition's ordinal, and on a declared arity
   relation so a length mismatch is a decode error rather than a silent truncation. Neither exists yet.
3. **The extractable-subgraph claim is now weaker** (§5.5), and whether a contingent set is *useful* to an
   assistant — or merely honest — is untested.
4. **Election policy's boundary is unsettled.** The structure is in the protocol graph and the preference
   expression is document-supplied; whether preference belongs in the protocol graph at all, or in the run,
   is not decided.
5. **`match` correlation's cost.** Index hints are declared and unproven; a large candidate set with a
   range predicate may not be servable at line rate.
6. **Combining evidence.** Candidate sets eliminate; they do not accumulate. Two weak inferences pointing
   at the same conclusion produce no stronger a belief than one, and no distribution is defined.
7. **Clock skew is acknowledged and thin.** `asOf` carries an uncertainty, but a deadline derived from a
   peer's clock is known only up to an unmeasured offset, and nothing makes that offset a first-class
   quantity — so a locally-armed deadline derived from remote data will always overlap the peer's view by
   an amount the model cannot state.


