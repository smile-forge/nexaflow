# Plan — unify registry handlers into External Apps + Default Actions tab

Status: **implemented** — §1–§3 landed on `claude/festive-wing-6bc672`, builds clean, unit tests green
(Core 440, WindowsFileSystem 178, incl. new coverage). Accepted compromise: the Default Actions tab labels
internal viewers by a prettified experience-id ("Text › Code") rather than the action's DisplayName,
because Options controls aren't handed a shell to materialise action instances.

Three pieces: **(§2)** remove the vestigial `/registry` FileMap scan and turn registry "open" handlers
into a clean, reversible gate + an opt-in import into External Apps; **(§3)** a new **Default Actions**
tab; **(§1)** the already-landed toggle guard. The **External Apps tab and the File Type Actions tab are
otherwise unchanged** in shape.

## 0. Findings / root cause

The file-explorer "open with" strip is the union of three *unrelated* mechanisms that merely share one
toggle (`ExternalAppsConfig.UseRegistryMapping`):

1. **Built-in internal actions** — viewers, matched via `FileMapManager` reverse index. Matching by
   PerceivedType/ContentType is done **live** at query time (`ShellTypeResolver`), *not* gated by the flag.
2. **Live shell-verb buttons** (`ShellVerbAction`, `/shell/{ext}`) — the visible "Open with X" from HKCR,
   computed per selected file, appended straight to the strip, **gated by the flag**
   ([FileSystemViewModel:499](../../src/Nexaflow.Features/Nexaflow.Features.WindowsFileSystem/ViewModels/FileSystemViewModel.cs)).
   **This is what the user perceives as "registry-discovered entries."**
3. **User external apps** (`CustomAction`) — launch buttons, from `ExternalAppRegistry`.

**The `/registry/.ext` FileMap scan is inert.** Traced every reader of `GetExperiencesForFile`:
- `FileActionManager.FileMatches` compares against real action ExperienceIds — none are `/registry/*`, so
  they match nothing.
- The scan's own dedup self-check is the only other reader.

The scan only *creates* a `/registry/.ext` entry for a PerceivedType/ContentType **no internal viewer
covers** (orphan records), keeps them hidden (`GetAllExperienceIds` is `User`-only), and
`ConvertRegistryMappingsToUser` promotes them into the File Type Actions tree as junk on toggle-off. It
carries no live behavior. Removing the scan does **not** affect internal-viewer matching (that path uses
`ShellTypeResolver` live, independent of the flag).

**Insight (drives the redesign):** registry "open" handlers and External Apps are the same thing —
launch-a-program-on-a-file buttons. Treat them uniformly.

---

## 1. Landed — toggle guard (safe, minimal)

[`ExternalAppsEditorControl.RegistryToggle_Unchecked`](../../src/Nexaflow.Features/Nexaflow.Features.WindowsFileSystem/Controls/ExternalAppsEditorControl.xaml.cs)
early-returns when `_suppressDirty` is set, so opening Options no longer pops the confirmation dialog or
silently disables registry mapping on load. Builds clean. (Superseded in spirit by §2, which removes the
scary dialog outright, but harmless to keep meanwhile.)

---

## 2. Unify registry handlers into External Apps

### 2.1 Remove the dead machinery

- Delete `FileMapManager.ScanHkcrAsync` (and its call in `Initialize`) and
  `FileMapManager.ConvertRegistryMappingsToUser`.
- FileMapManager no longer needs `UseRegistryMapping`; internal-viewer PerceivedType/ContentType matching
  is unchanged (already live via `ShellTypeResolver`).
- **Cleanup on upgrade:** on load, delete any leftover `registry_*.json` files in the filemap dir (from
  prior scans/conversions) so the File Type Actions tree is clean for users who previously toggled off.
- `MappingSource.Registry` becomes unused; can be retired or left inert. (Leave the enum for now to avoid
  churn; drop in a follow-up.)

### 2.2 Toggle becomes a clean, reversible gate

`UseRegistryMapping` now governs exactly **one** thing: whether live `ShellVerbAction` buttons appear in
the action strip. Turning off → buttons stop; turning on → buttons return. Fully reversible, no
conversion, no persistence side effects. Toggle stays in the External Apps tab (per review); relabel to
something honest like **"Show Windows 'Open with' handlers"** with a one-line tooltip.

### 2.3 Import-on-disable

When the user turns the toggle **off**, prompt: *"Import your Windows 'Open with' handlers into External
Apps so you keep them as buttons?"*

- **Enumerate** every HKCR `.ext` with an "open" handler via
  [`ShellAssocHandlers.ForExtension`](../../src/Nexaflow.Features/Nexaflow.Features.WindowsFileSystem/Services/ShellAssocHandlers.cs)
  (already returns `(name, exePath)`, skips UWP/protocol).
- **Dedupe by executable path** → one `ExternalAppDefinition` per unique exe, `Criteria` = the union of
  `*.<ext>` it opens. `Arguments = "#filepath"`, `MultiFile = SingleFileOnly`, new GUID `Id`.
- **Merge, don't duplicate:** if an app with that exe already exists in `Apps`, union its extensions into
  the existing `Criteria`.
- Add to `cfg.Apps` and mark dirty for the user to review/save; then set the flag off.
- "No" → just set the flag off (buttons stop, nothing imported).

**Cost:** `SHAssocEnumHandlers` is COM/STA and runs per-extension across all of HKCR (thousands) — dedupe
yields only ~dozens of apps, but the scan is slow. Run it as a **cancelable, progress-reported** operation
on the UI thread's idle path (or marshal a cached HKCR read). Flagged in §5.

**Optional:** an explicit "Import Windows handlers…" button in the External Apps tab (same routine)
independent of the toggle — trivial add.

---

## 3. Default Actions tab

**Goal.** User enters an extension → sees every action currently registered for it (internal viewers +
external apps + Windows shell verbs) → picks which one is the **double-click** action. Unset extensions
keep today's automatic resolution.

### 3.1 UI — two views (list ⇄ add/edit)

- **List view (default):** the configured overrides (extension → chosen action) with Edit/Remove per row,
  plus a **+ Add default** button. Empty-state hint when none. Keeps the list from being crushed when many
  overrides exist.
- **Add/edit view:** an extension box with **live search** — typing rebuilds (and thereby clears the prior)
  candidate list: internal viewers applicable to the ext, external apps applicable to the ext, and Windows
  shell verbs from `ShellTypeResolver`. **Automatic** is the first option. **Confirm** stores the choice into
  the in-memory list and returns to the list view; **Cancel** discards.
- Edits **accumulate in memory** across multiple Add/Edit cycles and are committed together only on the
  Options **Save** (`Apply` → config + `DefaultActionRegistry`) — no per-item save.

### 3.2 Resolution priority (new)

`DefaultFileOpener.OpenAsync` gains a first step before the existing specificity logic:

1. **Override** for the file's extension (longest-match via `ExtensionCandidates`), if its target applies:
   - `InternalViewer(experienceId)` → applicable internal action with that `ExperienceId`.
   - `ExternalApp(appId)` → `ExternalAppDefinition` with that `Id` → `CustomAction` → launch.
   - `WindowsVerb(verb)` → shell verb via `ProcessStartInfo { Verb, UseShellExecute = true }`.
2. **Else** → current internal-vs-shell specificity resolution (unchanged).

Dangling override (target removed) silently falls through to step 2.

### 3.3 Data model

```csharp
[CustomControl(typeof(DefaultActionsEditorControl))]
sealed class DefaultActionsConfig : IFeatureConfig {
    string ConfigName   => "defaultactions";
    string FriendlyName => "Default Actions";
    List<DefaultActionOverride> Overrides { get; set; } = [];
}
sealed class DefaultActionOverride {
    string            Extension { get; set; }   // ".pdf" normalized
    DefaultActionKind Kind      { get; set; }
    string            TargetId  { get; set; }   // experienceId | external-app Id | verb name
}
enum DefaultActionKind { InternalViewer, ExternalApp, WindowsVerb }
```

- **`ExternalAppDefinition` gains a stable `Id` (GUID)** — also produced by the §2 import. Backfill
  assign-if-empty in `ExternalAppRegistry.Initialize`, persist once.
- New singleton **`DefaultActionRegistry`** (mirrors `ExternalAppRegistry`): `Initialize`/`Update`/
  `Resolve`, consulted by `DefaultFileOpener`.
- New helper **`DefaultActionResolver`**: for a synthesised `dummy.<ext>`, returns internal-viewer
  candidates (`FileActionManager.FilterActions`, excluding universal ops by specificity), external-app
  candidates (`ExternalAppRegistry.Resolve`), Windows verbs (`ShellTypeResolver`), and the current
  effective default — shared by the tab and `DefaultFileOpener`.

### 3.4 Wiring

- Register `DefaultActionsConfig` + `DefaultActionRegistry.Initialize` in
  [`App.xaml.cs`](../../src/Nexaflow.Core/App.xaml.cs).
- `DefaultActionsEditorControl` implements `ICustomConfigApply` (+ `IConfigChangeTracker`); `Apply()`
  pushes into `DefaultActionRegistry.Instance.Update`. Open tabs re-resolve on next double-click.

---

## 4. Files touched

| Area | Files |
|------|-------|
| Fix (done) | `Controls/ExternalAppsEditorControl.xaml.cs` |
| §2 unify | `Services/FileMapManager.cs` (delete scan + convert + leftover cleanup), `Controls/ExternalAppsEditorControl.xaml(.cs)` (relabel toggle, import prompt + routine), new `Services/RegistryHandlerImport.cs` (enumerate+dedupe), `App.xaml.cs` (drop scan wiring) |
| §3 model | `FileActions/ExternalAppsConfig.cs` (add `Id`); new `FileActions/DefaultActionsConfig.cs` |
| §3 services | new `Services/DefaultActionRegistry.cs`, `Services/DefaultActionResolver.cs`; edit `Services/DefaultFileOpener.cs`, `Services/ExternalAppRegistry.cs` (assign Ids) |
| §3 UI | new `Controls/DefaultActionsEditorControl.xaml(.cs)` |
| Wiring | `src/Nexaflow.Core/App.xaml.cs` |
| Docs | `docs/features.md`, `docs/Architecture.md` |

## 5. Risks / open decisions

- **Import cost/UX:** all-HKCR `SHAssocEnumHandlers` is slow — needs progress + cancel, and shouldn't
  freeze the UI. Confirm a modal progress dialog is acceptable, or cache/parse from `ShellTypeResolver`
  command templates instead of COM.
- **Import "open" target selection:** `ShellAssocHandlers` returns *recommended* handlers; take the
  primary per extension as the "open" target. Confirm that's the desired semantic vs. the registry
  *default* handler specifically.
- **External-app `Id`:** assign-and-persist on first load (no formal migration hook) — confirm.
- **`MappingSource.Registry` retirement:** leave inert now, drop later — OK?
- **Viewer candidate filtering** (§3): specificity threshold to exclude universal operations; verify no
  legitimate viewer sits at universal level.

## 6. Testing

- Unit (`Nexaflow.Tests.Features`): import dedupe-by-exe + extension union + merge into existing app;
  override priority (override > specificity); external-app-as-default launches the right `CustomAction`;
  dangling override falls back; compound extension (`.tar.gz`); `Id` assign-if-missing.
- Regression: `SampleFileViewerTests` unchanged; internal-viewer matching unaffected by removing the scan;
  leftover `registry_*.json` cleaned on load.
- UI (manual): toggle off→on restores shell-verb buttons with no tree pollution; import prompt populates
  External Apps; Default Actions candidate list + selection drives double-click.

## 7. Sequencing

1. ✅ Toggle guard (landed).
2. ✅ §2 unify — removed the `/registry` scan + `ConvertRegistryMappingsToUser`, added leftover cleanup,
   reversible toggle, import-on-disable (dedupe-by-exe), external-app `Id` + backfill.
3. ✅ §3 Default Actions — `DefaultActionsConfig`/`DefaultActionRegistry`, `DefaultFileOpener` override,
   `DefaultActionResolver` (candidates + reflection-based viewer set — no shell needed), tab UI, wiring, tests.

Remaining (optional): richer internal-viewer labels (needs a shell in Options), and the follow-ups in §5.
