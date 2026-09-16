# Nexaflow — Claude Context

Nexaflow is a WPF shell replacement for Windows — file explorer, terminal, editors, viewers, project management and
an AI assistant in one tabbed window. `.NET 10 / WPF / MVVM Community Toolkit`; solution `Nexaflow.slnx`, pinned x64.

This file is what every turn needs. Depth lives in the docs below — open one when the work enters its territory.

| When the work involves… | Read |
|---|---|
| the product tree — its shape and granularity, concerns, snaplinks, `validate` / `remap` / `pending` / `promote`, what the graph can answer | [docs/product-graph.md](docs/product-graph.md) |
| writing tests — usually once the change works: which suite, the journey + leaf-unit-test model, `[CoversNode]`, running them | [docs/testing.md](docs/testing.md) |
| scope (global / `Workspace` / `WorkspaceRuntime`), config paths + migration, elevation, contracts, large-file reading | [docs/Architecture.md](docs/Architecture.md) |
| a new feature or page kind | the `add-feature` skill · [docs/features.md](docs/features.md) |
| colours, styles, themes | [docs/theming.md](docs/theming.md) |
| anything under `external/` — **read first** | [docs/externals.md](docs/externals.md) |
| menus, AutomationIds, `TextBox` in UIA, `RichTextBox` hit-testing | [docs/wpf-gotchas.md](docs/wpf-gotchas.md) |
| strings, help pages, language packs | [docs/localization.md](docs/localization.md) |
| a Mermaid diagram — converting one onto the shared layout tree, or anything under `Markdown/Mermaid` or `Markdown/Graphs` | [docs/mermaid-diagrams.md](docs/mermaid-diagrams.md) · the `mermaid-diagram` skill |

## Build

```powershell
dotnet build Nexaflow.slnx
dotnet run --project src/Nexaflow.Core/Nexaflow.Core.csproj
```

## Layout

```
src/
  Nexaflow.Core/                      shell chrome, main window, ribbon, AI input bar, FeatureManager
  Nexaflow.Features/
    Nexaflow.Features.Common/         ALL contracts (interfaces + small DTOs) — no FeatureManager, that's Core
    Nexaflow.Features.<Feature>/      one assembly per feature (~30)
      …Compressed.{Modern,SecureZip,SharpCompress}/   codec backends — reference IO.Common ONLY
  Nexaflow.Visuals.Common/            shared WPF controls, converters, formatters
  Nexaflow.Visuals.Text/              markdown rendering — reuse SelectableMarkdownView, never hand-roll a RichTextBox
  Nexaflow.Visuals.Terminal/          terminal input logic
  Nexaflow.IO.Common/                 WPF-free IO leaves: encoding, glob, hashing, codec/archive contracts, VFS, file watching
  Nexaflow.IO.Terminal/               PTY host
  Nexaflow.Syntax/                    tree-sitter engine + StructuralEdit; grammars built from submodules (externals.md)
  Nexaflow.Services.Initiatives/      WPF-free product tree + graph backend — never references WPF/Core/Features
  Nexaflow.Services.Initiatives.Cli/  nfi
  Nexaflow.Elevation/                 Elevation.Contracts (DTOs) + PrivilegeBridge (the elevated exe)
  Nexaflow.Languages/                 one resource-only pack per language
  Nexaflow.Providers/                 Providers.Common + one project per LLM backend (Claude, Gemini, OpenAI, Ollama, Aria)
  Nexaflow.Tests/                     one suite per subject — docs/testing.md
```

The feature inventory is deliberately not here — it is the product tree: `nfi find <term>`, `nfi tree <id> --full`.

## nfi — how this repository is read and changed

Find with `ask`, change with `graph edit`, prove with `test`. Each answer carries what the next step needs: the ids an
edit takes, the errors an edit introduced and what uses what it changed, the tests that cover it. A check ends on one
verdict, clean or not, that names what it covered, with anything wrong listed beneath it —
`compile: no new errors in 2 file(s) — Nexaflow.Core (0.04s, all warm)`,
`0 node(s) - checked Nexaflow.Features.Json (41 C# file(s), 6 additional) by XamlAutomationIdAnalyzer`. That line is the
build's answer and needs no build behind it; `not checked` lines are what it could not give, and `nothing was checked`
is not a zero.

`$nfi` is `tools/graph-cli/nfi.exe` in the main checkout, or your own build at
`src/Nexaflow.Services.Initiatives.Cli/bin/x64/Debug/net10.0/nfi.exe`. It self-locates the tree (a worktree follows
its pointer to the main checkout), so it takes **no root argument**, and a resident process holds the graph between
calls — nothing to start or stop (`nfi daemon` says what it is doing). `nfi` alone lists every verb; `nfi graph help`
the graph ones. **In Git Bash, `export MSYS2_ARG_CONV_EXCL='*'` first** — MSYS rewrites anything that looks like a
path (a `// comment` passed as `--text` arrives as `/ comment`); payloads through `--file`/`--stdin` never touch the
shell.

### Find — `ask`

| You want | Run |
|---|---|
| where something is, what it is | `ask 'search <term>'` — falls back to a source search when nothing is *named* that |
| it, and its code | `ask 'search <term> \| source'` |
| everything about one node | `graph context <id>` — its source, neighbours, owning feature, and the grep for that feature |
| who uses it, and how | `ask 'node <id> \| callers \| source'` |
| what a type or a file offers | `ask 'node <id> \| members'` — every declaration with its signature, so no body has to be read to learn one |
| what the compiler or an analyzer reports | `ask 'diagnostics NXUI001 --project Features.Solver'` · for what you found: `ask 'node <id> \| diagnostics'` — a XAML finding comes with the `--at` path of its element |
| does anything still do X, and where | `ask 'grep <regex> \| files'` · just the number: `\| count` |
| a pattern inside one feature | `ask 'node product:<slug> \| owned \| grep <regex>'` |
| near this code | `ask 'node <id> \| near 2 \| grep <regex>'` |
| to narrow what you just found | `ask '@ \| like <regex> \| files'` — every answer ends with its handle; `@3` is the third |
| the rest of a long answer | `ask '@3 more'` — nothing is cut without the command that shows the rest |
| a block's text | `graph code <id> [--lines A-B]` · a file: `graph cat file:<relpath>` (past 400 lines, its outline; `--all` for the whole) |
| a feature from the tree | `find <term>` · `describe <id> [--code]` · `tree <id> [--full]` |

`ask` chains stages with `|`, the set of nodes flowing left to right, and **only the last stage prints**:

- **start** — `search <term>` / `grep <regex> [--from <id>] [--scope owned|hops] [--hops <n>]` / `node <id>[,<id>…]` /
  `diagnostics [<id-regex>] --project <name>` / `@` / `@<n>`
- **narrow** — `callers` / `callees` / `members` / `owned` / `near <n>` / `grep` / `diagnostics` / `like <regex>` / `limit <n>`
- **print** — `ids [n]` / `source [n]` / `blocks [n]` / `files` / `count`

Several questions are several quoted arguments — `ask 'search A | source' 'grep B | files'` — each answered in the one
call, sharing one page. Quote a regex holding a `|`; inside it a backslash keeps a quote or a bar literal (`grep "say \"hi\""`), and a
question awkward to quote goes on stdin (`@' … '@ | & $nfi ask --stdin`, one per line). Stages are strict: an unknown flag or an id the graph lacks is refused, and a zero
names the stage that emptied the set.

**An answer is the size of its question**, because every turn re-reads everything already printed. `ids` gives each
declaration's signature under its shared file path; `source` after `grep`/`diagnostics` gives the matched lines with
two either side, inside their declaration; `blocks` gives whole declarations; `files` and `count` are for sweeps.
Past about 20,000 characters an answer is cut where a block ends, with the `@n more` that continues it. A pattern
search is `grep` too, never `grep -rn` — every hit names its owning member and feature, it reads docs as well as code,
and `--limit` trims the printout, never the search.

The graph keeps up with your tree on its own — changed files are folded in before a query answers, and silence means
current. Each worktree has its own graph. Node ids: `product:<slug>` · `code:<relpath>#<astpath>` ·
`file:<relpath>` · `external:<name>`.

### Change — `graph edit`

What it guarantees, and why it is the only way files here are changed:

- **Addressed by what it is.** A declaration is found in the file as it is *now*; one that moved is re-found by name,
  and an edit is refused rather than guessed when the name is gone or several share it.
- **Nothing broken is written.** The result is re-parsed; `signature` proves the body unchanged and `body` the
  signature; a raw control character, a whole file handed to a declaration op, and a find that matches twice are refused.
- **Compiled before it is written.** A C# edit is compiled in memory against the project's own build, and the answer's
  `compile:` lines list the errors it **introduced and fixed** — in its project and every project compiling against
  it; errors already there cancel out. A XAML edit is put to the analyzers its project hands the view, before and after
  (`views:` — an NXUI001 fix shows as `fixed`); the markup itself is compiled only by a build. `--must-compile` refuses
  the write; `--no-check` skips it.
- **What it affects is in the answer.** A declaration whose outside changed (removed, renamed, re-signed) is followed
  by the compiler's binding: `impact:` names each user, and XAML that names it as text.
- **Several edits are one change** — `script` writes all the files or none.
- **Whitespace, endings, encoding and escaping are the tool's problem.** Write flush-left with `\n`; it lands indented
  for its destination with the file's endings and BOM. A single line written *with* leading whitespace is placed as
  written — that is how an aligned continuation is done.

| To | Run |
|---|---|
| replace, re-sign, re-body or delete a declaration | `replace` / `signature` / `body` / `delete <id>` (`replace` keeps the doc comment unless yours has a `<summary>`) |
| add beside one, or into a type | `insert-before` / `insert-after` / `append <id>` |
| change a few lines inside one member | `substitute <id> --find … --text …` — its doc comment too; literal unless `--regex`, once unless `--all` |
| rename it | `rename <id> --to N` — **`--references`** carries it to every use, override and implementation the compiler binds |
| move it | `move <id> --to code:<file>#<type>` · to its own file: `--to file:<path>` (imports and namespace come along) |
| a new file | `create <relpath> --file …` |
| a using | `import <file-or-id> --text 'using X;'` |
| something in no declaration, a doc, a text file | `substitute file:<relpath> --find …` |
| a XAML element, a project file | a XAML id (`T:`/`N:`/`K:`/`A:`) takes every op; `file:<path> --at "<xpath>"` any element, or `--at <line>:<column>` straight from a build warning; `set-attribute` / `remove-attribute --name` |
| several of the above | `script --stdin` |
| take it back | `undo` — the last edit, file for file; refused when anything changed since |
| see the result | `--show` prints the declaration as it now stands |

Text comes from `--text` (literal), `--text-escaped` (`\n`, `\t`, `\uXXXX`), `--file` or `--stdin`, and `--find` has
the same four. An edit is planned, parsed and compiled before it is written either way, so write it, read what it
printed, and `undo` if it is wrong — `--dry-run` only adds a call.

A script is one command per line, as it would follow `graph edit`, with multi-line text in blocks beneath, and a quoted
value's own quote written `\"`. Pass it on stdin so writing and running it are one call — with the PowerShell tool,
`@' … '@ | & $nfi graph edit script --stdin`. Not a bash heredoc: the Bash tool can run a command through `eval`, where a
heredoc holding an apostrophe fails to parse (`unexpected EOF while looking for matching '`). Failing both, write the
script to the scratchpad and pass `--file`:

```
substitute code:src/A.cs#T:A/M:Run
<<< find
Old(x);
>>>
<<< text
New(x, y);
>>>
move code:src/A.cs#T:Parser --to file:src/Parsing/Parser.cs
rename code:src/A.cs#T:A/M:Helper --to Assist --references
```

A find matches ignoring indentation and line endings, may start and end part-way along a line, and when it is not
there the refusal names where it is. `$1` is a backreference only with `--regex`. The `#N` in `T:C/M:Add#1` is that
overload's position, so an edit that adds or removes an overload says so — later edits re-list or pin with
`--expect`. `--with-trivia` stops at a blank line, so a section comment above a deleted member stays.

### Prove — `test`

`test <id>` builds and runs the tests that use the node, the tests of what uses it (three steps out, by the
compiler's binding) and the tests declaring `[CoversNode]` of its feature. `--list` shows the choice and why without
building; `test --failed` reruns exactly what failed. UI journey suites are named with their filter, never run for you.

## Hard Rules

**How work is done**

- **Discovery goes through `nfi` — never Grep/Glob/Read first.** "Where / what is X, who calls it, what owns it" and
  "which code looks like Y" are both `ask`; reading is `graph code` / `graph cat`, never hand-sliced line ranges.
  Chain a question into one `ask` rather than instalments. Sub-agents follow the same rule: point them at the exe,
  or spawn `nexaflow-explorer`.
- **Every change to a repository file goes through `nfi graph edit`** — never Edit/Write, `sed -i`, a heredoc or a
  redirect. Source, tests, docs, project files, this file. Only scratch files outside the repo are written otherwise.
- **A change is not complete until you've asked who depends on it**: read the edit's `impact:` / `compile:` lines,
  then `nfi test <id>`. A package bump: `graph node external:<Name>` names every consuming project. (NAudio is used by both
  the Audio feature and Core's `VoiceManager` — a grep of the feature you're in names only one.)
- **After touching `Nexaflow.Core`, run its unit tests before committing** (`Tests.Core`, `--filter
  "FullyQualifiedName~Unit"` — [testing.md](docs/testing.md#fast-inner-loop)).
- **Update the product tree as you build, not after merge** — concerns, snaplinks, descriptions. Snaplink edits on a
  branch land in `docs/product/pending/<branch>.json`; **commit it with the change**.
  [product-graph.md](docs/product-graph.md#snaplinks)

**Architecture** — the reference and dispatcher rules are enforced by `ArchitectureRulesTests` (in
`Tests.Features.Architecture` and `Tests.Providers`); `FeatureTouchPointTests` names any missed add-a-feature step.

- Features depend only on `Features.Common` and `Nexaflow.Visuals.*` — never Core, rarely each other. Providers depend
  only on `Providers.Common` — never Core, never each other.
- Core never instantiates feature views or view-models — all tab and viewlet creation goes through `FeatureManager`. A
  feature advertises a page with an `IPageRegistration` (`PageKind` + `CreatePage`, plus `static string
  StaticPageKind`), discovered by reflection.
- Features reach the shell only through `IShellServices`: **never touch the dispatcher** (`RunOnUiAsync`, and
  `WatchFile` for file watching) and **never elevate** (`RunElevatedAsync`, never `Process.Start` with `runas`).
- **Features never hard-code colours** — palette/semantic tokens (`TextBrush`, `AccentBrush`, `SuccessBrush`, …), the
  `Swatch.*` bank, or a feature-owned token shipped via `IThemeContribution`. → [theming.md](docs/theming.md)
- **Every button carries an `AutomationProperties.AutomationId`** (`NXUI001`), **and every id is named by a journey**
  (`AutomationIdJourneyCoverageTests`, a ratchet over `automation-ids-without-a-journey.txt` that can only shrink). Ids
  only resolve reliably on `Control`s, not `Border`s or panels. → [testing.md](docs/testing.md#automation-ids-nxui001--automationidjourneycoveragetests)
- **Third-party source deps are git submodules under `external/`** from the smile-forge fork, consumed by
  `ProjectReference` — never `PackageReference`, never vendored (native grammars compile via an MSBuild target
  instead). Read [externals.md](docs/externals.md) before touching one; use `git -C external/<name> …`.
- Shared non-contract code goes in `Nexaflow.Visuals.*` / `Nexaflow.IO.*` / `Nexaflow.Syntax`, never
  `Features.Common`.

## Scope

Feature settings = **global** (unless `[WorkspaceScopedConfig]`); persona, AI ability grid, provider configs,
conversations, ribbon, tabsets = **per-`Workspace`** (saved, under `%APPDATA%\Smile\nexaflow\Contexts\<name>\`);
`AIService`, provider instances, windows and tabs = **per-`WorkspaceRuntime`**. The `IShellServices` / `IAIService` a
feature gets are the active runtime's. The on-disk/IPC strings `workcontexts`, `Contexts\`
and `--context` are frozen compat names; `Profile` / `WorkContext` are never type or member names. Getting scope wrong is the easiest bug
to write: [Architecture.md → Ownership & Lifetime](docs/Architecture.md#ownership--lifetime).

## Key files

| File | Why you'd touch it |
|---|---|
| `src/Nexaflow.Core/ViewModels/ShellViewModel.cs` | tab lifecycle, ribbon, AI routing — god object, be careful |
| `src/Nexaflow.Core/Services/ShellServices.cs` | the `IShellServices` implementation |
| `src/Nexaflow.Core/FeatureManager.cs` | per-`WorkspaceRuntime` constructor injection for features |
| `src/Nexaflow.Core/Services/FeatureCatalog.cs` | cached feature discovery; Debug builds always rescan — never clear the cache by hand |
| `src/Nexaflow.Core/Services/WorkspaceManager.cs` | the workspace list + live runtimes: create / switch / reconfigure / dispose |
| `src/Nexaflow.Features/Nexaflow.Features.WindowsFileSystem/Services/FileSystemFeatureRegistry.cs` | discovery for `IFileAction` / `IFolderAction` / `IFileCreateAction` / `IFolderViewlet` / `IThisPcItemProvider` — not `FeatureManager` |
| `src/Nexaflow.Features/Nexaflow.Features.Common/*.cs` | contracts (`IPageRegistration`, `Page`, …) — changes here affect everything |
| `src/Nexaflow.Core/Themes/Styles.xaml` | app-merged shared styles and brushes, referenced from any feature by `{StaticResource …}` |

## Style

- Terse commits. Say why it changed, not what.
- Docs describe the present. No "used to", "no longer", "was moved", before/after stories or dated counts — history
  is git's. When something changes, rewrite the sentence as it now stands.
- Prefer clean architecture over simplicity for an action. Design for good structure, then trust it — if something is
  hard or convoluted, the structure is wrong.
- Trust the DI. MVVM Toolkit patterns: `[ObservableProperty]`, `[RelayCommand]`, constructor injection. No static
  singletons in feature ViewModels.

## Working with the user

- Direct, short questions; terse commit-style explanations.
- One concrete recommendation over a list of options.
- Feature-branch work goes in a worktree and merges via PR.
- Session transcripts are not searchable — the docs above are the memory.