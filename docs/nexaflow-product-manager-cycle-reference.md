# Nexaflow Product Manager — Cycle Reference

A one-page map of the design. Not a build spec — a reminder of the shape, to glance at before
writing a Claude Code task so the right contracts make it into the prompt.

---

## The cycle

The product moves through four phases, then turns. The viewlet is the launchpad onto whichever
phase the product currently sits in.

```
   ┌─────────────────────────────────────────────────────────┐
   │                                                         │
   ▼                                                         │
 As-Is  ──▶  Transition Plan  ──▶  Change  ──▶  Review  ─────┘
 (tree)      (shaping canvas)      (work)       (seal)
```

| Phase | Surface | Owns | Writes to tree? |
|---|---|---|---|
| **As-Is** | Two-layer sunburst | *What is* — present-state truth | reads only |
| **Transition Plan** | Swim-lane shaping canvas | *What we intend to change* | no (writes planning side) |
| **Change** | Work-package execution (Claude Code, Loop 1) | *What is being changed now* | no (writes work items) |
| **Review** | Reconciliation / seal | *Reconciling reality back into truth* | **YES — the only writer** |

Review's output **is** the next As-Is. The cycle turns: As-Is → Plan → Change → Review → As-Is′.

---

## Three principles (defend these — they look like flaws and aren't)

1. **Only Review writes to As-Is.** Three phases can't touch the tree; one can, and its whole job is
   reconciliation. So "why did the tree change?" always has one answer: *a snapshot was reviewed.*
   Single valve, clean audit.

2. **The tree is eventually-consistent; the consistency point is Review.** You work on **vNext**
   (mutable, expected to be in flux). Review **seals** vNext into an immutable **snapshot**. Between
   Change landing and Review sealing, the tree is stale *on purpose* — the same accepted seam as
   committing a version number and release notes before a release actually ships. Truth is
   deliberate, not reactive. Sealed snapshots are never stale because they record *what was true
   then*, not what's current.

3. **Churn is git-invisible; snapshots are the git-committed pair.** Day-to-day planning churn
   (claiming gaps, shaping, dragging lanes) never touches git — it would bury code history under
   planning noise. A **snapshot** is committed: an `.md` (legible in diffs/PRs/by Claude Code) **and**
   a `.json` (the app loads/compares/computes). Two views of one sealed truth → **releases are
   comparable** by diffing snapshots. The md-diff between two snapshots is most of a release note for
   free.

---

## Snapshots, vNext, releases

- **vNext** — the single mutable tree + planning state. The only writable thing. Not in git.
- **Snapshot** — a sealed point-in-time consistency boundary (a *private release*). Produced by every
  Review. Immutable. Committed to git as `.md` + `.json`. Previous snapshots are read-only.
- **Public release** — a snapshot *chosen* to ship, and tagged. Orthogonal to snapshotting: every
  Review snapshots; only some snapshots are blessed public. A `git` snaplink resolves a node's
  version by which snapshot-tag it last went `done` in.

```
.product/
  vNext/            # mutable working tree + planning — NOT in git (app store / git-ignored)
    tree.json
    planning/…      # gap-ownership, work items, lanes, dependencies
  snapshots/        # sealed consistency points — COMMITTED to git
    v0.5.0.md  v0.5.0.json
    v0.6.0.md  v0.6.0.json
```

---

## Entity model (reference)

**Hierarchy:** Product → **Workload** (1 process, or N) → **TreeOfFunctionality** → **Nodes** (nested).

**Node** — the *only* content primitive (no "items"; a parent and a leaf are the same thing).
Holds: title, children, statustags, snaplinks. The tree is **present-state only** — never work
history, never plans.

**Statustag** — a tag carrying exactly one present-tense status. No "not done", no history, no reason.
- `done` — exists / works
- `should` — should exist, doesn't yet  (← net-new features enter scope *here*)
- `shouldnt` — deliberately decided against (a recorded negative requirement)
- `faulted` — exists but broken
Attaches to nodes, to cross-cutting **concerns** (a concern is a statustag spanning many nodes), and
to snaplinks. Derived, never stored: **queue** = `should`−`done`; **bugs** = `faulted`;
**deprecation** = `shouldnt` + live snaplink.

**Snaplink** — one typed *loose-binding* primitive (records intended alignment, doesn't break on
misalignment — misalignment is *signal*). Carries its own statustag + an **advisory** assessment
(separate field, never overrides status). First-cut types: `markdown` (by title-path), `code` (by
`Namespace.Class.Method`), `git` (by tag/branch — also the version-resolution mechanism, nearest-git-
ancestor wins). Engine is a dumb background sweep ("snaplink, need updating?"); the snaplink decides
cheap-check vs LLM, advisory-only.

**Gap** — *derived*, never authored: any node not `done`/`shouldnt` is latent work. `should` = a
build-gap; `faulted` = a fix-gap. Shown in Transition Plan as a breadcrumbed **list** (not a tree —
shaped work doesn't care about origin structure).

**Work item** — minted by dragging a gap onto the canvas (an act of **ownership transfer**: the gap
leaves the list because it's now *claimed*, not because its tree-state changed). Holds `0..n` node
backlinks (its gap-sources). Can **split / join / link**. Its **swim-lane = its release target**
(position is the data). 
- *Join* unions gap-sources. *Split* copies the full source set to both halves (prune manually).
  *Remove* **returns** ownership → the gap reappears in the list.
- **Invariant:** every gap is either unclaimed-and-listed or owned by exactly the work items that
  claimed it — never lost, never duplicated. (Assert in tests.)
- A claimed gap whose node's live state **changes** (e.g. a node planned for extension goes
  `faulted`) **alerts** its work item — the plan's ground moved, review it.

**Dependencies** — typed edges between work items. *Same-lane* edges build the work-package
hierarchy (the work-package list renders as a dependency-ordered vertical tree). *Cross-lane* edges
**collapse** to an item→**release** dependency (you don't see another release's internals). Resolution
changes with the view.

**Scope gate:** every work item derives from a gap, and a gap is a node — so **all planned work
already exists in the tree as at least a `should`.** To plan net-new work you first add a `should`
node (one cheap capture action = an act of scoping). Scope creep on the planning side is therefore
*structurally impossible*. This is load-bearing friction — keep it.

---

## Completion → Review valve

When a work item completes, its node backlinks drive an **evaluation** at Review (not an auto-write):
for each referenced node — does it go `done`? does a cross-cutting concern's status change? **should
the node subdivide?** (the one sanctioned path by which work reshapes the tree — legitimate because
completed work *is* a change in reality). Build-gaps evaluate "is the `should` now satisfied"; fix-
gaps evaluate "is the `faulted` now cleared." Confirming the batch for a snapshot **is** the atomic
"this is the new As-Is" seal.

---

## Loops (build order = dependency order)

1. **Capture** (built first) — create node + statustag; nothing else works without a populated tree.
2. **Survey** — the sunburst (two layers, focus + children, **mandatory subtree rollup** so focus
   never hides a downstream fault) + derived lists. Read-only.
3. **Snaplink engine** — the dumb sweep; markdown/code/git; advisory writes only.
4. **Transition Plan + Change** — gap list, shaping canvas, work-packages.
5. **Review** — the seal: evaluate completions, write tree, emit snapshot (md+json).
6. **Build** (Claude Code skill, built *last*) — resolves node via snaplink, sets statustag, commits
   with the code. Needs a populated tree + snaplink resolution to exist first.
