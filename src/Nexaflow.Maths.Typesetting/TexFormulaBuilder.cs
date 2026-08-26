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
/// <b>Space is not built.</b> TeX takes the gaps between symbols from what class each atom is, not from
/// what was typed: <c>a+b</c> and <c>a + b</c> are set identically. The spaces are in the parse tree
/// because they are in the source, and they produce no atom. <c>\,</c> and <c>\quad</c> are commands and
/// do.
/// </para>
/// </summary>
public static class TexFormulaBuilder
{
    /// <summary>The formula that reading stands for, or null if it holds something not built here yet.</summary>
    public static TexFormula? Build(TexReading reading)
    {
        System.ArgumentNullException.ThrowIfNull(reading);

        var root = Run(reading.Root.Parts, reading.Root);

        return root is null ? null : new TexFormula { RootAtom = root };
    }

    /// <summary>Whether this reading can be built at all — the corpus's coverage question.</summary>
    public static bool CanBuild(TexReading reading) => Build(reading) is not null;

    // ── One part ────────────────────────────────────────────────────────────

    private static Atom? Of(TexPart part) =>
        part.Kind switch
        {
            TexKind.Char => Character(part),
            TexKind.Sequence => Run(part.Parts, part),
            TexKind.Group when Written(part) => Group(part),
            TexKind.Group => Run(part.Parts, part),
            TexKind.Script => Script(part),
            TexKind.Command => Command(part),
            TexKind.Fence => Fence(part),
            TexKind.Environment => Environment(part),
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
    private static Atom? Environment(TexPart part)
    {
        if (part.Part(TexRole.Begin) is not { } begin) return null;

        // Half-typed, with nothing yet saying where the block stops. The parser throws on it, so there
        // is no atom to agree with.
        if (part.Part(TexRole.End) is null) return null;

        if (!StandardCommands.Environments.TryGetValue(TexParser.NameOf(begin.Node), out var written))
            return null;

        // Every piece of it has to be one this knows. A block is begun, optionally shaped, and then made
        // of rows; anything else in there is something the reading has not been taught.
        foreach (var child in part.Parts)
            if (child.Role is not (TexRole.Begin or TexRole.End or TexRole.Option or TexRole.Row))
                return null;

        if (Cells(part) is not { } cells) return null;

        return written switch
        {
            // The part goes in rather than being hung on what comes back. A bracketed matrix is several
            // atoms — the fence, the grid, sometimes a style — one construct drawn in parts, as a
            // fraction's box and its bar are, and every one of them came from this \begin. Tagging the
            // outermost alone would leave the grid itself knowing nothing, and there is no reaching in
            // from outside to fix that: a style atom names no parts, so a walk stops at it.
            MatrixCommandParser matrix => matrix.Assemble(null, cells, part),
            ArrayCommandParser => Array(part, cells),

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
    private static Atom? Array(TexPart part, List<List<Atom?>> cells)
    {
        if (part.Part(TexRole.Option) is not { } option) return null;

        var preamble = option.Node.Print();
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

        return ArrayCommandParser.Assemble(null, cells, spec, null, part);
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
    private static List<List<Atom?>>? Cells(TexPart environment)
    {
        var rows = new List<List<Atom?>>();

        foreach (var row in environment.Children)
        {
            if (row.Role != TexRole.Row) continue;

            var cells = new List<Atom?>();

            foreach (var cell in row.Children)
            {
                if (cell.Role != TexRole.Cell) continue;

                var built = Built(cell.Parts);
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
    private static Atom? Fence(TexPart part)
    {
        if (part.Part(TexRole.Body) is not { } body) return null;

        // A fence inside a fence, not yet. Delimiters grow to fit what is between them, and the parser
        // puts a scripted fence inside boxes of its own before measuring it — so `\left[ \left( a
        // \right)^2 \right]` picks a smaller bracket there than it does here. Every disagreement the
        // corpus had left was this, and a bracket one size out is not something to guess at.
        if (body.SelfAndDescendants().Any(inner => inner.Kind == TexKind.Fence)) return null;

        if (Of(body) is not { } inside) return null;

        // An unclosed fence is something half-typed, and the parser has no atom for it. Left to fall
        // back rather than closed on the writer's behalf.
        if (part.Part(TexRole.Open) is not { } open) return null;
        if (part.Part(TexRole.Close) is not { } close) return null;

        // \| — the double bar. Taking the backslash off and looking up what is left draws a single bar,
        // and naming it Vert instead does not agree with the parser either. Every norm in the corpus is
        // written with it, so it is worth getting right rather than guessing at: see docs, "still to be
        // settled".
        if (Names(open) == @"\|" || Names(close) == @"\|") return null;

        return Tag(
            new FencedAtom(null, inside, Delimiter(open), Delimiter(close)),
            part);
    }

    /// <summary>What a <c>\left</c> or <c>\right</c> was written with, as written.</summary>
    private static string Names(TexPart fence) =>
        fence.Part(TexRole.Argument)?.Node.Print() ?? string.Empty;

    /// <summary>The delimiter a <c>\left</c> or <c>\right</c> was written with.</summary>
    private static SymbolAtom? Delimiter(TexPart fence)
    {
        if (fence.Part(TexRole.Argument) is not { } written) return null;

        var text = written.Node.Print();

        // A character stands for a delimiter through TeX's own table — `(` is not the symbol named "(".
        // A command names one directly, without its backslash.
        return text.Length == 1
            ? TexFormulaParser.DelimiterOf(text[0], null)
            : TexFormulaParser.DelimiterOf(text.TrimStart('\\'), null);
    }

    /// <summary>
    /// Characters the parser does something of its own with, and this does not do yet. Declined rather
    /// than built plainly, because built plainly they would be set in the wrong place.
    /// <list type="bullet">
    ///   <item><c>'</c> — gathers into a row of primes attached to whatever it follows.</item>
    ///   <item><c>~</c> — a tie: a space that a line may not be broken at.</item>
    /// </list>
    /// </summary>
    private const string Peculiar = "'~";

    private static Atom? Character(TexPart part)
    {
        if (part.Node.Text.Length != 1) return null;

        var character = part.Node.Text[0];
        if (Peculiar.Contains(character)) return null;

        return Tag(TexFormulaParser.CharacterOf(character, null), part);
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
    private static bool Written(TexPart group) =>
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
    private static Atom? Group(TexPart part)
    {
        if (Run(part.Parts, part) is not { } inner) return null;

        return Tag(
            new TypedAtom(null, inner, TexAtomType.Ordinary, TexAtomType.Ordinary),
            part);
    }

    /// <summary>
    /// Several things in a row. One thing on its own is that thing: a row of one would add a level the
    /// typesetter's own reading does not have, and every box would sit inside a box.
    /// </summary>
    private static Atom? Run(IEnumerable<TexPart> parts, TexPart whole)
    {
        var built = Built(parts);
        if (built is null || built.Count == 0) return null;

        return built.Count == 1 ? built[0] : Rowed(built, whole);
    }

    private static List<Atom>? Built(IEnumerable<TexPart> parts)
    {
        var built = new List<Atom>();

        foreach (var part in parts)
        {
            var atom = Of(part);
            if (atom is null) return null;

            built.Add(atom);
        }

        return built;
    }

    private static Atom Rowed(List<Atom> built, TexPart whole)
    {
        var row = new RowAtom(null);
        foreach (var atom in built) row = row.Add(atom);

        return Tag(row, whole);
    }

    private static Atom? Script(TexPart part)
    {
        if (Part(part, TexRole.Base) is not { } baseAtom) return null;

        // A rule drawn over something, with a script after it. The parser attaches the script to a
        // different atom than this does, and the two set the script at different heights — visible only
        // once, in a corpus of a quarter of a million formulas, and wrong is wrong.
        if (part.Part(TexRole.Base) is { Kind: TexKind.Command } command
            && command.Node.Part(TexRole.Name)?.Text is @"\overline" or @"\underline")
            return null;

        var superscript = Part(part, TexRole.Superscript);
        var subscript = Part(part, TexRole.Subscript);

        // A script with nothing written in it is not something this builds — the parser has a
        // placeholder for that and the two would not agree.
        if (part.Part(TexRole.Superscript) is not null && superscript is null) return null;
        if (part.Part(TexRole.Subscript) is not null && subscript is null) return null;

        // Scripts on a big operator are its limits, not scripts. TeX stacks a sum's above and below it
        // and sets an integral's beside it, and it is a different atom that knows the difference.
        if (baseAtom is BigOperatorAtom big)
            return Tag(
                new BigOperatorAtom(null, big.BaseAtom, subscript, superscript, big.UseVerticalLimits),
                part);

        return Tag(new ScriptsAtom(null, baseAtom, subscript, superscript), part);
    }

    private static Atom? Command(TexPart part)
    {
        if (part.Node.Part(TexRole.Name)?.Text is not { } name) return null;

        switch (name)
        {
            case @"\frac":
            {
                if (Part(part, TexRole.Numerator) is not { } numerator) return null;
                if (Part(part, TexRole.Denominator) is not { } denominator) return null;

                return Tag(new FractionAtom(null, numerator, denominator, true), part);
            }

            case @"\sqrt":
            {
                if (part.Part(TexRole.Degree) is not null) return null;   // not yet
                if (Part(part, TexRole.Radicand) is not { } radicand) return null;

                return Tag(new Radical(null, radicand), part);
            }

            case @"\overline":
            {
                if (Part(part, TexRole.Base) is not { } inner) return null;

                return Tag(new OverlinedAtom(null, inner), part);
            }

            case @"\underline":
            {
                if (Part(part, TexRole.Base) is not { } inner) return null;

                return Tag(new UnderlinedAtom(null, inner), part);
            }
        }

        // Every accent at once, rather than a case each. What makes \vec an accent is that the symbol
        // table says so — the parser reads it the same way, and a table that grows a new one teaches
        // both of us together.
        if (part.Part(TexRole.Base) is { } accented && Accent(name) is { } accent)
        {
            if (Of(accented) is not { } inner) return null;

            return Tag(new AccentedAtom(null, inner, accent.Name), part);
        }

        // Anything else has to be a symbol standing on its own; a command with arguments this does not
        // know is exactly the case that must fall back rather than be guessed at.
        if (part.Parts.Any()) return null;

        // \prime is gathered into a row of primes by the parser, the same as an apostrophe.
        if (name == @"\prime") return null;

        return Symbol(name[1..], part);
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

    private static Atom? Symbol(string name, TexPart part)
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
            return null;
        }
    }

    // ── Bookkeeping ─────────────────────────────────────────────────────────

    /// <summary>The one part with this role, built — or null when it is absent or not buildable.</summary>
    private static Atom? Part(TexPart whole, string role)
    {
        foreach (var part in whole.Children)
            if (part.Role == role) return Of(part);

        return null;
    }

    /// <summary>Hangs the part on the atom built from it. The whole reason for building them here.</summary>
    private static Atom Tag(Atom atom, TexPart part)
    {
        atom.Origin = part;
        return atom;
    }
}
