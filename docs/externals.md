# External source dependencies (git submodules) — runbook

**Audience: me (the AI assistant).** If the user points me at this file, this is everything I need to
work with Nexaflow's third-party source forks correctly — both **building Nexaflow against them** and
**posting clean atomic PRs upstream** — without asking for a more detailed prompt. Read the whole file
before touching anything under `external/`.

Nexaflow consumes four third-party dependencies **from source**, each via a fork the `smile-forge` org
controls. Three are .NET libraries wired in with `ProjectReference` (never `PackageReference`); the fourth
(**tree-sitter-xml**) is a **native C grammar** and is the documented exception to that rule — there is no
`.csproj` to reference, so it is *compiled* by an MSBuild target instead (see **Native grammar submodules**
below). This lets Nexaflow build against our version while keeping upstream sync and PRs working.
**Never vendor/copy this source into the tree — submodules only.**

**tree-sitter-dotnet-bindings is both kinds at once**, and worth understanding before touching it: its
`src/TreeSitter.csproj` is a normal `ProjectReference`, while the tree-sitter **runtime and every grammar**
are nested submodules of C source that *we* compile. It replaced the `TreeSitter.DotNet` NuGet package
because the package's ~30 prebuilt natives freeze at whatever its own submodules pointed at when it was
published — and they had gone stale enough to be wrong. Its C# grammar predated collection expressions and
slice patterns, so `= []` and `[.. var rest]` were parse errors; one slice pattern cost
`src/Nexaflow.Services.Initiatives.Cli/Program.cs` its **entire** parse (root `ERROR`, no type nodes),
making a 1,900-line file with ~90 methods invisible to the knowledge graph and the outline pane, with no
exception or warning anywhere. Seven of the fourteen grammars Nexaflow registers were behind upstream.
Upstream has been quiet for about a year and has no other maintainers, so treat our `nexaflow` branch as
the long-term home rather than a staging area for PRs that may never be reviewed.

---

## Golden rules (read first, don't violate)

1. **`git -C <path>` for EVERY git command inside a submodule.** The desktop app pins the shell's working
   directory to the first folder used, so `cd` into a submodule does **not** take effect. Always
   `git -C <repo>/external/<name> …`.
2. **`origin` = the `smile-forge` fork. `upstream` = the original repo.** The committed `.gitmodules` URL
   is the fork, so the whole org (not one person) can push branches and open PRs.
3. **The fork's default branch stays pristine** — it tracks upstream and NEVER receives our commits. Only
   fast-forward it from `upstream`.
4. **Every upstream-bound change lives on its own atomic feature branch off the upstream default branch.**
   Focused and rebaseable — that's what becomes a PR.
5. **`nexaflow` is the per-repo integration branch.** It merges our in-flight feature branches together and
   is the branch the submodule *tracks* (`git submodule set-branch`). **Nexaflow's build pins a commit on
   (or reachable from) `nexaflow`. NEVER open a PR from `nexaflow`** — PRs come from the single-topic
   feature branches only.
6. **Pin by commit; commit the pointer.** The Nexaflow superproject records a specific submodule commit
   (the gitlink) plus `.gitmodules`. Bumping the dependency = moving that gitlink and committing it.
7. **Keep diffs PR-clean.** Don't reformat or touch upstream source you aren't deliberately changing.
8. **Opening a PR / pushing is outward-facing — confirm with the user before `gh pr create` or any push.**

---

## The registry (the per-submodule facts)

`<repo>` below = the Nexaflow checkout root you're working in (the main checkout **or** a worktree —
submodule working trees are per-worktree; `.gitmodules` and the pinned commit are shared via git).

| | **xaml-math** (aka "wpf-math") | **DiscUtils** | **tree-sitter-xml** |
|---|---|---|---|
| Submodule path | `external/xaml-math` | `external/DiscUtils` | `external/tree-sitter-xml` |
| `origin` (committed, our fork) | `https://github.com/smile-forge/xaml-math.git` | `https://github.com/smile-forge/DiscUtils.git` | `https://github.com/smile-forge/tree-sitter-xml.git` |
| `upstream` (original) | `https://github.com/ForNeVeR/xaml-math.git` | `https://github.com/LTRData/DiscUtils.git` | `https://github.com/tree-sitter-grammars/tree-sitter-xml.git` |
| **Upstream default branch** | **`master`** | **`LTRData.DiscUtils-initial`** ⚠ not `master`/`main` | **`master`** |
| Tracked integration branch | `nexaflow` | `nexaflow` | `nexaflow` |
| In-flight feature branches | `feat/matrix-delimiter-environments` (LaTeX matrix delimiter env work) | *(none yet)* | *(none yet)* |
| Nexaflow consumer project | `src/Nexaflow.Visuals.Text/Nexaflow.Visuals.Text.csproj` | `src/Nexaflow.Features/Nexaflow.Features.VirtualDisk/Nexaflow.Features.VirtualDisk.csproj` | `src/Nexaflow.Syntax/Nexaflow.Syntax.csproj` |
| Wired project(s) | `external/xaml-math/src/WpfMath/WpfMath.csproj` (replaced the `WpfMath` NuGet). `XamlMath.Shared` comes in **transitively** — don't add it explicitly. | `Library/DiscUtils.Core`, `Library/DiscUtils.Containers`, `Library/DiscUtils.FileSystems` (Containers/FileSystems each pull the format libs). | **None — no `ProjectReference`.** The `BuildTreeSitterXml` target in `Nexaflow.Syntax.csproj` compiles `xml/src/{parser,scanner}.c` via `tools/build-tree-sitter-xml.ps1`. |
| Do **NOT** reference | `AvaloniaMath*`, `*.ApiTest*`, `*.Example`, `Tool.TTFMetrics` | Test/Utilities projects; individual format libs (use the `Containers`/`FileSystems` meta-projects) | The `dtd/` grammar (we only build `xml/`); the Rust/Node/Swift/Python bindings |
| Notes | `ForNeVeR/wpf-math` **redirects to `xaml-math`** (renamed). WpfMath is one project inside a multi-project repo. Multi-targets `net462;net8.0-windows` → consumers resolve `net8.0-windows`. Repo ships its own `Directory.Build.props` + CPM `Directory.Packages.props`. | Default branch is unusual — **base feature branches off `LTRData.DiscUtils-initial`** unless the upstream PR guide says otherwise. Library projects multi-target incl. `net10.0`. `SignAssembly=false` (the `SigningKey.snk` isn't needed). `.gitmodules` sets **`ignore = untracked`** (see gotchas). | Supplies the `xml` grammar for `.xaml`/`.xml`/`.xsl` (TreeSitter.DotNet bundles ~30 natives but no XML one). MIT. Sources are self-contained — `xml/src/tree_sitter/*.h` are vendored, and `scanner.c` includes `../../common/scanner.h`, so the `xml/` + `common/` layout must be preserved. Needs the **MSVC C toolchain**. Objects go to `src/Nexaflow.Syntax/obj/native/`, never into the submodule — so this one needs **no** `ignore = untracked`. |

…and the fourth, kept in its own table because its wiring is a hybrid the columns above can't express
(a `ProjectReference` **plus** nested C submodules we compile):

| | **tree-sitter-dotnet-bindings** |
|---|---|
| Submodule path | `external/tree-sitter-dotnet-bindings` |
| `origin` (committed, our fork) | `https://github.com/smile-forge/tree-sitter-dotnet-bindings.git` |
| `upstream` (original) | `https://github.com/mariusgreuel/tree-sitter-dotnet-bindings.git` |
| **Upstream default branch** | **`main`** |
| Tracked integration branch | `nexaflow` |
| In-flight feature branches | *(none — see the note on upstream being dormant)* |
| Nexaflow consumer project | `src/Nexaflow.Syntax/Nexaflow.Syntax.csproj` |
| Wired project(s) | `external/tree-sitter-dotnet-bindings/src/TreeSitter.csproj` (the managed bindings; **replaced the `TreeSitter.DotNet` PackageReference**). The natives are **not** built by that project — see below. |
| Do **NOT** reference | `tests/`, `examples/`. Don't build the repo's `tree-sitter-native/*.vcxproj`: they'd need all 29 grammar submodules and produce languages Nexaflow never registers. |
| Nested submodules | **29 grammars + the tree-sitter runtime**, under `tree-sitter-native/`. Only the ones in `tools/tree-sitter-grammars.props` (plus the runtime) are initialised — `tools/ensure-submodules.ps1` reads that manifest. They're shallow (`--depth 1`); the pin still resolves because it's a commit on a branch the remote advertises. |
| Our commits on `nexaflow` | (1) the grammar/runtime pin bumps; (2) an **empty `Directory.Build.props` + `.targets`** at the repo root. The second is integration-only and must never be upstreamed: the repo has none of its own, so without it MSBuild's walk-up would reach Nexaflow's root and silently apply our `Platforms=x64` and `NoWarn` to upstream's projects. |
| Notes | The reason this exists at all is in the intro: the package's prebuilt grammars go stale and one of them silently deleted a whole file from the graph. **Grammar submodules point at their own upstreams** (`tree-sitter/tree-sitter-*`), not at `smile-forge` forks — we bump pins, we don't patch grammar source. If a grammar ever *does* need patching, fork that one repo then. Needs the **MSVC C toolchain**. |

To read the *current* pinned commits and tracked branch at any time:

```bash
git -C <repo> submodule status                 # <sha> external/<name> (<describe>)
cat <repo>/.gitmodules                          # url + branch (+ ignore) per submodule
git -C <repo>/external/<name> remote -v         # confirm origin=smile-forge, upstream=original
```

---

## Mental model (branch topology, per submodule)

```
upstream/<default>  ──●──●──●   (pristine; origin/<default> mirrors it; never our commits)
                       \   \
        feat/topic-A ●──●   \       each single-topic branch = one upstream PR
        feat/topic-B         ●──●   (both off upstream/<default>)
                              /
        nexaflow  ●──●──●──●─┘       integration: merges feat/* ; Nexaflow pins a commit here
```

- Nexaflow's build follows the **pinned commit** (usually the `nexaflow` tip).
- A PR is `smile-forge:feat/topic  →  <upstream-owner>:<default>` — one topic, rebaseable, never `nexaflow`.

---

## Recipes

### 0. Fresh checkout / submodules look empty
```bash
git -C <repo> submodule update --init --recursive
```
(In a new worktree the submodule working trees may need this too.)

**This is normally automatic — a new worktree self-populates.** Because the `external/*`
submodules are wired as `ProjectReference`s, an empty submodule directory means the referenced
`.csproj` doesn't exist and Visual Studio / `dotnet build` fails to resolve it. Two committed
safety nets fix this so "create a worktree, open VS, hit F5" just works:

- **`.githooks/post-checkout`** runs after `git worktree add` (and any checkout) and initialises
  any *uninitialised* submodule via `tools/ensure-submodules.ps1` — before the IDE opens. It only
  touches submodules that aren't checked out, so it never resets one you're editing. Install it
  **once per clone** with `pwsh tools/install-hooks.ps1` (it copies the committed hooks into the
  shared `.git/hooks` that every worktree uses, and clears any stale `core.hooksPath`). A relative
  `core.hooksPath` is deliberately *not* used — git resolves it against the invoking checkout, so it
  wouldn't fire for a freshly-added worktree.
- **`EnsureSubmodulesInitialized`** in `Directory.Build.targets` is a build-time self-heal: if a
  submodule `.csproj` is still missing when a build starts, it runs the same helper before project
  references are resolved. Covers clones where the hook path wasn't set, and CI. (Opt out with
  `-p:NexaflowSkipSubmoduleInit=true`.) The submodule projects have their own `Directory.Build.*`,
  so this target never runs inside them — only for Nexaflow's own projects.

`tools/ensure-submodules.ps1` is safe to run by hand anytime; it's a no-op once submodules exist.

### 1. I'm only building Nexaflow (not changing external source)
Nothing special — the pinned commit is already checked out. Just build (see **Verify** below). If the
submodule folder is empty, run recipe 0.

### 2. Make a change I intend to upstream (THE core PR flow)
Example: change xaml-math (`upstream/master`). For DiscUtils swap `master` → `LTRData.DiscUtils-initial`.
```bash
SM=<repo>/external/xaml-math
git -C "$SM" fetch upstream
git -C "$SM" switch -c feat/<topic> upstream/master     # atomic branch off the pristine default
# …edit files under $SM, keeping the diff minimal and PR-clean…
git -C "$SM" add -A
git -C "$SM" commit -m "<focused message>"
# push to OUR fork (confirm with user first — outward-facing):
git -C "$SM" push -u origin feat/<topic>
# open the PR into UPSTREAM from our fork's branch (confirm with user first):
gh pr create --repo ForNeVeR/xaml-math --base master \
             --head smile-forge:feat/<topic> --title "…" --body "…"
```
Keep one concern per branch/PR. If several concerns, make several `feat/*` branches.

### 3. Get an in-flight change into Nexaflow's build (integration + repin)
Nexaflow builds the pinned commit, so merge the feature branch into `nexaflow`, then move the pin.
```bash
SM=<repo>/external/xaml-math
git -C "$SM" switch nexaflow
git -C "$SM" merge --no-ff feat/<topic>        # integrate (nexaflow is allowed to hold merges)
git -C "$SM" push origin nexaflow              # (confirm push with user)
# now the submodule HEAD (nexaflow tip) is the new commit — record it in Nexaflow:
git -C <repo> add external/xaml-math           # stages the new gitlink
git -C <repo> commit -m "Bump xaml-math: <topic>"
```
Rule of thumb: `nexaflow` = "what Nexaflow builds against right now" = union of our merged-but-maybe-not-yet-
upstreamed work. When a PR merges upstream, you can later drop that branch from `nexaflow` and repin to a
plain `upstream/<default>` commit that now contains it.

### 4. Keep the fork's default branch pristine / current with upstream
```bash
SM=<repo>/external/xaml-math
git -C "$SM" fetch upstream
git -C "$SM" switch master                     # DiscUtils: LTRData.DiscUtils-initial
git -C "$SM" merge --ff-only upstream/master   # fast-forward ONLY — never a real merge/commit here
git -C "$SM" push origin master                # (confirm push)
```
If `--ff-only` fails, someone put commits on the default branch — that violates the convention; investigate
rather than force it.

### 5. Rebase a feature branch onto the latest upstream (before/for a PR)
```bash
SM=<repo>/external/xaml-math
git -C "$SM" fetch upstream
git -C "$SM" rebase upstream/master feat/<topic>
git -C "$SM" push --force-with-lease origin feat/<topic>
```

### 6. Bump to a newer upstream release/commit
Move `nexaflow` to include the upstream target, then repin (recipe 3 shape):
```bash
SM=<repo>/external/xaml-math
git -C "$SM" fetch upstream
git -C "$SM" switch nexaflow
git -C "$SM" merge --ff-only upstream/master   # or merge a specific tag, e.g. v2.2.0
git -C "$SM" push origin nexaflow
git -C <repo> add external/xaml-math && git -C <repo> commit -m "Bump xaml-math to <ref>"
```
Then **rebuild the consumer** and fix any API drift in Nexaflow (see Verify). WpfMath master already runs
244+ commits ahead of the 2.1.0 NuGet — expect possible API differences on big bumps.

### 7. Add a brand-new external dependency (repeat the convention identically)
```bash
# 7a. Fork into the org (matches Nexaflow's smile-forge ownership):
gh repo fork <owner>/<repo> --org smile-forge --clone=false
# 7b. Add the submodule under external/ with origin = the org fork:
git -C <repo> submodule add https://github.com/smile-forge/<repo>.git external/<repo>
# 7c. Add + fetch upstream:
git -C <repo>/external/<repo> remote add upstream https://github.com/<owner>/<repo>.git
git -C <repo>/external/<repo> fetch upstream
# 7d. Integration + any feature branch off the UPSTREAM DEFAULT (check what it actually is!):
DEF=$(git -C <repo>/external/<repo> rev-parse --abbrev-ref origin/HEAD | sed 's|origin/||')
git -C <repo>/external/<repo> branch nexaflow upstream/$DEF
git -C <repo>/external/<repo> switch nexaflow
git -C <repo>/external/<repo> push -u origin nexaflow
# 7e. Track nexaflow, then STAGE .gitmodules AGAIN (set-branch edits it but doesn't stage — see gotchas):
git -C <repo> submodule set-branch --branch nexaflow external/<repo>
git -C <repo> add .gitmodules external/<repo>
git -C <repo> commit -m "Add <repo> source submodule, tracking nexaflow"
# 7f. Wire only the needed project(s) via ProjectReference (see Wiring), build, then add a docs row + a
#     forward-looking product-tree node, and add a registry row to THIS file.
```

---

## Wiring rules (consuming the source in Nexaflow)

### Native grammar submodules (the `ProjectReference` exception)

`tree-sitter-xml` is a **C** grammar, not a .NET library — there is no `.csproj`, so rule "`ProjectReference`,
never `PackageReference`" cannot apply. It is instead **compiled from source on build**. Everything else about
the submodule convention (org fork, `upstream` remote, `nexaflow` integration branch, pin-by-commit, atomic
`feat/*` branches for upstream PRs) is unchanged.

- **Who builds it:** the `BuildTreeSitterNatives` target in `src/Nexaflow.Syntax/Nexaflow.Syntax.csproj`,
  which shells out to `tools/build-tree-sitter-natives.ps1`. That one script builds **everything native**:
  the tree-sitter runtime (`lib/src/lib.c`, exporting `ts_*` via the fork's `tree-sitter.def`) and every
  grammar in `tools/tree-sitter-grammars.props`. One `vcvars64` session for the whole batch, because
  starting it costs more than most of the compiles; ~16s cold, nothing when warm (each artefact is
  timestamp-checked against its own sources).
- **`tools/tree-sitter-grammars.props` is the single source of truth for the set** — the csproj imports it
  for the `Content` items, the build script reads it to know what to compile, and `ensure-submodules.ps1`
  reads it to know which nested submodules a fresh worktree needs. Adding a language is one row.
- **Object files go to a folder per artefact** (`obj/native/int/<name>/`). Not cosmetic: every grammar
  compiles a file called `parser.c`, so a shared `/Fo` directory would have them overwrite each other's
  `parser.obj`.
- **`/O1`, not `/O2`.** These are generated table-driven parsers and some are enormous — the C# grammar's
  `parser.c` is 31 MB — where `/O2` costs minutes for no measurable parse-time gain.
- **Where the output goes:** `src/Nexaflow.Syntax/obj/native/tree-sitter*.dll`, flowed to every consuming
  project's **output root** by `Content` items carrying `TargetPath` + `CopyToOutputDirectory="PreserveNewest"`
  (`GetCopyToOutputDirectoryItems` recurses through `ProjectReference`s, so it reaches the app two hops out via
  `Syntax -> Visuals.Text -> Core`). Note the app's real `OutDir` is the RID subfolder
  `bin/x64/Debug/<tfm>/win-x64/` - not the `<tfm>` folder above it.
- **The output root is required, and for two different reasons.** A *grammar* is resolved by id through the
  bindings' own loader, which probes `AppContext.BaseDirectory` **before** `runtimes/<rid>/native/` — that
  ordering is what let a hand-built grammar override a packaged one while the NuGet package was still in
  play. The *runtime* is different: it is a plain `[DllImport("tree-sitter")]` with no custom resolver, so
  it goes through .NET's default probing and a packaged `deps.json` asset would have won. Overriding the
  runtime was therefore impossible without dropping the package — which is the concrete reason the
  `PackageReference` had to go rather than merely being tidier.
- **Never write build output into the submodule.** Object files go to `obj/native/`, which is why this
  submodule needs no `ignore = untracked` (contrast DiscUtils).
- **Prerequisite:** the MSVC C toolchain (VS "Desktop development with C++", component
  `Microsoft.VisualStudio.Component.VC.Tools.x86.x64`, or Build Tools). The script fails with an explicit
  install instruction if it is missing.
- **Adding another grammar** later: add one row to `tools/tree-sitter-grammars.props` (id + `SourceDir`,
  plus `Root` only if it lives outside the bindings submodule, as `xml` does). The build script, the
  `Content` items and the submodule bootstrap all follow from that row. A brand-new *submodule* still needs
  a sentinel path in `Directory.Build.targets`' `EnsureSubmodulesInitialized` condition.
- **A stale grammar has no natural alarm**, which is why `GrammarParseHealthTests` asserts that every `.cs`
  and `.xaml` file under `src/` parses, and names the C# features this repo actually relies on. Before that
  guard existed, a grammar too old for the language silently produced *absence* — and absence reads as a
  fact about the code, not about the parser. `CodeOutline.ParseFailed` is what makes it assertable.


- **`ProjectReference`, never `PackageReference`** for these deps. Match how Nexaflow references its own
  projects: a plain relative-path include, e.g.
  `<ProjectReference Include="..\..\external\xaml-math\src\WpfMath\WpfMath.csproj" />`
  (feature projects are one level deeper, so `..\..\..\external\…`).
- Reference only the **minimal** project(s); let meta-projects (`XamlMath.Shared`, DiscUtils
  `Containers`/`FileSystems`) bring the rest transitively.
- Submodule projects build with **their own** `Directory.Build.props` / Central Package Management — the
  MSBuild walk-up finds theirs and stops before Nexaflow's root, so they don't inherit Nexaflow's `x64`
  (they build AnyCPU managed IL, which the x64 host consumes fine). Nexaflow has no `Directory.Packages.props`,
  so there's no CPM collision.
- **A new submodule ⇒ add a sentinel path** to the `EnsureSubmodulesInitialized` condition in
  `Directory.Build.targets` (a file that only exists once the submodule is checked out), or a fresh
  worktree won't self-populate it.
- **New `src/**` project ⇒ add it to `Nexaflow.slnx`** (guard: `SolutionMembershipTests`). Submodule
  projects under `external/` are **exempt** — they build transitively via ProjectReference and must NOT be
  added to the solution.
- A new consuming feature needs the full add-feature wiring (Core + Tests `ProjectReference`, a test class,
  slnx membership, product-tree node) or the architecture guards go red. See `.claude/skills/add-feature`.

---

## Gotchas / traps I have actually hit

- **cwd is pinned** → `git -C` everywhere (rule 1). This is the #1 source of "why did nothing happen".
- **DiscUtils' default branch is `LTRData.DiscUtils-initial`**, not `master`/`main`. Base branches off the
  real default (recipe 7d computes it via `origin/HEAD`).
- **wpf-math was renamed to xaml-math.** `ForNeVeR/wpf-math` redirects. The WpfMath project lives inside the
  multi-project `xaml-math` repo; our fork is `smile-forge/xaml-math`.
- **`.gitmodules` staging trap:** `git submodule set-url` / `set-branch` edit `.gitmodules` in the working
  tree but do **not** stage it. Always `git add .gitmodules` again right before committing, or you'll commit
  a stale URL/branch (I did this once — the committed URL still pointed at a personal fork).
- **DiscUtils dirties the superproject on every build.** Its `OutputPath` writes DLLs to `Library/<Config>/`,
  which upstream's `.gitignore` doesn't cover. `.gitmodules` sets **`ignore = untracked`** for it so build
  artifacts don't show as submodule changes — while real source edits and pin changes still do. Don't remove
  that; add the same to any future submodule with the same behavior. (xaml-math needs none — its output goes
  to gitignored `bin/obj`.)
- **`.product` tree lives only in the main checkout** (untracked, not in worktrees). When adding a feature's
  product-tree node, run the CLI against the main checkout root:
  `dotnet run --project src/Nexaflow.Services.Initiatives.Cli -- add-node features "<Title>" d:/codedev/nexaflow`.
  That edit is local to main — it is NOT part of a worktree's branch/PR.
- **Forks are org-owned (`smile-forge`), not personal.** If a fork is accidentally created under a personal
  account, re-fork with `--org smile-forge` and `git -C <repo> submodule set-url <path> <org-url>` +
  `git -C <repo> submodule sync`.
- **Never PackageReference these** and **never open a PR from `nexaflow`.**

---

## Verify (after any change to externals or their pin)

```powershell
# xaml-math source → the WpfMath consumer (catches WpfMath API drift in BlockRenderer.cs):
dotnet build src/Nexaflow.Visuals.Text/Nexaflow.Visuals.Text.csproj -c Debug

# DiscUtils source → the VirtualDisk consumer:
dotnet build src/Nexaflow.Features/Nexaflow.Features.VirtualDisk/Nexaflow.Features.VirtualDisk.csproj -c Debug

# Whole app against both source submodules:
dotnet build Nexaflow.slnx -c Debug

# Architecture/wiring guards (run FIRST when a consuming feature changed):
dotnet build src/Nexaflow.Tests/Nexaflow.Tests.Features.Architecture/Nexaflow.Tests.Features.Architecture.csproj -c Debug
& "src/Nexaflow.Tests/Nexaflow.Tests.Features.Architecture/bin/x64/Debug/net10.0-windows10.0.19041.0/Nexaflow.Tests.Features.Architecture.exe"
```

Expect **0 errors**. Pre-existing warnings are fine (Core `MVVMTK0045`, `MSTEST0044`, and upstream
XamlMath.Shared nullable warnings) — don't "fix" upstream warnings; keep diffs PR-clean.

Finally: confirm the working tree is clean and the pins are what you intend —
`git -C <repo> status` and `git -C <repo> submodule status`.
