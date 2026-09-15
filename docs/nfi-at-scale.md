# nfi at scale — product evolution

A design, not yet built. What nfi does today is in [product-graph.md](product-graph.md); this is where it goes so
that the same questions can be answered on a codebase several hundred times the size of this one, kept in a
version control system that is not git, by someone who is not working on Nexaflow. The work it describes is
tracked in the product tree under `initiatives-scale`.

Windows is the only platform, and stays so: every mechanism below may use NTFS/ReFS and Win32 directly.

---

## 1. What has to stay true

The primary use: **ask a node, and learn what implements it, what tests it and where it is documented — without
reading source.** Everything else serves that, and it holds at 80 million lines only if four budgets hold.

| Budget | Means |
|---|---|
| **An answer costs its size** | `describe`, `ask 'node … \| callers'`, a tests lookup: proportional to what is printed, never to the repository |
| **Staying current costs the change** | an edit, a sync of 10,000 files, a branch switch: proportional to the files that changed, never to the files that exist |
| **Memory follows attention** | resident memory proportional to what the connected agents and app sessions are exploring, with a hard ceiling |
| **Nothing assumes the host** | no version control system, language, build system, test framework or repository layout is required; each is a provider, and "none" is a valid provider |

Every milestone in §8 is accepted against these budgets on a generated corpus, not on this repository — at
Nexaflow's size every design passes.

---

## 2. Where it stands

Measured against the budgets, the current design breaks in these places, worst first:

| # | Cost | Where |
|---|---|---|
| 1 | **The whole graph is one file, held whole.** `graph.bin` (tens of megabytes at this repository's size: nodes, edges, per-file contributions, one global string pool) is read eagerly into each working copy's workspace, rewritten whole on any dirty flush, and copied whole to seed each worktree. At several hundred times the size it cannot load | `GraphArchive`, `GraphWorkspace`, `Program.GraphStore` |
| 2 | **`grep` has no text index.** Every unscoped grep reads every file from disk and already takes seconds here; `search` falls back to it on a miss. It holds the working copy's turn while it runs, so every other query on that tree waits | `GraphAsk`, `Program.GrepNodes`, `GraphQuery.Find` |
| 3 | **Edit checks and test selection read the repository.** `graph edit` finds consumer projects by reading every `.cs`/`.xaml` for mentions; `nfi test` does it up to three times | `GraphMentions.Of`, `EditCheck`, `TestSelection` |
| 4 | **Indexes are rebuilt per call.** `node`, `callers`, `members`, `owned`, `near` and `graph context` build a dictionary of every node and scan every edge; `near` builds the whole adjacency; `context` does both several times | `GraphQuery.Index`, `GraphQuery.Adjacency` |
| 5 | **Refreshing one file costs the graph.** Pruning and relinking a changed file scans all nodes, edges and contributions and rebuilds the global name indexes — twice per edit | `GraphBuilder.RefreshFiles` |
| 6 | **Freshness walks.** Each query stats every known file and lists every project directory; each flush stats them again | `GraphFreshness.Check`, `GraphWorkspace.Reconcile` |
| 7 | **Nothing is evicted.** Roslyn compilations and the metadata cache only grow; the only idle behaviour is the whole process exiting after 20 minutes | `CompileHost`, `DaemonServer` |

Alongside those: the parse cache is keyed by path rather than content, so nothing is shared between working copies
holding the same bytes; the Product page hosts its own in-process copy of the graph instead of using the resident
process; a worktree's graph is scoped by folder name, so two same-named folders collide; and
`GraphBuildOptions.MaxFiles` caps a build at 20,000 files.

---

## 3. Links that write themselves — the node index

The product tree answers the primary question only if the links are in it. Written by hand they drift: reconciling
this repository's `[CoversNode]` declarations against the tree's `tests` links leaves close to half unmatched — two
thirds of those only because the tree names one class or one exemplar method where the declarations name every
method, the rest real gaps. Nobody can keep a second copy of thousands of facts in step, and nobody should have to.

So the tree gets two halves under one node key, answered by one query:

| Half | Written by | Holds |
|---|---|---|
| **Authored** — `tree.json` | people, through `nfi` and the Product page | nodes, status, concerns, descriptions; links to documents; links to code a node *uses* but does not own |
| **Indexed** — the node index | nfi, from declarations in source | what *implements* a node; what *tests* it |

A lookup reads both, from stored data. Nothing is scanned per call — §4 says how the index stays current, §6 how
it is stored.

**Declarations.** Code states which node it belongs to:

- `[CoversNode("id")]` on a test class or method — exists today;
- `[ImplementsNode("id")]` on a type or member that *is* the node's implementation;
- `[NoCoverage("reason")]` — exists today.

The rule for choosing between an attribute and an authored link is ownership: code that belongs to a node
declares it, and the link moves with the code and dies with it — a file deleted with its declarations takes its
links along, so a link can never outlive what it names. A node pointing at shared code it merely uses keeps an
authored link. Documents cannot carry attributes and stay authored; `remap` and heading validation already keep
those honest.

**The attributes package.** The attributes leave `Nexaflow.Tests.Fixtures` for a standalone assembly with no
dependencies (`netstandard2.0`), shipped beside `nfi.exe` together with the analyzer that checks node ids against
the tree (today's NXCOV rules). Two properties matter:

- **Read from source, not from binaries.** Today `scan-tests` reflects built test DLLs, which needs a build and
  goes stale. The index reads declarations with the same parse that builds the graph, so the attributes carry
  `[Conditional]`: the compiler drops them from the output, a consuming project ships nothing, and the build is
  not involved.
- **Not C#-only.** A language without attributes declares the same thing in a structured comment
  (`// nfi: implements <id>`), recognised by that language's provider (§7).

**Matching.** A class-level declaration covers the class's members; a method declaration is not "unlinked"
because its class is. The index stores what was declared and the query answers at the granularity asked.

**What becomes of the authored `tests` links.** Once the index answers tests, an authored `tests` link that a
declaration already states is deleted. One naming a test that carries no matching declaration is a gating
integrity issue — the declaration is the source of truth, and a link that disagrees with it is drift.

---

## 4. Knowing what changed, without version control

Every budget above rests on one ability: learning which files changed since the index last looked, without
walking the tree and without reading content. Today that is git for ignore rules and branch identity, and a
stat of every known file plus a walk of every project directory on each check (`GraphFreshness.Check`).

**The change journal.** NTFS and ReFS (including Dev Drive) keep a per-volume USN journal: every create, delete,
rename, data change and close, in order, with the file's reference number and its parent's. nfi keeps a cursor
per volume — `(JournalId, Usn)` — and on each wake reads only the records after it. This is independent of any
version control system: a sync from a proprietary server, a branch switch, a build and an editor save all land
in the journal the same way.

| Situation | What nfi does |
|---|---|
| resident process running | reads the journal from its cursor on a short interval, and folds the changed files in before answering |
| cold start, journal intact | reads from the stored cursor — a sync of 10,000 files costs 10,000 records, not a walk of 80 million lines |
| journal wrapped past the cursor, or its `JournalId` changed | one full reconcile by enumerating the volume's file records (`FSCTL_ENUM_USN_DATA`), comparing `(file id, size, last write)` against the stamps it holds, hashing only mismatches. Rare, and still no content read for unchanged files |
| journal unavailable (network share, FAT, no rights) | `ReadDirectoryChangesW` while resident; stat reconcile on cold start. Correct, but outside the budget — reported as such |

**Identity.** A file is identified two ways, and neither is a version control id:

- **File id** (the NTFS/ReFS file reference number) survives a rename or move within the volume. A rename in the
  journal is a rename in the index: authored links into that file are re-pointed with no `remap` and no history
  query. Editors that save by writing a temporary file and renaming it over the original change the file id;
  a name that disappears and reappears within one batch of records is treated as a modification.
- **Content hash** (XxHash128) is computed only for files the journal says changed. Extracted results are cached
  by content hash and extractor version, so a result is reusable by every branch, working copy and machine that
  holds the same bytes — which is what makes a shared cache possible without a shared version control system.

**Virtualised working copies.** Some version control systems project files on demand (ProjFS placeholders). No
step above reads the content of a file that has not changed, so enumerating and tracking such a tree does not
hydrate it. Where the provider can report a content id it already knows (§5), nfi uses that instead of hashing.

**Rights.** Change journal operations are documented as requiring administrator rights
([FSCTL_READ_USN_JOURNAL](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-fsctl_read_usn_journal)).
The reader is therefore the one elevated piece: a small per-machine service, installed once, that reads each
volume's journal and hands filtered records to any resident process over a pipe — the resident processes and
every client stay unprivileged. Inside Nexaflow the same reader can run in `PrivilegeBridge`. Without the
service, nfi falls back to the watcher-and-reconcile row above.

---

## 5. Version control as a provider

nfi touches git in seven places — one `git` process (`remap --from-git`), LibGit2Sharp in `ProductGit` and
`RepoFiles`, and four helpers reading `.git` directly — and everything else consumes what those produce: a
working copy root, the baseline copy, a branch name, the linked worktrees. That reduces to one interface:

| Operation | Replaces | Required |
|---|---|---|
| `Open(path)` → working copy root, baseline root, working copy id, is-baseline | `WorkingTreeRootOf`, `TryFindMainCheckout`, `ReadGitdirPointer`, the folder-name graph scope | yes — "none" answers: the directory holding `.product`, found by walking up |
| `OtherWorkingCopies(baseline)` | `GitWorktrees.Roots` | no — empty is valid |
| `CurrentChangeSet(workingCopy)` → id or none | `ProductGit.CurrentBranch` | no — none means writes go to the shared tree |
| `IsIgnored(paths)` / `EnumerateTracked()` | per-file `repo.Ignore.IsPathIgnored` | no — configured globs apply either way |
| `ContentId(path)` | nothing today; skips hashing when the system already knows a digest | no |
| `Renames(from, to)` | `git log -M` | no — the change journal sees renames as they happen |
| `Submit(paths, message)`, `Label(name)` | `CommitPaths`, `CommitAndTag` | no — promote and snapshots export without them |

"Has this change set merged" needs no operation, and stays that way: a pending set is a file that travels with
the change, and it has merged when it is present in the baseline copy. The one requirement on a version control
system is that it carries `docs/product/pending/` with the change.

The Product page writes snaplinks straight to the shared tree whatever change set is checked out, where the CLI
defers them; both go through the provider.

---

## 6. A paged graph

Whoever is asking — an agent through `nfi`, several agents at once, a person talking to the model in the app —
explores a small, local part of the graph at a time, and their combined exploration is still a small fraction of
it. The graph is therefore stored in pages, a page is in memory only while something is using it, and every
client of one tree shares one resident process and so shares its pages.

### Pages

**The file is already the unit** of extraction, invalidation and provenance: each node and edge records the file
that asserted it, and the archive already keeps one record per file. A page is a group of files — a directory
prefix, sized to a target byte count, following project directories where the build provider knows them. A node
id carries its file's path (`code:<relpath>#<astpath>`), so a small prefix table maps any id to its page without a
global id map.

A page holds, for its files only:

- file stamps (§4) and content hashes;
- nodes, the `contains` edges between them, and every edge and hyperedge its files assert — so a node's
  **out-edges** are always in its own page;
- the names its files use and declare, unresolved, as extracted;
- a **reverse summary**: for each of its nodes, which pages hold edges pointing at it;
- a trigram index of its files' text;
- its own string pool.

Pages are immutable files, memory-mapped. A change to a file is written to that page's small delta log and applied
over the page when it loads; a page whose log has grown is rewritten alone, in the background, when idle. Nothing
ever rewrites the whole graph.

### Always resident

Small, global, and memory-mapped:

| Index | Answers | Size, from this repository |
|---|---|---|
| page table | id → page | proportional to directories |
| authored tree + node index (§3) | the primary question | a few percent of nodes |
| name index — label → (page, ordinal) | `search`; resolving a reference by name | most nodes' labels — types and members — stored compactly |
| name-use index — name → pages using it | which files to relink when a declaration appears or disappears; which projects an edit affects | proportional to distinct names |
| external nodes, projects and `depends_on` | package and project dependencies | around one percent |

### Queries touch only the pages they need

| Stage | Pages |
|---|---|
| `node`, `members`, printing a block | the node's page |
| `callees` | the node's page; targets load only if printed |
| `callers` | the node's page for its reverse summary, then the pages it names |
| `owned` | the node index, then the pages of the files it names |
| `near <n>` | a breadth-first walk loading pages as it reaches them, under a page budget; an answer cut by the budget says so and names the command that continues it |
| `search` | the name index; a miss falls back to the trigram indexes, never to reading the repository |
| `grep` | trigram postings narrow to candidate files, and only those are read; a scoped grep (`--from`, `owned`, `near`) narrows the pages first |
| `graph edit` impact, `nfi test` selection | the name-use index and reverse summaries — the consumers the graph already knows, not a text search of every file |
| compile checks | one compilation per affected project, loaded and evicted like pages |

Resolution by name keeps its fan-out bounded by the uses of a name, not by the repository: a declaration added or
removed relinks only the files the name-use index lists. A very common name fans out widely, which is where a
language provider with real semantic binding (§7) earns its place over name matching.

### Memory

- A **byte budget** for decoded pages and compilations, configured per machine. Pages a running query uses are
  pinned; the rest are evicted least recently used when the budget is reached.
- **Idle flush:** a page not touched for a configured interval is evicted regardless of budget, and delta logs
  are compacted then. A quiet resident process shrinks to its always-resident indexes.
- The memory-mapped bytes of evicted pages are the operating system's to page out.
- **One resident process per tree serves every client.** The Product page uses it too, rather than its own copy,
  so a person and their agents exploring the same area share the same pages.

### Working copies share pages

Pages are addressed by the content hashes of their files, and node ids are the same in every working copy because
paths are relative. A working copy is therefore the baseline's pages plus an overlay of the pages whose files
differ — not a copy of the graph. Switching change sets, or a second worktree, costs the files that differ.

### Accepted when

Set as budgets on the generated corpus at the start of the work and held from then on; the first values to try:

| Measure | Target |
|---|---|
| `describe <node>`, `ask 'node <id> \| callers'`, pages warm | under 100 ms |
| the same, pages cold | under 1 s |
| a single-file edit folded in | under 1 s, independent of repository size |
| a repository-wide `grep` for a selective pattern | under 2 s |
| resident memory, idle | the always-resident indexes only |
| resident memory, busy | never above the configured budget |

---

## 7. From Nexaflow's tool to a tool

Most of what assumes this repository is hard-coded. It sorts three ways.

**Per-repository configuration** — `nfi.json` beside `product.json`:

- layout: directories that are not source (today `RepoFiles.SkipDirs` and `EditCheck.NotSource`, two copies),
  the third-party prefix (`external/`), default roots, export and pending folders;
- tests: where test projects are (today `src/Nexaflow.Tests`), what marks a UI journey suite or a desktop test;
- the concern tags code relies on (`tests`, `docs`, `theming`) and the lint thresholds (12 tests, 12 links);
- the root marker, walking up from the current directory, in place of `.git` and `Nexaflow.slnx`;
- the per-user state directory (today `%LOCALAPPDATA%\Smile\nfi`) and every size limit.

**Providers** — interfaces with a Nexaflow implementation and a "none":

| Provider | Today, hard-coded |
|---|---|
| Version control | git (§5) |
| Language | tree-sitter grammars and outlines; relationship edges (calls, implements, instantiates) for C# only; "source" meaning `.cs` and `.xaml` |
| Build and compile check | MSBuild design-time builds and Roslyn compilations; `.csproj`/`.sln(x)` projects; `dotnet build` |
| Test framework | MSTest/xUnit/NUnit attribute patterns, VSTest filters, `dotnet test`, metadata scans of test DLLs |
| Lint rules | one modelling convention compiled into `StructureLinter` |

**Nexaflow's own plugin** — stays out of the core: WPF/XAML pairing, bindings and `view_of`/`binds_to` edges;
AutomationId-based journey selection and `NXUI001`; the UI / Functionality / AI Integration backbone lint and
`AI Ready`; the Features/Providers project-family inventory in `SnaplinkValidator`; the CommunityToolkit and
`IPageRegistration` orphan excuses; `tools/graph-cli` staleness warnings.

---

## 8. Order of work

Each step ships on its own, is accepted against the §1 budgets on a generated corpus (a synthetic repository at
10× and 100× this one, plus a sample from the prospective codebase when available), and leaves Nexaflow working
throughout.

1. **Configuration and providers, extracted.** `nfi.json`; the version control, language, build, test and lint
   provider interfaces with today's behaviour behind them; the Nexaflow plugin separated; `MaxFiles` removed.
   No behaviour changes — this is what every later step is written against.
2. **Change journal.** The USN reader and its service, file-id and content-hash stamps, reconcile on journal
   loss; the parse cache keyed by content. Replaces the per-call freshness check and `remap --from-git`. Budget
   met: staying current costs the change.
3. **Node index, tests first.** Declarations extracted from source into the index; the tree's queries,
   `validate` and the Integrity page read it; matching by granularity; authored `tests` links retired.
4. **Attributes package.** `[CoversNode]`, `[ImplementsNode]`, `[NoCoverage]` and the analyzer shipped beside
   `nfi.exe`; `scan-tests` retired.
5. **Indexed queries.** Trigram grep, the name and name-use indexes, reverse summaries; no per-call index
   rebuilds; edit impact and test selection from the graph instead of text searches; refresh proportional to the
   file. Still one archive — but this pays for itself at Nexaflow's size, where grep already takes seconds, and
   it is the query engine §6 pages underneath.
6. **Paged store.** Pages, delta logs, the memory budget and idle eviction; working copies as overlays; the
   Product page through the resident process. Budget met: memory follows attention.
7. **Implementation declarations.** `[ImplementsNode]` adopted; authored code links that it states retired.

## 9. Open questions

- The prospective codebase's version control system: does it project files on demand, and can it report a
  content digest per file?
- Its languages and build systems — which decides the first provider after C#.
- Volume types (NTFS, ReFS Dev Drive, network shares) and whether developers hold administrator rights.
- Whether a shared, content-addressed cache server is acceptable, so a new machine starts from the team's index
  rather than parsing everything.
