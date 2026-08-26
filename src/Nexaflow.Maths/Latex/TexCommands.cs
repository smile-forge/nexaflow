namespace Nexaflow.Maths.Latex;

/// <summary>A command, and what the things after it are to it.</summary>
/// <param name="Arguments">One role per required argument, in the order they are written.</param>
/// <param name="Option">The role of a bracketed argument before them, or null if it takes none.</param>
public sealed record TexCommand(string Name, IReadOnlyList<string> Arguments, string? Option = null);

/// <summary>An environment, and how what is between its <c>\begin</c> and <c>\end</c> should be read.</summary>
/// <param name="Grid">Whether the body is rows of cells rather than a plain run.</param>
/// <param name="Spec">The role of a required argument after <c>\begin{name}</c>, or null.</param>
public sealed record TexEnvironment(string Name, bool Grid, string? Spec = null);

/// <summary>
/// What the parser knows about commands: how many arguments each takes, and what to call them.
///
/// <para>
/// This is the only part of the parser that is a matter of fact rather than of syntax, and so the only
/// part that can be <em>wrong</em> while everything still round-trips: give <c>\frac</c> one argument
/// instead of two and the source still prints back exactly, the tree is just shallower than the truth.
/// Which is why the table is checked against the typesetter's own reading over the corpus rather than
/// against itself — see docs/latex-parse-tree.md.
/// </para>
/// <para>
/// A command that is not here takes no arguments. That is the right default: an unknown command is a
/// symbol as far as we can tell, whatever follows it stays a sibling, and nothing is lost — the tree is
/// flatter than it might be and the source is intact. Adding a row is how it gets deeper.
/// </para>
/// </summary>
public static class TexCommands
{
    private static readonly Dictionary<string, TexCommand> Known = Build();

    private static readonly Dictionary<string, TexEnvironment> Environments = BuildEnvironments();

    /// <summary>What this command takes, or null if nothing is known about it.</summary>
    public static TexCommand? Lookup(string name) =>
        Known.TryGetValue(name, out var command) ? command : null;

    /// <summary>Every command the table knows, so that what it claims can be checked against what the
    /// parser does with it.</summary>
    public static IEnumerable<TexCommand> All => Known.Values;

    /// <summary>How this environment's body should be read. Unknown ones are a plain run.</summary>
    public static TexEnvironment Environment(string name) =>
        Environments.TryGetValue(name, out var found) ? found : new TexEnvironment(name, Grid: false);

    private static Dictionary<string, TexCommand> Build()
    {
        var table = new Dictionary<string, TexCommand>(StringComparer.Ordinal);

        void Add(string name, string[] arguments, string? option = null) =>
            table[name] = new TexCommand(name, arguments, option);

        void All(string[] names, string[] arguments, string? option = null)
        {
            foreach (var name in names) Add(name, arguments, option);
        }

        // ── Two things, one above the other ─────────────────────────────────
        string[] overUnder = [TexRole.Numerator, TexRole.Denominator];
        All([@"\frac", @"\dfrac", @"\tfrac", @"\nicefrac", @"\sfrac"], overUnder);
        All([@"\binom", @"\dbinom", @"\tbinom"], overUnder);

        // \cfrac takes [l] or [r] to say which way the numerator leans.
        Add(@"\cfrac", overUnder, TexRole.Option);

        // \atop, \over, \brace and \brack are infix — a and b are written either side of them, not
        // after. Left with no arguments on purpose: reading them properly means rewriting the run they
        // sit in, which is a shape this parser does not have yet, and giving them arguments they do not
        // take would be worse than admitting it. (\brace and \brack were in the list above until the
        // corpus survey showed them coming up an argument short 8 times in 10.)

        // ── Roots ───────────────────────────────────────────────────────────
        Add(@"\sqrt", [TexRole.Radicand], TexRole.Degree);

        // ── One thing, decorated ────────────────────────────────────────────
        string[] one = [TexRole.Base];
        All([@"\overline", @"\underline", @"\overbrace", @"\underbrace", @"\boxed", @"\fbox"], one);
        All([@"\cancel", @"\bcancel", @"\xcancel", @"\sout", @"\phantom", @"\hphantom", @"\vphantom"], one);
        All([@"\vec", @"\hat", @"\widehat", @"\tilde", @"\widetilde", @"\bar", @"\dot", @"\ddot",
             @"\dddot", @"\acute", @"\grave", @"\check", @"\breve", @"\mathring"], one);
        All([@"\overrightarrow", @"\overleftarrow", @"\overleftrightarrow",
             @"\underrightarrow", @"\underleftarrow", @"\underleftrightarrow"], one);

        // \not slashes whatever comes next, braced or not: \not\in and \not{=} are both written.
        Add(@"\not", one);

        // Set in place without taking up room, or without taking up height.
        All([@"\smash", @"\llap", @"\rlap", @"\mathrlap", @"\mathllap", @"\mathclap"], one);

        // Modular arithmetic: \bmod is a binary operator and takes nothing; the rest take the modulus.
        All([@"\pmod", @"\pod", @"\mod"], [TexRole.Argument]);

        // \substack{a \\ b} — the stack under a big operator's limit.
        Add(@"\substack", one);

        // MathJax's box, which says how to draw the frame before what to put in it.
        Add(@"\bbox", one, TexRole.Option);

        // ── Fonts and text ──────────────────────────────────────────────────
        All([@"\mathrm", @"\mathbf", @"\mathit", @"\mathsf", @"\mathtt", @"\mathcal", @"\mathbb",
             @"\mathfrak", @"\mathscr", @"\mathnormal", @"\boldsymbol", @"\pmb"], one);
        All([@"\text", @"\mbox", @"\textbf", @"\textit", @"\texttt", @"\textrm", @"\textsf",
             @"\textnormal", @"\emph", @"\operatorname", @"\operatorname*"], one);

        // ── One thing above or below another ────────────────────────────────
        Add(@"\overset", [TexRole.Over, TexRole.Base]);
        Add(@"\stackrel", [TexRole.Over, TexRole.Base]);
        Add(@"\underset", [TexRole.Under, TexRole.Base]);

        // The extensible arrows: \xrightarrow[below]{above}.
        All([@"\xrightarrow", @"\xleftarrow", @"\xleftrightarrow", @"\xmapsto",
             @"\xrightharpoonup", @"\xleftharpoondown", @"\xhookrightarrow", @"\xhookleftarrow"],
            [TexRole.Over], TexRole.Under);

        // ── Delimiters ──────────────────────────────────────────────────────
        // The delimiter is one token, not a braced group — \left( , \bigl\{ — and reading it as an
        // argument is what keeps it attached to the command that sizes it.
        All([@"\left", @"\right", @"\middle"], [TexRole.Argument]);
        All([@"\big", @"\Big", @"\bigg", @"\Bigg",
             @"\bigl", @"\Bigl", @"\biggl", @"\Biggl",
             @"\bigr", @"\Bigr", @"\biggr", @"\Biggr",
             @"\bigm", @"\Bigm", @"\biggm", @"\Biggm"], [TexRole.Argument]);

        // ── Dirac notation ──────────────────────────────────────────────────
        All([@"\bra", @"\ket", @"\Bra", @"\Ket"], one);
        All([@"\braket", @"\Braket", @"\set", @"\Set"], one);

        // ── Machinery ───────────────────────────────────────────────────────
        All([@"\begin", @"\end"], [TexRole.Argument]);
        All([@"\hspace", @"\vspace", @"\hspace*", @"\vspace*", @"\mspace", @"\kern", @"\mkern"],
            [TexRole.Argument]);
        Add(@"\color", [TexRole.Argument]);
        Add(@"\textcolor", [TexRole.Argument, TexRole.Base]);
        Add(@"\colorbox", [TexRole.Argument, TexRole.Base]);
        Add(@"\raisebox", [TexRole.Base], TexRole.Option);
        Add(@"\label", [TexRole.Argument]);
        Add(@"\tag", [TexRole.Argument]);

        // A line break may say how much room to leave after it: \\[2pt].
        Add(@"\\", [], TexRole.Option);

        return table;
    }

    private static Dictionary<string, TexEnvironment> BuildEnvironments()
    {
        var table = new Dictionary<string, TexEnvironment>(StringComparer.Ordinal);

        void Grid(string name, string? spec = null) => table[name] = new TexEnvironment(name, true, spec);

        foreach (var name in new[] { "matrix", "pmatrix", "bmatrix", "Bmatrix", "vmatrix", "Vmatrix",
                                     "smallmatrix", "psmallmatrix", "bsmallmatrix", "Bsmallmatrix",
                                     "vsmallmatrix", "Vsmallmatrix", "cases", "dcases", "rcases" })
            Grid(name);

        foreach (var name in new[] { "align", "align*", "aligned", "alignat", "alignat*", "alignedat",
                                     "gather", "gather*", "gathered", "split", "eqnarray", "eqnarray*",
                                     "multline", "multline*", "flalign", "flalign*" })
            Grid(name);

        // The one with a shape of its own: \begin{array}{cc} says how its columns are set, and moving a
        // column means moving a letter of that spec in step. Read as a grid so the cells are there; the
        // spec is named so the day that becomes possible it is not a re-parse away.
        Grid("array", TexRole.Option);
        Grid("subarray", TexRole.Option);
        Grid("tabular", TexRole.Option);

        return table;
    }
}
