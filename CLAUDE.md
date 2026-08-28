# Nexaflow — Claude Context

## What This Is

Nexaflow is a WPF shell replacement for Windows — file explorer, terminal, text editor, markdown editor, image viewer, project management, and AI assistant in one tabbed window. `.NET 10 / WPF / MVVM Community Toolkit`. The solution file is `Nexaflow.slnx`.

## Build

```powershell
dotnet build Nexaflow.slnx
dotnet run --project src/Nexaflow.Core/Nexaflow.Core.csproj
```

## Project Layout

```
src/
  Nexaflow.Core/                    shell chrome, main window, ribbon, AI input bar, FeatureManager
  Nexaflow.Features/
    Nexaflow.Features.Common/       ALL contracts (interfaces + small DTOs). NO FeatureManager — that's in Core.
    Nexaflow.Features.<Feature>/    ONE assembly per feature (~30: viewers, editors, players, managers, viewlets)
      Nexaflow.Features.Compressed.{Modern,SecureZip,SharpCompress}/  codec backends — implement IO.Common's
                                    IArchiveHandler/IStreamCodec, reference IO.Common ONLY (not even Compressed)
  Nexaflow.Visuals.Common/          shared WPF controls + converters + formatters (PieChart, ThemedRegion, SizeFormatter/DurationFormatter, …)
  Nexaflow.Visuals.Text/            shared markdown rendering (MarkdownView / SelectableMarkdownView / MarkdownFlowDocument, Mermaid)
  Nexaflow.Visuals.Terminal/        terminal input logic (command classifier, keys/history, environments model)
  Nexaflow.IO.Common/               shared IO leaves (net10.0, no WPF): EncodingDetector/LineEnding, Glob, Hashing,
                                    Base64Codec + stream-codec/archive contracts (IStreamCodec/IArchiveHandler/IArchiveEncryptor),
                                    VirtualFileSystem (archive-as-folder), TextLineIndex/TextTransforms, FileChangeWatcher, FileSplitter
  Nexaflow.IO.Terminal/             PTY host (pseudo-console service, terminal screen, job objects)
  Nexaflow.Syntax/                  syntax engine (highlighting, outline, structure extraction) — used by Code,
                                    Notebook, ProductManager, Visuals.Text. Owns TreeSitterLanguages
                                    (extension→grammar), so headless callers resolve a grammar without WPF.
                                    The tree-sitter runtime AND every grammar are compiled from pinned
                                    submodules (external/tree-sitter-dotnet-bindings + tree-sitter-xml) —
                                    NOT the TreeSitter.DotNet package, whose prebuilt natives go stale (its
                                    C# grammar predated `= []` and `[.. var rest]`, which cost a whole file
                                    its parse). One row per language in tools/tree-sitter-grammars.props;
                                    `.xaml` parses with the xml grammar under its own id so the extractor
                                    can read WPF meaning (x:Class/x:Name/x:Key/handlers)
  Nexaflow.Services.Initiatives/    WPF-free backend for the "initiatives" domain — Product today, Projects later:
                                    model, ProductStore, ProductAggregator/TreeOps, SnaplinkValidator. Never
                                    reference WPF/Core/Features.* from here
  Nexaflow.Services.Initiatives.Cli/ `nfi validate <root>` — the SAME SnaplinkValidator, headless.
                                    Powers the installer build gate and PowerShell tooling (exit 1 = broken links)
  Nexaflow.Elevation/               Elevation.Contracts (pure DTO leaf) + PrivilegeBridge (separate requireAdministrator
                                    exe) — the RunElevatedAsync trust boundary; see docs/Architecture.md → Elevation
  Nexaflow.Providers/
    Nexaflow.Providers.Common/      LlmProviderRegistry, shared message types, PromptComposer
    Nexaflow.Providers.{Claude,Gemini,OpenAI,Ollama,Aria}/  one per LLM backend (Aria = named pipe, rest = vendor SDK)
```

**The feature inventory is NOT listed here on purpose** — a hand-copied list goes stale. The authoritative
inventory and per-component status (incl. the `tests` / `AI Ready` / `theming` / `docs` concerns) is the
**product tree**. **To locate a feature's code/tests/docs, query the tree first** (it beats grepping — every
node carries snaplinks to its source):

**`nfi.exe` self-locates the `.product` tree — it follows a git worktree to its main checkout (where
the gitignored tree lives) — so run it from any checkout or worktree with NO root arg.** Build it once, then call the
exe directly (fast; no per-call rebuild). In the main checkout a prebuilt copy also sits at `tools/graph-cli/`
(`tools/publish-graph-cli.ps1` refreshes it). `$nfi` below is that exe. (The installer also ships it as an
opt-in **Command-line tools** feature — `[InstallFolder]\tools` added to the system PATH — so on an installed
box `nfi` is just on PATH. Off by default; `nexaflowBundle.exe /quiet InstallTools=1` for unattended.)

```powershell
dotnet build src/Nexaflow.Services.Initiatives.Cli    # once
$nfi = "src/Nexaflow.Services.Initiatives.Cli/bin/x64/Debug/net10.0/nfi.exe"   # or tools/graph-cli/nfi.exe
& $nfi find <term>                # nodes matching id/title/description
& $nfi describe <node-id>         # path, concerns, code/test/doc snaplinks
& $nfi describe <node-id> --code  # …plus every code snaplink resolved to its real source block (from YOUR working tree)
& $nfi tree [<node-id>] [--full]  # the WHOLE subtree as an outline — "show me this entire feature" (no id = every root; --full = +snaplinks/about)
& $nfi lint --under <node-id>     # does this feature follow the modelling rules? (advisory; see docs/feature-tree-and-tests.md)
& $nfi diff                       # what changed in the tree since the last release snapshot (nodes added/removed, status, concerns)
```

**Code discovery is graph-first, always — there is no case in this repo where discovery starts with
Read/Grep/Glob.** Query the graph FIRST; then Read only the specific block it names. Same before spawning an
Explore/Plan agent — and require any sub-agent you do spawn to use it too. The `graph` command builds
`.product/graph.json` — the product tree ⊕ whole-repo AST ⊕ their snaplinks — and queries it headlessly. It's more
token-efficient than reading files and surfaces relationships grep can't (who calls/instantiates a type, a project's
`depends_on`, a view's `view_of` code-behind, the file a member `mentions`, the product feature that owns a code
node). Regenerate with `graph build` (incremental) after code changes, then explore (`graph help` lists the full set):

```powershell
& $nfi graph search <term>     # find nodes (product/type/member/file) by id/label
& $nfi graph node <id>         # a node + ALL its edges (both directions) + hyperedges
& $nfi graph context <id>      # ONE-SHOT: node + its source + neighbours + owning feature
& $nfi graph walk <id> --hops 2                                # its N-hop neighbourhood
& $nfi graph grep <regex> --from <id> --hops 2 --mode content        # grep source NEAR a code node
& $nfi graph grep <regex> --from product:<slug> --scope owned --mode content   # grep a whole FEATURE
& $nfi graph grep <regex> --mode content                             # grep EVERY code node (~3s, 64k nodes)
& $nfi graph code <code-id>    # a code node's source block; `graph cat file:<path>` = whole file
& $nfi graph build             # regenerate .product/graph.json after code changes (incremental)
```

**The graph edits too, and structurally — `graph edit <op> <node-id>`.** Addressing a change by *what it is*
rather than by which lines it currently occupies:

```powershell
& $nfi graph edit replace    <node-id> --file new-method.cs        # whole declaration (keeps its doc comment)
& $nfi graph edit signature  <node-id> --text 'public long Add(int a, int b)'   # body stays byte-for-byte
& $nfi graph edit body       <node-id> --stdin                     # signature stays byte-for-byte
& $nfi graph edit rename     <node-id> --to NewName
& $nfi graph edit delete     <node-id>                             # takes its doc/attributes with it
& $nfi graph edit append     <type-id> --text-escaped 'public int Zero() => 0;'   # into a type's body
& $nfi graph edit insert-before|insert-after|doc <node-id> --file …
& $nfi graph edit substitute <node-id> --find 'old();' --text 'new();'   # find/replace INSIDE one declaration
& $nfi graph edit import     <file-or-node-id> --text 'using System.Linq;'   # where the file keeps its imports
& $nfi graph edit create     <relpath> --file new-class.cs         # a new file (refuses to overwrite; must parse)
& $nfi graph edit substitute file:<relpath> --find 'namespace A;' --text 'namespace B;'   # what is in NO declaration
```

**You never have to think about the graph being stale.** Three things make it a non-issue, so don't reach for
`graph build` before editing:

- **The target file is re-read and merged into the graph on every edit** (`GraphBuilder.RefreshFile`) — one
  file's parse, not the ~90s whole-repo walk. A file you *just created* is in the graph after the first edit
  to it, and a file you deleted is pruned from it. `--no-refresh` skips this for a batch.
- **A moved declaration is re-found by name.** If the recorded AST path no longer resolves but the name is
  declared once in that file, the edit goes ahead and says so. It refuses only when the name is gone, or when
  several declarations share it — it will not pick one for you.
- **A node the graph has never seen is still editable.** `code:<relpath>#<astpath>` and `file:<relpath>` name
  everything an edit needs, so the id works whether or not the graph holds it. The graph is how you *find* an
  id; it is not what makes one valid.

`graph build` is for the cross-file passes — call/inheritance resolution, communities — not for editing.

**Every graph query tells you whether its answer is current**, so you never have to guess. It compares
`graph.json`'s own write time against the files the graph recorded (a stat each — no re-reading, which is the
90s) and the directories the solution's projects live in (4,009 files, not the 17,038 the whole repo holds,
because 14,849 of those are pinned submodule corpora). Roughly 0.2s, and it always says one of:

```
graph: current — 6,204 files, none changed since it was built.
graph: 2 changed, 5 added vs this working tree — this answer may be out of date. Re-run with --refresh …
```

`--refresh` (on `search`/`list`/`node`/`walk`/`context`/`grep`/`code`) folds those files in *before*
answering. **From a worktree the refresh is in-memory only** — `graph.json` is shared with the main checkout
and every other session, so a branch never silently writes its view into it; `graph build` from the worktree
is how a branch deliberately publishes one.

**Prefer this over hand-editing a file, and over `sed` in particular.** Each edit re-resolves the declaration
in the file *in hand*, refuses unless the parser agrees it is still the one the graph labelled (so a stale
graph can never overwrite whatever now occupies those lines), and re-parses the result — an edit that would
break the file is refused, not written. `signature` and `body` each prove the other half is unchanged
afterwards rather than assuming it. `substitute` is the safe form of a stream edit: literal unless `--regex`,
bounded to the one declaration so a common identifier can't be rewritten across the file, and refused unless
it matches exactly once (`--all` to override, and it reports how many it touched). **`--find` does not need
matching indentation** — an exact match wins, and failing that the fragment is matched line-by-line ignoring
leading whitespace, so a snippet pasted as you read it or written flush-left is found either way. When it
isn't there at all, the refusal names the declaration that *does* contain it, with its node id.

There is deliberately **no line-addressed edit inside a body**: line numbers are the failure mode this design
removes, and a `substitute` whose search text you extend by a line either side is both unambiguous and
self-verifying.

**Several edits to one file in a row** are safe — each re-resolves against the file as it then is, so line
drift is a non-issue, and a path invalidated by an earlier edit (a rename, a delete) is *refused*, not
guessed at. The one exception is **overloads**: the `#N` in `T:C/M:Add#1` is that overload's *position* among
its same-named siblings, so deleting or inserting one renumbers the rest, and a later edit reusing an earlier
listing would aim at a different method while the name check still passes. Such an edit says so in its notes
(`… renumbers the others`). After one, either re-list, or pin the next edit with `--expect` — that is the only
guard that still refuses when the path itself has come to mean something else.

You do not have to think about **line endings, indentation, BOMs or escaping**: write the replacement
flush-left with `\n` and it lands correctly indented with the file's own endings and encoding. Text comes from
`--text` (literal), `--text-escaped` (decodes `\n`/`\t`/`\uXXXX`, leaving anything else — a regex, a Windows
path — alone), `--file`, or `--stdin`; `--find`/`--find-escaped` mirror that pair. `--dry-run` prints the hunk
and writes nothing; `--expect S` refuses unless the block still contains `S`, pinning the edit to what you
read. Rebuild the graph afterwards so its record matches.

`import` is file-level (it takes a `file:` id, or any code node in that file) and lands where the file already
keeps its imports — under the last one, or below a licence header when there are none. Reaching that through
`insert-before` on the first declaration was possible and wrong: with a file-scoped namespace it put the
`using` *underneath* the `namespace`, which compiles and reads as a mistake.

The engine is `StructuralEdit` in **`Nexaflow.Syntax`** — source text in, source text out, no graph and no
file IO. `GraphEdit` (in `Services.Initiatives`) is only the adapter that turns a node id into a file, an AST
path and the name to verify against. Three surfaces drive it:

| Surface | How a declaration is addressed |
|---|---|
| `nfi graph edit` | a graph node id |
| `graph_edit` client tool (assistant) | a graph node id |
| `list_declarations` + `edit_declaration` (any editor tab) | an `ast_path` from the open buffer — no graph needed |

The editor tools live on `FileTextEditorViewModel`, so every tab built on the shared editor base (Code,
Notebook, …) gets them. They apply the edit as a **minimal splice** rather than reassigning the document, so
it is one undo step and the caret and scroll position survive. Prefer `edit_declaration` over the older
`set_editor_text` / `replace_all`, which respectively restate the whole file and match across all of it.

**Searching for a code *pattern* is a graph query too** — not just "where is X". A defect signature ("a path
compared with a bare `StartsWith`"), an idiom sweep, a "does anything still do Y" — all of it is
`graph grep … --mode content`, which reports each hit as file:line **plus the owning type/member and feature**.
Reach for it exactly where you would otherwise type `grep -rn`; a blanket text search is never the better tool
here, and with no `--from` it covers the whole repo in a few seconds. **`--limit` trims the printed list, never
the search** — the total on the summary line is the real total, and a trimmed run says `showing N, raise --limit
for the rest`. Only an explicit `--scan-cap` can cut the search short, and that prints a loud `INCOMPLETE`. So a
count with neither notice is a count you can reason about, including a zero.

Pick the scope by what you mean, not by tuning a number:

| You mean | Use |
|---|---|
| near THIS code (callers, collaborators) | `--from <code-id> --hops 1..2` |
| inside THIS feature | `--from product:<slug> --scope owned` |
| anywhere in the repo | no `--from` |

`--scope owned` searches every file the feature's snaplinks land in, so it covers members no link names while
still stopping at the feature boundary — widening `--hops` until a feature is covered also drags in whatever
else happens to be that far away. `graph context <id>` prints the owned-file list **and the exact grep command
for it**, so the anchor never has to be guessed. The two are mutually exclusive by design: passing both
`--hops` and `--scope owned` is a hard error rather than a silently ignored option.

Node ids: `product:<slug>` · `code:<relpath>#<astpath>` · `file:<relpath>` · `external:<name>`. **`graph` is
worktree-aware**: run from a linked worktree and both `graph build` and the source-dumping queries (`code`/`context`/
`grep --mode content`) use THAT branch's code — the build re-parses only the files that differ from the main checkout
(the cache is content-addressed), so it's cheap. The product tree + `graph.json` still live in the main checkout.
`--main` forces the main-checkout source; `graph --code-root <dir>` points it anywhere. (`describe --code` is likewise
working-tree-first.) For repo discovery you can also spawn the **`nexaflow-explorer`** sub-agent, which drives this exe.

The product-folder skill has fast-query recipes for deeper questions; the per-release export
[docs/product/PRODUCT.md](docs/product/PRODUCT.md) is the human dashboard. Per-feature tab parameters are in
[docs/features.md](docs/features.md). **How to model a feature as tree nodes and back each with the right test**
— the UI/Functionality/AI backbone, concern-by-role rules, the one-journey-plus-per-leaf-unit-test model, and
the roadmap of analyzers/validators to lock it down — is in
[docs/feature-tree-and-tests.md](docs/feature-tree-and-tests.md) (the Text Viewer is the worked reference).

**Every git-reading verb runs git where *you* stand, not where the tree lives.** `remap --from-git` resolves its repository from the caller's working tree — from a linked worktree the product root is the MAIN checkout, whose `HEAD` has never seen your commits, so a range ending at `HEAD` came back empty and the verb rewrote nothing while reporting success. Note the blind spot that hid it: `validate` falls back to the product root when your working tree lacks a file (deliberately — it is what stops a worktree flagging every not-yet-merged path), so a file you have **moved away** still resolves in the main checkout and the tree reads clean while its links are stale. After a rename or move, run `remap --from-git <base>..HEAD --dry-run` — do not infer from a clean `validate` that nothing needs remapping.

Every verb's arguments are **strict** — an unknown option, a missing option value or a surplus positional is a
hard error naming that verb's usage, never silently ignored (`batch` parses each line the same way and is
all-or-nothing).

**`validate` answers about the branch you are on.** From a linked worktree it resolves each snaplink against
*that* tree — not the main checkout — because "does this file exist somewhere" is not the question a branch
needs answered. It then splits what it finds: a link to a file that is in **neither** checkout belongs to some
other branch's not-yet-merged work and is reported but does **not** set the exit code, while a link to a file
main has and your branch does not **is** yours and does. `validate --main` gives the main checkout's view,
which is what the installer's release gate runs (from the main checkout, where the two are the same anyway).
The old fallback to the product root is what let a file you had *moved away* keep resolving through main, so a
branch read clean while its links were stale.

When a rename/move breaks snaplinks, don't hand-edit `tree.json` — `remap` rewrites them under validation:

```powershell
dotnet run --project src/Nexaflow.Services.Initiatives.Cli -- remap <old-path> <new-path> [--class <n>] [--method <n>]
```

**A snaplink `doc` is the repo's own path — never a path through a linked worktree.**
`.claude/worktrees/<name>/src/Foo.cs` resolves only while that branch is checked out, so `validate` reports it
as a gating `WorktreePath` issue even though the file exists, and `doctor --fix` re-roots every one back onto
`src/Foo.cs`. `scan-tests` normalises the same way, so a test DLL built inside a worktree still records repo
paths in the coverage manifest (and therefore in the Integrity page's *Add link* suggestions).

**Snaplinks are mechanically checked.** Every snaplink (on a node *and* on each concern link) must still point
at a real target — the file exists, the markdown heading path resolves, the class/method is still declared, the
URL is well formed. Run it from the Product tab (⋮ → **Validate snaplinks**, or the root's integrity tile) — both
open the **Integrity page** (`ProductIntegrity`), which rescans on the shell's background queue (a full scan
tree-sitter-parses every referenced file and takes seconds, so it never runs on the dispatcher) and lets you
re-point or remove each broken link. Or run it headlessly:

```powershell
dotnet run --project src/Nexaflow.Services.Initiatives.Cli -- validate .   # exit 1 = broken links
```

The **installer build runs the same check and fails on any broken link** (`nexaflowSetup.wixproj` →
`ValidateSnaplinks`), so `NexaflowSetup.slnx` is the release gate; a plain `dotnet build Nexaflow.slnx` never runs
it. Results persist to the gitignored `.product/integrity.json` (derived — safe to delete). A file whose extension
has no tree-sitter grammar (`.txt`) is treated as **unverifiable, not broken** — never make the validator guess.
**`.xaml` is verifiable now** (the `xml` grammar is built from `external/tree-sitter-xml`): a link may name an
`x:Class`, an `x:Name`, an `x:Key`, an `AutomationProperties.AutomationId` or an event handler, and a rename
breaks it loudly instead of rotting.

A second, **non-gating** channel sits beside the issues: a link whose file and class are sound but whose finer
`ast` target no longer resolves is an **advisory**, printed with the `nfi set-snaplink` command that fixes it.
`ast` had never been validated by anything, so it holds prose as often as a path — failing a release build on
that would punish links whose real target is fine. Advisories never affect the exit code.

**The tree is forward-looking** — the plan of what *should* be in place for the **next release**, not a snapshot
of what shipped (that's the label-aligned [docs/product](docs/product) export). So **update it as you build** — flip
concerns, add snaplinks, fix descriptions — *right then, not after merge*. Because the snaplink check is
setup-build-only (above), pointing a `done` snaplink at a not-yet-merged file never blocks a regular build; it's the
intended forward-looking state.

**Test coverage is declared on the test, not hand-linked in the tree.** Every concrete `[TestClass]` carries
`[CoversNode("node-id")]` (from `Nexaflow.Tests.Fixtures`) naming the node(s) it backs — or `[NoCoverage("reason")]`
for architecture/corpus/infra tests that map to no node. The tree stays authoritative; the attributes are a
cross-check with a one-click reconcile:

```powershell
dotnet run --project src/Nexaflow.Services.Initiatives.Cli -- scan-tests .                     # → .product/test-coverage.json
dotnet run --project src/Nexaflow.Services.Initiatives.Cli -- scan-tests . --suggest-attributes # tree-derived [CoversNode] starter set
```

Put a `[CoversNode]` at **class level** only when the whole class covers that node (usually a container with
children); a specific behaviour (a leaf node) goes on the individual `[TestMethod]`(s) — the manifest then carries
precise class+method links. Grow the tree finer when a leaf needs sub-nodes:

```powershell
dotnet run --project src/Nexaflow.Services.Initiatives.Cli -- add-node <parent-id> "<title>"   # + default concerns, re-validates
```

`scan-tests` reflects the built test DLLs (metadata-only, via `MetadataLoadContext` + the portable PDB for the
source path) into the derived **coverage manifest**. The Integrity page reconciles that manifest against the tree
and shows each *declared-but-unlinked* test as a **non-gating advisory** with an **Add link** button (writes the
`tests`-concern `code` snaplink for you). This is a separate channel from the gating `Issues` — advisories never
fail the installer. Two enforcement layers keep it honest: the **Roslyn analyzer** `Nexaflow.Analyzers.Coverage`
(NXCOV001 missing declaration / NXCOV002 stale id / NXCOV003 a class-level leaf that over-claims when the class
covers other nodes too, author-time) and the **guard tests** `CoverageDeclarationGuardTests` (CI). Id validity is
checked against the live `.product/tree.json` (gitignored → absent in CI, where it degrades to presence-only).
Separately, a `ConcernDef` can set **`RequiresSnaplink`** (enabled for `tests`): a node whose `tests` concern is
`done`/`faulted` with no snaplink is a *gating* `MissingSnaplink` integrity issue — so "tests done" can't ship unbacked.

Shared, non-contract code lives in `Nexaflow.Visuals.*` (UI), `Nexaflow.IO.*` (IO), and `Nexaflow.Syntax` — mirror that pattern for any future shared-but-not-a-contract code rather than dumping it in `Features.Common`.

## Hard Rules

- **Discovery goes through `nfi.exe` — never Grep/Glob/Read-first.** "Where is X / what is X / who calls or instantiates X / what feature owns X / how does X relate to Y" is answered by `graph search`, `graph context`, `find` or `describe` (see above) **before any file is opened**; Read is for the specific block the graph names. Reach for it as reflexively as for Grep — it is cheaper and it surfaces call/ownership/dependency edges grep cannot. Sub-agents follow the same rule: point them at the exe, or spawn `nexaflow-explorer`.
  - **This covers searching for a code *pattern*, not just a named thing.** "Which code looks like Y" feels like a different job from "where is X" and is the same verb: `graph grep <regex> --mode content` (scope it per the table above). That split is how the rule gets abandoned in practice — the entity lookup goes through the graph, then the pattern hunt falls back to `grep -rn`. It shouldn't: the graph answers it, faster, and names the owning member and feature of every hit instead of just a line.
  - **Read a block with `graph code <id>`, a whole file with `graph cat file:<relpath>`** — never `sed -n A,Bp` on line numbers guessed from a search hit. Both are worktree-aware; hand-sliced ranges are not.
- **Before calling a change complete, ask the graph who else depends on what you touched.** Discovery-first finds the thing; this finds the *rest* of it. A package bump is `graph node external:<Name>` (its `depends_on` edges list every consuming project); a type or member is `graph node <id>` / `graph walk <id> --hops 2` for the incoming callers. Do this before you say a fix is done — grep answers "where is this token", the graph answers "what else breaks", and only the second one closes a change.
  > Worked example: the NAudio 3.0 bump renamed `WaveOutEvent`→`WaveOut` and `WaveInEvent`→`WaveIn`. Fixing the Audio feature's playback looked complete and wasn't — Core's `VoiceManager` captures audio and broke the same way. `graph node external:NAudio` names both `Nexaflow.Core.csproj` and `Nexaflow.Features.Audio.csproj` in one query; a grep of the feature you happen to be in names neither.
- Features depend only on `Features.Common` (and the `Nexaflow.Visuals.*` UI libs) — never on Core, rarely on each other
- Providers depend only on 'Providers.Common' - never on Core, never on each other.
- Core never instantiates feature view or ViewModel types directly. All view (tabs) and viewlet creation goes through `FeatureManager`.
- Features communicate back to the shell only via `IShellServices` (injected into `IPageRegistration` constructors by `FeatureManager`).
- **Features never touch the UI dispatcher.** No `Application.Current.Dispatcher` / `Dispatcher.CurrentDispatcher` in a feature. Marshal background work to the UI thread with `IShellServices.RunOnUiAsync(...)`, and watch files with `IShellServices.WatchFile(path, onChanged)` (the shell owns the watcher, dedups by path, marshals the callback, and tears it down). UI-thread ownership lives in Core (ShellServices captures its own `_ui`).
- A feature advertises a page via `IPageRegistration` (`PageKind` + `CreatePage`); `FeatureManager` discovers it by reflection at startup (each registration exposes a `static string StaticPageKind`).
- **Features never hard-code colours.** Every colour — even one a feature "owns" (status pip, chart/pie series, selection/search wash, post-it paper) — resolves from a theme resource so a theme can retune it: reuse a palette/semantic token (`TextBrush`/`AccentBrush`/`SuccessBrush`/`WarningBrush`/`DangerBrush`/`OnAccentBrush`), the categorical `Swatch.*` bank (for N distinct colours), or a feature-owned token shipped via `IThemeContribution` (like the scratchpad's `PostIt.*`). Code-drawn surfaces read the resource at paint time with a literal only as a last-resort fallback. Full rule + patterns in [docs/theming.md](docs/theming.md) → *Rule: a feature never hard-codes a colour*.
- **Features never elevate directly.** No `Process.Start` with `runas` in a feature — route admin actions through `IShellServices.RunElevatedAsync` (a DTO in `Elevation.Contracts` + an `IElevatedOperation` in the PrivilegeBridge). See [docs/Architecture.md → Elevation](docs/Architecture.md#elevation--privilege-bridge).
- **Third-party source deps are git submodules under `external/`, consumed via `ProjectReference` from the smile-forge fork — never `PackageReference`, never vendored/copied.** The one exception is a **native grammar**: C has no `.csproj` to reference, so an MSBuild target compiles it instead — same fork/branch/pin convention, different wiring (see *Native grammar submodules* in the runbook). That covers `external/tree-sitter-xml` and the nested grammar/runtime submodules inside `external/tree-sitter-dotnet-bindings`, whose own `src/TreeSitter.csproj` **is** a normal `ProjectReference` (it replaced the `TreeSitter.DotNet` package — the package's frozen natives were silently wrong). `origin` = the org fork, `upstream` = the original; a per-repo `nexaflow` integration branch is what's pinned, and upstream PRs come only from atomic `feat/*` branches. **Before touching anything under `external/`, adding/bumping a submodule, or wiring one of these deps, read the runbook [docs/externals.md](docs/externals.md)** (also pointed to by `external/README.md`). Remember the cwd is pinned → use `git -C external/<name> …` for every submodule git op.

The reference and dispatcher rules are **mechanically enforced**: `Nexaflow.Tests.Features.Architecture/Architecture/ArchitectureRulesTests` + `Nexaflow.Tests.Providers/ArchitectureRulesTests` fail on a violation, and `FeatureTouchPointTests` names any missed add-a-feature wiring step (Core/tests ProjectReference, filemap entry).

## Key Files

| File | Why You'd Touch It |
|------|--------------------|
| `src/Nexaflow.Core/ViewModels/ShellViewModel.cs` | Tab lifecycle, ribbon, AI routing — god object, be careful |
| `src/Nexaflow.Core/Services/ShellServices.cs` | `IShellServices` implementation |
| `src/Nexaflow.Core/FeatureManager.cs` | Per-`WorkspaceRuntime` constructor injection for features (lives in **Core**, not Common); `EvictWorkspace` clears the cache on reconfigure. Discovery is delegated to `FeatureCatalog` |
| `src/Nexaflow.Core/Services/FeatureCatalog.cs` | Disk-cached feature discovery index (`discovery/catalog.json`) → a normal launch loads **no** feature DLLs; assemblies load + activate lazily (first use or post-paint warm-up). Stamped with the app version **and** the feature-DLL set (name/size/write-time — `FeatureCatalogStamp`, one directory enumeration, no assembly loads), so **any** rebuilt/added/removed DLL forces a rescan, not just a version bump. **Debug builds bypass the cache entirely** (always rescan, never write, stale file deleted) so a full scan also eagerly activates every feature — never clear it by hand while developing |
| `src/Nexaflow.Core/Services/WorkspaceManager.cs` | The `Workspaces` list (dropdown) + live `WorkspaceRuntime`s; create/switch/reconfigure/dispose lifecycle |
| `src/Nexaflow.Features/Nexaflow.Features.WindowsFileSystem/Services/FileSystemFeatureRegistry.cs` | Discovery for the file-system contracts (`IFileAction`/`IFolderAction`/`IFileCreateAction`/`IFolderViewlet`/`IThisPcItemProvider`) — NOT FeatureManager. Lives in the **feature**, not Core. Also publishes each provider's local-path row as a VFS mount |
| `src/Nexaflow.Features/Nexaflow.Features.Common/*.cs` | Contracts — changes here affect everything |
| `src/Nexaflow.Features/Nexaflow.Features.Common/IPageRegistration.cs`, `Page.cs` | The tab/page factory contract (`CreatePage`) and the `Page` model (`Title`/`Icon`/`Breadcrumbs`/`ContentFactory`) |
| `src/Nexaflow.Core/Themes/Styles.xaml` | App-merged shared control styles. Feature XAML references theme keys by `{StaticResource …}` — no assembly ref needed. A theme is layered (palette → region tokens → per-theme overrides + scenes → styles); see [docs/theming.md](docs/theming.md) |

## Config & Data Paths

Base: `%APPDATA%\Smile\nexaflow\`

```
{ConfigName}\                 GLOBAL app/feature config (IFeatureConfig.ConfigName) — shared by all workspaces
Contexts\<name>\              PER-WORKSPACE data (folder named "Contexts" for on-disk compat):
  ai-abilities\               AI ability grid (which provider/model per ability)
  ai-persona\                 assistant persona (name + system prompt)
  <provider configs>\         provider API keys / subscriptions for THIS workspace
  Conversations\              AI chat history for THIS workspace
  <ribbon layout>             ribbon items for THIS workspace
  <scoped feature configs>    any IFeatureConfig marked [WorkspaceScopedConfig]
```

Ribbon, AI ability grid, persona, provider configs and conversations are **per-workspace** (not global). Feature `IFeatureConfig` is **global** (one instance per assembly) unless marked `[WorkspaceScopedConfig]`.

**Versioned, self-migrating.** Each config persists as `…\{configName}\config_{AssemblyVersion}.json`. When an assembly version bumps, `ConfigManager` **migrates the newest older file forward** instead of discarding it — a lenient field-by-field carry-over (unknown fields dropped, missing ones keep defaults) plus an optional `IConfigMigration.MigrateFrom(previousJson, version)` hook for renames/restructures. So an update keeps the user's data, and the setup wizard re-asks only for genuinely new required info. File-type mappings merge changed bundled defaults while preserving user customizations. Options → About has a **Reset Config** button (confirmation-gated) that wipes `%APPDATA%\Smile\nexaflow` and relaunches into first-run. Full detail in [docs/Architecture.md → Config versioning & migration](docs/Architecture.md#config-versioning--migration).

## Workspace / WorkspaceRuntime scoping

A **`Workspace`** (`Models/Workspace.cs`) is the saved, shared config shown in the dropdown; a **`WorkspaceRuntime`** (`Models/WorkspaceRuntime.cs`) is a runtime grouping of one-or-more window frames running ONE workspace. Getting scope wrong is the easiest way to add a bug — full detail in [docs/Architecture.md → Ownership & Lifetime](docs/Architecture.md#ownership--lifetime).

> **Naming history:** pre-2026-07 the saved half was called `Profile` (before that `WorkContext`). Those names survive ONLY as frozen on-disk/IPC compat strings — the `workcontexts` config name, the `Contexts\` folder, the `--context` flag. Never reintroduce them as type/member names.

- **Central (one per process):** `ConfigManager`, `ProviderManager` (loads provider **assemblies/types**; owns the **ref-counted instance pool** — identical configs share one provider), `WorkspaceManager` (the `Workspaces` list + live `WorkspaceRuntime`s), `FeatureManager` (feature **types**; builds instances per runtime), `BackgroundActivityManager`. Global configs = every feature `IFeatureConfig` not marked `[WorkspaceScopedConfig]`.
- **Per-`Workspace` (shared, saved):** ability→model assignments (`AiConfig`), the **AI persona** (`AiPersonaConfig`, under `ai-persona`), provider configs (API keys), ribbon layout (live-synced across its runtimes via `RibbonChanged`), conversations, default + last-session tabsets, `[WorkspaceScopedConfig]` feature configs. All under `Contexts\<name>\`.
- **Per-`WorkspaceRuntime` (runtime):** `ShellServices` (windows/tabs), `AIService` (agent loop), the **acquired** provider instances. App/IPC launch = a new runtime; tear-off / "open in new window" = same runtime; dropdown switch reconfigures the current runtime in place (tabs close, providers/AIService rebuilt); closing the last window disposes it.
- The `IShellServices` / `IAIService` injected into a feature are the **active runtime's** — opening a tab or asking the AI always acts within one runtime.
- Options & Manage-AI overlays are **modal** (block switching); you can't delete a live workspace; there's always ≥1 workspace.

Mnemonic: **feature settings = global (unless `[WorkspaceScopedConfig]`); persona, ability grid, provider configs, conversations, ribbon, tabsets = per-`Workspace` (saved); AIService, providers, windows/tabs = per-`WorkspaceRuntime`.**

## Tests

Test projects under `src/Nexaflow.Tests/`, plus a shared fixtures library. Full guide: [docs/testing.md](docs/testing.md).

| Project | Covers |
|---------|--------|
| `Nexaflow.Tests.UIJourneys` | **Every test that launches the app** and drives the real mouse — `Core\` for the shell, `Features\<Feature>\` for the rest. References **only** `Tests.Fixtures`: a journey knows the app as a running process, never as an assembly. Fixtures it can't click into being are built by the suite that owns that format, into `test-samples/ui/`; a missing one is *inconclusive*, not a failure. **So run the other suites first.** |
| `Nexaflow.Tests.Core` | The Core shell **only** — config/workspaces, feature catalog + DI, shell services, agent loop, the `?` search route, theming. References Core, and Core hard-references every feature and provider (they must land in its output for `FeatureCatalog`), so building this builds the solution. That is why everything not needing Core lives in the two suites below. |
| `Nexaflow.Tests.Visuals` | `Nexaflow.Visuals.*` — markdown/LaTeX/music rendering, the inline editor, editor highlighting, shared controls + layout, the WebView surface. **Never Core.** Its `TestCategory("UI")` tests render WPF off-screen — an STA thread, no window; its `TestCategory("Desktop")` ones show a real window and so must also be `[DoNotParallelize]`. |
| `Nexaflow.Tests.Components` | The shared leaves that are neither IO nor UI: `Nexaflow.Syntax`, `Nexaflow.Search`, `Elevation.Contracts`. Same shape as `Tests.IO` — references the subjects and `Tests.Fixtures`, nothing else, and no WPF. |
| `Nexaflow.Tests.Features` | The shell-adjacent features — AI chat, console, network, OneDrive, Product/Projects, scratchpad, This PC, web — plus the **folder viewlets** (Git, Dotnet) and the feature-agnostic search plumbing. References the feature projects, **not** Core. |
| `Nexaflow.Tests.Features.Viewers` | Every viewer/editor/player (Audio…Video) and the sample-file corpus. A feature that registers no page is not a viewer: Git and Dotnet are `IFolderViewlet`s and live in `.Features` beside the other three. |
| `Nexaflow.Tests.Features.WindowsOS` | The Windows-integration features: file system, registry, search index, installed apps, processes, system info. |
| `Nexaflow.Tests.Features.Architecture` | The whole-repo guards. References the other suites for their **output** — the rules reflect over every feature and test assembly. A new suite must be added to `FeatureTestSuites.Patterns` or it silently drops out of the `[CoversNode]` guard. |
| `Nexaflow.Tests.Features.Common` | **Not a test project** — shared support for the suites above: `AsyncPump`, `RepoRoot`, `DicomTestFiles`, the `ISearchable` and viewer-`IFileAction` conformance contracts. No feature reference. (The FlaUI bases left with the journeys; `ViewerMap` moved to `Tests.Fixtures`, which both its consumers reference.) |
| `Nexaflow.Tests.IO` | `Nexaflow.IO.*` — the WPF-free IO leaves. References the IO projects and **nothing else**: no Core, no Features, no Visuals, so it needs neither a desktop session nor a shell. |
| `Nexaflow.Tests.Initiatives` | `Nexaflow.Services.Initiatives` + its CLI — the product tree, the graph, `SnaplinkValidator`, `ProductTreeOps`, the verb parser. Same shape as `Tests.IO`: plain `net10.0`, references the backend and `Tests.Fixtures` and nothing else, so it needs no desktop session. What stayed in `.Features` is the ProductManager *feature* (view-models, AI client tools, graph viewer). |
| `Nexaflow.Tests.Providers` | Provider clients. |
| `Nexaflow.Tests.Fixtures` | **Not a test project** — a dependency-free `net10.0` library that generates the shared sample-file dataset, plus `UiFixtures` (the material the journeys open) and `ViewerMap`. Referenced by every test project. |

A test belongs in `Tests.IO` when its **subject** is an IO library. One that merely *uses* one — `Text`
reading through `EncodingDetector`, `Compressed` through the VFS — stays with its feature: a test follows
what it is about, not what it imports.
The same rule owns `Tests.Initiatives`: its subject is the WPF-free backend, so the suite is WPF-free too.

After any change touching `Nexaflow.Core`, run the unit tests before committing:

```powershell
dotnet build src/Nexaflow.Tests/Nexaflow.Tests.Core/Nexaflow.Tests.Core.csproj
src/Nexaflow.Tests/Nexaflow.Tests.Core/bin/x64/Debug/net10.0-windows10.0.19041.0/Nexaflow.Tests.Core.exe --filter "FullyQualifiedName~Unit"
```

UI journeys live in their own assembly and take over the mouse and keyboard, so they are never part of a
feature's inner loop — editing a feature cannot launch the app any more, because nothing in its suite can.
Run them last, on a machine you are not using, and after the other suites (which build the fixtures they
open):

```powershell
src/Nexaflow.Tests/Nexaflow.Tests.UIJourneys/bin/x64/Debug/net10.0-windows10.0.19041.0/Nexaflow.Tests.UIJourneys.exe
```

They ask once before taking the machine (`NEXAFLOW_UITESTS_NOPROMPT=1` skips it), and `UiTestGate` holds a
machine-wide semaphore so only one app instance runs at a time even if another test host is live.

`Tests.Core`'s own WPF tests split by **what they need, not what they touch**, because the two behave
completely differently under a parallel run:

| Category | Needs | Parallel-safe |
|---|---|---|
| `TestCategory("UI")` | an STA thread; constructs and renders WPF off-screen, opens no window | yes |
| `TestCategory("Desktop")` | an interactive session; **shows a real window and takes focus** | **no** — must carry `[DoNotParallelize]` |

Focus is machine-wide, so two window-showing tests running at once take it from each other mid-assertion.
That surfaces as a *different* test failing on each run, which reads like a real bug and is not one.
`DesktopTestCategoryGuardTests` enforces both halves — anything whose source shows a window must declare
`Desktop` and must not run in parallel — so the trap cannot be re-entered by forgetting an attribute.

A feature's tests go in the suite matching its **subject** — a viewer in `.Viewers`, a Windows integration in `.WindowsOS`, anything shell-adjacent in `.Features`. Namespaces are the same in all of them (`Nexaflow.Tests.Features.<Folder>`), so only the project a file belongs to changed.

**Fast inner loop for feature work:** build only the feature csproj you touched (features don't depend on Core), then build + run the one suite that owns it with `--filter "FullyQualifiedName~<Class>"` — that split is why editing a viewer test no longer rebuilds the Windows suite. Test output is under `bin/x64/<Config>/` (the solution is pinned x64 — a stray `bin/<Config>/` is a stale pre-pin leftover; delete it).

**Sample files.** `TestSampleData` (in `Nexaflow.Tests.Fixtures`) lazily materialises a git-ignored, cached dataset under `<repoRoot>/test-samples/` — markdown, tabular (csv/tsv), text (varied BOMs + line endings), json, logs, and binary fixtures. Generation is idempotent: a file is rewritten only when missing or drifted, so deleting `test-samples/` forces a clean rebuild. Use these instead of hand-curated machine-local sample folders. Add a new family by implementing `ISampleSet` and registering it in `TestSampleData.Sets`. Every sample file has a per-file UI test (`SampleFileViewerTests`) asserting it opens in the expected viewer. Details + the file→viewer map in [docs/testing.md](docs/testing.md).

## Potential WPF Gotchas

The global MenuItem style in src/Nexaflow.Core/Themes/Styles.xaml overrides the default WPF template. If you need submenus, header arrows, or Role-dependent behavior, extend that template — adding child MenuItems in code isn't enough.

ItemsControl.ItemsSource binding + Items.Add is illegal — pick one

ObservableCollection.Clear() + N × Add() fires N+1 CollectionChanged events — the intermediate state of "empty" can render as a blank frame if anything in the view rebuilds on each event. Batch updates via Dispatcher.BeginInvoke

A bare string assigned to ToolTip inherits the parent's TextAlignment when WPF wraps it in the default popup TextBlock. Assign an explicit TextBlock if you care about alignment

**`AutomationProperties.AutomationId` is only reliable on elements that create an `AutomationPeer`** — `Control` subclasses (TextBox, TabItem, Button, ContentControl, TabControl…). Decorators (`Border`) and panels (`Grid`, `StackPanel`) create none by default, so an id set on one may never resolve: `FindFirstDescendant(ByAutomationId(…))` returns null forever. It is *unpredictable* rather than always-absent — `Pdf_Panel` on a Border never appears, while `TabItem_{PageKind}` (also a Border, set in `TabStrip.xaml.cs`) does — so treat an id on a non-control as unusable regardless of whether it happens to work today. The failure is nastier than a red test: the *inverse* assertion ("hidden → the id is null") passes for the wrong reason, so a toggle test reads green while testing nothing. To assert a container is shown/hidden, assert on a real control inside it — collapsing the container removes its children from the tree too. Don't reshape the visual tree just to host an id.

**A WPF `TextBox` publishes its text through the UIA `Value` pattern, not its `Name`.** Anything rendered as a selectable/copyable TextBox (the PDF panel's property rows, for instance) is invisible to `ByName`/`element.Name` — read `element.Patterns.Value` (or `AsTextBox().Text`) instead. A test searching by name for a value it can see on screen is the usual symptom.

A UIElement embedded in a RichTextBox (BlockUIContainer) does not reliably receive mouse events, and the routed event's OriginalSource over it is unreliable too — the text container attributes clicks to the container, the FlowDocument, or even a NEIGHBOURING Paragraph/Run depending on the region. To give an embedded element its own mouse interaction, hook the RichTextBox's Preview event, find the element with a geometric VisualTreeHelper.HitTest, and drive it directly (see IInteractiveBlock + InlineMarkdownEditor/SelectableMarkdownView)

## Other design considerations

**Large-file reading** — there are four established strategies; pick the one whose access pattern matches your data shape before inventing a fifth. Each reader's *strategy* is deliberately feature-specific (the data structure differs). The mechanical leaves now live in `Nexaflow.IO.Common`: `EncodingDetector` (BOM/UTF-8 sniff — Tabular's detector, the canonical one) and `FileChangeWatcher` (the debounced `FileSystemWatcher` wrapper used by Logs and Text). Reuse those rather than re-rolling them — current architectural findings live in [docs/arch_review_2026-07.md](docs/arch_review_2026-07.md).

| Strategy | Canonical reader | When |
|----------|------------------|------|
| Tail-first + background head-load | `Logs/ViewModels/LogViewModel.cs` | append-only files where the recent end matters most |
| Head-first window + placeholder padding for scrollbar | `Text/ViewModels/TextViewModel.cs` | top-of-file-first text; line index built up front |
| Full-rescan per window (no byte anchors) | `Tabular/RowWindowReader.cs` | row data; `StreamReader` buffering makes `BaseStream.Position` unreliable for cross-call seeks |
| Seek-by-item via byte-offset index | `Json/JsonFileLoader.cs` | random access to structured items |

**Shared UI & theming** — shared controls live in `Nexaflow.Visuals.Common` (controls + converters) and `Nexaflow.Visuals.Text` (markdown). Theme brushes **and** shared control styles live in the app-merged `src/Nexaflow.Core/Themes/Styles.xaml`; feature XAML pulls them by `{StaticResource <key>}` with no assembly reference (the resource lookup walks up to `Application.Resources`). Put a new *shared* style there and reference it by key rather than copy-pasting per view. A theme is assembled in layers (palette → region tokens → per-theme overrides + scenes → styles) by `ThemeManager`; a region can carry an animated backdrop via `ThemedRegion` + a `Scene.{Region}` template, and a feature can extend theming without Core referencing it via `IThemeContribution` — full model in [docs/theming.md](docs/theming.md). Markdown rendering is centralised in `Nexaflow.Visuals.Text` (`SelectableMarkdownView`) — used by Core's AI overlay and AIChat; reuse it rather than hand-rolling `RichTextBox`.

## Style Notes

- Terse commits. Say why it changed, not what.
- Prefer clean architecture over simplicity for an action.
- Design for good structure and then trust the structure to make life easy - if its hard or convoluted or the structure is wrong.
- Trust the DI.
- Primary MVVM Toolkit patterns: `[ObservableProperty]`, `[RelayCommand]`, constructor injection. No static singletons in feature ViewModels.

## Working With the User

- Direct, short questions and terse commit-style explanations preferred.
- One concrete recommendation over a list of options.
- When something needs a worktree (feature branch), use one; merge via PR.
- Session transcripts are not searchable — check [docs/Architecture.md](docs/Architecture.md) and if implementing a feature [docs/features.md](docs/features.md) for context before exploring the codebase.