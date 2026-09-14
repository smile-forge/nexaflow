# The product graph

`nfi graph` builds one graph from three layers: the **product tree** (`.product/tree.json` — the authoritative
feature inventory and per-component status), the **code** (every file in the repository, parsed into types, members
and edges), and the **snaplinks** that bind a node to the code, tests and docs behind it. This document is how the
tree is shaped, how its snaplinks behave and are checked, what enforces the model, and what the graph as a whole can
and cannot answer.

- Reading and changing the repository through `nfi` (`ask`, `graph edit`, `test`) is in [CLAUDE.md](../CLAUDE.md).
- Backing a node with the right kind of test — the journey, the leaf unit tests, `[CoversNode]` — is in
  [testing.md](testing.md).
- The product-folder skill has fast-query recipes; the per-release export [product/PRODUCT.md](product/PRODUCT.md)
  is the human dashboard; per-feature tab parameters are in [features.md](features.md).

> The `.product/` tree is gitignored working state (the app live-reloads it). Edit it **only through the `nfi`
> CLI**, never by hand — see [The CLI](#the-cli).

---

## Querying the tree

To locate a feature's code, tests or docs, query the tree first — it beats grepping, because every node carries
snaplinks to its source:

```powershell
& $nfi find <term>                # nodes matching id/title/description
& $nfi describe <node-id>         # path, concerns, code/test/doc snaplinks
& $nfi describe <node-id> --code  # …plus every code snaplink resolved to its real source block (from YOUR working tree)
& $nfi tree [<node-id>] [--full]  # the WHOLE subtree as an outline — "show me this entire feature"
& $nfi query --under <id> --concern tests --status should --leaf   # e.g. leaves still owing a test
& $nfi lint --under <node-id>     # does this feature follow the modelling rules? (advisory)
& $nfi diff                       # what changed in the tree since the last release snapshot
```

---

## Features and Common / Shared are different kinds of list

Before the shape of a subtree, the shape of the two halves — they are not the same thing modelled twice:

| | Answers | A description says |
|---|---|---|
| **`Features > …`** | *what can a user interact with* — a structured list of **use cases** | what the user does, and what they are trusting when they do it |
| **`Common / Shared > …`** | *what functional blocks and capabilities exist* — which is why it aligns closely with code structure | the mechanism, its contract, and the traps in it |

**The same code can legitimately be modelled on both sides, and that is not duplication.** The Product
tab's *Snaplink validation* is a use case: open the Integrity page, see what broke, re-point it. The
validator is a capability: what it checks, what it refuses, what it treats as unverifiable. Deleting
either one loses something real.

What goes wrong is the **description**, not the existence of the node. A `Features` node that describes a
class, a store's write-scoping rules or an engine's contract has absorbed capability detail — usually
because the capability has no node of its own yet. The fix is to move that detail to the Shared node
and leave the feature node saying what the user does, naming its capability at the end.

This also settles which node a test declares: a unit test whose **subject** is the capability declares the
**Shared** node (the same subject rule that decides which suite it lives in), while a use case wants a
test that exercises the use case. When a `[CoversNode]` and a node look mismatched, it is one of two
things — the test class is scoped wrongly, or the node is not granular enough to name what the test
actually pins down. Fix the cause; do not fold several precise declarations onto one coarse node.

---

## The shape of a feature subtree

Each feature root lives under `Features` and has (up to) three children — **UI**, **Functionality**,
**AI Integration** — so the three concerns never mix:

```
<feature>                         (feature root — carries the "AI Ready" maturity verdict)
├─ UI                             everything the user sees or touches
│  ├─ <Panel>                     first layer = panels: a distinct visual region / surface
│  │  ├─ <Control>                leaf: one node per button / toggle / input / display
│  │  └─ <State group>            a logical grouping of controls that share a state (e.g. Edit_mode)
│  │     └─ <Control> …
│  ├─ <Panel (state-governed)>    a panel may be gated by a state (Search Bar ← IsSearchActive) — still a panel
│  └─ …
├─ Functionality                  behaviours the feature performs that are NOT a UI control or an AI tool
│  ├─ <Behaviour>                  the "steps of a use-case": search engine, windowing, encoding-detect,
│  └─ …                            file-monitoring, confined-window editing, file-splitting, …
└─ <feature>-ai                   AI Integration
   ├─ <feature>-ai-context        get_context honesty
   ├─ <feature>-ai-act            client tools — ONE leaf per tool (<…>-ai-act-<tool>)
   └─ <feature>-ai-preview        IContextPreview
```

**Panel vs. state node** — the one subtlety:
- A **panel** is a distinct visual surface (the toolbar, the editor, the status bar, a pop-over like the
  search bar or split panel). It is a first-layer child of `UI`. It gets a `theming` concern.
- A **state node** is a *logical grouping* of controls that only appear in a state, but which share the
  parent panel's surface (e.g. `Edit_mode` groups Save / Cut / Paste / the "Editing" button, all living inside
  the toolbar). It has **no concerns** — it's pure structure.
- Split a control into two nodes when the same widget presents as two things to the user in two states — e.g.
  the editing toggle is an **Edit** button (read-only state) and a separate **Editing** button (edit state);
  "it's one ToggleButton" is an implementation detail.

**Node granularity:** a node for every button and every display control. Split a group (cut/copy/paste) into
separate leaves when their **test states differ** (copy works read-only; cut/paste are edit-only), because each
is tested separately.

**Ids are one flat global namespace.** `add-node` slugs the title, so a node titled "Run" under any feature
claims the bare id `run`. Give every node a feature-prefixed id (`dotnet-verb-run`, not `run`). A feature modelled
in isolation tends to claim generic ones — `cine`, `measurement`, `reports`, `ai-integration` — which read fine
inside their own subtree and are unusable from outside it. `rename` fixes them under validation, retargeting the
parent, the children and every `node` snaplink, but **not** a `[CoversNode("old-id")]` in test source — retag in
the same commit (NXCOV002 flags what you miss). And **check for a stray duplicate root** before you start: an empty
sibling of the feature root, carrying only the feature-root `AI Ready` concern, is invisible in every view that starts
from the real root.

### A feature with more than one view

Several features are more than one tab — SysInfo is three pages, Processes and Projects and AI Chat are two,
Product is three with two more planned. It is tempting to make each view its own subtree with its own
UI/Functionality/AI beneath it, because a view genuinely *is* a bundle. Don't: **category first, views as
panels under `UI`.**

The reason is that the concerns don't decompose per view. `AI Ready` is a single feature-level verdict by
definition; `theming` only ever sits on UI nodes. Split by view and "does this feature's theming hold up?"
becomes a mental union of N subtrees instead of one node's derived status — and the feature-wide AI node has
nowhere natural to live — view-first ends up parenting it *inside* one of the views.

The navigational appeal of the view bundle is real, though, so keep it — in the **id prefix** rather than the
hierarchy, which can only encode one grouping:

```
UI            → product-integrity            (the page, a panel)
                product-integrity-issue-list
Functionality → product-integrity-scan
AI            → product-ai-context-integrity
```

`find integrity` then returns the whole view bundle across all three categories in one call. The shape gives
you the concern roll-up; the namespace gives you the view bundle.

**The one thing that does split per view is `get_context`.** Each view has its own `IPageViewModel`, so its
own `GetContext`, its own readiness gate and therefore **its own test state** — which is the rule from
*Node granularity* above. Collapsed into a single `<feature>-ai-context`, a view whose context is untested is
invisible — AI Chat's context surface, say, or `ProcessDetailViewModel`'s distinctive
`HasData || IsGone` gate (a process that has exited is describable as *gone*, rather than waiting forever for
data that will never arrive).

The **tool set does not split**: the views act on one underlying model, so a tool means the same thing
wherever it is called from. Only tools that act on what is *rendered* — the graph canvas's `focus_node` and
`read_graph` — are view-specific, and they sit beside the model tools under the one `-ai-act` node.

---

## Concerns, by role

The concern vocabulary is fixed in `product.json` (`theming`, `tests`, `docs`, `i18n`, `AI Ready`,
`Expanded`). Every node and concern carries exactly one present-tense status: `done` (exists / works),
`should` (should exist, doesn't yet — net-new work enters scope here), `shouldnt` (deliberately decided
against — a recorded negative requirement) or `faulted` (exists but broken). Which concerns a node *should*
carry depends on its role:

| Role | `theming` | `tests` | `AI Ready` | notes |
|------|:---:|:---:|:---:|-------|
| **Feature root** | ✔ | (derived) | ✔ (only here) | `AI Ready` is the human maturity verdict; it lives **only** on the feature root and nowhere below. |
| **Panel** | ✔ | — | — | theming, **no tests** — the one UI journey covers the integrated interaction. |
| **State node** | — | — | — | no concerns; pure grouping. |
| **Leaf control** | ✔ | ✔ | — | theming + tests. |
| **Functionality behaviour** | (if it renders) | ✔ | — | tests; most add `theming` only if they own a visual. |
| **AI act leaf** | — | ✔ | — | tests → the tool's test; may carry `Expanded` if the tool exceeds what the user can do. |

`theming`, `tests` are `is_default` (auto-attach to new nodes as `should`). `AI Ready` is **not** default, so `add-node` never puts it on a leaf. If a concern auto-attaches where it doesn't belong, strip it with
`remove-concern`.

**Strip it — do not mark it `shouldnt`.** The two look interchangeable and are not. `shouldnt` is a
*claim*: this node has a visual and it deliberately isn't themed — which is worth asserting, and worth a
note saying why. An absent concern is the weaker and correct statement for a node that simply has no
visual at all. The practical difference shows up later: when such a component *does* grow a visual
aspect, adding the concern is obvious, whereas someone facing a `theming=shouldnt` first has to work out
whether it means "no UI here" or "we decided against theming this UI" — and only one of those is safe to
flip. A WPF-free backend, a console tool, a pure grammar or a parser has no `theming` concern at all.

What each `tests` concern should be backed by — one journey on the UI node, one unit test per leaf — is
[testing.md → What a feature's tests look like](testing.md#what-a-features-tests-look-like).

---

## Granularity — is the node about one thing?

Every other rule asks whether the tree *says* what it should. These two ask whether the tree is granular
enough to be worth saying anything about, and they are the only rules that read a node's **size** rather
than its shape. They come in a pair because the same node usually trips both, from opposite directions.

| Rule | Fires when | Evidence |
|------|-----------|----------|
| `LeafCoveredByTooManyTests` | a **leaf** is declared by more than `MaxTestsPerLeaf` (12) tests | the `scan-tests` manifest |
| `TooManySnaplinks` | **any node** carries more than `MaxSnaplinksPerNode` (12) snaplinks, its own plus its concerns' | the tree itself |

When a leaf accumulates far more tests than one-unit-test-per-behaviour implies, the tests have enumerated
behaviours the tree never named: the node's status then means "some of these work" and nothing can tell you
which, so `tests=done` is a claim about a dozen things at once. A node carrying a pile of snaplinks is the
same statement made in code references instead. Both thresholds are 12, deliberately — the honest ceiling
depends on how much code a feature involves and how user-facing it is, and no constant knows that, so one
catch-all number the reader can hold beats two tuned ones.

Two scoping rules worth knowing:

- **`LeafCoveredByTooManyTests` skips containers.** A panel accumulating its children's tests is the tree
  working. Snaplinks do not aggregate that way — a parent never inherits its children's — so
  `TooManySnaplinks` applies to every node.
- **Both run over the whole tree, not just `Features`.** The other rules are feature-shaped (a backbone, a
  panel's theming) and mean nothing outside it. "This node is about too much" means the same anywhere, and *Common / Shared* nodes are as prone to it as features.

`LeafCoveredByTooManyTests` needs the manifest, which is derived and gitignored — absent, it simply doesn't
run and every other rule still does.

```powershell
& $nfi scan-tests .          # refresh the manifest first — the tests half is silent without it
& $nfi lint --under common   # any node, not just a feature root
```

A node that trips both is the clearest signal: a node doing several jobs shows it in its links and in its tests at once. Fixing one means
`add-node` for the behaviours the test names already describe, then re-pointing the snaplinks — not
deleting tests.

---

## Snaplinks

A snaplink is one typed, *loose* binding from a node (or one of its concerns) to what backs it: `code` (a file,
class, method or `ast` path), `markdown` (a file and heading path), `node` (another node) or `url`. It records
*intended* alignment; misalignment is signal, which is what the checks below surface.

### Snaplink discipline

- A leaf's `tests` concern, once `done`, **snaplinks to the test that covers it** — point it at the **unit
  test**, not the journey. The `ui` node's `tests` snaplink → the **journey**. Panels/state nodes have no
  `tests` concern, so no snaplink.
- Keep the tree snaplink and the test's `[CoversNode]` **in agreement** (same `Class.Method`). They're two
  channels for the same fact; drift between them is a smell.
- **Address a snaplink by what it is, not where it sits.** `remove-snaplink <id> [--concern <tag>]` takes
  `--type/--doc/--class/--method/--target`, and every field you give has to agree: `--doc` alone drops every
  link into that file, adding `--method` narrows it to the one (paths compare slash- and case-insensitively,
  so a backslashed path pasted from Explorer still matches). `--index <n>` still removes by position, but a
  position is only valid until the next edit reorders the list — which is exactly what an earlier line in the
  same batch does — so the two are mutually exclusive and giving both is an error. **Naming nothing removes
  nothing**: clearing the list is `--all`, said outright, so the call that wipes a node's links never looks like the call that removes one.
- **Repair a link, don't rebuild it.** `set-snaplink <id> --index <n>` edits one link in place — a moved file
  (`--doc`), a renamed class or method, a heading that shifted (`--title-path`), a field a target no longer
  has (`--clear class`). Remove-and-re-add loses the link's status and its position for the sake of the one
  thing about it that changed. Pin the edit with `--expect <text>` (required inside a batch) so an index that
  now means a different link is refused rather than silently rewritten.

### The tree is forward-looking

The tree is the plan of what *should* be in place for the **next release**, not a snapshot of what shipped (that's
the label-aligned [product](product) export). So **update it as you build** — flip concerns, add snaplinks, fix
descriptions — *right then, not after merge*. Because the snaplink check is setup-build-only (below), pointing a
`done` snaplink at a not-yet-merged file never blocks a regular build; it's the intended forward-looking state.

### A snaplink changed on a branch stays with the branch

Nodes and snaplinks are different kinds of claim: a node is a plan, and the tree is deliberately forward-looking
about those, so `add-node` / `set-status` / `set-concern` write to the shared tree at once. A snaplink says *this
file exists and contains this*, which from an unmerged branch is true nowhere else — so `add-snaplink` /
`set-snaplink` / `remove-snaplink` record into `docs/product/pending/<branch>.json` instead, and the shared tree is
left alone. Every read overlays your branch's set, so `describe`/`validate`/`tree` show your links normally; only
the write is deferred.

```powershell
& $nfi pending                  # what this branch has changed and not merged — review before committing
& $nfi promote [--dry-run]      # fold arrived sets into the shared tree, delete them, and commit that
```

**Commit that file with your change.** It is under the committed export dir on purpose: it rides along with the PR,
so at merge the change set arrives in the main checkout together with the code it describes — no knowing which
worktree, on whose machine, produced it. Its presence there *is* the merged signal. A branch that is abandoned never
merges, so its set never arrives and there is nothing to clean up.

### Promote is run deliberately, from the main checkout

`promote` is the only verb that writes to git. Everything else only reads — including `validate`, which the
installer's release gate, the Product page and every agent run: it *reports* arrived sets
(`note: N merged link set(s) … Fold them in with: nfi promote`) and writes nothing, so running it never moves the
branch its caller stands on. `promote` from a linked worktree is refused: a pending set there is that branch's own
unmerged work, not something that has merged.

### Validate answers about the branch you are on

A snaplink to a file that does not exist is an error. From a linked worktree `validate` resolves each snaplink
against *that* tree — not the main checkout — because "does this file exist somewhere" is not the question a branch
needs answered, and resolving through main would let a file you moved away keep resolving, so a branch would read
clean while its links are stale. **Every** missing file sets the exit code, including one that is in neither this
tree nor main: a snaplink written on a branch is deferred into `docs/product/pending/<branch>.json` and overlaid only
for that branch, so the shared tree gains a link only once the file it names has merged, and a link naming a file
that exists nowhere is always wrong. The split is still *printed* — files absent everywhere, then files main has and
you do not — because it separates "I moved this" from "this was never here". `validate --main` gives the main
checkout's view, which is what the installer's release gate runs.

### After a rename or move: remap

When a rename/move breaks snaplinks, don't hand-edit `tree.json` — `remap` rewrites them under validation:

```powershell
& $nfi remap <old-path> <new-path> [--class <n>] [--method <n>]
& $nfi remap --from-git <base>..HEAD --dry-run
```

**Every git-reading verb runs git where *you* stand, not where the tree lives.** `remap --from-git` resolves its
repository from the caller's working tree. From a linked worktree the product root is the main checkout, whose `HEAD` has not seen your commits, so a range
resolved there would find nothing to rewrite. After a rename or move, run `remap --from-git <base>..HEAD --dry-run` —
don't assume nothing needs remapping.

`remap` is a batch instruction too, so a move that shifts several files lands as one validated transaction and
`--dry-run` reports how many snaplinks each line would rewrite before anything is written. Inside a batch a remap
that matches nothing is an *error* (the path came from a move you already made, so a miss means the script is
wrong); standalone it is just reported, because there "nothing references that path" is a fair answer to a question.

### Snaplink paths are repo paths, never worktree paths

A snaplink `doc` is the repo's own path. `.claude/worktrees/<name>/src/Foo.cs` resolves only while that branch is
checked out and dies the moment the worktree is removed, so `validate` reports it as a gating `WorktreePath` issue
even though the file exists today, and **`doctor --fix` re-roots every one** back onto `src/Foo.cs`. `scan-tests`
normalises the same way: a test DLL built inside a worktree carries that checkout's absolute paths in its PDB, and
the manifest records the repo path — otherwise the Integrity page's *Add link* suggestions would seed the tree with
links that break at merge.

### How snaplinks are checked

Every snaplink (on a node *and* on each concern link) must still point at a real target — the file exists, the
markdown heading path resolves, the class/method is still declared, the URL is well formed. Run it from the Product
tab (⋮ → **Validate snaplinks**, or the root's integrity tile) — both open the **Integrity page**
(`ProductIntegrity`), which rescans on the shell's background queue (a full scan tree-sitter-parses every referenced
file and takes seconds, so it never runs on the dispatcher) and lets you re-point or remove each broken link. Or run
it headlessly:

```powershell
& $nfi validate   # exit 1 = broken links
```

The **installer build runs the same check and fails on any broken link** (`nexaflowSetup.wixproj` →
`ValidateSnaplinks`), so `NexaflowSetup.slnx` is the release gate; a plain `dotnet build Nexaflow.slnx` never runs it.
Results persist to the gitignored `.product/integrity.json` (derived — safe to delete). A file whose extension has no
tree-sitter grammar (`.txt`) is treated as **unverifiable, not broken** — never make the validator guess. **`.xaml` is
verifiable** (the `xml` grammar is built from `external/tree-sitter-xml`): a link may name an `x:Class`, an `x:Name`,
an `x:Key`, an `AutomationProperties.AutomationId` or an event handler, and a rename breaks it loudly instead of
rotting.

A second, **non-gating** channel sits beside the issues: a link whose file and class are sound but whose finer `ast`
target no longer resolves is an **advisory**, printed with the `nfi set-snaplink` command that fixes it. An `ast` field holds prose as often as a path, so failing a release build on that would punish
links whose real target is fine. Advisories never affect the exit code. The coverage manifest feeds a third channel —
*declared-but-unlinked* tests, each with an **Add link** button — described in
[testing.md → Coverage declaration](testing.md#coverage-declaration-coversnode--nocoverage).

---

## The CLI

> **The in-app assistant has this same surface** — both families. Every verb below that reads or changes the
> tree is a `product_*` client tool, and every `graph` verb is a `graph_*` one, on all three Product views,
> running the same `Services.Initiatives` call and rendering through the same reporter (`ProductReport` /
> `GraphReport`) so the CLI and the assistant print identical text. Reads auto-run; the structural tree ops
> (`add-node` / `move` / `rename` / `remove` / `remap` / `doctor --fix`) and `graph build` are
> approval-gated. `batch` is deliberately CLI-only — the model already has the individual operations.
>
> So the loop this document describes — `find` the node, `query` what it owes, edit, then `validate` and
> `lint` to check the edit — is one a model can run unaided — `lint` tells it when a `tests=done` it just set is an unbacked claim.

`nfi` with no arguments lists every verb with its options.

- **Discover:** `find`, `describe`, `describe <id> --code`, `tree <id> [--full]` (the whole subtree as one outline —
  the view to start *and* finish a feature pass with), `query` (filter by subtree/concern/status/leafness), `diff`,
  and `graph …` for the code.
- **Edit (one node):** `add-node`, `move <id> <new-parent>`, `rename <old-id> <new-id>`,
  `remove <id> [--recursive]`, `set-status`, `set-concern`, `remove-concern`, `add-snaplink`,
  `set-snaplink`, `remove-snaplink`, `set-node`. **A note belongs to a node, not a concern**:
  `set-node <id> --note "…"`, not `set-concern <id> <tag> <status> --note "…"` (which is rejected).
- **Bulk / integrity:** `batch <file>` (transactional; `--dry-run` first), `doctor [--fix]`, `validate`,
  **`lint [--under <id>]`** — checks a feature against the shape, concern and snaplink rules above and the test
  model in [testing.md](testing.md) (backbone present, `AI Ready` only on the feature root, panels/state nodes
  journey-covered, every leaf unit-tested, a `done` `tests` concern naming its test). Advisory: roles are inferred
  from position, so a finding is a prompt to look, not a verdict, and nothing here fails a build. Run
  `lint --under <feature>` at the start and end of a pass — the reference features all lint clean, so a finding
  means you've diverged from them. On a feature without the backbone it reports only `MissingBackbone` (it
  short-circuits); add the `UI` / `Functionality` nodes and re-run to see the real list.

**Workflow for a restructure:** generate a `.batch` file (one instruction per line — the standalone verbs
minus `<root>`; `#` comments; `"quote"` spaces), `batch … --dry-run`, apply, then `doctor` + `validate`.
Prefer generating the batch with a script over hand-writing dozens of lines. Grow the tree finer when a leaf needs
sub-nodes with `add-node <parent-id> "<title>"` (default concerns attach, and it re-validates).

> **Arguments are strict.** Every verb declares exactly what it accepts, so an unknown option, a missing
> option value, or a surplus positional is a hard error naming that verb's usage — never silently ignored.
> `batch` parses each line the same way and is all-or-nothing, so one typo aborts before anything is written.

---

## What enforces the model

### Already enforced
- **`SnaplinkValidator`** (Integrity page + setup-build gate): every snaplink resolves (file/heading/class/
  method exists); a concern flagged `requires_snaplink` that's `done`/`faulted` with no snaplink is gating. No
  concern sets the flag yet — see (a).
- **NXCOV analyzer** (NXCOV001 missing / NXCOV002 stale id / NXCOV003 class-level over-claim) +
  **`CoverageDeclarationGuardTests`**: `[CoversNode]` declarations are present, valid, and don't over-claim.
- **`NXUI001`** + **`AutomationIdJourneyCoverageTests`**: every button carries an AutomationId, and every id is
  named by a journey (a ratchet) — see [testing.md → Automation ids](testing.md#automation-ids-nxui001--automationidjourneycoveragetests).
- **`AiSurfaceRulesTests`** (`KnownNullScope`): a tool-bearing page must return a distinct
  `GetSecurityContext()`.
- **`FeatureTouchPointTests`** / architecture rules: add-a-feature wiring, reference/dispatcher rules.
- **`StructureLinter`** (`nfi lint`, advisory): the shape, concern and snaplink conventions, plus the two
  granularity rules (`LeafCoveredByTooManyTests`, `TooManySnaplinks`).

### The gap
The **role-based rules are conventions, not checks.** Nothing gating stops a future feature from giving a
panel a `tests` concern, sticking `AI Ready` on a leaf, marking a leaf `done` with no test, or letting the snaplink
and the `[CoversNode]` drift apart. Proposals to close it, cheapest first:

**(a) Turn on `requires_snaplink` for `tests`.** One flag in `product.json`. Instantly makes "a `done`/`faulted`
`tests` concern must name its test" a gating rule (it's off today). Zero new code — but **not free**:
`query --concern tests --status done --unbacked` returns a large set, so this needs a burn-down (baseline the current set, gate only new violations) rather than a flip. `lint` already
reports it per-feature as `TestsDoneWithoutSnaplink`, which is the cheap way to hold new work to the rule meanwhile.

**(b) Add an explicit node `kind` — the enabling change for everything else.** Add `kind` to `ProductNode`
(`feature | panel | control | state | behaviour | ai-context | ai-act | container`), set via
`add-node --kind …`. Roles are currently *inferred* from position ("child of a UI node = panel", "no children
= leaf"), which is brittle. An explicit `kind` makes the rules below robust and self-documenting. (Store it as
a field, or — lower-friction — as a reserved concern/tag.)

**(c) A `StructureValidator` in the release-gate path** (alongside `SnaplinkValidator`; degrade to
presence-only when the tree is absent in CI). Gating rules, keyed on `kind`:
- `AI Ready` only on a `feature` node.
- a `panel` has `theming`, has no `tests`; a `state` node has no concerns; a `control`/`behaviour`/`ai-act`
  leaf has a `tests` concern.
- every feature has the backbone (a UI + Functionality + AI Integration; AI Integration has
  context/act/preview) — a *template conformance* check per feature root.
- a leaf under UI/Functionality is a real leaf (no empty container pretending to be one).

**(d) Snaplink ↔ `[CoversNode]` cross-check, made gating.** "Declared-but-unlinked" is a non-gating
advisory. Add the reverse and gate it: a leaf whose `tests` snaplink names `Class.Method` where that method
carries **no** matching `[CoversNode]` for the leaf is drift. This keeps the two channels locked together (and
`scan-tests` already reflects the manifest needed to check it).

**(e) A Roslyn analyzer for the test side (author-time):**
- a test method that launches the app (derives from `UiJourneyTestBase`) should `[CoversNode]` a `ui`-kind
  node — warn otherwise (enforces "one journey, at the UI node").
- a `[CoversNode]` on a `panel`-kind node from a *unit* (non-UI) test is suspicious — panels are journey-
  covered — warn.

### Cross-checks against the code (the higher-value drift catchers)
(a)–(e) keep the tree *self*-consistent. These instead assert the tree/tests still match the **real code**
— which is where drift actually happens (a control or tool added and the tree/tests never updated).

**Build guards like these on the graph, not on a second scanner:** every `AutomationProperties.AutomationId` in the repo is
already a node — `code:<view>.xaml#A:<id>` — carrying its file and line, and the graph sees the code-behind and its
`handles`/`references` edges back to the element.

**(f) AI-tool ↔ act-node cross-check.** Per feature, the set of names from `GetClientTools()` must equal the
titles of the `<feature>-ai-act-*` leaves — catching a tool added / renamed / removed in code with no matching
tree leaf, and stale leaves. The robust form has to *call* `GetClientTools()` (an instance), so:
- **(i) a shared assert helper each feature's AI test calls** with its already-constructed VM and the act-node
  id — tree-backed, degrades when the tree is absent (CI). Cheapest, because the AI tests already build the VM
  and several already pin the tool set with an "update the tree to match" comment; this just *enforces* it.
- **(ii) a central guard** that constructs each tool-bearing page VM generically (interface deps mocked, a temp
  file for path params) and cross-checks — no per-test wiring, but fragile for VMs with awkward ctors.
- (A pure source-scan for tool-name string literals avoids construction but is heuristic — tool names also live
  in separate `IClientTool` classes, e.g. the git/font tools.)
Recommend **(i)**.

**Recommended order:** (a) now, as a burn-down, then (b) `kind`, then (c) the structure rules and (d) the cross-check
on top of it; (f) as the code-drift catcher; (e) last, if the author-time nudges prove worth an analyzer.

---

## What the graph can and cannot answer

For each question a reader might bring to an unfamiliar repository: which command answers it, and how honestly.
Where the answer is "it doesn't help", that is stated rather than dressed up.

| | meaning |
|---|---|
| ✅ **Direct** | one command answers it |
| 🟡 **Derived** | answerable, but you assemble it — two or more commands, or a judgement call on the output |
| ❌ **No** | the graph has no notion of this; the entry says what would be needed |

### Is the whole solution actually in the graph?

`nfi graph stats` gives the node, edge and hyperedge counts over every file.

Every file in the repo is *at least* a node, so nothing is invisible. The question is how much of each is
*understood* — parsed into types, members and edges — versus merely located.

**Don't re-derive this by hand — `nfi graph list --unparsed` answers it.** It lists every file nothing read:
no grammar, no structured extractor, so no types, members or edges.

In `src/` it is a handful of non-code files (fixtures, a font licence, icons, a `.config`); at the repo root, images,
docs, `.ps1` scripts, the CI `.yml` and git dotfiles; under `external/`, third-party bindings, CI, queries and docs in
other languages.

The WiX installer and the MSBuild logic every project inherits are read: `.wxs`/`.wxl`/`.props`/`.targets`/
`.manifest` with the `xml` grammar, and `.wixproj` through the csproj path like any project file. `.ps1` and `.yml`
have no grammar in the binding, so they are located but not parsed.

#### Generated files: the test is the file, not the language

`external/` holds the tree-sitter grammars' `.c`/`.h` files. A generated `parser.c` is megabytes of tables nobody
queries, while the hand-written `scanner.c` beside it is real source someone debugging a grammar would ask about —
so excluding the language would lose the second to avoid the first. Instead `RepoFiles.IsGenerated` reads the head
of a file for the marker every generator leaves (`@generated`, `<auto-generated`, `DO NOT EDIT`). A generated file
is **located, sized, labelled with its language and left unexpanded** — it gets `generated: true` and contributes no
types, members or edges — and its hand-written neighbour is parsed in full. A file too large to read says which it
is, via `unread: generated` or `unread: too-large-or-binary`, rather than being silently emptier than its neighbours.

#### Two caveats that colour every answer below

1. **Edges are name-resolved, not type-resolved.** A call edge is a name matched against the symbol index and
   scored — `~0.95` in scope, `~0.85` unique globally, lower when ambiguous. So "what calls this" is a strong
   lead, not a compiler's answer: a common method name can over-match, and overloads are approximate. (An edit's
   `impact:` lines are the exception — they come from the compiler's binding.) The resolver declines what the
   language itself would decline — a nested type is not addressable by its simple name from another file, only a
   method can be called, a private member is invisible outside its file, and neither a call nor a mention crosses
   languages — and it rejects *after* resolving rather than narrowing the candidate set first, because narrowing can
   leave one survivor where there were two and turn an ambiguous name into a confident wrong edge. Every type a member
   names gets a `references` edge; an unresolved mention is dropped rather than stubbed, so a mention is only ever
   evidence about a type this repo declares.
2. **Nothing resolved at runtime is visible.** DI wiring, string-keyed lookups and event dispatch leave no
   edge. Where the repo uses an *interface* for extensibility this doesn't bite (see plugins, below); where it
   uses a string, it does.

### Orientation — what is this repo?

| Question | How | |
|---|---|---|
| What is this repository actually for? | `nfi tree <root-id>` / `.product/product.json` — the product tree is a written description, not an inference | ✅ |
| What are its major subsystems/modules? | `nfi tree features`, `nfi find <term>`; project level: `graph list --type file` filtered to `.csproj` | ✅ |
| What are the important libraries/packages within it? | `graph list --type external`; per-project via `depends_on` | ✅ |
| Which parts are app code vs tests/tooling/generated? | Product tree + path convention (`src/Nexaflow.Tests/**`), or `sdk` on the project node: a test project declares `MSTest.Sdk`, and that is often the ONLY statement it makes (a test project may carry no package reference at all, so a `depends_on MSTest` rule is not enough). **Generated** code is the interesting one: `annotated` hyperedges name `[ObservableProperty]`/`[RelayCommand]`, so the generated public surface is identifiable from its declaration | 🟡 |
| What are the major dependency chains? | `graph node file:<x>.csproj` → `depends_on`. Project-level layering is exact — it comes from `ProjectReference`, not inference | ✅ |
| Which components are central to the repository? | `nfi graph rank [--by fanin\|fanout] [--type t] [--under path]` (AI: `graph_rank`) — most-depended-on first. Containment is never counted: it would rank a type by how many members it declares, which is size, not importance | ✅ |
| What are the major architectural boundaries? | `depends_on` gives the real project graph. But the *rules* (features never reference Core, etc.) are enforced by `Nexaflow.Tests.Features.Architecture`, not the graph | 🟡 |
| What code is dead, orphaned, or disconnected? | `nfi graph orphans` (AI: `graph_orphans`) — declarations with no incoming reference, scoped to `src/`, with the innocent explanations (a test run by reflection, an interface member, a name two declarations share, a type the shell finds by scanning assemblies, a theme key another dictionary also defines, a language the extractor does not read) filtered out and shown by `--all` | 🟡 — **a lead, not a verdict**, but a short, hand-checkable list |
| What are the primary entry points? | `graph node file:<x>.csproj` carries `output_type` (`winexe`/`exe`/`library`) and `target_framework`, so the executables are a property rather than a `Main` text search | ✅ for *which projects are executables*; 🟡 for what each one reaches — see *Executable surfaces* |
| What is the public surface of X / which members are private? | `graph node <id>` → `visibility` (`public`/`internal`/`protected`/`private`) on every type and member. C# only; other grammars have no meaningful type-level modifier and report the record's public default | ✅ |
| How big is this file / what are the biggest files? | `graph node file:<path>` → `lines` | ✅ |
| What are the executable surfaces, and what participates in each? | — | 🟡/❌ — see below |

### Change impact — if I touch this, what breaks?

This is the graph's strongest area.

| Question | How | |
|---|---|---|
| What calls this? / What does this call? | `graph node <id>` — shows **both directions** plus hyperedges, in one call | ✅ |
| If I change this class/function, what could be affected? | `graph node <id>` for direct callers, `graph walk <id> --hops 2` for the neighbourhood; for a declaration you edit, the edit's own `impact:` lines | ✅ |
| Which external dependencies are involved? | `graph node external:<Name>` → every consuming project. *This is the NAudio case in CLAUDE.md* | ✅ |
| Which tests exercise this code? | `nfi test <id> --list`; or `graph node product:<slug>` → `tests` edges | 🟡 — the `tests` edges are **declared** (`[CoversNode]` + tree snaplinks), not derived from call paths; `nfi test` adds the compiler-bound tests of what uses it |
| What is the smallest set of files I need to understand first? | `graph context <id>` — node, its source, neighbours, owning feature, **and the list of files that feature owns** | ✅ |
| What are the blast-radius boundaries? | `graph walk --hops 2` for code, `depends_on` for the project boundary | 🟡 |
| What parts are safe to change independently? | Inverse of the above; communities hint at clusters | 🟡 |
| I want to change X — what should I be careful not to break? | `graph node` incoming edges + `tests` edges | 🟡 |
| What are all the paths from this entry point to this component? | `nfi graph paths <from> <to> [--undirected] [--hops N]` (AI: `graph_paths`) — shortest routes, each hop naming its relationship. Only shortest ones: enumerating every route between two nodes here is exponential and unreadable | ✅ |
| Which executable surfaces depend on this component? | — | ❌ — no entry-point node kind (`graph-entry-points`) |
| Which configuration affects this component? | `graph grep "IFeatureConfig" --from product:<slug> --scope owned --mode content` | 🟡 — works because this repo names config by convention |

### Flow — how does work move through the system?

The graph is **structural**, not a control-flow or dataflow model. These questions are answered by pointing
you at the right code fast, not by tracing execution.

| Question | How | |
|---|---|---|
| Where is this state created and modified? | `graph node <field-id>` → members that reference it | 🟡 |
| Where does this operation perform I/O? | `graph grep "File\.(Read\|Write)AllText\|Stream" --from product:<slug> --scope owned --mode content` | 🟡 — a text search, correctly scoped |
| Where are errors generated and handled? | `graph grep "catch\|throw new" --scope owned` | 🟡 — same |
| Where does authentication/authorization happen? | No auth in a desktop shell; the analogue is privilege escalation: `graph grep "RunElevatedAsync" --mode content` | 🟡 |
| Where is configuration loaded? | `graph search ConfigManager`, then `graph node` for its callers | 🟡 |
| Where are dependencies instantiated? | `instantiates` edges — `graph node <type-id>` shows who constructs it | ✅ |
| Where does persistence occur? | `graph search ProductStore` / `graph grep` over IO types | 🟡 |
| Which components communicate with external systems? | `graph list --type external` and `depends_on`; for outbound calls, grep | 🟡 |
| How does a request/message/command enter the system? | — | ❌ for a general answer; this repo has no request pipeline. The nearest real answer is the tab/page factory (`IPageRegistration`) |
| What happens after this entry point is invoked? | `graph walk <id> --hops 2` | 🟡 — a neighbourhood, not a sequence |
| Where is this data transformed? | — | ❌ no dataflow model |
| What are the important call chains for this feature? | `calls` edges + `walk` / `paths`; assembled by hand | 🟡 |

### Locating things

Consistently strong — this is what the graph is for.

| Question | How | |
|---|---|---|
| Where is the definition of X? | `ask 'search X \| source'` | ✅ |
| Where is the behaviour for X implemented? | `ask 'grep <pattern> \| source'` — reports file:line **plus owning type/member/feature** | ✅ |
| Where is X consumed? | `ask 'node <id> \| callers \| source'` (incoming edges — `calls`, `instantiates`, and `references` for every type a member merely names) | ✅ |
| Where is X tested? | `graph node product:<slug>` → `tests` | ✅ |
| Where is the user-facing behaviour for X? | `graph node product:<slug>` → its UI subtree; for a view, `view_of` / `handles` / `binds_to` reach the XAML element | ✅ |
| Where is X configured? | grep for the config type | 🟡 |
| Where are the defaults for X established? | grep | 🟡 |
| Where can X be overridden? | `implements` / `extends` edges | 🟡 |
| What other things modify X? | `references` edges into it — complete for *naming* X, though naming is not the same as modifying | 🟡 |
| Trace the execution path from X to Y. | `graph paths <X> <Y>` | 🟡 — the shortest structural routes, not an execution trace |

### Justifying a component

| Question | How | |
|---|---|---|
| Why does this component exist? | `nfi describe <node>` — the product tree carries an `about` written by a human | ✅ |
| What depends on it? / What would stop working if removed? | `graph node <id>` incoming edges | ✅ |
| Is this abstraction actually used? | `graph node <interface-id>` → `implements` count | ✅ |
| Is this code generated? | `annotated` hyperedges name the attribute — `[ObservableProperty]`, `[RelayCommand]` | ✅ |
| Is this apparently-unused code reachable by reflection? | Partly. Reflection-discovered extensions here go through interfaces, so `graph node code:...#T:IPageRegistration` → `implements` lists every discoverable tab. String-keyed dynamic loading would be invisible | 🟡 |
| Is this module an architectural boundary or merely organisational? | `depends_on` shows whether anything actually crosses it | 🟡 |
| Is this dependency runtime or dev-only? | — | ❌ `depends_on` does not record `PrivateAssets`/`IsImplicitlyDefined`, so a NuGet analyzer looks like a runtime dependency |
| Is this code framework-required? | — | ❌ no notion |

### Dependencies

| Question | How | |
|---|---|---|
| What does this repository depend on? | `graph list --type external` | ✅ |
| Which internal modules depend on external library X? | `graph node external:X` → `depends_on` | ✅ |
| Which dependencies are declared but apparently unused? | Compare a project's `depends_on` against `references`/`instantiates` reaching that library's types | 🟡 — doable, no single command |
| What would happen if dependency X disappeared? | `graph node external:X` names every consumer | ✅ |
| Which components are tightly coupled? | Communities cluster by connectivity | 🟡 |
| Which have unusually high fan-in/fan-out? | `graph rank --by fanin` / `--by fanout` | ✅ |
| What are the dependency cycles? | — | ❌ **No cycle detection** (`graph-cycles`). The data supports it; the verb doesn't exist |
| Are architectural layers being violated? | — | ❌ from the graph. `ArchitectureRulesTests` enforces this instead, and is the right place for it |
| Which dependencies are shared across unrelated subsystems? | `graph node external:X` + communities | 🟡 |
| What external dependencies are on critical execution paths? | — | ❌ requires an entry-point kind to anchor the paths |

### Executable surfaces

The weakest area: **the graph understands the application better than the building and shipping of it.**

| Question | How | |
|---|---|---|
| What library entry points exist? | `graph list --type type --file <project-dir>` lists a project's types, nested and private included; read `visibility` on each to isolate the public ones | 🟡 |
| What plugins/extensions can be loaded? | `graph node <IPageRegistration>` → `implements`. Works because extensibility is an interface here | ✅ |
| What test suites are executable? | Test projects are `.csproj` nodes; the product tree records which suite owns what | 🟡 |
| What CLI commands exist? | `graph search VerbSpec` finds the table, then read it | 🟡 — repo-specific knowledge, not structural |
| What are the entry points? | `ask 'grep "static.*void Main" \| source'` | 🟡 — text search; **there is no `entrypoints` verb and no entry-point node kind** |
| What scheduled/background jobs exist? | `graph search BackgroundActivity` — naming convention, not structure | 🟡 |
| What build/deployment entry points exist? | The installer (`.wxs`/`.wixproj`) and MSBuild logic read as XML; build scripts (`.ps1`) and CI (`.yml`) are unparsed | 🟡 |
| What HTTP endpoints exist? | — | ❌ none in a WPF app; also no extractor if there were |
| What message/event consumers exist? | — | ❌ C# `event`/`+=` wiring produces no edge (`graph-event-edges`) |
| What public APIs does it expose? | `visibility` is on every type and member node, but there is no verb that assembles "the public surface of assembly X" | 🟡 |
| For each entry point, what code does it reach? | — | ❌ needs an entry-point kind to start `paths` from |

### Summary

**Answered well:** anything phrased as *"what is connected to this?"* — callers, callees, implementers,
constructors, consumers, dependencies, owning feature, covering tests, and which method handles a
button, which code touches an element, which ViewModel member a binding names.

**Answered by pointing:** *"where does X happen?"* — scoped `grep` is fast and reports the owning
member and feature of every hit, which a text search cannot.

**Not answered yet** (in rough value order). Each is a `should` node under `initiatives-graph`, so it appears in
`nfi query --status should` beside every other gap; the grammar gap sits under the Syntax Engine as `parser-ps1-yml`,
because a missing grammar is the parser's gap and not the graph's.

1. **Entry points as a first-class kind** (`graph-entry-points`) — `Main`, `IPageRegistration`, test entry,
   installer. `output_type` on a project node names the executables, but not what each one reaches; an entry-point
   kind would anchor `graph paths`.
2. **Cycle detection** (`graph-cycles`) — the cheapest: the data already supports it, only the verb is missing.
3. **Visibility roll-up** (`graph-visibility`) — `visibility` is on type and member nodes, but no verb filters or
   rolls it up, so *the public surface of assembly X* is assembled by hand.
4. **Event wiring** (`graph-event-edges`) — a C# `event` and its `+=` subscriptions produce no edge, so *what
   consumes this event* is invisible.
5. **`.ps1` and `.yml`** (`parser-ps1-yml`) — a missing grammar rather than a missing extractor.

**Not answered, and correctly so:** dataflow, control flow, runtime DI resolution. Those need a different
kind of model, and inventing them from name-resolved edges would produce confident wrong answers — the
failure mode the confidence ladder exists to avoid.

---

## Where it is heading: the Product Manager cycle

A one-page map of the design the Product Manager is built toward. Not a build spec — a reminder of the shape, to
glance at before writing a task so the right contracts make it into the prompt.

> **As built today.** The *vNext* tree is `.product/tree.json` (gitignored, edited through `nfi`, live-reloaded by the
> app). Sealed snapshots are the committed `docs/product/v<version>.json` exports with [PRODUCT.md](product/PRODUCT.md),
> and `nfi diff` compares against the last one. Snaplink types built so far are `code`, `markdown`, `node` and
> `url`. Transition Plan, Change and Review below are the design, not yet surfaces.

### The cycle

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

### Three principles (defend these — they look like flaws and aren't)

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

### Snapshots, vNext, releases

- **vNext** — the single mutable tree + planning state. The only writable thing. Not in git.
- **Snapshot** — a sealed point-in-time consistency boundary (a *private release*). Produced by every
  Review. Immutable. Committed to git as `.md` + `.json`. Previous snapshots are read-only.
- **Public release** — a snapshot *chosen* to ship, and tagged. Orthogonal to snapshotting: every
  Review snapshots; only some snapshots are blessed public. A `git` snaplink resolves a node's
  version by which snapshot-tag it last went `done` in.

### Entity model

**Hierarchy:** Product → **Workload** (1 process, or N) → **TreeOfFunctionality** → **Nodes** (nested).

**Node** — the *only* content primitive (no "items"; a parent and a leaf are the same thing).
Holds: title, children, statustags, snaplinks. The tree is **present-state only** — never work
history, never plans.

**Statustag** — a tag carrying exactly one present-tense status (`done` / `should` / `shouldnt` / `faulted`, as in
[Concerns, by role](#concerns-by-role)). No "not done", no history, no reason. Attaches to nodes, to cross-cutting
**concerns** (a concern is a statustag spanning many nodes), and to snaplinks. Derived, never stored:
**queue** = `should`−`done`; **bugs** = `faulted`; **deprecation** = `shouldnt` + live snaplink.

**Snaplink** — one typed *loose-binding* primitive (records intended alignment, doesn't break on
misalignment — misalignment is *signal*). Carries its own statustag + an **advisory** assessment
(separate field, never overrides status). Designed types: `markdown` (by title-path), `code` (by
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

### Completion → Review valve

When a work item completes, its node backlinks drive an **evaluation** at Review (not an auto-write):
for each referenced node — does it go `done`? does a cross-cutting concern's status change? **should
the node subdivide?** (the one sanctioned path by which work reshapes the tree — legitimate because
completed work *is* a change in reality). Build-gaps evaluate "is the `should` now satisfied"; fix-
gaps evaluate "is the `faulted` now cleared." Confirming the batch for a snapshot **is** the atomic
"this is the new As-Is" seal.

### Loops (build order = dependency order)

1. **Capture** (built first) — create node + statustag; nothing else works without a populated tree.
2. **Survey** — the sunburst (two layers, focus + children, **mandatory subtree rollup** so focus
   never hides a downstream fault) + derived lists. Read-only.
3. **Snaplink engine** — the dumb sweep; markdown/code/git; advisory writes only.
4. **Transition Plan + Change** — gap list, shaping canvas, work-packages.
5. **Review** — the seal: evaluate completions, write tree, emit snapshot (md+json).
6. **Build** (Claude Code skill, built *last*) — resolves node via snaplink, sets statustag, commits
   with the code. Needs a populated tree + snaplink resolution to exist first.
