# `external/` — third-party source dependencies (git submodules)

Nexaflow builds a few third-party libraries **from source**, each via a fork the **`smile-forge`** org
controls, wired in with `ProjectReference` (never `PackageReference`, never vendored). This lets Nexaflow
build against our version while keeping upstream sync and clean atomic PRs working.

## ⚠ Before you touch anything in here

**Read [`../docs/externals.md`](../docs/externals.md)** — the full runbook: how to build against these,
create feature branches, post upstream PRs, bump pins, keep the fork default pristine, and add a new one.
Don't improvise submodule surgery from memory.

Key reminders (the runbook has the rest):

- **The shell's working directory is pinned** (desktop app), so `cd` into a submodule does nothing. Use
  `git -C external/<name> …` for **every** git operation inside a submodule.
- `origin` = our `smile-forge` fork; `upstream` = the original repo. The fork's default branch stays
  pristine (tracks upstream, never our commits).
- Each upstream-bound change = its own atomic `feat/*` branch off the **upstream default**. The per-repo
  **`nexaflow`** integration branch is what the submodule tracks and what Nexaflow pins. **Never PR from
  `nexaflow`.**
- Pushing branches / opening PRs is outward-facing — confirm with the user first.

## Current submodules

| Path | Fork (`origin`) | Upstream | Upstream default | Consumed by |
|------|-----------------|----------|------------------|-------------|
| `xaml-math` | `smile-forge/xaml-math` | `ForNeVeR/xaml-math` (`wpf-math` redirects here) | `master` | `Nexaflow.Visuals.Text` → `src/WpfMath` |
| `DiscUtils` | `smile-forge/DiscUtils` | `LTRData/DiscUtils` | `LTRData.DiscUtils-initial` | `Nexaflow.Features.VirtualDisk` → `Library/DiscUtils.{Core,Containers,FileSystems}` |

Run `git submodule status` and `cat ../.gitmodules` for the live pinned commits and tracked branches.
