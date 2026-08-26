using System.Collections.Generic;
using System.Linq;
using Nexaflow.Maths.Latex;
using XamlMath.Atoms;

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

        var source = new SourceSpan("User input", reading.Latex, 0, reading.Latex.Length);
        var root = Run(reading.Root.Parts, reading.Root, source);

        return root is null ? null : new TexFormula { RootAtom = root, Source = source };
    }

    /// <summary>Whether this reading can be built at all — the corpus's coverage question.</summary>
    public static bool CanBuild(TexReading reading) => Build(reading) is not null;

    // ── One part ────────────────────────────────────────────────────────────

    private static Atom? Of(TexPart part, SourceSpan source) =>
        part.Kind switch
        {
            TexKind.Char => Character(part, source),
            TexKind.Sequence => Run(part.Parts, part, source),
            TexKind.Group when Written(part) => Group(part, source),
            TexKind.Group => Run(part.Parts, part, source),
            TexKind.Script => Script(part, source),
            TexKind.Command => Command(part, source),
            TexKind.Fence => Fence(part, source),
            _ => null,
        };

    /// <summary>
    /// Something between delimiters that grow to hold it. The delimiters are drawn by the fence rather
    /// than being things inside it, which is why they are named here and not built.
    /// </summary>
    private static Atom? Fence(TexPart part, SourceSpan source)
    {
        if (part.Part(TexRole.Body) is not { } body) return null;

        // A fence inside a fence, not yet. Delimiters grow to fit what is between them, and the parser
        // puts a scripted fence inside boxes of its own before measuring it — so `\left[ \left( a
        // \right)^2 \right]` picks a smaller bracket there than it does here. Every disagreement the
        // corpus had left was this, and a bracket one size out is not something to guess at.
        if (body.SelfAndDescendants().Any(inner => inner.Kind == TexKind.Fence)) return null;

        if (Of(body, source) is not { } inside) return null;

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
            new FencedAtom(Span(part, source), inside, Delimiter(open, source), Delimiter(close, source)),
            part);
    }

    /// <summary>What a <c>\left</c> or <c>\right</c> was written with, as written.</summary>
    private static string Names(TexPart fence) =>
        fence.Part(TexRole.Argument)?.Node.Print() ?? string.Empty;

    /// <summary>The delimiter a <c>\left</c> or <c>\right</c> was written with.</summary>
    private static SymbolAtom? Delimiter(TexPart fence, SourceSpan source)
    {
        if (fence.Part(TexRole.Argument) is not { } written) return null;

        var span = Span(written, source);
        var text = written.Node.Print();

        // A character stands for a delimiter through TeX's own table — `(` is not the symbol named "(".
        // A command names one directly, without its backslash.
        return text.Length == 1
            ? TexFormulaParser.DelimiterOf(text[0], span)
            : TexFormulaParser.DelimiterOf(text.TrimStart('\\'), span);
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

    private static Atom? Character(TexPart part, SourceSpan source)
    {
        if (part.Node.Text.Length != 1) return null;

        var character = part.Node.Text[0];
        if (Peculiar.Contains(character)) return null;

        return Tag(TexFormulaParser.CharacterOf(character, Span(part, source)), part);
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
    private static Atom? Group(TexPart part, SourceSpan source)
    {
        if (Run(part.Parts, part, source) is not { } inner) return null;

        return Tag(
            new TypedAtom(Span(part, source), inner, TexAtomType.Ordinary, TexAtomType.Ordinary),
            part);
    }

    /// <summary>
    /// Several things in a row. One thing on its own is that thing: a row of one would add a level the
    /// typesetter's own reading does not have, and every box would sit inside a box.
    /// </summary>
    private static Atom? Run(IEnumerable<TexPart> parts, TexPart whole, SourceSpan source)
    {
        var built = Built(parts, source);
        if (built is null || built.Count == 0) return null;

        return built.Count == 1 ? built[0] : Rowed(built, whole, source);
    }

    /// <summary>The same, but a row however few things are in it — see <see cref="Fence"/>.</summary>
    private static Atom? Row(IEnumerable<TexPart> parts, TexPart whole, SourceSpan source)
    {
        var built = Built(parts, source);
        if (built is null || built.Count == 0) return null;

        return Rowed(built, whole, source);
    }

    private static List<Atom>? Built(IEnumerable<TexPart> parts, SourceSpan source)
    {
        var built = new List<Atom>();

        foreach (var part in parts)
        {
            var atom = Of(part, source);
            if (atom is null) return null;

            built.Add(atom);
        }

        return built;
    }

    private static Atom Rowed(List<Atom> built, TexPart whole, SourceSpan source)
    {
        var row = new RowAtom(Span(whole, source));
        foreach (var atom in built) row = row.Add(atom);

        return Tag(row, whole);
    }

    private static Atom? Script(TexPart part, SourceSpan source)
    {
        if (Part(part, TexRole.Base, source) is not { } baseAtom) return null;

        // A rule drawn over something, with a script after it. The parser attaches the script to a
        // different atom than this does, and the two set the script at different heights — visible only
        // once, in a corpus of a quarter of a million formulas, and wrong is wrong.
        if (part.Part(TexRole.Base) is { Kind: TexKind.Command } command
            && command.Node.Part(TexRole.Name)?.Text is @"\overline" or @"\underline")
            return null;

        var superscript = Part(part, TexRole.Superscript, source);
        var subscript = Part(part, TexRole.Subscript, source);

        // A script with nothing written in it is not something this builds — the parser has a
        // placeholder for that and the two would not agree.
        if (part.Part(TexRole.Superscript) is not null && superscript is null) return null;
        if (part.Part(TexRole.Subscript) is not null && subscript is null) return null;

        // Scripts on a big operator are its limits, not scripts. TeX stacks a sum's above and below it
        // and sets an integral's beside it, and it is a different atom that knows the difference.
        if (baseAtom is BigOperatorAtom big)
            return Tag(
                new BigOperatorAtom(Span(part, source), big.BaseAtom, subscript, superscript, big.UseVerticalLimits),
                part);

        return Tag(new ScriptsAtom(Span(part, source), baseAtom, subscript, superscript), part);
    }

    private static Atom? Command(TexPart part, SourceSpan source)
    {
        if (part.Node.Part(TexRole.Name)?.Text is not { } name) return null;

        switch (name)
        {
            case @"\frac":
            {
                if (Part(part, TexRole.Numerator, source) is not { } numerator) return null;
                if (Part(part, TexRole.Denominator, source) is not { } denominator) return null;

                return Tag(new FractionAtom(Span(part, source), numerator, denominator, true), part);
            }

            case @"\sqrt":
            {
                if (part.Part(TexRole.Degree) is not null) return null;   // not yet
                if (Part(part, TexRole.Radicand, source) is not { } radicand) return null;

                return Tag(new Radical(Span(part, source), radicand), part);
            }

            case @"\overline":
            {
                if (Part(part, TexRole.Base, source) is not { } inner) return null;

                return Tag(new OverlinedAtom(Span(part, source), inner), part);
            }

            case @"\underline":
            {
                if (Part(part, TexRole.Base, source) is not { } inner) return null;

                return Tag(new UnderlinedAtom(Span(part, source), inner), part);
            }
        }

        // Every accent at once, rather than a case each. What makes \vec an accent is that the symbol
        // table says so — the parser reads it the same way, and a table that grows a new one teaches
        // both of us together.
        if (part.Part(TexRole.Base) is { } accented && Accent(name) is { } accent)
        {
            if (Of(accented, source) is not { } inner) return null;

            return Tag(new AccentedAtom(Span(part, source), inner, accent.Name), part);
        }

        // Anything else has to be a symbol standing on its own; a command with arguments this does not
        // know is exactly the case that must fall back rather than be guessed at.
        if (part.Parts.Any()) return null;

        // \prime is gathered into a row of primes by the parser, the same as an apostrophe.
        if (name == @"\prime") return null;

        return Symbol(name[1..], part, source);
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

    private static Atom? Symbol(string name, TexPart part, SourceSpan source)
    {
        try
        {
            var symbol = SymbolAtom.GetAtom(name, Span(part, source));

            // A big operator is never merely a symbol: every sum and integral gets its own atom whether
            // or not anything was written above or below it, because how its limits would be set is
            // part of what it is.
            return symbol.Type == TexAtomType.BigOperator
                ? Tag(TexFormulaParser.BigOperatorOf(symbol, Span(part, source)), part)
                : Tag(symbol, part);
        }
        catch (SymbolNotFoundException)
        {
            return null;
        }
    }

    // ── Bookkeeping ─────────────────────────────────────────────────────────

    /// <summary>The one part with this role, built — or null when it is absent or not buildable.</summary>
    private static Atom? Part(TexPart whole, string role, SourceSpan source)
    {
        foreach (var part in whole.Children)
            if (part.Role == role) return Of(part, source);

        return null;
    }

    private static SourceSpan Span(TexPart part, SourceSpan source) =>
        source.Segment(part.Start, part.Length);

    /// <summary>Hangs the part on the atom built from it. The whole reason for building them here.</summary>
    private static Atom Tag(Atom atom, TexPart part)
    {
        atom.Origin = part;
        return atom;
    }
}
