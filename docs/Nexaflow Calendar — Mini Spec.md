# Nexaflow Calendar — Mini Spec

Oct 7, 2026 · @Adam

## Purpose and principles

Nexaflow gets a local-first calendar built around fortnight planning, with three entry types (time blocks, appointments, reminders) that can nest inside each other. This spec covers the domain model, editing rules, views and sync boundaries for a first implementation in C#/WPF (MVVM).

Design principles:

- **Fortnight is the planning unit.** The primary planner shows 14 days. Weekly and daily grids are not the main perspective.
- **The user decides importance.** The tool never infers priority or auto-schedules. It gives the user tools to elevate things (reminders, nagging, locking), and an AI assistant may help later, but the base tool does not.
- **Local-first.** The local store is the source of truth. Cloud providers are sync targets reached through adapters, and the internal model is richer than any single provider.
- **Ownership is explicit.** Every entry is either owned by the user or sourced from someone else. Sourced data is preserved and shown distinctly from what the user adds.
- **Low-friction reflection.** Recently finished entries are surfaced so the user can close loops cheaply; that same capture feeds the reflection view.

## Domain model

Use distinct entity types (`Block`, `Appointment`, `Reminder`) over a shared abstract base `TimeEntry`. Universal behaviour (nesting, locking, ownership, escalation, appearance, categories) lives on the base; anything provider-specific lives in the sync adapters, not in conditionals on the model.

### TimeEntry (abstract base)

| Field | Type | Notes |
| --- | --- | --- |
| Id | Guid | Local identity, never a provider id |
| Title, Notes | string | User-editable content |
| Start | DateTimeOffset | Stored UTC plus an IANA time zone id |
| End | DateTimeOffset? | Null means a point in time (Reminder) |
| IsAllDay | bool |  |
| ParentId | Guid? | Nesting; see next section |
| AnchorMode | enum Relative, Absolute | How the entry behaves when its parent moves |
| Owner | OwnerRef | Self, or External (provider, account, organizer) |
| Source | SourceReference? | Null for self-owned entries; see next section |
| IsLocked | bool | Locked entries refuse time and structural changes |
| CategoryId | Guid? | Null means inherit from parent |
| Color, Icon | optional overrides | Fall back to category appearance |
| Availability | enum Busy, Free, Tentative | Set per entry, independent of nesting |
| Escalation | EscalationPolicy | Reminders, lead times, nag; see later section |
| Links | list of EntityLink | Links to Nexaflow entities (Product, Workload, task, file) |
| Outcome | enum Unconfirmed, Happened, Partial, Skipped | Set after the entry ends, via Just happened |
| ReflectionNote | string? | Captured after the fact |

### Subtypes

- **Block**: a span of intended time owned by the user. Adds `BufferBefore` and `BufferAfter` (TimeSpan, default zero). There is no separate type for focus, flexible or "not doing" time; those are categories.
- **Appointment**: a span committed with other people. Adds attendees, location, online meeting URL, and an optional `ProposedReschedule` (start and end the user wants to propose without changing the source).
- **Reminder**: a point in time with no duration (birthdays, renewals, due dates). A due date is a Reminder with escalation, not a separate type.

### Category

`Id`, `Name`, `Color`, `Icon`, `DefaultAvailability`. Categories drive appearance, the budget banner and reflection totals.

## Nesting, ownership and locking

Entries form a tree of time at different granularities: a 4-day "In Rome" block contains a "Vatican open" block on day 2, which contains a 15:00 tour reservation. Who owns an entry and whether it is locked decide what can change.

### Nesting

- Blocks and Appointments can be parents. Reminders are points and can only be children.
- A child must fit inside its parent's span when created. If a later change pushes a child outside, keep it and flag it visually as overflowing; never clip or delete silently.
- No cycles. Depth is unlimited in the model; the UI can collapse deep levels.
- When an entry (new or synced in) falls entirely within one user-owned block, nest it under the innermost such block by default. The user can un-nest or re-parent.

### Anchoring

- **Relative** children move with the parent by the same delta, keeping their offset from the parent's start.
- **Absolute** children stay put when the parent moves; if they end up outside, they are flagged as overflow.
- User-owned children default to Relative. Externally owned entries are always Absolute.

### Ownership and source references

An externally owned entry carries a `SourceReference`: provider, account, external id, change token (etag or changeKey), and a snapshot of the source fields (title, times, location, attendees, organizer, description).

The entry has two layers:

- **Source layer**: the provider's fields. Never overwritten locally. Updated only by sync. Rendered visually distinct (for example muted styling plus a provider badge).
- **User layer**: notes, links to Nexaflow entities, local escalation, color/icon/category, nesting under a user block, a proposed reschedule, outcome and reflection note. Always editable, preserved across source updates.

If the source changes the time, the entry moves (it is Absolute). If the source cancels or deletes it, mark it Cancelled and keep the user layer until the user dismisses it.

### Locking

A locked entry refuses move, resize, re-parent and delete until explicitly unlocked. Annotations stay editable. Locks are not inherited: children of a locked block can still be edited within it.

### Editability summary

| Entry | Change time or structure | Edit content | Annotate (user layer) |
| --- | --- | --- | --- |
| Self-owned, unlocked | Yes | Yes | Yes |
| Self-owned, locked | No, until unlocked | Yes | Yes |
| Externally owned | No; propose a reschedule instead | Source fields read-only | Yes |

## Escalation, buffers and time attribution

Importance is something the user applies to any entry, not a type. Time is attributed to categories by the innermost entry covering it.

### Escalation policy

Any entry, including externally owned ones (as part of the user layer), can carry an `EscalationPolicy`:

- **Reminders**: zero or more offsets before the entry (for example 4 days and 1 hour). Each can carry a short action label, such as "order gift", so a lead-time reminder doubles as an act-by date.
- **Nag until acknowledged**: once triggered, the entry stays pinned in the Soon view and in the shell's notification surface, and re-notifies at a set interval until the user confirms it. Snooze is allowed; dismissal requires an explicit acknowledge.
- Acknowledgement state is stored locally. Neither Google nor M365 has a nag concept, so the policy round-trips only via extended properties (see Storage and sync).

### Buffers

- A Block can have an optional buffer before and/or after (travel, prep, recovery).
- Buffers move and resize with the block and render as attached, visually lighter segments.
- Buffer time counts as allocated time under the block's category.
- Buffers may overlap other entries; the planner shows the overlap rather than preventing it.

### Time attribution

Used by the budget banner and the reflection view.

- **Effective category** = the entry's own category, else its parent's effective category, else Uncategorized.
- **Innermost wins**: time covered by a child is attributed to the child's effective category; the rest of the parent's span goes to the parent's. A customer meeting inside a dev block counts as customer time.
- Reminders are points and contribute no time.
- Overlapping entries that are not nested split the overlapping time equally (v1 rule).
- Attribution ignores Availability: a Free block still counts toward its category.

## Views

Four views: the Fortnight planner for allocating time, the Soon view for what's next, the Reflection view for looking back, and templates for reusing a fortnight's shape. There is no day view as a primary perspective.

### Fortnight planner

- 14 day columns on a vertical time axis, with configurable visible hours. Fortnights are anchored to a user-chosen reference date so boundaries are stable, not rolling.
- Multi-day entries render in a spanning lane above the grid; their children render inside the relevant days.
- Direct manipulation: drag on empty space to create a block (category picker inline), drag edges to resize, drag the body to move, drop onto or inside another entry to nest. Color, icon and category are quick-editable from a context menu or side panel.
- Visual states: source-layer styling and provider badge for external entries, lock icon, overflow flag for children outside their parent, lighter attached segments for buffers.
- **Best-working-time band**: user-defined hours per weekday, shaded behind the grid. Display only; never used for scheduling.

### Budget banner

A wide banner across the top of the planner showing planned time per category for the current fortnight as a horizontal stacked bar, with the previous fortnight beneath it for comparison. It uses the time attribution rules and updates live while dragging. If the stamped template defines category targets, show them as markers on the bar.

### Templates

- Save a fortnight as a named template. Stores user-owned entries only, as offsets from the fortnight start (day plus time), with nesting, categories, buffers, escalation and locks, plus optional per-category targets.
- Stamp a template onto a fortnight to create copies of its entries. Conflicts with existing entries are placed anyway and highlighted, never auto-resolved.
- Users can keep several templates (seasonal, project phases, travel).

### Soon view

Top to bottom:

1. **Pinned**: entries with an active nag, until acknowledged.
2. **Just happened** (collapsed by default, with a count): entries that ended recently with Outcome Unconfirmed. One click sets the outcome (Happened, Partial, Skipped); inline quick note; create a follow-up entry or link to a Nexaflow task. Confirmed items drop off.
3. **Now**: current entries, shown with their ancestors as a breadcrumb (In Rome › Vatican open).
4. **Next few hours**: full detail, default window 4 hours, configurable.
5. **Coming up**: a generated summary of the rest of the fortnight, grouped by category, for example "4 customer meetings and a reserved dev block".
6. **Ahead**: salient items further out. An item is salient when one of its reminder offsets has been reached or it has a nag policy, for example "Jessica's birthday in 4 days". Everything else is only counted in summaries.

The summary text is built deterministically from counts and categories, not by an AI model.

### Reflection view

A traditional month grid with drill-down to a day, defaulting to the previous month. It shows entries with outcome markers and reflection notes, category totals per day and month, and planned versus confirmed time. A fortnight comparison reuses the budget banner's data across past fortnights.

### Shell integration (later phase)

An entry linked to a Nexaflow Product or Workload can, opt-in, surface that workspace when it starts. This is where the calendar benefits from sitting in the shell rather than in an app.

## Storage and sync

The local store is the source of truth; each cloud provider is reached through its own adapter, with one mapping per entry type. Sync is not needed for the first phases, but the model must not prevent it.

### Local store

- Embedded store behind a repository interface, so the backing store can change later (SQLite is a reasonable v1).
- Sync metadata lives in its own table keyed by (entry id, adapter id): external id, change token, last-synced snapshot. Nothing provider-specific goes on `TimeEntry` except `SourceReference` for externally owned entries.

### Adapter contract

`ICalendarSyncAdapter`: delta pull, push, per-type mapping (`Block`, `Appointment`, `Reminder`), and conflict reporting. Google and Microsoft 365 first; ICS/CalDAV and a Nexaflow cloud service are later options behind the same interface.

### Mapping per type

| Type | Exported as | Metadata round-trip |
| --- | --- | --- |
| Block | Event; show-as from Availability; buffers optionally included in the exported span (setting) | Local id, type, parent id, category, color, icon, escalation policy |
| Appointment (self-organized) | Event with attendees | As Block |
| Appointment (external) | Not pushed; pulled into the source layer | User layer stays local, or in private properties where the provider allows writing them on the user's copy |
| Reminder | All-day or zero-duration event marked Free; provider reminder set to the first offset | Local id, type, parent id, escalation policy |

Metadata travels in Google Calendar `extendedProperties.private` and in Microsoft Graph open extensions or single-value extended properties on events. Verify exact limits and attendee write permissions against current API documentation before implementing.

### Nesting and conflicts

- Neither provider supports nesting. Flatten on export and store the parent id in metadata; on import, re-nest by parent id when known, otherwise apply the default nesting rule.
- For externally owned entries, the provider always wins on the source layer; the user layer is never touched by sync.
- For self-owned entries, local is authoritative. If the change token shows a remote edit since last sync, surface a conflict for the user to resolve rather than overwriting silently.
- A proposed reschedule is sent through the provider's propose-new-time mechanism where one exists; otherwise it stays local.

## Scope, open questions and build phases

Build the model and its rules first, test them without UI, then layer views and sync on top.

### Out of scope for v1

- Auto-scheduling, priority inference, or using the best-working-time band for placement.
- Displacement tracking (time lost to interruptions).
- A Nexaflow cloud calendar service.
- Sharing self-owned entries with other people beyond what provider sync gives.

### Open questions

- Recurrence for individual entries (for example RRULE semantics), separate from fortnight templates.
- Whether stamped entries keep a link back to their template.
- Whether the equal-split rule for overlapping, non-nested entries is good enough.
- Default length of the Just happened window.

### Build phases

1. Domain model, local store and repository, plus nesting, anchoring, locking, editability and time attribution rules, with unit tests and no UI.
2. Fortnight planner: create, move, resize and nest by drag; categories, colors, icons, buffers; best-working-time band.
3. Budget banner and templates (save, stamp, multiple templates).
4. Soon view with escalation, nag via the shell notification surface, and Just happened outcome capture.
5. Reflection view.
6. Sync adapters: Google and Microsoft 365, pull first, then push.
7. Shell integration with Products and Workloads.
