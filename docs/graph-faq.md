# What the knowledge graph can and cannot answer

A validation pass over `nfi graph`: for each question a reader might bring to an unfamiliar repository,
which command answers it, and how honestly.

Every command below was run against this repo while writing this. Where the answer is "it doesn't help",
that is stated rather than dressed up.

| | meaning |
|---|---|
| ✅ **Direct** | one command answers it |
| 🟡 **Derived** | answerable, but you assemble it — two or more commands, or a judgement call on the output |
| ❌ **No** | the graph has no notion of this; the entry says what would be needed |

---

## First: is the whole solution actually in the graph?

`nfi graph stats` — **56,303 nodes, 97,575 edges, 15,338 hyperedges** over 3,977 files.

Every file in the repo is *at least* a node, so nothing is invisible. The question is how much of each is
*understood* — parsed into types, members and edges — versus merely located.

| area | parsed (AST) | structured | markdown | binary | **not understood** | total |
|---|---|---|---|---|---|---|
| **`src/` (our code)** | 2,031 | 77 | 3 | 2 | **5** | 2,118 |
| repo root / tooling | 6 | 2 | 19 | 23 | **25** | 75 |
| `external/` (submodules) | 1,557 | 73 | 16 | 26 | **112** | 1,784 |

**Our own source is effectively fully understood.** The five exceptions are not code: two `app.manifest`
files, two `.ndjson` test fixtures, and a font licence.

The real gap is **the repo root — the build and packaging of the app, as opposed to the app**:

| not understood | what it is | why |
|---|---|---|
| `.wxs` `.wxl` `.wixproj` (7) | the WiX installer — a genuine deployment entry point | XML; no extractor registered |
| `.ps1` (5) | `tools/*.ps1`, `ensure-submodules.ps1`, `build-tree-sitter-xml.ps1` | no PowerShell grammar in `TreeSitter.DotNet` |
| `.props` `.targets` (2) | `Directory.Build.*` — build logic every project inherits | XML; deliberately not registered |
| `.yml` (1) | the CI workflow | no YAML grammar in the package |
| extensionless (5) | git hooks | shell; no grammar |

Two of those are now cheap to close: `.wxs`/`.wixproj`/`.props`/`.targets` are XML, and the repo gained an
`xml` grammar. `.ps1` and `.yml` need grammars the binding does not ship.

`external/`'s 112 are other languages inside submodules (F#, C headers, Go, Swift) — third-party, and not
what anyone queries. Worth noting though: **`.c` and `.h` have no mapping even though `tree-sitter-c` ships
in the package** — `TreeSitterLanguages` registers `cpp` for `.cpp/.hpp/...` and never registers plain C.

### Two caveats that colour every answer below

1. **Edges are name-resolved, not type-resolved.** A call edge is a name matched against the symbol index and
   scored — `~0.95` in scope, `~0.85` unique globally, lower when ambiguous. So "what calls this" is a strong
   lead, not a compiler's answer: a common method name can over-match, and overloads are approximate.
2. **Nothing resolved at runtime is visible.** DI wiring, string-keyed lookups and event dispatch leave no
   edge. Where the repo uses an *interface* for extensibility this doesn't bite (see plugins, below); where it
   uses a string, it does.

---

## Orientation — what is this repo?

| Question | How | |
|---|---|---|
| What is this repository actually for? | `nfi tree <root-id>` / `.product/product.json` — the product tree is a written description, not an inference | ✅ |
| What are its major subsystems/modules? | `nfi tree features`, `nfi find <term>`; project level: `graph list --type file` filtered to `.csproj` | ✅ |
| What are the important libraries/packages within it? | `graph list --type external` (915 of them); per-project via `depends_on` | ✅ |
| Which parts are app code vs tests/tooling/generated? | Product tree + path convention (`src/Nexaflow.Tests/**`). **Generated** code is the interesting one: `annotated` hyperedges name `[ObservableProperty]`/`[RelayCommand]`, so the generated public surface is identifiable from its declaration | 🟡 |
| What are the major dependency chains? | `graph node file:<x>.csproj` → `depends_on`. Project-level layering is exact — it comes from `ProjectReference`, not inference | ✅ |
| Which components are central to the repository? | `graph stats` names the largest communities; `graph node` shows a node's edge counts | 🟡 — there is no fan-in ranking, so "central" is eyeballed |
| What are the major architectural boundaries? | `depends_on` gives the real project graph. But the *rules* (features never reference Core, etc.) are enforced by `Nexaflow.Tests.Features.Architecture`, not the graph | 🟡 |
| What code is dead, orphaned, or disconnected? | — | ❌ **No orphan query.** Nothing lists nodes with no incoming edges. This is the single most valuable missing verb, and it is straightforward: the data is already there |
| What are the primary entry points? | `graph grep "static.*void Main" --mode content` finds them, but by text | 🟡 — see *Executable surfaces* |
| What are the executable surfaces, and what participates in each? | — | 🟡/❌ — see below |

## Change impact — if I touch this, what breaks?

This is the graph's strongest area.

| Question | How | |
|---|---|---|
| What calls this? / What does this call? | `graph node <id>` — shows **both directions** plus hyperedges, in one call | ✅ |
| If I change this class/function, what could be affected? | `graph node <id>` for direct callers, `graph walk <id> --hops 2` for the neighbourhood | ✅ |
| Which external dependencies are involved? | `graph node external:<Name>` → every consuming project. *This is the NAudio case in CLAUDE.md* | ✅ |
| Which tests exercise this code? | `graph node product:<slug>` → `tests` edges | 🟡 — these are **declared** (`[CoversNode]` + tree snaplinks), not derived from call paths. It answers "which tests claim to cover this" |
| What is the smallest set of files I need to understand first? | `graph context <id>` — node, its source, neighbours, owning feature, **and the list of files that feature owns** | ✅ |
| What are the blast-radius boundaries? | `graph walk --hops 2` for code, `depends_on` for the project boundary | 🟡 |
| What parts are safe to change independently? | Inverse of the above; communities hint at clusters | 🟡 |
| I want to change X — what should I be careful not to break? | `graph node` incoming edges + `tests` edges | 🟡 |
| What are all the paths from this entry point to this component? | — | ❌ **No path query.** `walk` returns a BFS *ball* around one node, not routes between two. Answering this means walking and correlating by hand |
| Which executable surfaces depend on this component? | — | ❌ — follows from the two gaps above (no entry-point concept, no paths) |
| Which configuration affects this component? | `graph grep "IFeatureConfig" --from product:<slug> --scope owned --mode content` | 🟡 — works because this repo names config by convention |

## Flow — how does work move through the system?

The graph is **structural**, not a control-flow or dataflow model. These questions are answered by pointing
you at the right code fast, not by tracing execution.

| Question | How | |
|---|---|---|
| Where is this state created and modified? | `graph node <field-id>` → members that reference it | 🟡 |
| Where does this operation perform I/O? | `graph grep "File\.(Read|Write)AllText|Stream" --from product:<slug> --scope owned --mode content` | 🟡 — a text search, correctly scoped |
| Where are errors generated and handled? | `graph grep "catch|throw new" --scope owned` | 🟡 — same |
| Where does authentication/authorization happen? | No auth in a desktop shell; the analogue is privilege escalation: `graph grep "RunElevatedAsync" --mode content` | 🟡 |
| Where is configuration loaded? | `graph search ConfigManager`, then `graph node` for its callers | 🟡 |
| Where are dependencies instantiated? | `instantiates` edges (13,521 of them) — `graph node <type-id>` shows who constructs it | ✅ |
| Where does persistence occur? | `graph search ProductStore` / `graph grep` over IO types | 🟡 |
| Which components communicate with external systems? | `graph list --type external` and `depends_on`; for outbound calls, grep | 🟡 |
| How does a request/message/command enter the system? | — | ❌ for a general answer; this repo has no request pipeline. The nearest real answer is the tab/page factory (`IPageRegistration`) |
| What happens after this entry point is invoked? | `graph walk <id> --hops 2` | 🟡 — a neighbourhood, not a sequence |
| Where is this data transformed? | — | ❌ no dataflow model |
| What are the important call chains for this feature? | `calls` edges + `walk`; assembled by hand | 🟡 |

## Locating things

Consistently strong — this is what the graph is for.

| Question | How | |
|---|---|---|
| Where is the definition of X? | `graph search X` → `graph code <id>` for the block | ✅ |
| Where is the behaviour for X implemented? | `graph grep <pattern> --mode content` — reports file:line **plus owning type/member/feature** | ✅ |
| Where is X consumed? | `graph node <id>` (incoming edges) | ✅ |
| Where is X tested? | `graph node product:<slug>` → `tests` | ✅ |
| Where is the user-facing behaviour for X? | `graph node product:<slug>` → its UI subtree; for a view, `view_of` / `handles` / `binds_to` reach the XAML element | ✅ |
| Where is X configured? | grep for the config type | 🟡 |
| Where are the defaults for X established? | grep | 🟡 |
| Where can X be overridden? | `implements` / `extends` edges | 🟡 |
| What other things modify X? | `references` edges into it | 🟡 |
| Trace the execution path from X to Y. | — | ❌ no path query (as above) |

## Justifying a component

| Question | How | |
|---|---|---|
| Why does this component exist? | `nfi describe <node>` — the product tree carries an `about` written by a human | ✅ |
| What depends on it? / What would stop working if removed? | `graph node <id>` incoming edges | ✅ |
| Is this abstraction actually used? | `graph node <interface-id>` → `implements` count | ✅ |
| Is this code generated? | `annotated` hyperedges name the attribute — `[ObservableProperty]`, `[RelayCommand]` | ✅ |
| Is this apparently-unused code reachable by reflection? | Partly. Reflection-discovered extensions here go through interfaces, so `graph node code:...#T:IPageRegistration` → **`implements (41)`** lists every discoverable tab. String-keyed dynamic loading would be invisible | 🟡 |
| Is this module an architectural boundary or merely organisational? | `depends_on` shows whether anything actually crosses it | 🟡 |
| Is this dependency runtime or dev-only? | — | ❌ `depends_on` does not record `PrivateAssets`/`IsImplicitlyDefined`, so a NuGet analyzer looks like a runtime dependency |
| Is this code framework-required? | — | ❌ no notion |

## Dependencies

| Question | How | |
|---|---|---|
| What does this repository depend on? | `graph list --type external` | ✅ |
| Which internal modules depend on external library X? | `graph node external:X` → `depends_on` | ✅ |
| Which dependencies are declared but apparently unused? | Compare a project's `depends_on` against `references`/`instantiates` reaching that library's types | 🟡 — doable, no single command |
| What would happen if dependency X disappeared? | `graph node external:X` names every consumer | ✅ |
| Which components are tightly coupled? | Communities (578 of them) cluster by connectivity | 🟡 |
| Which have unusually high fan-in/fan-out? | — | ❌ no ranking. `graph node` prints counts one node at a time |
| What are the dependency cycles? | — | ❌ **No cycle detection.** The data supports it; the verb doesn't exist |
| Are architectural layers being violated? | — | ❌ from the graph. `ArchitectureRulesTests` enforces this instead, and is the right place for it |
| Which dependencies are shared across unrelated subsystems? | `graph node external:X` + communities | 🟡 |
| What external dependencies are on critical execution paths? | — | ❌ requires the missing path query |

## Executable surfaces

The weakest area, and it maps exactly onto the census gap above: **the graph understands the application, not
the building and shipping of it.**

| Question | How | |
|---|---|---|
| What library entry points exist? | `graph list --type type --file <project-dir>` lists a project's types — but **all** of them, nested and private included: type nodes carry `kind`/`line`/`ast` and no visibility, so "the public surface" cannot be isolated | 🟡 |
| What plugins/extensions can be loaded? | `graph node <IPageRegistration>` → `implements (41)`. Works because extensibility is an interface here | ✅ |
| What test suites are executable? | Test projects are `.csproj` nodes; the product tree records which suite owns what | 🟡 |
| What CLI commands exist? | `graph search VerbSpec` finds the table, then read it | 🟡 — repo-specific knowledge, not structural |
| What are the entry points? | `graph grep "static.*void Main" --mode content` | 🟡 — text search; **there is no `entrypoints` verb and no entry-point node kind** |
| What scheduled/background jobs exist? | `graph search BackgroundActivity` — naming convention, not structure | 🟡 |
| What build/deployment entry points exist? | — | ❌ **The installer and every build script are unparsed.** `.wxs`, `.wixproj`, `.ps1`, CI `.yml` |
| What HTTP endpoints exist? | — | ❌ none in a WPF app; also no extractor if there were |
| What message/event consumers exist? | — | ❌ C# `event`/`+=` wiring produces no edge |
| What public APIs does it expose? | — | ❌ the outline knows a *member's* visibility, but no visibility reaches the graph at all — so "the public surface of assembly X" is not expressible |
| For each entry point, what code does it reach? | — | ❌ needs both missing pieces: entry points and paths |

---

## Summary

**Answered well:** anything phrased as *"what is connected to this?"* — callers, callees, implementers,
constructors, consumers, dependencies, owning feature, covering tests, and (new) which method handles a
button, which code touches an element, which ViewModel member a binding names.

**Answered by pointing:** *"where does X happen?"* — scoped `graph grep` is fast and reports the owning
member and feature of every hit, which a text search cannot.

**Not answered, and worth fixing** (in rough value order):

1. **Orphans** — nodes with no incoming edges. Highest value, lowest effort; the data exists.
2. **Paths between two nodes** — blocks every "trace X to Y" and "what does this entry point reach" question.
3. **Fan-in/fan-out ranking** — turns "which components are central" from eyeballing into an answer.
4. **Entry points as a first-class kind** — `Main`, `IPageRegistration`, test entry, installer.
5. **Cycle detection.**
6. **Parse the build** — `.wxs`/`.wixproj`/`.props`/`.targets` are XML and the grammar now exists.

**Not answered, and correctly so:** dataflow, control flow, runtime DI resolution. Those need a different
kind of model, and inventing them from name-resolved edges would produce confident wrong answers — the
failure mode the confidence ladder exists to avoid.
