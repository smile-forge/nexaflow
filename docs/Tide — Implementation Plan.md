# Tide — implementation plan

## Context

[`Nexaflow Calendar — Mini Spec.md`](Nexaflow%20Calendar%20%E2%80%94%20Mini%20Spec.md) describes a local-first calendar built around fortnight planning. This plan turns it into `Nexaflow.Features.Tide` (Time & Tide, after the fortnight default view), mapped onto the architecture the repo already has. It covers the usual requirements:

- the add-feature touch-points
- the product tree
- help pages
- `en`/`fr` strings
- theming
- AutomationIds plus journeys
- AI tools held to the same rules as the UI

The feature is greenfield: there is no calendar, Tide, time-grid or stacked-bar code. The shell already provides the background and notification machinery Tide needs:

- `IBackgroundTask` with `IShellServices.QueueBackgroundTask`
- `RegisterMediatedTask`, for chrome the user can interact with
- `ShowNotification`, backed by Core's `MessageCenter` / `NotificationItem`, which already supports severity, actions, persistence and interactive toast content

Two small gaps stop a feature using that machinery fully: nothing queues a feature's task at startup, and the rich toast is not exposed to features. Phase 0 closes both by extending what exists. It adds no new host and no parallel notification system.

**Decisions:**
- **Store:** SQLite, through `Microsoft.Data.Sqlite`.
- **Nags:** an auto-started `IBackgroundTask` plus rich in-app toasts. A Windows desktop toast mirror is left for later.
- **Entity links:** none for now. `Links` and the spec's "shell integration" phase come out of v1 entirely; there is no field and no placeholder.
- **Rendering:** Tide draws its own planner canvas and does not use the markdown layout engine.

---

## 1. Assemblies and where each piece lives

The layout follows two existing precedents:
- ProductManager → `Nexaflow.Services.Initiatives`: a WPF-free domain backend with its own test suite, shared through `InitiativesHosts` / `InitiativesLease` and wrapped by the feature's `ProductSession`.
- Network → `IO.Network` contract + `[Subfeature]` plugin assemblies.

| Project | Holds | References |
|---|---|---|
| `src/Nexaflow.Services.Tide/` (new, `net10.0`, WPF-free) | the domain model and rules; time attribution; the escalation/nag schedule; `ITideStore` with its SQLite implementation and migrations; template save/stamp; the Soon summary builder; `TideHosts` / `TideHost` / `TideLease` (one store per database path per process, ref-counted, following `InitiativesHosts`); the `ICalendarSyncAdapter` contract | `Microsoft.Data.Sqlite` only |
| `src/Nexaflow.Features/Nexaflow.Features.Tide/` (new, WPF) | the page registrations; view-models and views; `FortnightCanvas` and its pure geometry; `TideSession`, which wraps a `TideLease` (following `ProductSession`); `TideReminderTask`; `TideConfig`; the theme contribution; client tools; `Localization/{en,fr}/` | Features.Common, Visuals.Common, Visuals.Icons, Services.Tide, Plugins.Contracts |
| `Nexaflow.Features.Tide.Google`, `…Tide.Microsoft365` (phase 6) | `[Subfeature("tide","google")]` adapters implementing `ICalendarSyncAdapter` | Services.Tide and Plugins.Contracts only, as the Network probes do |

**Name collisions.** These were checked with nfi. Wherever a spec name collides with an existing type, the new name changes:

| Spec name | Collides with | Becomes | Shown in the UI as |
|---|---|---|---|
| `Block` | `DiscUtils.Block`, `VersionInfoParser.Block` | `TimeBlock` | "Block" (unchanged) |
| `Category` | the nested type in `XyBuilder.Category` | `TimeCategory` | — |
| `Outcome` | `EditPlan.Outcome`, `TestRunner.Outcome` | `EntryOutcome` | — |

These are all free:
- `TimeEntry`, `Appointment`, `Reminder`, `EscalationPolicy`, `SourceReference`, `OwnerRef`, `AnchorMode`, `ICalendarSyncAdapter`
- `TideHosts`, `TideHost`, `TideLease`, `TideSession`
- `FortnightCanvas`, `StackedBarChart`
- `QueueAtStartup`, `ShellNotification`, `ShellNotificationAction`

Every name introduced during a phase is re-checked with `nfi ask 'grep …'` before it is created.

## 2. Phase 0 — two small shell extensions (Features.Common + Core)

These extend existing contracts. Both are generic and contain nothing Tide-specific.

1. **`[QueueAtStartup]` on `IBackgroundTask`** (Features.Common attribute):
   - `FeatureCatalog` records the marker, as it records other contracts.
   - Once a `WorkspaceRuntime`'s shell is wired, Core builds each marked task through `FeatureManager` DI and hands it to that runtime's own `QueueBackgroundTask` with the runtime's cancellation token. Teardown cancels it, so a cancelled task ends quietly, as the existing contract says.
   - The marked task gets the usual feature DI: shell, configs, optional parameters.
   - No new host; this reuses the background-activity manager as it stands.
2. **A rich `ShowNotification` overload on `IShellServices`:** `IDisposable ShowNotification(ShellNotification notification)`.
   - `ShellNotification` (Features.Common) is a description with title, body, severity, `Persistent` (inbox) or transient, toast duration, `ShellNotificationAction(Label, Action Invoke, IsPrimary)` items, and an optional `Func<FrameworkElement>` for interactive toast content.
   - `ShellServices` maps it onto the existing `NotificationItem` / `MessageAction` / `MessageSeverity` and `MessageCenter.Post`. Disposing the returned handle withdraws the notification through `MessageCenter.Remove`.
   - The existing string overloads are left as they are.
   - Snooze and re-notify are not a shell concept. They stay Tide's logic, expressed as toast actions.
3. **Clock:** no DI change. Tide types take an optional `TimeProvider? time = null` that falls back to `TimeProvider.System`, following the `Hdf5SourceCache` precedent; `TryResolveArgs` already passes `Type.Missing` for optional parameters. Tests pass `FakeTimeProvider`.
4. **Docs:** `docs/features.md` describes the startup marker and the rich notification overload. `docs/Architecture.md` notes that startup tasks are scoped per runtime.
5. **Tests** (`Tests.Core` Unit, run before each commit as `CLAUDE.md` requires):
   - a marked task is queued once per runtime and cancelled at teardown
   - the `ShellNotification` → `NotificationItem` mapping, action invocation, and withdraw through the handle

## 3. Phase 1 — domain, store, rules (Services.Tide, no UI)

**Model.** Abstract `TimeEntry` with the subclasses `TimeBlock` (BufferBefore/After), `Appointment` (attendees, location, meeting URL, `ProposedReschedule`) and `Reminder` (a point in time). `TimeEntry` holds the spec's fields minus `Links`:

- times are stored in UTC, with an IANA zone id resolved through `TimeZoneInfo` (ICU handles IANA ids on Windows, so no NodaTime)
- `ParentId`, `AnchorMode`, `OwnerRef`, `SourceReference?`, `IsLocked`, `CategoryId?`
- appearance: `ColorSpec` stored as a string, plus an icon key
- `Availability`, `EscalationPolicy`, `EntryOutcome`, `ReflectionNote`, and `IsCancelled` for cancelled external entries

`TimeCategory` holds Name, Color (a `ColorSpec` string), Icon and DefaultAvailability.

**All mutation goes through `TideEditor`.** It is the single command layer, shared by drag, menus and AI tools, which is the AI-fidelity guarantee.
- Each operation (create, move, resize, re-parent, delete, lock, set appearance, set escalation, propose reschedule, set outcome, set note) returns `EditResult`: either `Applied(ChangeSet)` or `Refused(reasonKey)`.
- The `reasonKey` is a `Tide.Refusal.*` string key, so the UI and the AI report the same reason.
- The rules are pure classes, each separately testable:
  - **`EditabilityRules`:** the spec's ownership × lock table; a lock is not inherited.
  - **`NestingRules`:** only blocks and appointments can be parents; no cycles; the child must fit at creation and is flagged as overflow afterwards; default innermost-block nesting.
  - **`AnchorRules`:** Relative children move with the parent's delta; Absolute children stay put; external entries are always Absolute.
  - **`TimeAttribution`:** effective category; innermost wins; reminders count as zero; non-nested overlap is split equally; buffers count; availability is ignored. It works incrementally over a time range, so the budget banner can update live while dragging.

**Store.** `ITideStore` offers `QueryRange(from, to)`, `Get(id)`, `Apply(ChangeSet)` (one transaction), and a `Changed` event.
- `SqliteTideStore` lives at `<config root>\Tide\tide.db`. The config root honours `NEXAFLOW_CONFIG_DIR`, as `PostItStore.DefaultRoot` does, and the data is global like Scratchpad's.
- `TideHosts` keeps one `TideHost` (store + editor) per database path per process. Every page and the reminder task in every runtime share it, so a change made anywhere raises `Changed` everywhere.
- Tables: `entries` (common columns plus a type discriminator and explicit subtype columns), `attendees`, `escalation_offsets`, `nag_state`, `categories`, `templates`, `template_entries`, `template_targets`, `sync_metadata(entry_id, adapter_id, external_id, change_token, snapshot)` and `schema_version`.
- Migrations are forward-only and numbered.
- Range queries are indexed on `(start_utc, end_utc)`.

**Packaging.** Verify that the `e_sqlite3` native lands in Core's `win-x64` output and in the WiX harvest, alongside the other native runtimes (Whisper, LibVLC, libgit2 and others).

**Tests.** A new suite, `src/Nexaflow.Tests/Nexaflow.Tests.Tide/` (MSTest), modelled on `Tests.Initiatives`: it references only Services.Tide and Fixtures. It has one test class per rule, with `[CoversNode("tide-fn-…")]`; store tests run against a temporary database, and `TideHosts` tests follow the `InitiativesHostsTests` pattern.

## 4. Phase 2 — Fortnight planner

**Page.** `TideFortnightTabRegistration` (StaticPageKind `TideFortnight`), with its own no-params `PageParams` identity.
- Its constructor takes `TideConfig` and `IShellServices`.
- `ContentFactory` opens a `TideSession` and builds the VM and view. The session is disposed on `Closed`.

**`FortnightCanvas`.** A `FrameworkElement` that draws in `OnRender` with layered `DrawingVisual`s, following the `OctagonNavigator` precedent:
- a custom `AutomationPeer` exposes each entry as a child, `Tide_Entry_<n>`, so journeys can reach it
- pure geometry in `FortnightGeometry` (time↔pixel, lane packing for overlaps, the spanning lane for multi-day entries, buffer segments, overflow flags), following the `PostItGeometry` precedent
- a pure `FortnightDragController` state machine (drag-create, resize edges, move body, drop-to-nest) that emits `TideEditor` operations
- the canvas renders only visible days and hours, with no element per entry
- also drawn: the best-working-time band, the source-layer style plus provider badge, the lock icon and the overflow flag
- the fortnight anchor comes from `TideConfig.FortnightAnchor`, so boundaries are stable

**Editing UI.**
- Inline category picker on create.
- A context menu and side panel for colour, icon and category. Colour uses `ColorPicker` with `ColorSpec`, so swatches follow the theme. Icon uses `IconPicker`.
- Context-menu items use the global `MenuItem` style.

**`TideConfig`** (global `IFeatureConfig`, with `[ConfigDisplayName("Tide.Config.…")]` on every row):
- fortnight anchor
- visible hours
- best working time per weekday
- the Next-few-hours window (4 h)
- the Just-happened window
- default nag interval
- whether exported spans include buffers

**Theme.** `Theming/Tide.xaml` and `TideThemeContribution` add `Tide.*` tokens: source layer, overflow, buffer wash, best-time band, now line, conflict highlight. They derive from the semantic tokens and contain no literal colours. Code-drawn surfaces read each token at paint time and reapply alpha, following the `HexPanelBase.Res` precedent.

**Touch-points:** `Nexaflow.slnx`; the Core `ProjectReference`; the `Tests.Features` reference; `default-ribbon.json` (a Fortnight button); the `docs/features.md` params row.

## 5. Phase 3 — budget banner and templates

- **`StackedBarChart`** is a new `OnRender` control in `Nexaflow.Visuals.Common/Controls/`, beside `PieChart` and `SparklineChart`. It shows target markers and takes theme tokens. The banner shows this fortnight above the previous one, and live drag updates come from `TimeAttribution`.
- **Templates** run through `TemplateService` in Services.Tide.
  - **Save** stores the user-owned entries as day-plus-time offsets, keeping nesting, categories, buffers, escalation and locks, plus the per-category targets.
  - **Stamp** runs as one `ChangeSet`. Conflicts are placed and flagged, never resolved automatically.
  - The template panel sits inside the Fortnight page.
- **Before this phase, answer two of the spec's open questions:** does a stamped entry keep a back-link to its template, and does the equal-split overlap rule stand?

## 6. Phase 4 — Soon view, escalation, nag

**Page.** `TideSoonTabRegistration` (`TideSoon`). It suits the right-hand pane (`OpenTab(…, inRightPane: true)`) next to the planner. Its sections, in order:

1. **Pinned:** active nags.
2. **Just happened:** collapsed, with a count. One click sets the outcome, there is an inline note, and the user can create a follow-up entry.
3. **Now:** current entries, with an ancestor breadcrumb.
4. **Next few hours.**
5. **Coming up:** a summary.
6. **Ahead:** salient items only.

**`SoonSummaryBuilder`** (Services.Tide) builds the summary deterministically from counts and categories. It returns *keys plus arguments*, not text, and the VM formats them with `Str.Format`.

**`TideReminderTask`** is a `[QueueAtStartup] IBackgroundTask`. Its `RunAsync` is a loop over `TimeProvider` delays until its token is cancelled.
- It opens a `TideSession` and asks the shared `TideHost` for the **nag-driver lease**. Only the holder fires, so several live runtimes never double-notify; when the holder's runtime tears down, the next waiting task takes the lease over.
- Each trigger raises `ShowNotification(ShellNotification)` with severity Warning, `Persistent`, and two actions: **Acknowledge** and **Snooze** (with a duration chooser as interactive toast content).
- It re-notifies at the policy's interval and stores state in `nag_state`.
- Acknowledging, whether in the toast or in Soon's Pinned section, disposes the notification handle. Snooze moves the next trigger.

`RegisterMediatedTask` is used only if the Pinned nags also need an always-visible chrome control. That is decided in this phase, with a journey to cover it if it is added.

## 7. Phase 5 — Reflection view

`TideReflectionTabRegistration` (`TideReflection`). It shows:
- a month grid (6×7), defaulting to the previous month, with drill-down to a day
- outcome markers and notes
- category totals per day and per month
- planned against confirmed time
- a fortnight comparison that reuses the banner's attribution data

## 8. Phase 6 — sync adapters

Google first, then Microsoft 365, each pulling before it pushes. They implement `ICalendarSyncAdapter`:
- `PullDelta`, `Push`, per-type mapping, and `ReportConflict`
- metadata round-trips through Google `extendedProperties.private` and Graph extended properties
- nesting is flattened with the parent id kept, and re-nested on import
- the source layer always follows the provider; local wins for self-owned entries, with conflicts surfaced

`TideReminderTask` (or a sibling startup task) takes `IReadOnlyList<ISubfeatureHandle<ICalendarSyncAdapter>>`. Provider limits and permissions are checked against the current API docs at the start of this phase. Its credentials need their own design question before it starts.

## 9. AI support

The AI must be able to do exactly what the UI does, with the same rules applied.

- **Same path:** every tool calls `TideEditor` and the same queries the UI uses. A tool cannot bypass locks, ownership or nesting. A refusal returns `ToolResult.Error` holding the English refusal text.
- **Honest context:** each VM's `GetContext()` states:
  - Fortnight page: the fortnight shown, the selection, the budget totals and the overflow/conflict flags
  - Soon page: each section with its counts
  - Reflection page: the month and its totals

  `IsContextReady` stays false until the store has loaded.
- **System-prompt guidance:** `GetAiSystemPromptGuidance()` encodes the spec's principle: never infer priority or schedule automatically, and act only on what the user asks.
- **Preview:** an `IContextPreview` per page.
- **Safety:** read and navigation tools are `SafeOperation`. Every mutation is `RequiresApproval`.
- **Tools** (snake_case, `tide_` prefix, built with `DelegateClientTool` as in `HexViewModel.GetClientTools`):

| Page | Tools |
|---|---|
| Fortnight | `tide_get_fortnight`, `tide_goto_fortnight`, `tide_list_entries`, `tide_get_entry`, `tide_select_entry`, `tide_create_entry`, `tide_move_entry`, `tide_resize_entry`, `tide_set_parent`, `tide_delete_entry`, `tide_set_lock`, `tide_set_appearance`, `tide_set_buffers`, `tide_set_escalation`, `tide_propose_reschedule`, `tide_list_categories`, `tide_save_category`, `tide_get_budget`, `tide_list_templates`, `tide_save_template`, `tide_stamp_template` |
| Soon | `tide_get_soon`, `tide_acknowledge_nag`, `tide_snooze_nag`, `tide_set_outcome`, `tide_create_follow_up` |
| Reflection | `tide_get_reflection`, `tide_goto_month`, `tide_set_reflection_note`, `tide_compare_fortnights` |

- **Search:** `ISearchable` on the Fortnight and Reflection VMs, over titles and notes, with regex support as `SearchableConformanceTests` requires.
- **Tests:** `Tests.Features/Tide/TideAiToolTests.cs` follows the `HexAiToolTests` / `JsonAiToolTests` pattern:
  - one test per tool, each `[CoversNode("tide-ai-act-<tool>")]`
  - context-honesty tests for `tide-ai-context`
  - `TheToolSurfaceIsExactlyWhatTheTreeSaysItIs`
- Tool descriptions stay English, as `docs/localization.md` requires.

## 10. Help and strings

- **Help:** one page per page kind under `Localization/en/help/`: `TideFortnight.md`, `TideSoon.md` and `TideReflection.md`, with a `fr/help/` translation of each.
  - Pages use `help:` cross-links and `locate:<AutomationId>` lassos.
  - Pictures go in `help/images/`.
  - F1 and the **?** button find these pages automatically.
  - `LocalizationContentGuardTests` checks them.
- **Strings:** `Localization/en/strings.json` and `fr/strings.json`, all keys `Tide.<Surface>.<Element>`. This includes the toast titles and action labels.
  - XAML uses `{loc:Str …}`; C# uses `Str.Get` and `Str.Format` with literal keys.
  - No string is cached in a static field.
  - Plurals use separate `…One` / `…Many` keys (the house pattern). Because French puts 0 in the singular, the key choice sits in one Tide helper that tests cover.
- **Dates:** day and month names, and times, are formatted with `CultureInfo.CurrentCulture`, the house rule.

  **Known gap:** the culture follows the OS, not the chosen language pack. Tide does not work around this; if it matters, it is a separate shell fix.

## 11. AutomationIds, tests, product tree

- **AutomationIds:** every button carries a `Tide_*` id (NXUI001). Each root view is `Tide<Page>View`. Every literal id is named by a journey, so `automation-ids-without-a-journey.txt` does not grow.
- **Journeys:** `UIJourneys/Features/Tide/TideFortnightJourneyTests.cs`, `TideSoonJourneyTests.cs` and `TideReflectionJourneyTests.cs`, in the `UiJourneyTestBase` style of the Solver and Scratchpad journeys.
- **VM and leaf tests:** in `Tests.Features/Tide/`, following the Solver exemplar, including the geometry and drag-controller tests (the `PostItGeometryTests` style). They cover `TideReminderTask` with `FakeTimeProvider`: the trigger, re-notify, snooze, acknowledge, and the nag-driver lease handover. A Tide-local fake shell follows `Projects/FakeShellServices.cs`.
- **Product tree,** built as the work goes, not after merge. Ids all prefixed `tide-`:
  - **root `tide`:** `AI Ready`, `docs`, `i18n` and `theming`
  - **`tide-ui`:** fortnight, budget banner, templates panel, soon, reflection
  - **`tide-fn`:** nesting, anchoring, locking, editability, attribution, escalation, nag, templates, store, sync
  - **`tide-ai`:** `-ai-context`, one `-ai-act-<tool>` leaf per tool, and `-ai-preview`

  Snaplinks are written to `docs/product/pending/<branch>.json` and committed with each change. The two phase 0 extensions are recorded on the existing shell nodes for background tasks and notifications.

## 12. Delivery

One PR per phase, each shippable and green. Phase 0 and phase 1 can be built in parallel; phase 4 needs phase 0.

Every change goes through `nfi graph edit`. For each one, read its `impact:` and `compile:` lines, then run `nfi test <id>`. Run `Tests.Core` Unit after any Core change. Run `nfi lint --under tide` before each PR.

**Still open from the spec:**
- recurrence (RRULE), which is out of v1
- the template back-link
- the equal-split overlap rule
- the default Just-happened window (24 h is proposed, configurable)

**Later, out of v1:** entity links and shell integration (they need a link contract first); a Windows desktop toast mirror on `MessageCenter`.

## Verification (per phase)

- `dotnet build Nexaflow.slnx` is clean, with no new NXUI001 or NXCOV warnings.
- Run `nfi test` on the touched nodes, then the `Nexaflow.Tests.Tide` suite. Also run `Tests.Features --filter "FullyQualifiedName~Tide"`, `Tests.Core --filter "FullyQualifiedName~Unit"`, and the architecture suite (`ArchitectureRulesTests`, `FeatureTouchPointTests`, `AutomationIdJourneyCoverageTests`, `LocalizationContentGuardTests`).
- Run the Tide journeys by their filter. Start the app with `dotnet run --project src/Nexaflow.Core/Nexaflow.Core.csproj` and check by hand:
  - drag-create, move and nest
  - the lock refusal
  - the live budget banner
  - a nag that toasts after a restart with no Tide tab open, re-notifies, snoozes, and acknowledges from both the toast and Soon
  - F1 help
  - the language switched to `fr`
