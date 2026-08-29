namespace Nexaflow.Maths.Latex;

/// <summary>
/// The names that are not commands at all, but shorthand: <c>\neq</c> is a slash over an equals sign,
/// and there is nothing else to know about it.
///
/// <para>
/// This belongs to the reader rather than to the typesetter, and that is the whole point of it being
/// here. A macro is a fact about <em>what was written</em> — one name standing for something somebody
/// could have written out longhand instead — and resolving it is reading, not setting. The typesetter
/// had a table of these because it was also the reader; now that it is not, it should not know the word
/// macro at all.
/// </para>
/// <para>
/// What comes of a lookup is written into the tree beneath the command, under
/// <see cref="TexRole.Expansion"/>: the command is still what the writer typed, and its expansion is
/// what it means, and both are there to be asked. An expansion stands for no source, so the tree still
/// prints back exactly as it was written.
/// </para>
/// <para>
/// A definition may name another macro — <c>\iff</c> is a thick space either side of
/// <c>\Longleftrightarrow</c>, which is itself one of these — so expansion recurses, and is bounded so
/// that a definition that named itself would be caught rather than run forever.
/// </para>
/// </summary>
public static class TexMacros
{
    private static readonly Dictionary<string, string> Known = Build();

    /// <summary>What this name is shorthand for, or null where it is not shorthand for anything.</summary>
    public static string? Lookup(string name) =>
        Known.TryGetValue(name, out var definition) ? definition : null;

    /// <summary>Every macro, so that what the table claims can be held against what it produces.</summary>
    public static IReadOnlyDictionary<string, string> All => Known;

    private static Dictionary<string, string> Build()
    {
        var table = new Dictionary<string, string>(StringComparer.Ordinal);

        void Add(string name, string definition) => table[name] = definition;

        void All(string[] names, string definition)
        {
            foreach (var name in names) Add(name, definition);
        }

        // ── Written-down space ──────────────────────────────────────────────
        // The short forms. What each of these is short *for* is a strut of so many mu, which has no
        // spelling in LaTeX and so is not a macro but a primitive: it stays with the typesetter.
        Add(@"\,", @"\thinspace");
        Add(@"\:", @"\medspace");
        Add(@"\;", @"\thickspace");
        Add(@"\!", @"\negthinspace");

        // ── Names for a symbol somebody else already named ──────────────────
        Add(@"\ne", @"\not\equals");
        Add(@"\neq", @"\not\equals");
        Add(@"\doublecup", @"\Cup");
        Add(@"\doublecap", @"\Cap");
        Add(@"\restriction", @"\upharpoonright");
        Add(@"\Doteq", @"\doteqdot");
        Add(@"\llless", @"\lll");
        Add(@"\gggtr", @"\ggg");

        // ── Dots ────────────────────────────────────────────────────────────
        // amsmath's semantic dots: named for what they are between rather than for how they sit, and
        // each resolving to one of the two that are drawn.
        All([@"\dots", @"\dotsc", @"\dotso"], @"\ldots");
        All([@"\dotsb", @"\dotsi", @"\dotsm"], @"\cdots");

        // ── Implication, which is an arrow with room either side ────────────
        Add(@"\implies", @"\thickspace\Longrightarrow\thickspace");
        Add(@"\impliedby", @"\thickspace\Longleftarrow\thickspace");
        Add(@"\iff", @"\thickspace\Longleftrightarrow\thickspace");

        // ── Limits written as a decorated \lim ──────────────────────────────
        Add(@"\varliminf", @"\underline{\lim}");
        Add(@"\varlimsup", @"\overline{\lim}");
        Add(@"\varinjlim", @"\underset{\longrightarrow}{\lim}");
        Add(@"\varprojlim", @"\underset{\longleftarrow}{\lim}");

        // ── Operators that are just their own names ─────────────────────────
        //
        // `\cos` is the letters c, o, s set upright and spaced as an operator, and amsmath says so in
        // exactly those terms: `\DeclareMathOperator{\cos}{cos}`. `\operatorname` is that written out.
        //
        // The starred form is for the eight whose limits go above and below rather than beside — and it
        // says "wherever this style puts limits" where the table it replaces said "always above". That
        // is a change, and the right way round: `\lim_{n}` belongs under the word in a displayed formula
        // and after it in a line of prose, which is what \operatorname* means and what a hard-coded
        // answer could not say.
        foreach (var name in new[]
                 {
                     "cos", "sin", "tan", "sec", "csc", "cot",
                     "arccos", "arcsin", "arctan", "cosh", "sinh", "tanh", "coth",
                     "log", "ln", "exp", "arg", "deg", "det", "dim", "gcd", "hom", "ker", "lg",
                     "max", "min",
                     // not "mod": the table above claims it, and takes the modulus after it.
                     "tg", "cosec", "ctg", "arctg", "ch", "sh", "th", "cth",
                 })
            Add(@"\" + name, @"\operatorname{\mathrm{" + name + "}}");

        foreach (var name in new[] { "lim", "inf", "sup", "Pr" })
            Add(@"\" + name, @"\operatorname*{\mathrm{" + name + "}}");

        // The two-word ones, with the thin space amsmath sets between the words.
        Add(@"\liminf", @"\operatorname*{\mathrm{lim\,inf}}");
        Add(@"\limsup", @"\operatorname*{\mathrm{lim\,sup}}");
        Add(@"\injlim", @"\operatorname*{\mathrm{inj\,lim}}");
        Add(@"\projlim", @"\operatorname*{\mathrm{proj\,lim}}");

        // ── Things TeX draws by butting two glyphs together ─────────────────
        //
        // Computer Modern has no long-arrow glyph. TeX makes one out of an en-dash and an arrowhead
        // pulled together, and says so itself — plain.tex defines `\longrightarrow` as
        // `\relbar\joinrel\rightarrow`, where `\joinrel` is `\mkern-3mu` and nothing else. So these are
        // composite because the font gives no alternative, and writing them out is repeating TeX's own
        // definition rather than inventing a spelling for it.
        //
        // `\mathrel` outside and `\mathord` inside is how LaTeX says the two-sided typing the old table
        // did by hand: relation spacing at the ends, none in the middle, which is what makes two glyphs
        // read as one arrow.
        //
        // The kern is TeX's `\joinrel`, three mu, for every one of them. The table being replaced had
        // drifted to -3.5 for `\models` and -1.8 for `\bowtie` with nothing to say for either; where the
        // two disagree and LaTeX can be followed, it is followed.
        string Joined(string left, string right, string mu = "-3") =>
            @"\mathrel{\mathord{" + left + @"}\mspace{" + mu + @"mu}\mathord{" + right + "}}";

        Add(@"\longrightarrow", Joined(@"\minus", @"\rightarrow"));
        Add(@"\longleftarrow", Joined(@"\leftarrow", @"\minus"));
        Add(@"\longleftrightarrow", Joined(@"\leftarrow", @"\rightarrow"));
        Add(@"\Longrightarrow", Joined(@"\equals", @"\Rightarrow"));
        Add(@"\Longleftarrow", Joined(@"\Leftarrow", @"\equals"));
        Add(@"\Longleftrightarrow", Joined(@"\Leftarrow", @"\Rightarrow"));
        Add(@"\hookrightarrow", Joined(@"\lhook", @"\rightarrow"));
        Add(@"\hookleftarrow", Joined(@"\leftarrow", @"\rhook"));
        Add(@"\models", Joined(@"\vert", @"\equals"));
        Add(@"\bowtie", Joined(@"\triangleright", @"\triangleleft"));

        // TeX's own, now that the bar it reaches for has a name. `\mapstochar` is zero width and sits on
        // the axis, so it draws over the arrow that follows and wants no kern at all — where the table
        // being replaced pulled a full-height `\vert` back by five mu to fake it.
        Add(@"\mapsto", @"\mapstochar\rightarrow");
        Add(@"\longmapsto", @"\mapstochar\longrightarrow");

        // Drawn from the pieces of an arrow rather than from two whole ones.
        All([@"\dashrightarrow", @"\dasharrow"], @"\mathrel{\axisshort\axisshort\arrowaxisright}");
        Add(@"\dashleftarrow", @"\mathrel{\arrowaxisleft\axisshort\axisshort}");

        // A bar with an h pulled back under it.
        Add(@"\hbar", @"\bar{}\mspace{-9mu}h");

        // ── Runs of dots, which are inner rather than ordinary ──────────────
        // The class is the whole point: an inner atom takes a thin space either side, which is what
        // stops `a\cdots b` closing up.
        Add(@"\ldots", @"\mathinner{\ldotp\ldotp\ldotp}");
        Add(@"\cdots", @"\mathinner{\cdotp\cdotp\cdotp}");

        // ── Two more that are a class and nothing else ──────────────────────
        Add(@"\notin", @"\mathrel{\not\in}");
        Add(@"\bmod", @"\mathbin{\mathrm{mod}}");

        return table;

    }
}
