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
            // Braces written as content are a construct; braces written as an argument are how the
            // argument was delimited and nothing more. `{x}` in a run is an ordinary atom the spacing is
            // read against, and so is the `{\gamma}` of `{\gamma}^2` — the script was attached to it
            // afterwards, it was not written as anything's argument. The `{x}` of `\frac{x}{y}` is just
            // where the numerator stops.
            TexKind.Group when part.Role is TexRole.Element or TexRole.Base => Group(part, source),
            TexKind.Group => Run(part.Parts, part, source),
            TexKind.Script => Script(part, source),
            TexKind.Command => Command(part, source),
            _ => null,
        };

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
        var built = new List<Atom>();

        foreach (var part in parts)
        {
            var atom = Of(part, source);
            if (atom is null) return null;

            built.Add(atom);
        }

        if (built.Count == 0) return null;
        if (built.Count == 1) return built[0];

        var row = new RowAtom(Span(whole, source));
        foreach (var atom in built) row = row.Add(atom);

        return Tag(row, whole);
    }

    private static Atom? Script(TexPart part, SourceSpan source)
    {
        if (Part(part, TexRole.Base, source) is not { } baseAtom) return null;

        // A script on a big operator is not a script at all. TeX stacks a sum's bounds above and below
        // it and sets an integral's beside it, and the parser builds a different atom entirely for that
        // — so building a plain script here would put the limits somewhere they have never been.
        if (baseAtom is SymbolAtom { Type: TexAtomType.BigOperator }) return null;

        var superscript = Part(part, TexRole.Superscript, source);
        var subscript = Part(part, TexRole.Subscript, source);

        // A script with nothing written in it is not something this builds — the parser has a
        // placeholder for that and the two would not agree.
        if (part.Part(TexRole.Superscript) is not null && superscript is null) return null;
        if (part.Part(TexRole.Subscript) is not null && subscript is null) return null;

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
        }

        // Anything else has to be a symbol standing on its own; a command with arguments this does not
        // know is exactly the case that must fall back rather than be guessed at.
        if (part.Parts.Any()) return null;

        // \prime is gathered into a row of primes by the parser, the same as an apostrophe.
        if (name == @"\prime") return null;

        return Symbol(name[1..], part, source);
    }

    private static Atom? Symbol(string name, TexPart part, SourceSpan source)
    {
        try
        {
            var symbol = SymbolAtom.GetAtom(name, Span(part, source));

            // A big operator is never merely a symbol — the parser gives every sum and integral its own
            // atom whether or not anything was written above or below it, and that atom is set
            // differently. Left alone until this builds one.
            return symbol.Type == TexAtomType.BigOperator ? null : Tag(symbol, part);
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
