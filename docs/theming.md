# Nexaflow Theming

> **Freshness:** written 2026-06-01. Covers the region-token + scene model that replaced the flat
> per-theme colour file. Pairs with [Architecture.md → Extensibility Points](Architecture.md#extensibility-points).

## What this solves

The old model was one `Colors.<Theme>.xaml` per theme: a flat list of `SolidColorBrush`es named by
*tone* (`AccentBrush`, `Surface2Brush`, `DeepBgBrush`). Two problems:

1. **Tone names don't say where a colour is used.** The AI bar and the file list both resolved to
   `Surface2Brush`, so you couldn't restyle one without the other.
2. **A theme could only be flat colours.** No way to express an immersive backdrop (the Ocean reef:
   drifting fish, rising bubbles, swaying coral, god rays).

The model below keeps the flat palette underneath (so Dark/Light stay simple and pixel-identical) and
layers two new concerns on top: **region tokens** (semantic, namespaced colour handles) and **scenes**
(theme-supplied animated backdrops behind a named region).

## The layered model

A theme is no longer one file. `ThemeManager.Apply` assembles the application's merged dictionaries in a
fixed order; **merge order = precedence, earliest is lowest** (WPF resolves a key from the last
dictionary that contains it):

```
1. Colors.<Theme>.xaml   the raw palette (BgColor, AccentBrush, … — unchanged from before)
2. Tokens.xaml           region tokens ({Region}.{Role}) that default-alias the palette
3. <feature contributions>   optional IThemeContribution dictionaries (fallbacks; usually none)
4. Theme.<Theme>.xaml    per-theme region overrides + Scene.* templates (optional; absent = Pro look)
5. Styles.xaml           shared control templates (reference the above by key)
```

`ThemeManager.Apply(theme, contributions)` runs once at startup (`App.xaml.cs`, after
`FeatureManager.RegisterFeatures()` so feature contributions are known). `App.xaml` holds a Dark
bootstrap merge for the designer; `Apply` rebuilds the list deterministically.

> **Theme switching restarts the window.** References are overwhelmingly `{StaticResource}` (resolved
> once, at load) for rendering performance — a deliberate choice, so an open window does not live-reflow.
> Instead, changing the theme in Options runs `ThemeManager.Apply` and then
> `ShellServices.RestartWindowForTheme` rebuilds the acting window in place — same position, same tabs
> (order + active tab preserved), now rendered against the new theme.

> **Markdown follows the theme too.** `MarkdownPalette.FromTheme()` (in `Nexaflow.Visuals.Text`) reads
> the active theme's brushes (`TextBrush`, `AccentBrush`, surfaces…) so rendered markdown — AI chat,
> the response overlay, the editor — gets dark text on light themes and the theme's own accents; the
> chart/graph renderers take a palette too, with a shared `MarkdownPalette.Series` mini-palette for
> series colours. Fixed-surface callers (scratchpad post-its) still pass `MarkdownPalette.Light`.

## Token contract

### Base palette (layer 1)

Every `Colors.<Theme>.xaml` supplies the same key set — both a `Color` and a matching `…Brush`:
`Bg`, `Surface`, `Surface2`, `Border`, `BorderLight`, `Accent`, `Accent2`, `Text`, `TextMuted`,
`TextDim`, `DeepBg`, plus `AccentGradientBrush` / `BarGradientBrush` and the
`TopBarHeight` / `TabBarHeight` / `InteractionHeight` dimensions. This is the legacy contract — most
feature views still bind these directly, and that's fine (see *Feature participation*).

> **No tone-named colour keys.** The palette is deliberately structural/neutral — there is **no**
> `GreenBrush`/`OrangeBrush` (removed). A "green" or "amber" need is a *purpose*: use the semantic
> tokens `SuccessBrush` / `WarningBrush` / `DangerBrush` (Tokens.xaml), or the categorical `Swatch.*`
> bank for syntax/series colours. This keeps callers from reaching for a tone when they mean a meaning.

### Region tokens (layer 2)

Namespaced `{Region}.{Role}` keys a shell region binds **instead of** the raw palette. Each defaults to
a palette colour in `Tokens.xaml`; a theme overrides only the ones it wants to art-direct. The name says
*where* the colour is used.

| Region | Tokens | Bound by |
|--------|--------|----------|
| `Chrome` | `Chrome.Deep`, `Chrome.Panel`, `Chrome.Bg`, `Chrome.Border` | `MainWindow.xaml` (drag bar, logo column, top bar, ribbon strip, right panel), `RibbonControl.xaml` (bevel) |
| `Ribbon` | `Ribbon.ButtonBg` | `Styles.xaml` (`RibbonButton` / `RibbonHalfButton` resting fill) |
| `TabStrip` / `Tab` | `TabStrip.Bg`, `TabStrip.Border`, `Tab.HoverBg`, `Tab.ActiveBg`, `Tab.Accent` | `PaneView.xaml` (header + breadcrumb divider), `TabStrip.xaml` (tab item states) |
| `AiBar` | `AiBar.Bg`, `AiBar.InputBg`, `AiBar.ClusterBg`, `AiBar.Border` | `MainWindow.xaml` (row 4 — surround, input panel, button clusters) |
| `FileList` | `FileList.PanelBg` | `FileSystemView.xaml` (tree + action strip; a *feature* consuming a Core token) |
| `Page` | *(none yet — passthrough)* | `PaneView.xaml` wraps the content host; relies on a translucent `BgBrush` + the window scene |
| `Window` | *(scene only — `Window.Bg` intentionally absent)* | `MainWindow.xaml` back layer |

`Ribbon.ButtonBg`, `AiBar.ClusterBg` and `FileList.PanelBg` show the pattern for keeping Dark identical
while restyling Ocean: each defaults (in `Tokens.xaml`) to the value that reproduces the old look
(`Transparent`, `Transparent`, `Surface`) and is overridden only in `Theme.Ocean.xaml`.

**Rule:** a region binds its own region tokens, never the palette directly. In Dark every token aliases
the palette → identical output. In Ocean the tokens go translucent so the reef shows through.

To add a token: declare it in `Tokens.xaml` aliasing the closest palette colour (keeps Dark identical),
then bind it from the region's XAML. Override it per-theme in `Theme.<Theme>.xaml`.

## Scenes — non-colour theming

### `ThemedRegion`

`Nexaflow.Visuals.Common/Controls/ThemedRegion.cs` (templated in that assembly's `Themes/Generic.xaml`)
is the generic mechanism. Wrap any region's content in it and give it a `Region` name:

```xml
<vcc:ThemedRegion Region="AiBar">
    <!-- the region's existing content, untouched -->
</vcc:ThemedRegion>
```

On load it resolves two keys from the active theme and renders three layers, bottom → top:

1. `Scene.{Region}` — a `DataTemplate` (animated backdrop). Absent → nothing.
2. `{Region}.Bg` — a `Brush` veil/tint over the scene. Absent → transparent.
3. the region's own content.

A Pro theme supplies neither → the wrapper is inert and the region looks exactly as before (zero extra
visuals, no layout change). An immersive theme drops a scene behind the exact region it names.

> **Translucency needs an opaque layer behind it.** A scene only shows where the content above it is
> (semi-)transparent. That's why Ocean makes its surface tokens (and the page's `BgBrush`) translucent.
> Conversely, a translucent surface with *no* scene behind it shows the window background — so put the
> scene first (e.g. the window-wide `Scene.Window`), then make surfaces translucent over it.

### Window-wide vs per-region scenes

Both are supported; pick per theme:

- **One `Scene.Window`** behind every row (a single `ThemedRegion Region="Window"` spanning `RootGrid`,
  `IsHitTestVisible=False`, `ZIndex=-10`) → a continuous backdrop. Make all shell surfaces translucent
  and the one scene reads cohesively through the whole window. *This is what Ocean uses.*
- **Per-region scenes** (`Scene.Page`, `Scene.AiBar`, …) → independent art per region. Use when regions
  should differ (e.g. a distinct treatment for the AI area vs the file list).

### The Ocean scene

`Themes/OceanReefScene.xaml(.cs)` is a procedural `UserControl` (sunlit-reef gradient, sun glow, god
rays, floor caustics, vivid fish, colourful coral, highlight bubbles). It's theme art — referenced only
by `Theme.Ocean.xaml`, never by `ThemedRegion`. Density scales with region size; it never hit-tests.
Colours were taken from the source HTML mock.

## Authoring a new theme

**Pro theme (flat, professional)** — like Dark/Light:
1. `Colors.<Name>.xaml` with the full base-palette key set.
2. `Theme.<Name>.xaml` is optional — skip it (like Dark) for a pure palette, or add region overrides
   *without* a scene (like Light's grey-frame zoning) to art-direct the chrome.
3. Add the name to the `ThemeOption` enum (`ShellConfig.cs`).

**Immersive theme** — like Ocean:
1. `Colors.<Name>.xaml` — palette tuned so surfaces are translucent enough for the scene to read while
   text stays legible (Ocean: deep-teal glass surfaces, light text, bright accent).
2. `Theme.<Name>.xaml` — override the region tokens you want translucent + a `Scene.{Region}` template
   (typically `Scene.Window`).
3. The scene visual: a `UserControl` in `Themes/` (or a `VisualBrush`/`ShaderEffect` behind the same
   `Scene.*` key — `ThemedRegion` only cares that it's a `DataTemplate`).
4. Add the name to `ThemeOption`.

## Feature participation

The only coupling between a feature, the shell, and a theme is **string resource keys** — the
token/region/scene contract. Core never references a feature; a feature never references Core or a theme.
Three optional levels:

1. **Free** — bind the generic palette/semantic keys (`BgBrush`, `Surface2Brush`, `TextBrush`, …). The
   feature is themed in every theme with no work; an immersive theme that makes `BgBrush` translucent
   gets the scene behind the page for free.
2. **Named region** — wrap the view root in `ThemedRegion Region="MyFeature"` and bind `MyFeature.*`
   tokens. Any theme *may* art-direct `Scene.MyFeature` / `MyFeature.Bg`; if none does, it falls back to
   the generic surface.
3. **Contributed defaults** — ship a `ResourceDictionary` in the feature assembly and advertise its pack
   URIs via `IThemeContribution` (`Features.Common`). `FeatureManager` discovers it by reflection across
   the loaded `Nexaflow.Features.*.dll`s (same path as `IPageRegistration`) and `ThemeManager` merges it
   at layer 3 — below the active theme, so a theme can override any contributed key by name.

```csharp
public sealed class MyThemeContribution : IThemeContribution
{
    public IReadOnlyList<Uri> ResourceDictionaryUris =>
    [
        new("pack://application:,,,/Nexaflow.Features.MyFeature;component/Theming/MyFeature.xaml"),
    ];
}
```

Add a feature later → existing themes don't name its region → it falls back. Update a theme → it can
art-direct a feature it was never compiled against. Both sides stay independently shippable.

## Rule: a feature never hard-codes a colour

Every colour a feature paints — **even one it "owns"** (a status pip, a chart/pie series, a selection
or search wash, post-it paper) — must resolve from a theme resource so a theme can retune it. There is
no "this colour is intrinsic to the feature" exception: expose it as a token *just in case*. Never ship
a literal `#RRGGBB`, `Color.FromRgb(...)` or `Brushes.X` as the **final** value. In order of preference:

1. **Reuse an existing token** — palette (`TextBrush`, `AccentBrush`, `BorderBrush`, surfaces…) or
   semantic (`SuccessBrush` / `WarningBrush` / `DangerBrush`, `OnAccentBrush`).
2. **The categorical `Swatch.*` bank** when you need *N mutually-distinct* colours (pie/chart series,
   category dots, colour pickers). Distinctness is the point — don't use the close-together chrome tones.
3. **A feature-owned token via `IThemeContribution`** when the colour is specific to the feature (e.g.
   the scratchpad's `PostIt.*`, a log-level tint). It merges below the active theme, so any theme
   overrides it by string key. This is the same seam as `IPageRegistration` — no Core⇄feature reference.

**XAML** binds the key: `Foreground="{StaticResource SuccessBrush}"`. **Code-drawn surfaces** (OnRender
panels, AvalonEdit colorizers) can't bind, so read the resource at paint time with a literal *fallback*:

```csharp
Application.Current?.Resources["AccentBrush"] as Brush ?? Brushes.DodgerBlue   // fallback ONLY for design-time/tests
```

The literal is the last resort, never the source of truth. For a translucent wash, pull the token's
`Color` and reapply alpha (see `VirtualizedRowsControl.SelectionWash` / `HexRenderPanel.MakeSemiAccent`).
Theme switching restarts the window, so a fresh read per paint always reflects the current theme.

**Genuinely-not-a-colour exceptions** (leave as literals): `Transparent`; drop-shadow `Color="Black"`;
modal scrims (`#CC000000`); and scene art (`OceanReefScene` etc., which *is* the theme).

## Keeping Dark (and the other plain themes) identical

- Region tokens in `Tokens.xaml` alias the exact palette brush each region used before → no visual
  change for any theme that doesn't override them.
- `ThemedRegion` with no `Scene.*` / `{Region}.Bg` renders nothing extra.
- `Theme.Dark.xaml` is intentionally empty — a pure palette (`ThemeManager.Load` would also skip a
  missing optional layer entirely). `Theme.Light.xaml` is a *Pro* theme — region overrides only (a soft
  grey frame around lighter content), no scene. `Theme.Ocean.xaml` (reef), `Theme.Nature.xaml` (forest)
  and `Theme.Sandstone.xaml` (cut-stone wall) are *dark immersive* — overrides + a `Scene.Window`
  (`OceanReefScene` / `ForestScene` / `SandstoneWall`). `Theme.Sunny.xaml` is a *light immersive* — a
  bright sky with drifting balloons/confetti (`SunnyScene`) behind translucent white panels, showing
  the same `Scene.Window` mechanism works for light themes too.
- Cross-dictionary `{StaticResource}` (a token in `Tokens.xaml` aliasing a colour in `Colors.*.xaml`)
  resolves because the palette is merged first — the same pattern `Styles.xaml` already relied on.

## File map

| File | Role |
|------|------|
| `Core/ThemeManager.cs` | Assembles the merged dictionaries in precedence order; skips missing optional layers |
| `Core/Themes/Colors.<Theme>.xaml` | Layer 1 — the base palette (one per theme) |
| `Core/Themes/Tokens.xaml` | Layer 2 — region tokens, default-aliasing the palette |
| `Core/Themes/Theme.<Theme>.xaml` | Layer 4 — per-theme region overrides + `Scene.*` (Dark = empty, Ocean = the reef) |
| `Core/Themes/Styles.xaml` | Layer 5 — shared control templates |
| `Core/Themes/OceanReefScene.xaml(.cs)` | The Ocean scene (theme art) |
| `Visuals.Common/Controls/ThemedRegion.cs` + `Themes/Generic.xaml` | The generic scene/veil wrapper |
| `Features.Common/IThemeContribution.cs` | The optional feature contribution seam |
| `Core/App.xaml`, `App.xaml.cs` | Bootstrap merge + the startup `Apply` call |

## Roadmap / not done yet

- **Per-feature region tokens (opt-in, panels only).** Most feature views bind the generic palette
  (`SurfaceBrush`/`Surface2Brush`/`BorderBrush`…) directly. That's correct and fully themed — *not* a
  bug — but it means a theme can't art-direct one feature differently from the rest (e.g. tint the
  Console panel unlike the JSON tree, or show a `Scene.*` behind only one feature), because they all
  resolve to the same shell-wide brushes. Making a feature independently styleable is the
  `FileList.PanelBg` pattern: wrap its container(s) in `ThemedRegion Region="<Feature>"`, add
  `<Feature>.*` surface/border tokens to `Tokens.xaml` that default-alias the palette (so plain themes
  stay pixel-identical), repoint that feature's *panel* bindings to them, then let a theme override
  the tokens / supply `Scene.<Feature>`. **Scope when doing this:**
  - It only covers **surface/background/border** containers (panels, headers, list/row fills). Leave
    `TextBrush`/`AccentBrush` foregrounds on the shared tokens — text and accents should stay consistent.
  - It does **not** include the shared control templates in `Styles.xaml` (buttons, combo, scrollbar,
    toggle, textbox): those are app-wide controls and should look the same everywhere, so they keep
    binding the palette directly.
  - It's **opt-in per feature** — only worth doing for a feature you actually want a theme to treat
    distinctly. Adding region tokens for all features up-front just creates palette-aliasing churn with
    no visible change until a theme uses them.
- Per-theme tuning of the remaining immersive themes (Sunny, Nature, Sandstone) toward their own scenes.
- Optional "reduce motion / performance" switch to disable scenes (falls back to the flat `{Region}.Bg`).
