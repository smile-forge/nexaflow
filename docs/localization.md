# Localization

Nexaflow ships its words — help pages today, UI strings as they move over — in **language packs**: one
resource-only assembly per language, `Languages\Nexaflow.Language.<code>.dll` beside `Nexaflow.exe`. Each project
keeps its own words in its own folder and the build gathers them. A language is picked in Options like a theme,
and like a theme it applies by restarting the window — so the everyday case, one language for good, pays nothing
for the ability to switch.

## The model

| | |
|---|---|
| **Source** | `src/<project>/Localization/<code>/…` — per project, beside the code it describes. A feature needs nothing from anyone else to ship its words. |
| **Build** | `src/Nexaflow.Languages/Nexaflow.Language.<code>/` — a three-line project importing `LanguagePack.targets`, which embeds every `Localization/<code>/` file under `src/*/` and `src/*/*/` (test projects excepted). |
| **Names** | Each file is embedded as `<ProjectFolder>/<path under Localization/<code>/>`, forward-slashed: `Nexaflow.Core/strings.json`, `Nexaflow.Features.Markdown/help/images/markdown/qr-codes.png`. |
| **Ship** | `LanguagePacks.Deploy.targets` (imported by Core and Tests.Core) builds every pack and copies it to `$(OutDir)Languages\`. The installer's harvest ships the folder, and a guard fails the installer build when a pack is missing. |
| **Run** | `LanguageManager` (Core, one per process) lists packs by file name, loads the active pack and English — only those, and only on first use — and serves every resource along the chain *active → English*. |

Packs are **drop-ins**. They are not in `deps.json`; `LanguageManager` loads them by path into a load context of
their own, and nothing in them is code.

## Picking a language

Options → Shell → **Language** lists the installed packs, each under its own name for itself ("English",
"Français"). `ShellConfig.Language` stores the culture code — `en`, `fr`, `pt-BR`. A code with no pack runs in
English, as does the `"English"` an earlier build stored there when the setting was an enum. Saving a change
restarts the window through `ShellServices.RestartWindowForAppearance`, the path a theme change takes, which keeps
both panes and each pane's active tab.

## UI strings

```xml
xmlns:loc="clr-namespace:Nexaflow.Visuals.Common.Localization;assembly=Nexaflow.Visuals.Common"

<TextBlock Text="{loc:Str Help.OtherPages.Header}"/>
```

```csharp
page.Title = Str.Format("Help.Tab.TitleFormat", title);
```

- **Where they live.** `Localization/<code>/strings.json` at a project's root: one flat object, key → text.
  Comments and trailing commas are allowed.
- **Keys** are `<Area>.<Surface>.<Element>`. The area is the owning project's short name — `Markdown` for
  `Nexaflow.Features.Markdown` — and Core owns `Shell` and `Help`. Every project's table merges into one, so the
  area is what keeps them apart.
- **Fallback.** A key the active language lacks shows its English text; a key nobody defines shows the key itself
  — visible, never a crash. `Str.Format` shows a translation whose placeholders don't fit its arguments as written
  rather than throwing.
- **Cost.** `{loc:Str}` resolves once, as the XAML loads: no `DynamicResource`, no binding. The first lookup reads
  every `strings.json` into one frozen table (`StartupTimings` marks it `Language.Strings`); every later lookup is
  a dictionary read.
- **Features** reach `Str` through `Nexaflow.Visuals.Common`, which they already reference. Core sets `Str.Source`
  at startup, the same shape as `TextTypography`, so nothing needs Core.

## Help pages

- A project ships the help for each page kind it registers as `Localization/<code>/help/<StaticPageKind>.md` — the
  file name *is* the page kind. Its first `# ` heading is its title.
- Pictures sit beside it (`help/images/…`) and are linked relatively. They resolve inside the pack, never outside
  the page's own project, and are never fetched from the web.
- `[text](help:Text)` opens another help page in place, and `[text](help:Text#searching)` opens it at a heading; any
  other link opens in the browser.
- Every heading has a GitHub-style anchor — lower-case, spaces to hyphens, punctuation dropped, a repeat numbered `-1` —
  so `[see Searching](#searching)` jumps within the page. This is the markdown renderer's, so it works on any rendered
  markdown surface, not just help.
- **Don't write a contents list.** The pane gives every page with two or more `##` sections a **Topics** list where its
  introduction ends, and a **↑ Back to topics** link at the end of each section (`HelpContents`) — generated from the
  headings as the page loads, so it can't drift, and each translation gets its own.
- `Nexaflow.Core/help/index.md` introduces help. It is shown — followed by a generated list of every help page —
  for a page kind with no help yet, and under the pane's **☰** button.
- A help page is the owning feature's **`docs` concern** in the product tree: give the feature node a `docs`
  snaplink to its English page (`nfi add-snaplink <node> --type markdown --concern docs --doc <path> --title-path <H1>`).

How the pane opens beside a page, follows it and searches is covered under `Help/` in
[Architecture.md](Architecture.md#nexaflowcore), and in the pane's own help,
`src/Nexaflow.Core/Localization/en/help/Help.md`.

## Adding a language

1. Copy `src/Nexaflow.Languages/Nexaflow.Language.en/` to `Nexaflow.Language.<code>/` and set `<LanguageCode>`.
2. Add it to `Nexaflow.slnx` under `/Nexaflow.Languages/` — `SolutionMembershipTests` insists.
3. Add `Localization/<code>/` beside each project's `Localization/en/`: a `strings.json` with the keys translated so
   far, and any help pages. Whatever is left out falls back to English.

Never put a culture in a file name under `Localization/` (`Help.fr.md`): MSBuild's culture detection would split it
off into a satellite assembly instead of the pack.

## What keeps it honest

| Guard | Catches |
|---|---|
| `LanguagePackContentTests` | a shipped pack that doesn't hold exactly the source files; a mangled name; a satellite |
| `LocalizationContentGuardTests` | a help page for a page kind its project doesn't register; an index outside Core; a missing picture; a key used in code but missing from English, or outside its project's area; a translation with keys or pages English lacks |
| `LanguageManagerTests`, `LocalizedStringsTests` | discovery that loads packs; the fallback chain; a pack loaded twice |
| `nexaflowSetup.wixproj` | an installer payload missing a pack |

## Not localized yet

Everything outside the Help pane and the Help button: the rest of the shell's chrome, feature views, AI prompts
and error messages. Moving a string over is mechanical — add its key to the project's `strings.json` and replace the
literal with `{loc:Str …}` or `Str.Get(…)` — and the guard checks each one as it lands.
