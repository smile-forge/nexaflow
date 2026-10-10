using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// One document holding a bit of every language, built rather than written out.
///
/// <para>
/// The rest of the markdown samples each show one language off, which makes them a poor picture of what opening a real
/// page costs: the largest of them is prose alone and nests nothing, so a profile taken from it names only the types
/// prose allocates. This one is prose with the usual marks on it, a table whose cells are formulas, a tune, a
/// scannable code, two kinds of chart, a plot, a conversation, and charts whose boxes are themselves formulas — so a
/// document, a diagram and a formula are all on the page at once, three languages deep.
/// </para>
/// <para>
/// Generated, because what it is is a shape rather than a text: a handful of sections, each the same shape with
/// different words in it, and a block of one language or another dropped in every few. Written out instead it would
/// be two thousand lines of literal saying what forty lines of this say, and nobody could change the mix without
/// retyping the file. <see cref="MixedLines"/> is held exactly so that a change in what it costs to open is a change
/// in the engine rather than in the fixture.
/// </para>
/// </summary>
internal sealed partial class MarkdownSamples
{
    /// <summary>How many lines the document comes to — held exactly, so its cost is comparable run to run.</summary>
    private const int MixedLines = 2000;

    private static readonly string[] MixedTopics =
    [
        "Reading", "Writing", "Laying out", "Painting", "Measuring", "Caching", "Editing", "Moving",
        "Spelling", "Nesting", "Staging", "Printing", "Selecting", "Dragging", "Undoing", "Scrolling",
        "Hosting", "Binding", "Theming", "Counting", "Breaking", "Wrapping", "Aligning", "Finishing",
    ];

    /// <summary>Paragraphs that say something true about the engine, so the prose is not filler.</summary>
    private static readonly string[] MixedProse =
    [
        "Every piece of the tree says which characters it was cut from, counted in the document rather than in whatever slice its own parser was handed. That is what lets a gesture name a stretch of source instead of a node, and what lets the engine throw the tree away and read it again without losing where the caret was.",
        "A stage may rewrite the tree as much as it likes, so long as printing it gives back exactly the characters that came in. The rule is checked after each one rather than once at the end, so a stage that breaks it is named rather than merely suspected.",
        "Nothing is reprinted on an edit. A gesture answers with a list of writes in the coordinates the document had before the edit, plus where the caret should stand afterwards, and the engine applies them and reads the whole thing again.",
        "The builder turns a tree into pieces and marks and nothing else. It never looks at the source, so a piece knows what it is without anything matching spans up afterwards, and two pieces drawn from the same reading cannot disagree about it.",
        "Measuring a word means shaping it, which is the dearest thing on the page. Widths are remembered per face and per size, so a document that has not changed around a word is not measured again on the next keystroke.",
        "A block that nothing has changed is kept as it was read, moved along by however far the characters in front of it grew. Comparing the characters rather than the offsets is what keeps a keystroke from invalidating every block below it.",
        "Where a language cannot hold what was asked of it, nothing is written. A percent sign in a formula, a quote in a label, a bracket in a name: each goes in spelled as that language spells it, or does not go in at all.",
        "Pictures are decoded once and held, which is why they are the largest thing the process keeps. A page of them is cheap to scroll and dear to open, and that is the trade as it stands.",
    ];

    private static readonly string[][] MixedLists =
    [
        ["what it reads", "what it rewrites", "what it draws"],
        ["parse", "nest", "stage", "build", "paint"],
        ["the source is the truth", "the tree is a reading of it", "the layout is a drawing of the tree"],
        ["a width", "a height", "a baseline"],
    ];

    /// <summary>The rows of the formula table: a name, what it says in symbols, and where it comes from.</summary>
    private static readonly (string What, string Symbols, string From)[] MixedFormulas =
    [
        ("Mass-energy", @"$E = mc^2$", "rest energy of a body"),
        ("Gaussian integral", @"$\int_{-\infty}^{\infty} e^{-x^2}\,dx = \sqrt{\pi}$", "the error function at its limit"),
        ("Euler", @"$e^{i\pi} + 1 = 0$", "the five constants in one line"),
        ("Binomial", @"$\binom{n}{k} = \frac{n!}{k!\,(n-k)!}$", @"ways of choosing $k$ from $n$"),
        ("Basel", @"$\sum_{n=1}^{\infty} \frac{1}{n^2} = \frac{\pi^2}{6}$", "Euler, 1734"),
        ("Stirling", @"$n! \approx \sqrt{2\pi n}\left(\frac{n}{e}\right)^n$", "large factorials"),
        ("Cauchy-Schwarz", @"$\left|\langle u, v\rangle\right| \le \lVert u \rVert \lVert v \rVert$", "inner product spaces"),
        ("Bayes", @"$P(A \mid B) = \frac{P(B \mid A)\,P(A)}{P(B)}$", "updating on evidence"),
        ("Fourier", @"$\hat{f}(\xi) = \int_{-\infty}^{\infty} f(x)e^{-2\pi i x\xi}\,dx$", "a signal by frequency"),
        ("Maxwell", @"$\nabla \cdot \mathbf{E} = \frac{\rho}{\varepsilon_0}$", "charge makes field"),
        ("Schrodinger", @"$i\hbar\frac{\partial}{\partial t}\Psi = \hat{H}\Psi$", "how a state moves"),
        ("Navier-Stokes", @"$\rho\left(\frac{\partial v}{\partial t} + v\cdot\nabla v\right) = -\nabla p + \mu\nabla^2 v$", "a fluid, pushed"),
    ];

    /// <summary>Formulas set on a line of their own, which is where a formula is drawn larger.</summary>
    private static readonly string[] MixedDisplay =
    [
        @"\frac{\partial^2 u}{\partial t^2} = c^2 \nabla^2 u",
        @"\begin{matrix} a & b & c \\ d & e & f \\ g & h & i \end{matrix}",
        @"\oint_{\partial \Sigma} \mathbf{B} \cdot d\boldsymbol{\ell} = \mu_0 I_{\text{enc}}",
        @"\zeta(s) = \prod_{p \text{ prime}} \frac{1}{1 - p^{-s}}",
        @"\begin{aligned} x + y &= 7 \\ 2x - y &= 2 \end{aligned}",
        @"\mathbf{J} = \sigma \mathbf{E} + \rho_v \mathbf{v}",
    ];

    /// <summary>The document, as its lines.</summary>
    private static string Mixed()
    {
        var lines = new List<string>(MixedLines);

        Opening(lines);
        Formulas(lines);
        Tune(lines);
        Charts(lines);
        Conversation(lines);
        Nested(lines);

        // Whole sections while one still fits, so nothing is ever cut through a fence.
        var at = 0;
        while (true)
        {
            var next = Section(at);
            if (lines.Count + next.Count > MixedLines - 4) break;

            lines.AddRange(next);
            at++;
        }

        Filling(lines);

        return string.Join("\n", lines) + "\n";
    }

    private static void Opening(List<string> lines) => lines.AddRange(
    [
        "# A document of everything",
        "",
        "A page that holds a bit of all of it: prose with the usual marks on it, a table of formulas, a",
        "tune, a code that scans, two kinds of chart, a plot, a conversation, and a chart whose boxes are",
        "themselves formulas. It is here to be *measured* rather than read — every language the editor",
        "draws appears, several of them nested inside another, and the prose around them is long enough",
        "that laying it out is real work.",
        "",
        "> Nothing in here is advice. The numbers are invented and the physics is borrowed.",
        "",
    ]);

    private static void Formulas(List<string> lines)
    {
        lines.AddRange(
        [
            "## A table that is mostly formulas",
            "",
            "Each row names a thing and says it in symbols, so the table is a row of nested formulas with",
            "words either side of them.",
            "",
            "| Quantity | In symbols | Where it comes from |",
            "| --- | --- | --- |",
        ]);

        foreach (var (what, symbols, from) in MixedFormulas) lines.Add($"| {what} | {symbols} | {from} |");

        lines.Add("");
        lines.Add("Set out on their own they take more room and read better:");
        lines.Add("");

        foreach (var formula in MixedDisplay)
        {
            lines.Add("$$");
            lines.Add(formula);
            lines.Add("$$");
            lines.Add("");
        }
    }

    private static void Tune(List<string> lines) => lines.AddRange(
    [
        "## A tune, and a code to carry it",
        "",
        "The staff comes first and the scannable code sits under it, which is where a note about a tune",
        "tends to go — a link to the recording, printed small.",
        "",
        "```abc",
        "X:1",
        "T:The Measured Reel",
        "C:Trad.",
        "M:4/4",
        "L:1/8",
        "K:Gmaj",
        "|: D2 | G2 GA BAGB | ABAG EDEG | DGGF GABd | egfd BAGB |",
        "| ABAG EDEG | DGGF GABd | egfd BAGA | BGAF G2 :|",
        "|: Bd | g2 gf gabg | fdcd efge | dBBA Bdef | gfge dBAF |",
        "| G2 GF GABd | egfd BAGB | ABAG EDEG | DGGF G2 :|",
        "W: Round and round the measured reel,",
        "W: faster than the page can feel.",
        "```",
        "",
        "```qr",
        "type: url",
        "url: https://markdown.org/tools/music/abc/",
        "```",
        "",
    ]);

    private static void Charts(List<string> lines) => lines.AddRange(
    [
        "## What the week looked like",
        "",
        "Two charts of the same fortnight, one as bars and one as points, because a bar chart answers",
        "*how much* and a scatter answers *how together*.",
        "",
        "```mermaid",
        "xychart-beta",
        "    title \"Blocks laid per build\"",
        "    x-axis [mon, tue, wed, thu, fri, sat, sun]",
        "    y-axis \"Blocks\" 0 --> 2600",
        "    bar [1210, 1480, 1905, 2240, 2380, 900, 640]",
        "    line [1100, 1400, 1800, 2100, 2300, 1000, 700]",
        "```",
        "",
        "```scatter",
        "width   height",
        "1.20    3.40",
        "2.50    5.10",
        "3.10    6.80",
        "4.40    7.20",
        "5.00    9.60",
        "5.80    10.10",
        "6.30    11.40",
        "7.10    12.00",
        "7.90    13.80",
        "8.40    14.20",
        "```",
        "",
    ]);

    private static void Conversation(List<string> lines) => lines.AddRange(
    [
        "## How a keystroke gets drawn",
        "",
        "The engine does not hand a tree to anything; it writes the source and reads it again.",
        "",
        "```mermaid",
        "sequenceDiagram",
        "    participant W as Writer",
        "    participant E as Engine",
        "    participant P as Parser",
        "    participant B as Builder",
        "    W->>E: types a character",
        "    E->>P: here is the source",
        "    P-->>E: a tree, each piece saying where it was read from",
        "    E->>E: runs each stage, checking the print is unchanged",
        "    E->>B: lay this",
        "    B-->>E: pieces and marks",
        "    E-->>W: a page, and a caret where the writing left it",
        "```",
        "",
    ]);

    /// <summary>The deepest the document goes: a page, holding a chart, holding maths.</summary>
    private static void Nested(List<string> lines) => lines.AddRange(
    [
        "## A chart whose boxes are formulas",
        "",
        "A label beginning with a fence is drawn as that language, so a box can hold a formula rather than",
        "a description of one — closed by a fence of its own, as it would arrive pasted from anywhere else.",
        "This is the deepest the document goes: a page, holding a chart, holding maths.",
        "",
        "```mermaid",
        "flowchart TD",
        @"    A[""```latex E = mc^2```""] --> B[""```latex \gamma = \frac{1}{\sqrt{1 - v^2/c^2}}```""]",
        @"    B --> C[""```latex E = \gamma m_0 c^2```""]",
        @"    C --> D{""```latex v \ll c```""}",
        @"    D -->|yes| E[""```latex E \approx m_0c^2 + \tfrac{1}{2}m_0v^2```""]",
        "    D -->|no| F[\"Keep the whole thing\"]",
        "```",
        "",
    ]);

    /// <summary>One section: the same shape every time, with a block of some language dropped in every few.</summary>
    private static List<string> Section(int at)
    {
        var topic = MixedTopics[at % MixedTopics.Length];
        var said = new List<string>
        {
            $"## {topic}, and what it costs ({(at / MixedTopics.Length) + 1})",
            "",
            MixedProse[at % MixedProse.Length],
            "",
            MixedProse[(at + 3) % MixedProse.Length],
            "",
            $"Three things are worth saying about **{topic.ToLowerInvariant()}**:",
            "",
        };

        foreach (var item in MixedLists[at % MixedLists.Length]) said.Add($"- {item}");

        said.AddRange(
        [
            "",
            "1. It happens once per block, not once per character.",
            "2. It is measured, and the measurement is in the repository.",
            "3. It is allowed to be slow only where a reader would expect it to be.",
            "",
        ]);

        if (at % 3 == 0)
        {
            var n = at + 2;
            said.Add(@"A formula belongs here: $\sum_{k=0}^{" + n + @"} k = \frac{" + n + "(" + n + @"+1)}{2}$, which is the");
            said.Add("number of comparisons a naive pass would make.");
            said.Add("");
        }

        if (at % 5 == 2)
        {
            said.AddRange(["```mermaid", "flowchart LR",
                           $@"    S[""{topic} {at}""] --> T[""```latex O(n^{at} \log n)""]", "    T --> U([done])", "```", ""]);
        }

        if (at % 7 == 3)
        {
            said.AddRange(["```mermaid", "sequenceDiagram", $"    participant R as {topic} {at}",
                           "    participant Q as Engine", "    R->>Q: a change", "    Q-->>R: a page", "```", ""]);
        }

        if (at % 8 == 5)
        {
            said.AddRange(["```qr", "type: text", $"text: {topic} {at} — measured on the sample corpus", "```", ""]);
        }

        if (at % 9 == 4)
        {
            said.AddRange(["```scatter", "n   ms"]);
            for (var k = 1; k <= 6; k++) said.Add($"{k}   {(k * 2) + at}.{(k + at) % 10}");
            said.AddRange(["```", ""]);
        }

        if (at % 11 == 6)
        {
            said.AddRange(["```abc", "X:1", $"T:Study in {topic} ({at})", "M:3/4", "L:1/8", "K:Dmaj",
                           "|: A2 | d2 de fedc | d2 cd ecAc | d2 de fedc | B2 A2 F2 :|", "```", ""]);
        }

        if (at % 6 == 1)
        {
            said.AddRange(["`A line of code`, and a fence of it:", "", "```csharp",
                           $"public static int Of(string text) => text.Length; // {topic} {at}", "```", ""]);
        }

        said.Add($"See also [the notes](https://markdown.org/notes/{topic.ToLowerInvariant().Replace(' ', '-')}) and the *table above*.");
        said.Add("");

        return said;
    }

    /// <summary>Prose to land exactly on <see cref="MixedLines"/>, since a section is never cut in half.</summary>
    private static void Filling(List<string> lines)
    {
        if (lines.Count >= MixedLines) return;

        lines.Add("## A last word, to fill the page");
        lines.Add("");

        var spare = MixedLines - lines.Count;
        for (var k = 0; k < spare; k++)
        {
            if (k == spare - 1) lines.Add("And that is the whole of it, measured end to end.");
            else if (k % 2 == 0) lines.Add(MixedProse[k % MixedProse.Length]);
            else lines.Add("");
        }
    }
}
