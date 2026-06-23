# Syntax Highlighting

The Edit Text editor colours code and markup with two engines, chosen per file extension by
`HighlightingRegistry` (`src/Nexaflow.Visuals.Text/Editor/Highlighting/HighlightingRegistry.cs`):

| Engine | Used for | What you get |
|--------|----------|--------------|
| **tree-sitter** | real code languages | A full parse tree → colouring, **code folding** (block-like nodes), and structure for AI / graphify (`get_syntax_tree`) |
| **AvalonEdit `.xshd`** | simple markup / config | Token colouring only, retinted to the theme |
| *(none)* | everything else | Plain text — the Windows spell-checker is eligible to attach |

Colours never come from literals: every role resolves to a **`TextSwatch.*`** theme token (see
[theming.md](theming.md)), so each theme art-directs code colours. `TreeSitterColorizer` maps tree-sitter
capture names → `TextSwatch.*`; `XshdTheming` retints AvalonEdit's built-in definitions to the same palette.

## Currently supported

**Tree-sitter (rich — parse tree + colour):**

| Language | Grammar id | Extensions |
|----------|-----------|------------|
| C#         | `c-sharp`    | `.cs` `.csx` |
| JavaScript | `javascript` | `.js` `.mjs` `.cjs` `.jsx` |
| TypeScript | `typescript` | `.ts` `.cts` `.mts` |
| Python     | `python`     | `.py` `.pyw` |
| Ruby       | `ruby`       | `.rb` `.rbw` `.rake` `.gemspec` `.ru` |
| JSON       | `json`       | `.json` |

**AvalonEdit `.xshd` (basic colour):** the markup/config formats AvalonEdit ships a definition for —
XML, **XAML** (`.xaml`, via the XML definition), HTML, CSS, and similar — resolved automatically by
extension and retinted by `XshdTheming`. There is no bundled tree-sitter XML grammar, so XAML/XML use
this path.

## The role palette (`TextSwatch.*`)

Capture/role names map to these theme tokens (defaults in `Tokens.xaml`, per-theme overrides in
`Theme.<name>.xaml`):

`Comment`, `Keyword`, `String`, `Number`, `Type`, `Constant`, `Function`, `Operator`, `Parameter`
(also used for `variable`), `Tag`, `Attribute`.

Add a new role only if an existing one doesn't fit: add the token to `Tokens.xaml` + every
`Theme.<name>.xaml`, then map the capture name in `TreeSitterColorizer.CaptureToToken`.

## Add a tree-sitter language

1. **Confirm the grammar ships.** Grammars are native binaries bundled by `TreeSitter.DotNet`
   (`~/.nuget/packages/treesitter.dotnet/<ver>/runtimes/win-x64/native/tree-sitter-<id>.dll`, 28+ langs).
   `new Language("<id>")` loads `tree-sitter-<id>.dll` / `tree_sitter_<id>`, so the **grammar id must
   match the native name** (e.g. `go`, `rust`, `ruby`, `c-sharp`).

2. **Write a highlight query** in `HighlightQueries.ByGrammar` (`src/Nexaflow.Syntax/HighlightQueries.cs`),
   keyed by grammar id. Conventions:
   - Capture names are roles — use ones the colourizer knows (`@comment`, `@string`, `@number`,
     `@keyword`, `@type`, `@constant`, `@function`, `@variable`, …).
   - **Order patterns low → high priority.** A later match overrides an earlier one for the same span,
     so put a catch-all `(identifier) @variable` first and let specific roles (declarations, calls,
     keywords) come after. A PascalCase→type heuristic is handy: `((identifier) @type (#match? @type "^[A-Z]"))`.
   - **Interpolation / templates:** capture only the literal fragments (e.g. C# `(string_content)`,
     JS/TS `(string_fragment)`) — never the whole interpolated node, or the embedded `${…}` / `{…}`
     identifiers get the string colour. The braces stay uncaptured (plain text).
   - One unknown node type fails the **whole** query. Discover the real node/field names by dumping the
     tree: `CodeHighlighter.TryCreate("<id>")!.GetParseTree(snippet)`. Offsets the binding reports are
     **UTF-16 char offsets** — they map straight to editor document offsets, no conversion.

3. **Register the extensions** in the `HighlightingRegistry` static constructor:
   `RegisterTreeSitter("<id>", ".ext", …);`

4. **Add reference fixtures + run the consistency test.** Drop a source file and its ANSI-highlighted
   reference under `src/Nexaflow.Tests/Nexaflow.Tests.Fixtures/syntax-tests/{source,highlighted}/<Lang>/`
   (the corpus is from [bat](https://github.com/sharkdp/bat) — see
   `src/Nexaflow.Core/Assets/ThirdPartyNotices.md`), then add
   `"<Lang>"` to the folder list in `SyntaxHighlightConsistencyTests`. The test asserts that tokens the
   reference colours identically resolve to a single one of our roles; tune the query until it reports
   **0 violations**. Optionally add a `CodeSamples` entry + a targeted capture assertion in
   `CodeHighlighterTests`.

## Add a basic (`.xshd`) language

AvalonEdit resolves its built-in definitions by extension automatically — nothing to register;
`XshdTheming` retints them. For a format AvalonEdit doesn't ship, register a custom `.xshd` with
`HighlightingManager.Instance.RegisterHighlighting(...)` and let the registry fall through to it (do
**not** add it to `RegisterTreeSitter`).

## Native packaging

Tree-sitter grammars are per-language native DLLs. `Nexaflow.Syntax` carries the `win-x64` RID so they
flow into the app output under `runtimes/win-x64/native/` next to `Nexaflow.Core.exe`; a missing native
surfaces only at runtime as `DllNotFoundException`, so verify a code file actually colours after adding
a grammar.

## File map

| File | Role |
|------|------|
| `src/Nexaflow.Syntax/HighlightQueries.cs` | Per-grammar tree-sitter highlight queries |
| `src/Nexaflow.Syntax/CodeHighlighter.cs` | Wraps a grammar + query; `Highlight` → spans, `GetParseTree` |
| `src/Nexaflow.Visuals.Text/Editor/Highlighting/HighlightingRegistry.cs` | Extension → engine (tree-sitter / xshd / plain) |
| `src/Nexaflow.Visuals.Text/Editor/Highlighting/TreeSitterColorizer.cs` | Capture → `TextSwatch.*`, painted onto AvalonEdit |
| `src/Nexaflow.Visuals.Text/Editor/Highlighting/XshdTheming.cs` | Retints built-in `.xshd` colours to the palette |
| `src/Nexaflow.Core/Themes/Tokens.xaml`, `Theme.<name>.xaml` | `TextSwatch.*` defaults + per-theme overrides |
| `src/Nexaflow.Tests/Nexaflow.Tests.Core/Unit/Editor/SyntaxHighlightConsistencyTests.cs` | Reference-corpus consistency test |
