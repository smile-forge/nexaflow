using System.Collections.Generic;
using System.Linq;
using Nexaflow.Maths.Latex;
using XamlMath.Atoms;
using XamlMath.Exceptions;
using XamlMath.Parsers;
using XamlMath.Parsers.Matrices;

namespace XamlMath;

/// <summary>
/// Builds a formula out of a reading that has already been done — the other way into this engine.
///
/// <para>
/// <see cref="TexFormulaParser"/> reads LaTeX and decides what the reading should be set as, in one pass,
/// and the reading it does is lossy by the time it reaches an atom: braces are gone, spacing is gone, and
/// where a construct began is remembered only approximately. That is fine for drawing a formula once and
/// no good at all for editing one. This takes the reading from <see cref="TexReading"/> instead, which
/// keeps all of it, and does only the second half of the job.
/// </para>
/// <para>
/// Every atom carries the part it was built from, in <see cref="IFormulaNode.Origin"/>. That is the point
/// of the exercise: a box then knows what it <em>is</em> without anything having to match spans back up
/// afterwards.
/// </para>
/// <para>
/// <b>This never touches the source, and the atoms it makes name no part of it.</b> Not a convention — it
/// is handed a reading and never the string, so there is nothing here to take an offset from, and every
/// atom's <c>Source</c> is left null. What a thing came from is its <see cref="IFormulaNode.Origin"/>, and
/// where that is written is the reading's to say, worked out by a walk when someone asks. An offset stored
/// beside a tree is a second copy of a fact the tree already holds, and the two go out of step the moment
/// anything is edited — which is the whole reason the boxes are being built from a parse tree at all.
/// </para>
/// <para>
/// <b>All or nothing.</b> A construct this does not know yet makes the whole formula come back null, and
/// the caller falls back to the parser. Building half a formula each way would mix two readings of the
/// same source, which is the thing being got rid of.
/// </para>
/// <para>
/// <b>Space that was typed is not built; space that was asked for is.</b> TeX takes the gaps between
/// symbols from what class each atom is, not from what was typed, so <c>a+b</c> and <c>a + b</c> are set
/// identically: the spaces are in the parse tree because they are in the source, and they produce no
/// atom. <c>\,</c>, <c>\;</c>, <c>\quad</c> and the rest are the writer overriding that, so they do —
/// and they are macros rather than commands, which is why they are looked up in
/// <see cref="TexFormulaParser.ExpansionOf"/> and not in the symbol table.
/// </para>
/// </summary>
public static class TexFormulaBuilder
{
    /// <summary>
    /// Whether the handful of disagreements parked for review are declined — which is what parking one
    /// means, and the default.
    /// <para>
    /// Turned off to look at one. A decline is never exercised, so it goes stale without anything saying
    /// so: *a script on `\overline`* sat on the list for weeks describing a difference that was not the
    /// difference, over ten times as many formulas as recorded, and it stayed there because looking took
    /// an edit to this file. It takes a line now.
    /// </para>
    /// <para>
    /// It changes what the builder draws, so it is not a thing to leave off. Nothing but a diagnostic
    /// sets it.
    /// </para>
    /// </summary>
    internal static bool DeclineUnsettled = true;

    /// <summary>
    /// The formula that reading stands for, or null if it holds something not built here yet.
    ///
    /// <para>
    /// Takes the read-only view of the tree and not the reading, so the formula's text is not in reach
    /// from in here at all. Nothing this builds may name a point in the source; making that a thing the
    /// signature settles is better than making it a thing to remember.
    /// </para>
    /// </summary>
    public static TexFormula? Build(ITexPart root, TexFormulaParser knowledge)
    {
        System.ArgumentNullException.ThrowIfNull(root);

        var built = Run(root.Parts, root, null, knowledge);

        return built is null ? null : new TexFormula { RootAtom = built };
    }

    /// <summary>Whether this reading can be built at all — the corpus's coverage question.</summary>
    public static bool CanBuild(ITexPart root, TexFormulaParser knowledge) =>
        Build(root, knowledge) is not null;

    // ── One part ────────────────────────────────────────────────────────────

    private static Atom? Of(ITexPart part, string? style, TexFormulaParser knowledge) =>
        part.Kind switch
        {
            TexKind.Char => Character(part, style, knowledge),
            TexKind.Sequence => Run(part.Parts, part, style, knowledge),
            TexKind.Group when Written(part) => Group(part, style, knowledge),
            TexKind.Group => Run(part.Parts, part, style, knowledge),
            TexKind.Script => Script(part, style, knowledge),
            TexKind.Command => Command(part, style, knowledge),
            TexKind.Fence => Fence(part, style, knowledge),
            TexKind.Environment => Environment(part, style, knowledge),
            _ => null,
        };

    /// <summary>
    /// A block written between <c>\begin</c> and <c>\end</c>: a matrix, a cases block, an aligned
    /// equation.
    /// <para>
    /// How one is arranged — the gaps between its columns, the brackets round it, the size it is set at —
    /// is the engine's own table's answer, given the cells built here. That half of the job is the same
    /// whichever reading the cells came from, so it is asked for rather than copied: a padding that
    /// changed in one place would otherwise set a matrix two ways.
    /// </para>
    /// </summary>
    private static Atom? Environment(ITexPart part, string? style, TexFormulaParser knowledge)
    {
        if (part.Part(TexRole.Begin) is not { } begin) return null;

        // Half-typed, with nothing yet saying where the block stops. The parser throws on it, so there
        // is no atom to agree with.
        if (part.Part(TexRole.End) is null) return null;

        if (!StandardCommands.Environments.TryGetValue(TexParser.NameOf(begin), out var arrangement))
            return null;

        // Every piece of it has to be one this knows. A block is begun, optionally shaped, and then made
        // of rows; anything else in there is something the reading has not been taught.
        foreach (var child in part.Parts)
            if (child.Role is not (TexRole.Begin or TexRole.End or TexRole.Option or TexRole.Row))
                return null;

        if (Cells(part, style, knowledge) is not { } cells) return null;

        return arrangement switch
        {
            // The part goes in rather than being hung on what comes back. A bracketed matrix is several
            // atoms — the fence, the grid, sometimes a style — one construct drawn in parts, as a
            // fraction's box and its bar are, and every one of them came from this \begin. Tagging the
            // outermost alone would leave the grid itself knowing nothing, and there is no reaching in
            // from outside to fix that: a style atom names no parts, so a walk stops at it.
            MatrixCommandParser matrix => matrix.Assemble(null, cells, Whole(part)),
            ArrayCommandParser => Array(part, cells, style, knowledge),

            // \begin{equation} and the counted alignments — \begin{alignat}{2} and its family, whose
            // count is written where this reading expects a cell.
            _ => null,
        };
    }

    /// <summary>
    /// <c>\begin{array}{cc}</c> — the one table whose shape is written down separately, so the shape has
    /// to be read as well.
    /// <para>
    /// <b>And read as text, which nothing else here does.</b> The reading names the preamble — it is the
    /// <see cref="TexRole.Option"/> part — but has never looked inside one, so there is no structure to
    /// walk and the characters are handed over as they stand. That is the habit this exercise exists to
    /// remove, kept deliberately in the one place it is still true: a column spec is not modelled, and
    /// pretending otherwise would hide it. Until it is, an <c>array</c> cannot have a column moved
    /// without moving a letter of this in step.
    /// </para>
    /// <para>
    /// <c>\hline</c> is not built: it is a command with no symbol behind it, so a cell holding one
    /// declines of its own accord and the whole array falls back. Rules are drawn between rows rather
    /// than in them, so they are a thing to read off the grid and never off a cell.
    /// </para>
    /// </summary>
    private static Atom? Array(ITexPart part, List<List<Atom?>> cells, string? style, TexFormulaParser knowledge)
    {
        if (part.Part(TexRole.Option) is not { } option) return null;

        var preamble = option.Print();
        if (preamble.Length < 2 || preamble[0] != '{' || preamble[^1] != '}') return null;

        ArrayColumnSpec spec;

        // A preamble is written in a small language of its own — `@{}`, `*{3}{c}`, `p{2cm}` — and only
        // some of it can be drawn. Not being able to read one is a decline like any other: building never
        // throws, so that a formula this cannot manage costs coverage and never a rendering.
        try
        {
            spec = ArrayColumnSpec.Parse(preamble[1..^1]);
        }
        catch (TexParseException)
        {
            return null;
        }

        return ArrayCommandParser.Assemble(null, cells, spec, null, Whole(part));
    }

    /// <summary>
    /// The grid, row by row, squared off.
    /// <para>
    /// A cell with nothing written in it is still a cell, and so is one that was never written at all: a
    /// row of two beside rows of four gets two more. Every position in the grid is then something, which
    /// is what makes "the third column" mean the same thing in every row.
    /// </para>
    /// <para>
    /// The two are not the same thing, and only one of them has a part. Typing <c>a &amp;</c> makes an
    /// empty cell — the reader wrote the <c>&amp;</c> that closed it, and there is a node in the reading
    /// standing exactly where the writing would go. Squaring off a short row makes cells nobody wrote, and
    /// there is nothing in the reading for them to be; they are the shape of the table rather than
    /// anything in it, and they carry no part precisely because inventing one would say otherwise.
    /// </para>
    /// </summary>
    private static List<List<Atom?>>? Cells(ITexPart environment, string? style, TexFormulaParser knowledge)
    {
        var rows = new List<List<Atom?>>();

        foreach (var row in environment.Children)
        {
            if (row.Role != TexRole.Row) continue;

            var cells = new List<Atom?>();

            foreach (var cell in row.Children)
            {
                if (cell.Role != TexRole.Cell) continue;

                var built = Built(cell.Parts, style, knowledge);
                if (built is null) return null;

                cells.Add(built.Count switch
                {
                    0 => Tag(new NullAtom(), cell),
                    1 => built[0],
                    _ => Rowed(built, cell),
                });
            }

            rows.Add(cells);
        }

        if (rows.Count == 0) return null;

        var columns = rows.Max(row => row.Count);
        if (columns == 0) return null;

        foreach (var row in rows)
            while (row.Count < columns) row.Add(new NullAtom());

        return rows;
    }

    /// <summary>
    /// Something between delimiters that grow to hold it. The delimiters are drawn by the fence rather
    /// than being things inside it, which is why they are named here and not built.
    /// </summary>
    private static Atom? Fence(ITexPart part, string? style, TexFormulaParser knowledge)
    {
        if (part.Part(TexRole.Body) is not { } body) return null;

        // A fence inside a fence was declined here, and is not any more: looked at, and ours is the one
        // to keep. The two draw it identically — every number the same — and differ in what they built
        // to draw it with. The parser collapses the `^{4}` of `\left( \left( \tfrac12 \right)^{4}, 0^4
        // \right)` into a single atom; ours keeps it a group holding one thing, which is what it was
        // written as and what a substitution has to be able to reach. Reviewed 2026-08-27.

        // A script on something a command built, between delimiters, was declined here and is not any
        // more: reviewed, and ours is the one to keep. Identical renderings again. The parser follows
        // TeX's rule that what comes after modifies what came before and flattens the two together;
        // that is right for setting type and wrong for selecting, where the thing scripted and the
        // script are separate things a reader points at. Reviewed 2026-08-27.

        if (Of(body, style, knowledge) is not { } inside) return null;

        // An unclosed fence is something half-typed, and the parser has no atom for it. Left to fall
        // back rather than closed on the writer's behalf.
        if (part.Part(TexRole.Open) is not { } open) return null;
        if (part.Part(TexRole.Close) is not { } close) return null;

        // \| — the double bar. Taking the backslash off and looking up what is left draws a single bar,
        // and naming it Vert instead does not agree with the parser either. Every norm in the corpus is
        // written with it, so it is worth getting right rather than guessing at: see docs, "still to be
        // settled".

        return Tag(
            new FencedAtom(null, inside, Delimiter(open), Delimiter(close)),
            part);
    }

    /// <summary>What a <c>\left</c> or <c>\right</c> was written with, as written.</summary>
    private static string Names(ITexPart fence) =>
        fence.Part(TexRole.Argument)?.Print() ?? string.Empty;

    /// <summary>The delimiter a <c>\left</c> or <c>\right</c> was written with.</summary>
    private static SymbolAtom? Delimiter(ITexPart fence)
    {
        if (fence.Part(TexRole.Argument) is not { } written) return null;

        var text = written.Print();

        // A character stands for a delimiter through TeX's own table — `(` is not the symbol named "(".
        // A command names one directly, without its backslash — except this one, which names itself
        // after nothing: `\|` is TeX's spelling of `\Vert`, the double bar every norm is written with,
        // and stripping its backslash asks for a symbol called `|` that no table has. Which is why the
        // bars were simply missing rather than drawn wrongly.
        var symbol = text switch
        {
            @"\|" => TexFormulaParser.DelimiterOf("Vert", null),
            { Length: 1 } => TexFormulaParser.DelimiterOf(text[0], null),
            _ => TexFormulaParser.DelimiterOf(text.TrimStart('\\'), null),
        };

        // The whole `\right]` it was written as, and not merely the bracket. A delimiter is a piece the
        // reader points at, and pointing at one has to mean the pair — a bracket without its partner
        // parses as nothing at all. Tagged here rather than by the caller because a fence's delimiters
        // are not parts of it in the sense `Slots` means: they are drawn by the fence rather than being
        // things inside it, so a walk of the formula's parts goes straight past them. Which is exactly
        // how they came to be carrying no part at all for a while, with every test still green.
        if (symbol is not null) Tag(symbol, fence);

        return symbol;
    }

    private static Atom? Character(ITexPart part, string? style, TexFormulaParser knowledge)
    {
        if (part.Text.Length != 1) return null;

        var character = part.Text[0];

        // A prime is never built here: it is not a thing standing in the row but a mark on whatever it
        // follows, which is a fact about the run and so is read one level up, in Built.
        if (character == '\'') return null;

        // A tie is an inter-word space that a line may not be broken at. Written as a character and not
        // one: nothing is drawn, and the spacing TeX would give a symbol of its class does not apply.
        if (character == '~') return Tag(new SpaceAtom(null), part);

        return Tag(TexFormulaParser.CharacterOf(character, null, style), part);
    }

    /// <summary>
    /// Whether these braces were written by the reader as part of the formula, rather than being how a
    /// command's argument was delimited.
    ///
    /// <para>
    /// The distinction decides whether the group becomes an atom of its own, and it is not the same as
    /// what the group is called. `{x}` standing in a run was written; so was the `{\gamma}` of
    /// <c>{\gamma}^2</c>, which is a script's base and got its script afterwards. But the `{q}` of
    /// <c>\dot{q}</c> is *also* a base, and it was not written — it is where <c>\dot</c>'s argument
    /// stops. Only what holds the group can tell those two apart.
    /// </para>
    /// </summary>
    private static bool Written(ITexPart group) =>
        group.Role == TexRole.Element
        || (group.Role == TexRole.Base && group.Parent?.Kind == TexKind.Script);

    /// <summary>
    /// A braced group, which is a thing in its own right and not merely what is inside it.
    /// <para>
    /// Braces change an atom's <em>class</em>, and TeX's spacing comes from classes: <c>-{\frac a b}</c>
    /// is set differently from <c>-\frac a b</c> because the braces make the fraction an ordinary atom
    /// and the minus is read against that. Dropping them because they hold only one thing looks harmless
    /// and moves the spacing of every formula written with them.
    /// </para>
    /// </summary>
    private static Atom? Group(ITexPart part, string? style, TexFormulaParser knowledge)
    {
        if (Run(part.Parts, part, style, knowledge) is not { } inner) return null;

        return Tag(
            new TypedAtom(null, inner, TexAtomType.Ordinary, TexAtomType.Ordinary),
            part);
    }

    /// <summary>
    /// Several things in a row. One thing on its own is that thing: a row of one would add a level the
    /// typesetter's own reading does not have, and every box would sit inside a box.
    /// </summary>
    private static Atom? Run(IEnumerable<ITexPart> parts, ITexPart whole, string? style, TexFormulaParser knowledge)
    {
        var built = Built(parts, style, knowledge);
        if (built is null || built.Count == 0) return null;

        return built.Count == 1 ? built[0] : Rowed(built, whole);
    }

    /// <summary>
    /// A run of parts, built.
    /// <para>
    /// A run and not a tree, deliberately: <c>a+b</c> is three things standing beside each other and
    /// nothing here groups them, because grouping them needs precedence and precedence is knowledge
    /// about mathematics rather than about what was written. What the <em>writing</em> groups — braces,
    /// arguments, a script binding to the atom before it — the reading has already grouped, and this
    /// finds it as one part.
    /// </para>
    /// </summary>
    private static List<Atom>? Built(IEnumerable<ITexPart> parts, string? style, TexFormulaParser knowledge)
    {
        var built = new List<Atom>();
        var run = parts.ToList();

        for (var at = 0; at < run.Count; at++)
        {
            // A switch takes the rest of the group it stands in, not an argument: `{\cal L M}` sets both
            // letters, and nothing says where it stops except the closing brace. So it is read here,
            // where the rest of the run is, and it makes no atom of its own — what it changes is how
            // everything after it is built.
            if (Switch(run[at]) is { } switched)
            {
                if (Built(run.Skip(at + 1), switched.TextStyle ?? style, knowledge) is not { } after)
                    return null;

                // What it covers is one thing, whichever kind of switch it is. An alphabet switch reaches
                // the letters and wraps nothing of its own — `{\bf x}` is a bold x and not a bold
                // anything — but its *scope* is still a unit, and splicing that back into the row it
                // stands in sets `c \bf{1} .` differently from `\bf{1}` alone. A size switch is the same
                // unit with a style on it.
                //
                // Which is the one thing here with no node to point at: a scope is "the rest of the
                // group", so where the switch does not begin its group there is nothing in the reading
                // covering exactly it. The switch is what made the row, so the row names the switch.
                var scope = after.Count switch
                {
                    // A switch with nothing after it at all — `p = \displaystyle`, written and about to
                    // be written into. The reading keeps it, as it keeps everything; what to *draw* for
                    // it is this builder's choice, and the choice is a box of no size rather than no box.
                    // Nothing to draw is not the same as nothing there: an editor needs somewhere for the
                    // caret to be and something for the part to point at, and a switch that produced no
                    // atom at all would be a piece of the reading with no place on the page.
                    0 => Tag(new NullAtom(), run[at]),
                    1 => after[0],
                    _ => Rowed(after, run[at]),
                };

                built.Add(switched.Style is { } size
                    ? Tag(new StyleAtom(null, scope, size), run[at])
                    : scope);

                break;
            }

            var atom = Of(run[at], style, knowledge);
            if (atom is null) return null;

            built.Add(atom);
        }

        // A row written first in a row — `\mathrm{vol}(10)` — was declined here and is not any more:
        // reviewed, and ours is the one to keep. The parser splices such a row into the row it is
        // starting, but only when it is written first; put anything before it and it nests, exactly as
        // this does. Both set identically. Ours respects the grouping the writer wrote and the parser's
        // depends on where the group happens to sit. Reviewed 2026-08-27.

        return built;
    }

    /// <summary>
    /// What this part switches, when it is a switch standing in a run rather than a command with an
    /// argument. Which of the two it is comes from the engine's own table — the same table the parser
    /// reads, so neither of us can come to think <c>\bf</c> takes an argument while the other does not.
    /// </summary>
    private static (string? TextStyle, TexStyle? Style)? Switch(ITexPart part)
    {
        if (part.Kind != TexKind.Command || part.Parts.Any()) return null;
        if (part.Part(TexRole.Name)?.Text is not { } name) return null;

        return StandardCommands.IsSwitch(name[1..], out var textStyle, out var style)
            ? (textStyle, style)
            : null;
    }

    private static Atom Rowed(List<Atom> built, ITexPart whole)
    {
        var row = new RowAtom(null);
        foreach (var atom in built) row = row.Add(atom);

        return Tag(row, whole);
    }

    private static Atom? Script(ITexPart part, string? style, TexFormulaParser knowledge)
    {
        // A script with no base at all — one written with nothing before it that could carry it and
        // nothing after it either. The reading gives what follows to it where there is anything to give;
        // where there is not, the parser sets it on an empty box, and a box that nothing in the reading
        // stands for is not a thing to invent.
        if (part.Part(TexRole.Base) is null) return null;

        if (Part(part, TexRole.Base, style, knowledge) is not { } baseAtom) return null;

        // A prefix: the scripts were written *before* the thing they are on. Ordinary notation in
        // chemistry — the 14 and the 6 of carbon-14 — and what a script written after something that
        // could not carry one becomes.
        //
        // TeX sets one as an empty box wearing the scripts, followed by the base: two things side by
        // side rather than one thing wearing two, because a script atom puts its scripts after whatever
        // it is on and there is no asking it to do otherwise. Which is the whole of the difference —
        // reading it as a prefix and then building it as a suffix is what moved the ink the first time.
        // Asked of the tree and not of where anything sits in the text: a script's children are in written
        // order, so a base that comes after the `^` was written after it. Same answer, and it is the
        // structural fact rather than a reading of two offsets that happen to agree with it.
        //
        // Only where there is a `^` or `_` to come after. A script made of marks alone — `f''` — has no
        // name child, and "after nothing" is not "first".
        if (Order(part, TexRole.Name) is var name and >= 0 && Order(part, TexRole.Base) > name)
        {
            // Not built yet, and the reading is the half that matters. `{}^{14}_{6}\mathrm{C}` now
            // *parses* as one thing with its scripts in front, which is what chemistry needs and what
            // an editor has to hold; drawing it is the other half. Sixteen corpus formulas still space
            // it differently from the parser — all of them with a tie or another space beside the
            // prefix — and sixteen unexamined differences are sixteen possible defects.
            return null;

            var carried = Scripts(part, new RowAtom(null), style, knowledge);
            if (carried is null) return null;

            var both = new RowAtom(null).Add(Tag(carried, part)).Add(baseAtom);
            return Tag(both, part);
        }

        // The marks first, and separately, because they do not merge with what was written after them:
        // `x''_{i}` sets the primes as a superscript on the x and then sets the subscript on the whole
        // of that. All the marks make one superscript, which is what puts them level with each other.
        var marks = part.Children.Where(child => child.Role == TexRole.Mark).ToList();

        if (marks.Count > 0)
        {
            var row = new RowAtom(null);
            foreach (var mark in marks) row = row.Add(Tag(SymbolAtom.GetAtom("prime", null), mark));

            // Both name the whole of it, which is now a thing the reading names: `f''` is one node, so
            // there is no longer a run here for an atom to stand for and nothing to understate.
            Tag(row, part);
            baseAtom = Tag(new ScriptsAtom(null, baseAtom, null, row), part);
        }

        var superscript = Part(part, TexRole.Superscript, style, knowledge);
        var subscript = Part(part, TexRole.Subscript, style, knowledge);

        // A script with nothing written in it is not something this builds — the parser has a
        // placeholder for that and the two would not agree.
        if (part.Part(TexRole.Superscript) is not null && superscript is null) return null;
        if (part.Part(TexRole.Subscript) is not null && subscript is null) return null;

        // Marks and nothing else — `f''`. What was built for them is the whole of it already.
        if (superscript is null && subscript is null) return marks.Count > 0 ? baseAtom : null;

        // Scripts on a big operator are its limits, not scripts. TeX stacks a sum's above and below it
        // and sets an integral's beside it, and it is a different atom that knows the difference.
        if (baseAtom is BigOperatorAtom big)
            return Tag(
                new BigOperatorAtom(null, big.BaseAtom, subscript, superscript, big.UseVerticalLimits),
                part);

        return Tag(new ScriptsAtom(null, baseAtom, subscript, superscript), part);
    }

    /// <summary>
    /// This node's scripts, set on whatever is handed in — used for a prefix, where what they go on is
    /// an empty box standing in front of the thing they belong to.
    /// </summary>
    private static Atom? Scripts(ITexPart part, Atom on, string? style, TexFormulaParser knowledge)
    {
        var superscript = Part(part, TexRole.Superscript, style, knowledge);
        var subscript = Part(part, TexRole.Subscript, style, knowledge);

        if (part.Part(TexRole.Superscript) is not null && superscript is null) return null;
        if (part.Part(TexRole.Subscript) is not null && subscript is null) return null;
        if (superscript is null && subscript is null) return null;

        return new ScriptsAtom(null, on, subscript, superscript);
    }

    private static Atom? Command(ITexPart part, string? style, TexFormulaParser knowledge)
    {
        if (part.Part(TexRole.Name)?.Text is not { } name) return null;

        switch (name)
        {
            case @"\frac":
            {
                if (Part(part, TexRole.Numerator, style, knowledge) is not { } numerator) return null;
                if (Part(part, TexRole.Denominator, style, knowledge) is not { } denominator) return null;

                return Tag(new FractionAtom(null, numerator, denominator, true), part);
            }

            case @"\sqrt":
            {
                if (Part(part, TexRole.Radicand, style, knowledge) is not { } radicand) return null;

                // A degree, where one was written. Setting it small and tucking it over the sign is the
                // radical's own business; here it is another argument like any other.
                var asked = part.Part(TexRole.Degree);
                var degree = asked is null ? null : Part(part, TexRole.Degree, style, knowledge);
                if (asked is not null && degree is null) return null;

                return Tag(new Radical(null, radicand, degree), part);
            }

            case @"\overline":
            {
                if (Part(part, TexRole.Base, style, knowledge) is not { } inner) return null;

                return Tag(new OverlinedAtom(null, inner), part);
            }

            case @"\underline":
            {
                if (Part(part, TexRole.Base, style, knowledge) is not { } inner) return null;

                return Tag(new UnderlinedAtom(null, inner), part);
            }

            // The control space, and the non-breaking one: an ordinary inter-word space, and the writer
            // asking for it rather than typing it — so it is built, where a typed space is not. Named
            // here rather than looked up because nothing defines it: unlike `\,` and `\quad`, which are
            // macros with a definition, this one is a case in the reader. It is the single most written
            // thing the builder did not know, by a factor of ten.
            case @"\ ":
            case @"\nbsp":
                return part.Parts.Any() ? null : Tag(new SpaceAtom(null), part);
        }

        // A sized delimiter — \big( , \Bigl\{ , \biggr] . Not a fence: nothing pairs it with anything, and
        // TeX does not either. `\big(` is one bracket drawn at a chosen size, standing on its own, which
        // is why a formula may open with `\bigl(` and never close it and still be perfectly good LaTeX.
        // So it reads as a command with one argument and builds as one atom, and the pairing a reader
        // sees is theirs rather than the tree's.
        if (Delimiter(part) is { } sized
            && StandardCommands.BigDelimiterOf(name[1..], sized.Name, Whole(part)) is { } big)
            return big;

        // A style is not an atom. \mathrm{abc} sets three roman letters and wraps them in nothing at all,
        // because which alphabet a letter is drawn from is a property of the letter. So it is carried
        // down the build rather than built: the argument is made under the new style, and what comes back
        // is what the command stands for.
        if (TexFormulaParser.TextStyleOf(name[1..]) is { } restyled)
        {
            // \text and its family read their contents as words and not as maths — every character as
            // written, spaces and all — and the spaces are exactly what this reading drops on the way to
            // an atom. A different job, not a harder one; not done here yet.
            if (TexFormulaParser.IsRawTextStyle(name[1..])) return null;

            if (part.Part(TexRole.Base) is not { } styled) return null;

            // A tie inside a style — `\mathrm { \quad ~ }`. Two formulas in the corpus, and the two
            // readings space it differently; not looked at yet, so not guessed at.
            if (styled.SelfAndDescendants().Any(inner => inner.Text == "~")) return null;

            if (Of(styled, restyled, knowledge) is not { } inner) return null;

            // The whole \mathrm{abc}, not the {abc}: the command and its argument are one construct and
            // the parser names it that way too. What was braced is still in the reading, under this.
            return Tag(inner, part);
        }

        // Every accent at once, rather than a case each. What makes \vec an accent is that the symbol
        // table says so — the parser reads it the same way, and a table that grows a new one teaches
        // both of us together.
        if (part.Part(TexRole.Base) is { } accented && Accent(name) is { } accent)
        {
            if (Of(accented, style, knowledge) is not { } inner) return null;

            return Tag(new AccentedAtom(null, inner, accent.Name), part);
        }

        // Anything else has to be a symbol standing on its own; a command with arguments this does not
        // know is exactly the case that must fall back rather than be guessed at.
        if (part.Parts.Any()) return null;

        return Symbol(name[1..], part, style, knowledge);
    }

    /// <summary>The accent this command names, or null when it names none.</summary>
    private static SymbolAtom? Accent(string name)
    {
        try
        {
            var symbol = SymbolAtom.GetAtom(name.TrimStart('\\'), null);
            return symbol.Type == TexAtomType.Accent ? symbol : null;
        }
        catch (SymbolNotFoundException)
        {
            return null;
        }
    }

    private static Atom? Symbol(string name, ITexPart part, string? style, TexFormulaParser knowledge)
    {
        try
        {
            var symbol = SymbolAtom.GetAtom(name, null);
            Tag(symbol, part);

            // A big operator is never merely a symbol: every sum and integral gets its own atom whether
            // or not anything was written above or below it, because how its limits would be set is part
            // of what it is. Both halves carry the part — the sign is the operator's own drawing of
            // itself — and both need to, because a script arriving later keeps the sign and builds a new
            // operator round it, so a sign that knew nothing would come out of \sum_{i=0}^{n} knowing
            // nothing still.
            return symbol.Type == TexAtomType.BigOperator
                ? Tag(TexFormulaParser.BigOperatorOf(symbol, null), part)
                : symbol;
        }
        catch (SymbolNotFoundException)
        {
            // Not a symbol, so perhaps shorthand for one. TeX's written-down spaces — `\,`, `\;`, `\!`,
            // `\quad` — are macros rather than commands: each is defined as a formula and expands to a
            // single space atom. A space that was *asked for*, and so is built, unlike the space that was
            // merely typed, which the spacing rules would have put there anyway.
            return knowledge.ExpansionOf(name) is { } expansion ? Tag(expansion, part) : null;
        }
    }

    // ── Bookkeeping ─────────────────────────────────────────────────────────

    /// <summary>The one part with this role, built — or null when it is absent or not buildable.</summary>
    private static Atom? Part(ITexPart whole, string role, string? style, TexFormulaParser knowledge)
    {
        foreach (var part in whole.Children)
            if (part.Role == role) return Of(part, style, knowledge);

        return null;
    }

    /// <summary>Where a part with this role was written among its siblings, or -1 for none.</summary>
    private static int Order(ITexPart whole, string role)
    {
        for (var at = 0; at < whole.Children.Count; at++)
            if (whole.Children[at].Role == role) return at;

        return -1;
    }

    /// <summary>
    /// The whole part behind the read-only view — the one place the narrowing is undone.
    ///
    /// <para>
    /// Everything in this file holds parts as <see cref="ITexPart"/>, so nothing here can read a position
    /// while it builds. What gets <em>stored</em> is the whole part, because the thing that follows the
    /// link afterwards is an editor and an editor needs to know where things are. Both halves are wanted,
    /// and the seam between them is worth having in exactly one place rather than at each handoff.
    /// </para>
    /// <para>
    /// <see cref="TexPart"/> is the only reading of a formula there is and it is sealed, so this cannot
    /// fail; if it ever could, a part that is not one is not part of any formula and failing loudly is
    /// the right answer.
    /// </para>
    /// </summary>
    private static TexPart Whole(ITexPart part) => (TexPart)part;

    /// <summary>Hangs the part on the atom built from it. The whole reason for building them here.</summary>
    private static Atom Tag(Atom atom, ITexPart part)
    {
        atom.Origin = Whole(part);
        return atom;
    }
}
