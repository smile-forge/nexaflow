# External source dependencies (git submodules) — runbook

**Audience: me (the AI assistant).** If the user points me at this file, this is everything I need to
work with Nexaflow's third-party source forks correctly — both **building Nexaflow against them** and
**posting clean atomic PRs upstream** — without asking for a more detailed prompt. Read the whole file
before touching anything under `external/`.

Nexaflow consumes two third-party libraries **from source**, each via a fork the `smile-forge` org
controls, wired in with `ProjectReference` (never `PackageReference`). This lets Nexaflow build against
our version while keeping upstream sync and PRs working. **Never vendor/copy this source into the tree —
submodules only.**

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

| | **xaml-math** (aka "wpf-math") | **DiscUtils** |
|---|---|---|
| Submodule path | `external/xaml-math` | `external/DiscUtils` |
| `origin` (committed, our fork) | `https://github.com/smile-forge/xaml-math.git` | `https://github.com/smile-forge/DiscUtils.git` |
| `upstream` (original) | `https://github.com/ForNeVeR/xaml-math.git` | `https://github.com/LTRData/DiscUtils.git` |
| **Upstream default branch** | **`master`** | **`LTRData.DiscUtils-initial`** ⚠ not `master`/`main` |
| Tracked integration branch | `nexaflow` | `nexaflow` |
| In-flight feature branches | `feat/matrix-delimiter-environments` (LaTeX matrix delimiter env work) | *(none yet)* |
| Nexaflow consumer project | `src/Nexaflow.Visuals.Text/Nexaflow.Visuals.Text.csproj` | `src/Nexaflow.Features/Nexaflow.Features.VirtualDisk/Nexaflow.Features.VirtualDisk.csproj` |
| Wired project(s) | `external/xaml-math/src/WpfMath/WpfMath.csproj` (replaced the `WpfMath` NuGet). `XamlMath.Shared` comes in **transitively** — don't add it explicitly. | `Library/DiscUtils.Core`, `Library/DiscUtils.Containers`, `Library/DiscUtils.FileSystems` (Containers/FileSystems each pull the format libs). |
| Do **NOT** reference | `AvaloniaMath*`, `*.ApiTest*`, `*.Example`, `Tool.TTFMetrics` | Test/Utilities projects; individual format libs (use the `Containers`/`FileSystems` meta-projects) |
| Notes | `ForNeVeR/wpf-math` **redirects to `xaml-math`** (renamed). WpfMath is one project inside a multi-project repo. Multi-targets `net462;net8.0-windows` → consumers resolve `net8.0-windows`. Repo ships its own `Directory.Build.props` + CPM `Directory.Packages.props`. | Default branch is unusual — **base feature branches off `LTRData.DiscUtils-initial`** unless the upstream PR guide says otherwise. Library projects multi-target incl. `net10.0`. `SignAssembly=false` (the `SigningKey.snk` isn't needed). `.gitmodules` sets **`ignore = untracked`** (see gotchas). |

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
