# Syntax Highlighting

The Edit Text editor colours code and markup with two engines, chosen per file extension by
`HighlightingRegistry` (`src/Nexaflow.Visuals.Text/Editor/Highlighting/HighlightingRegistry.cs`):

| Engine | Used for | What you get |
|--------|----------|--------------|
| **tree-sitter** | every language with a grammar — code *and* markup | A full parse tree → colouring, **code folding** (block-like nodes), the class/method outline snaplinks resolve against, and structure for AI / graphify (`get_syntax_tree`) |
| **AvalonEdit `.xshd`** | the fallback: a format AvalonEdit knows and no grammar claims | Token colouring only, retinted to the theme. No tree, so no outline, folding or injection |
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
| Rust       | `rust`       | `.rs` |
| C++        | `cpp`        | `.cpp` `.cc` `.cxx` `.hpp` `.hh` `.hxx` `.ipp` |
| C          | `c`          | `.c` `.h` |
| Java       | `java`       | `.java` |
| HTML       | `html`       | `.html` `.htm` |
| CSS        | `css`        | `.css` |
| ERB        | `embedded-template` | `.erb` |
| Razor      | `razor`      | `.razor` `.cshtml` |
| PHP        | `php`        | `.php` `.phtml` |
| Jinja      | `jinja` *(→html native)* | `.j2` `.jinja` `.jinja2` |
| XAML       | `xaml` *(→xml native)* | `.xaml` |
| XML        | `xml`        | `.xml` `.xsl` `.xslt` `.wxs` `.wxl` `.props` `.targets` `.manifest` |

Several of these are **injection hosts** — they embed another language (see *Embedded languages* below).
Two are **aliases**, parsing with another language's native grammar while keeping their own id
(`CodeHighlighter.NativeAlias`): `jinja` parses with html and routes injection by its own id; `xaml`
parses with xml so the structure extractor can read WPF meaning — `x:Class`, `x:Name`, `x:Key`, event
handlers — out of the same tree, while a plain `.xml` gets a generic element outline.

The XML rows are why the build and the installer are no longer opaque: `.wxs`/`.wxl` author the MSI and
`.props`/`.targets` carry the logic every project inherits, so all of them now parse, outline and take a
snaplink like any other source. (`.wixproj` reads through the csproj path — it is a project file with
`PropertyGroup`s like any other.)

> Jupyter notebooks (`.ipynb`) are **not** handled here — a notebook is a structured cell document, not a
> flat source file, so it has its own `Nexaflow.Features.Notebook` feature (cell viewer + per-cell outline).
> That feature reuses the read-only `CodeBlockView` (in `Nexaflow.Visuals.Text`) to highlight each code cell.

**AvalonEdit `.xshd` (colour only, no tree):** the fallback for the extensions AvalonEdit ships a
definition for that no tree-sitter grammar claims — PowerShell (`.ps1`), T-SQL (`.sql`), TeX (`.tex`),
VB (`.vb`), patch files, ASPX, Boo — resolved by extension and retinted by `XshdTheming`.

It is a fallback rather than a tier: `Resolve` asks `TreeSitterLanguages` first and reaches `.xshd` only
when nothing matched, so a language leaves this path the moment its grammar is registered. C#, C++, CSS,
HTML, Java, JavaScript, JSON, PHP, Python and the whole XML family have all already left it — their
`.xshd` definitions still ship inside the package and are simply never reached.

**Colour is all it gives**, which is the thing to notice about a format that is only here. `.sql` is
coloured but has no outline, no folding, no injection spans and nothing a snaplink can name, because
every one of those comes from a parse tree — SQL is detected as an injection site and waits on a grammar
(`parser-sql-graphql`). `.xshd` also serves the one surface that knows its language **by name rather than
by file**: `HighlightingRegistry.Themed("XML")`, the PE inspector's manifest pane, which has no extension
to resolve.

## Embedded languages (injection)

A file can embed another language — JavaScript/CSS inside HTML's `<script>`/`<style>`, Ruby inside an ERB
`<% %>`, SQL/HTML inside a Ruby heredoc, Python inside a Jinja `{{ }}`.
`LanguageInjections` (`src/Nexaflow.Syntax/LanguageInjections.cs`) finds those sub-ranges; the
highlighter re-parses each substring with a cached **child** highlighter and merges the spans (offset into
parent coordinates), recursing so nesting works (ERB → HTML → `<script>` → JS). The same ranges drive code
folding, the spliced AST s-expression (`get_syntax_tree`), and the **class viewer** (each embedded region
renders as a linked `namespace` sub-graph).

Detection is **decoupled** from rendering: a site for a language we don't ship yet (`sql`, `graphql`) is
still detected — it simply produces no spans until that grammar is added, at which point it lights up with
no code change. To add a missing target language, follow *Add a tree-sitter language* below; the injection
rule already points at it.

An injection is either **isolated** (a self-contained block parsed on its own) or **combined** (a
`GroupKey` on the range groups non-contiguous fragments of one language into a single
`Parser.IncludedRanges` parse, so they form one coherent tree in original-document coordinates).

| Host | Embeds | Rule (`LanguageInjections.Find`) | Mode |
|------|--------|----------------------------------|------|
| `html` | `<script>`→`javascript`, `<style>`→`css` | the element's `raw_text` child | isolated |
| `ruby` | heredoc tag → `html`/`css`/`javascript`/`json`/`embedded-template` (`sql` no-op) | `heredoc_body`, language by `<<~TAG` | isolated |
| `embedded-template` (ERB) | `code`→`ruby`, `content`→`html` | by node type | **combined** |
| `php` | `text`→`html` | the raw-HTML `text` nodes | **combined** |
| `razor` | *(none — the grammar unifies C# + markup; coloured directly)* | — | — |
| `jinja` | `{{ }}` / `{% %}`→`python` (+ html script/style) | text scan | isolated |
| `javascript`/`typescript` | `` gql`…` `` / `` graphql`…` ``→`graphql` | tagged-template call (no-op until grammar) | isolated |

Combined injection is what lets PHP's HTML around `<?php?>` parse as one document (so the trailing
`</body></html>` close tags colour) and ERB's `<% if %>…<% end %>` pair across directives. `IncludedRanges`
is char-indexed (consistent with `Node.StartIndex`), so it's Unicode-safe.

## The role palette (`TextSwatch.*`)

Capture/role names map to these theme tokens (defaults in `Tokens.xaml`, per-theme overrides in
`Theme.<name>.xaml`):

`Comment`, `Keyword`, `String`, `Number`, `Type`, `Constant`, `Function`, `Operator`, `Parameter`
(also used for `variable`), `Tag`, `Attribute`.

Add a new role only if an existing one doesn't fit: add the token to `Tokens.xaml` + every
`Theme.<name>.xaml`, then map the capture name in `TreeSitterColorizer.CaptureToToken`.

## Add a tree-sitter language

1. **Add a row to `tools/tree-sitter-grammars.props`.** Grammars are **compiled from pinned submodules**,
   not taken from a package: `TreeSitter.DotNet`'s prebuilt natives freeze at whatever its own submodules
   pointed at when it was published, and its C# grammar predated `= []` and `[.. var rest]` — one slice
   pattern cost `Program.cs` its entire parse (root `ERROR`, no type nodes, invisible to the graph).
   That props file is the single source of truth for the set: `Nexaflow.Syntax.csproj` imports it,
   `tools/build-tree-sitter-natives.ps1` compiles what it lists, and `tools/ensure-submodules.ps1` reads it
   to know which nested submodules a fresh worktree must populate. `SourceDir` is where that grammar's C
   lives (usually `src`); `Root` is needed only when the grammar is its own submodule rather than one
   nested inside the bindings repo (`xml` is the only one today).

   The **id decides two names** — the DLL (`tree-sitter-<id>.dll`) and the export the runtime binds
   (`tree_sitter_<id>`) — so it must match what `CodeHighlighter` passes to `new Language(...)`. An alias
   (`xaml`→`xml`, `jinja`→`html`) resolves to another id and needs no row of its own.

   A grammar that isn't a submodule yet has to become one first, on the same fork/branch/pin convention as
   every other external — but a grammar is C source with no `.csproj` to reference, so it is wired by the
   MSBuild target above rather than a `ProjectReference`. [externals.md](externals.md) → *Native grammar
   submodules* is the runbook; read it before adding one.

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

3. **Register the extensions** in the `TreeSitterLanguages` static constructor
   (`src/Nexaflow.Syntax/TreeSitterLanguages.cs`): `Register("<id>", ".ext", …);`. The map lives there
   rather than in the editor so headless callers — the snaplink validator and its CLI — can resolve a
   grammar, and therefore an outline, without dragging in WPF/AvalonEdit.
   `HighlightingRegistry.RegisterTreeSitter` still exists and forwards here, as the editor-side entry point.

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

The runtime (`tree-sitter.dll`) and every grammar (`tree-sitter-<id>.dll`) are compiled into
`src/Nexaflow.Syntax/obj/native/` by the `BuildTreeSitterNatives` target — one script invocation for the
whole set (~16s cold, nothing when warm), with objects under `obj/native/int/` so the superproject never
sees a submodule as dirty.

Each one must land in the consuming app's output **root**, not a `runtimes/` subfolder: the runtime is
bound by a plain `DllImport("tree-sitter")` and a grammar is resolved by id through `LoadLibrary`, and
both probe the app directory. `Nexaflow.Syntax` declares them as `Content`, which flows through
ProjectReferences to the app two hops out (whose real output is the RID subfolder
`bin\x64\<Config>\<tfm>\win-x64\`). A missing native surfaces only at runtime as `DllNotFoundException`,
so verify a file of that language actually colours after adding a grammar.

## File map

| File | Role |
|------|------|
| `tools/tree-sitter-grammars.props` | **The grammar set** — one row per language; read by the csproj, the native build script and the submodule bootstrap |
| `src/Nexaflow.Syntax/TreeSitterLanguages.cs` | Extension → grammar id (WPF-free, so headless callers can resolve one) |
| `src/Nexaflow.Syntax/HighlightQueries.cs` | Per-grammar tree-sitter highlight queries |
| `src/Nexaflow.Syntax/CodeHighlighter.cs` | Wraps a grammar + query; `Highlight` → spans, `GetParseTree`; recurses into embedded languages |
| `src/Nexaflow.Syntax/LanguageInjections.cs` | Finds embedded-language sub-ranges per host grammar |
| `src/Nexaflow.Visuals.Text/Editor/Highlighting/HighlightingRegistry.cs` | Extension → engine (tree-sitter / xshd / plain) |
| `src/Nexaflow.Visuals.Text/Editor/Highlighting/TreeSitterColorizer.cs` | Capture → `TextSwatch.*`, painted onto AvalonEdit |
| `src/Nexaflow.Visuals.Text/Editor/Highlighting/XshdTheming.cs` | Retints built-in `.xshd` colours to the palette |
| `src/Nexaflow.Core/Themes/Tokens.xaml`, `Theme.<name>.xaml` | `TextSwatch.*` defaults + per-theme overrides |
| `src/Nexaflow.Tests/Nexaflow.Tests.Visuals/Editor/SyntaxHighlightConsistencyTests.cs` | Reference-corpus consistency test |
| `src/Nexaflow.Tests/Nexaflow.Tests.Components/Syntax/CodeHighlighterTests.cs` | Grammar/capture assertions (the engine's own suite — no WPF) |
