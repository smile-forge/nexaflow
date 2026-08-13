# DynamicProtocol — the graph model

The target. `protocol-graph-migration.md` is why; this is what.

Settled with the author 2026-08-13. Where it says *open*, it is genuinely undecided and the answer
should be written back here.

## The one sentence

A protocol is **one graph**. Three families of edge run through it and they intersect at exactly three
kinds of node — fields, state, and inputs. The engine looks up a message, picks the packing that fits the
current state, walks that packing to build the message, and runs each node's requirements path to resolve
its value.

## Nodes

| Node | What it is |
|---|---|
| `Message` | What is being spoken. Points at its packings. |
| `Packing` | **New.** One arrangement of a message. A message has one or more. |
| `Field` | Something that occupies octets. |
| `Slice` | **New as a node.** A run of bits inside an octet group, with facets of its own. |
| `Term` | **New.** One node of a value computation — a literal, an operator, a call, a reference. |
| `Context` | A value from outside the message. |
| `Subject` / `Phase` / `Party` / `Recall` | State, as today. |
| `Rule` / `ValueSet` / `Concept` / `Default` / `Subprotocol` | Meaning, as today. |

## Edges, in three families

**Arrangement — what follows what.**

- `Packs` — message → packing.
- `Starts` — packing → its first node.
- `Then { When? }` — node → the node after it. The guard is a term. This single edge, guarded, is
  sequence, repetition and alternation: an unguarded `Then` is "and then"; a `Then` back to the node it
  left is a chain; several `Then` edges out of one node are a choice, and which one is taken is whichever
  guard holds.
- `Fits { When? }` — packing → the state node or term that makes it the right arrangement. Absent on a
  message with only one packing.

**Computation — how a value is resolved.** Starts at the field, which is the intersection.

- `Computes { Role }` — field or slice → the term rooted at its computation. Role survives only as long as
  a node has more than one computation; see *open* below.
- `Uses { Ordinal }` — term → operand term.
- `Reads { Facet }` — term → the field or slice whose facet it denotes.
- `Draws` — term → context.
- `Recalls` — term → a state slot.

**Meaning.** `Names`, `Admits`, `Constrains`, `Embeds`, `Speaks`, `Triggers`, `Moves`, `Viewed`,
`Remembers`, `Assumes` — unchanged.

The families are separate because they answer different questions, and one graph because they have to
meet: a `Reads` edge from a term lands on a field an arrangement edge also touches, and that join is the
whole model. Nothing else needs to know both.

## The walk

1. Look up the message.
2. Choose the packing: evaluate each `Fits` guard, take the one that holds. Two holding is an error, as
   two matching transitions already are — not resolved by declaration order.
3. Walk `Starts`, then `Then`, evaluating each guard as it is reached.
4. At each node, demand its facets. A facet's prerequisites are the terms it `Computes`; a term's are what
   it `Uses`, `Reads`, `Draws` and `Recalls`.

Demand-driven, as now. **Terms are schedulable, and that is not an optimisation** — a term can be
*temporarily* unresolvable rather than wrong. A header carrying the length of a body that has not been
injected yet must suspend and resume, and that only works if the term is a unit of work the resolver owns.
This is the existing facet model extended one level down, not a new mechanism.

## What this deletes

Everything below exists to stand in for a missing edge, and goes when the edge arrives.

| Goes | Because |
|---|---|
| `Contains { Ordinal }` | `Then` |
| `Pattern.Group` | a stretch of a packing |
| `Pattern.Choice`, `Arm`, `Offers.Key` | several `Then` edges with guards |
| `Pattern.Chain`, `Pattern.Assorted` | a `Then` that returns, guarded |
| `Chain.Seed/Carry`, `Assorted.Seed`, `Arm.Carry`, `Roles.Carrying` | a term reading the previous occurrence |
| `ExprSite`, `Vocabulary`, `Roles` | reachability |
| `FieldReferences` / `ContextReferences` / `PresenceReferences` | edge traversal |
| `Optionals`, `present.x` | no `Then` reached the node |
| `Named` / `FacetNamed` | one relation, one spelling |

Both HPACK refusals go with them. A carry that reads inside a branch is a term under a guarded `Then`, so
it is demanded only when that edge is taken — the prerequisite is not "optional", it is *absent*. And a bit
group settling as one unit stops being a thing that can happen once slices are nodes.

## What must survive

From the migration note, unchanged: the facet model (no passes, no fixed points, no back-patching);
identity is the object, and any node a record owns is copied explicitly in its copy constructor; one
declaration serving both directions, with asymmetries sited and declared; the RFC/engine rule; and
canonicality.

Add one: **`Expr.Parse` stays.** Authoring a graph by hand would be miserable and the string syntax is
good. What changes is what parsing produces.

## Open

- **Does arm selection unify with packing selection?** A guarded `Then` and a guarded `Packing` are the
  same mechanism at two scales, and if they are, `Fits` is just `Then`'s guard on the edge out of the
  message. Likely yes; decide before writing either.
- **How does a term name a previous occurrence?** This is what replaces threading, and it is the sharpest
  remaining question. Something like `Reads { Facet, Of = Previous }` — occurrence-relative rather than
  node-relative. Everything about folds, tables and running totals rests on it.
- **Does `Role` survive on `Computes`?** Only needed while one node has several computations. If a slice
  is a node and a guard lives on an edge, a field may have exactly one.
- **Are terms memoised per occurrence?** Almost certainly yes, but say so.
- **Width, byte order, signedness** — stay as pattern data on the field rather than becoming nodes.
  Recommended: yes, they are properties, not relationships.

## Order

The author has said an intermediate broken state is acceptable, so this is a straight line rather than
green-at-every-step. The corpus is the safety net at the end of it, not during.

1. `Term` nodes, with facets, scheduled by the resolver. Evaluator walks terms.
2. `Slice` nodes, with facets.
3. `Packing` and `Then`. `Contains` retired; group, choice, chain and assortment re-expressed.
4. Delete the compensations in the table above.
5. Re-green the corpus.

**Acceptance:** the corpus round-trips as it does today, and the two refusals pinned in
`HeaderTableCaptureTests` flip to passing without either being edited into agreement.
