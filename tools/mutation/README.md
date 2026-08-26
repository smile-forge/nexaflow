# Mutation testing

`Run-Mutation.ps1` drives [Stryker.NET](https://stryker-mutator.io/docs/stryker-net/introduction/) over one
Nexaflow leaf library. Mutation testing asks the question a green suite cannot: **if this line were wrong,
would any test notice?** Stryker rewrites one operator, literal or branch at a time, reruns the tests that
cover it, and records whether they went red.

**This is an occasional review tool, run by hand.** It is deliberately not wired into `dotnet build`, the
architecture guards, `ci.yml`, or the `NexaflowSetup.slnx` release gate — those are seconds-scale pass/fail
checks and a sweep here is minutes, for an answer that barely moves commit to commit. Run it when you are
thinking about a subsystem's test quality; read the survivors; decide.

```powershell
cd tools/mutation
./Run-Mutation.ps1                                    # list the targets
./Run-Mutation.ps1 -Target initiatives                # a full sweep of one
./Run-Mutation.ps1 -Target all -Since origin/main     # only what this branch changed
./Run-Mutation.ps1 -Cleanup                           # after an interrupted run
./Run-Mutation.ps1 -Target search -Concurrency 4      # leave yourself some machine
```

`-Concurrency` defaults to **half** your logical cores rather than Stryker's more aggressive default,
because this is a tool you leave running while you work.

Reports land in `artifacts/mutation/<target>/reports/` (gitignored). Read the HTML one; the JSON is what the
script's summary table is computed from.

Two numbers, two different problems:

- **Survived** — a test ran over that line and did not notice it change. Wants a sharper assertion.
- **Uncovered** — no test executes that line at all. Wants a test.

## Baselines

Recorded so a later run has something to be a regression *against*. Re-measure and update when you move one
deliberately; a drop you did not intend is the thing this table exists to make visible.

| Target | Score | Killed | Survived | Uncovered | Measured |
|--------|-------|--------|----------|-----------|----------|
| `initiatives` | **78.1%** | 1533 | 142 | 289 | 2026-08-26, 5m03s |

For scale on what moves it: adding `BlockEndTests`' seven escape/comment cases and the seven `RepoFilesTests`
took this target from 61.3% to 73.4% in one sitting.

### Read the score as a band, not a number

This file used to say the score was reproducible to 0.15 points, on the evidence of two runs that came back
73.26% and 73.41%. Three later runs over near-identical code came back **75.0%, 76.1% and 78.1%**, and the
movement was not where the edits were: between the last two, the only change was two tests in `SourceSpans`,
yet `GraphQuery` gained 12 kills and `SnaplinkValidator` and `StructureLinter` — untouched — moved several
points each. The mutant population itself differed run to run (1254 vs 1026 `CompileError`), so the runs are
not strictly comparable mutant-for-mutant.

So: treat a single number as a **band of a few points**, compare a file against itself rather than the
target total, and do not read a 1-2 point move as a result. `break` at 55 is still a sane tripwire — it is
far enough below the band to mean something — but walking it up to just under the last score would make it
a coin toss. Why the population shifts has not been chased down; it is the first thing to look at before
anyone leans on these numbers harder.

## The targets

Each has a `stryker-<name>.json` beside this file. They are kept free of comments because Stryker validates
the config strictly and rejects unknown keys, including a `//` — hence this file.

### `io-common` — `Nexaflow.IO.Common` via `Nexaflow.Tests.IO`

The cleanest 1:1 in the repo. `Tests.IO` references the IO projects and nothing else, and every subject is a
pure function over bytes or text: `EncodingDetector`, `Glob`, `Hashing`, `Base64Codec`, `TextLineIndex`,
`FileSplitter`. No WPF anywhere in the chain, so it is also the safe one to run on the machine you are
sitting at. Start here.

### `initiatives` — `Nexaflow.Services.Initiatives` via `Nexaflow.Tests.Initiatives`

The highest-value target, and the reason this tooling exists. `SnaplinkValidator` gates the installer build
(`nexaflowSetup.wixproj` → `ValidateSnaplinks`). Every one of its tests hands it something broken and checks
that it complains — so none of them can detect a change that makes it *stop* complaining, and a validator
that quietly fails open looks exactly like a clean tree. The first run found four such mutants alive:

| Line | Mutation | What it would mean |
|------|----------|--------------------|
| 66 | `text is not null && fullPath is not null` → `\|\|` | a guard that stops guarding |
| 318 | `!facts.Exists && …` → negation dropped | the existence check inverted |
| 346 | `outline.Types.Concat(members)` → `.Except(members)` | the candidate set emptied |
| 422 | `type.Members.Any(…)` → `.All(…)` | "this method exists" becomes "every member is this method" |

`Tests.Initiatives` is WPF-free, so this target is safe to run locally.

Widening the glob to `Graph/` later found the bigger gap. `GraphBuilder` and `GraphQuery` sat at 47-49%
against 72-88% for the product-tree services, and two clusters were worth acting on immediately:

- **`GraphQuery.BlockEnd`** - 35 surviving mutants in a hand-written C# lexer (line comments, block
  comments, verbatim and raw string literals, char literals) that decides where a member's source stops.
  It backs `graph code`, `graph context` and a content `graph grep`, so a wrong answer is a false "no
  match" rather than an error. **Resolved by deletion** - see below.
- **`RepoFiles.SkipDirs`** - all ten entries could be blanked with nothing going red. That set is what
  keeps a graph build out of `node_modules`; losing one costs minutes per build and fails nothing.
  `RepoFilesTests` covers it, plus the generated-file markers beside it.

**The lexer is gone.** It was the clearest thing this tool has found: 35 survivors clustered on exactly the
cases a second, partial C# parser gets wrong, in code that only existed because nobody had asked the real
parser. Tree-sitter records both ends of every declaration it extracts, for every grammar, so
`SourceSpans` re-parses the file in hand and `BlockEnd` was deleted outright. The re-parse is what makes a
query from a linked worktree describe *that* branch's code, and testing each file whole before attributing
anything keeps a full-repo content grep at ~1.3s - a file matching nothing costs no parse.

What the follow-up sweep then found is worth reading as a pattern, because it is the same one twice: every
survivor in the new class was a decision the code stated only in a comment - declarations consulted in line
order, the tighter of two bounds, a stale line on a declaration's own first or last line still counting as
inside it. Six tests took `SourceSpans` from 83.8% to 92.6%, the top of the `Graph/` files. The four left
are equivalent mutants and are recorded as such in the commit rather than chased: `Clamp` already
neutralises both `lineCount - 1` mutations, `Extract` already guards a null grammar id, and
`{ Length: > 0 }` to `>= 0` reaches the same failed dictionary lookup.

`GraphQuery` itself now reads *lower* (around 52-56%), which is the arithmetic of the move rather than a
regression: its best-tested method left for a file of its own. What remains there is 125 **uncovered**
mutants - lines no test executes at all - which is a different problem from the one this sweep solved, and
the next one worth taking.

### `search` — `Nexaflow.Search` via three Features suites

Query syntax, term parsing and AQS condition evaluation, feeding 27 `ISearchable` surfaces.

Note the **three** test projects. `Nexaflow.Search` is a shared leaf and no single suite covers it: run it
against `Tests.Features` alone and `SearchQueryScorer` is reported as 99 mutants of dead code, because its
tests live in `.WindowsOS`. Before adding any target for a shared library, check
`nfi graph node code:<path>#T:<Type>` for the real consumer set.

⚠️ This is the one target whose suites are `UseWPF`. See *Cleaning up*.

## Adding a target

A library earns a target when its **failure mode is silence** — validators, parsers, matchers — and its
tests map onto it cleanly. Feature ViewModels and anything WPF are deliberately excluded: most of their
mutable surface is binding glue, their tests need a pumped UI context, and Stryker's project analysis already
fails on several `net10.0-windows` feature projects.

Copy a config, then check two things:

1. **`"test-runner": "mtp"` is set.** Every suite here uses `EnableMSTestRunner` + `OutputType=Exe`, i.e.
   Microsoft.Testing.Platform. Stryker still defaults to VSTest, which cannot see these tests and dies inside
   `VsTestHelper` with an unrelated `ArgumentNullException` about `path3`. Nothing in that message points at
   the cause. (MTP support is marked *preview* in Stryker 4.16 — that is the standing adoption risk.)
2. **Every suite that exercises the subject is listed** in `test-projects`, not just the obvious one.

Then add a row to `$Targets` in `Run-Mutation.ps1` and a row to the table in
[docs/testing.md](../../docs/testing.md#mutation-testing-strykernet).

## Cleaning up

Stryker rebuilds and re-runs per mutant, and it leaks: a sweep leaves dozens of MSBuild node-reuse workers
and test hosts behind. Harmless for a WPF-free target. For `search`, enough orphaned WPF hosts **exhaust the
interactive session's desktop heap**, and the symptom does not look like a resource problem — unrelated WPF
tests start failing with `Win32Exception: Not enough memory resources` out of `HwndWrapper..ctor` while the
machine has tens of GB free. It does not clear until you sign out.

`Run-Mutation.ps1` runs the cleanup itself after every sweep (identifying orphans by start time, so it never
touches an MSBuild node belonging to a Visual Studio you have open). `-Cleanup` does it standalone, for when a
run was interrupted. If WPF tests still fail that way afterwards, sign out and back in.

A run can also end with `Failed to restore output assembly … Mutated assembly is still in place` — Stryker
copies the mutated assembly into the test project's output and could not put the original back because a
handle was held. The script rebuilds afterwards to undo it. A hand-rolled `dotnet stryker` must too, or the
build tree quietly lies to you.
