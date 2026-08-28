using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using XamlMath.Atoms;
using XamlMath.Boxes;
using XamlMath.Rendering;

namespace XamlMath;

/// <summary>Represents mathematical formula that can be rendered.</summary>
public sealed class TexFormula
{
    public string? TextStyle
    {
        get;
        set;
    }

    internal Atom? RootAtom
    {
        get;
        set;
    }

    /// <summary>
    /// What this formula is made of, as a tree of named parts — or null if it is made of nothing.
    /// <para>
    /// <see cref="IFormulaNode"/> exists so a consumer can ask what a piece is <em>to</em> the thing
    /// holding it, and until this was here the only way to reach one was to render the formula and take
    /// them off the boxes as they were drawn. That put fonts, a graphics stack and a whole layout pass
    /// between a caller and a question about the parse — which is not a question the layout answers, and
    /// not one that should need a screen to ask.
    /// </para>
    /// </summary>
    public IFormulaNode? Root => this.RootAtom;

    public SourceSpan? Source { get; set; }

    /// <summary>
    /// The parts of the input that could not be read, when this came from
    /// <see cref="TexFormulaParser.ParseWithRecovery(SourceSpan, string?)"/>. Empty for a clean parse, and
    /// always empty for a parse that was not recovering — that one throws instead.
    /// </summary>
    public IReadOnlyList<TexParseDiagnostic> Diagnostics { get; internal set; } = new List<TexParseDiagnostic>();

    /// <summary>
    /// The commands this was built from that nothing had a reading for, as the parts they were written
    /// as. Empty unless it came from <see cref="TexFormulaBuilder"/>, which draws nothing for one.
    ///
    /// <para>
    /// Parts rather than spans, and that is the whole reason this is a second list beside
    /// <see cref="Diagnostics"/> instead of more of the same. A diagnostic names a stretch of input; the
    /// builder may not name one, and does not have the input to name it from. So it reports <em>what</em>
    /// it ignored and whoever shows the reader asks that part where it was written — which is also the
    /// only moment the answer can still be right, after an edit or two.
    /// </para>
    /// </summary>
    public IReadOnlyList<Nexaflow.Maths.Latex.TexPart> Ignored { get; internal set; } =
        new List<Nexaflow.Maths.Latex.TexPart>();

    public void Add(TexFormula formula, SourceSpan? source = null)
    {
        Debug.Assert(formula != null);
        Debug.Assert(formula.RootAtom != null);

        this.Add(
            formula.RootAtom is RowAtom rowAtom
                ? new RowAtom(source, rowAtom)
                : formula.RootAtom,
            source);
    }

    /// <summary>
    /// Adds an atom to the formula. If the <see cref="RootAtom"/> exists and is not a <see cref="RowAtom"/>, it
    /// will become one.
    /// </summary>
    /// <param name="atom">The atom to add.</param>
    /// <param name="rowSource">The source that will be set for the resulting row atom.</param>
    internal void Add(Atom atom, SourceSpan? rowSource)
    {
        if (this.RootAtom == null)
        {
            this.RootAtom = atom;
        }
        else
        {
            var elements = (this.RootAtom is RowAtom r
                ? (IEnumerable<Atom>)r.Elements
                : new[] { this.RootAtom }).ToList();
            elements.Add(atom);
            this.RootAtom = new RowAtom(rowSource, elements);
        }
    }

    public void SetForeground(IBrush brush)
    {
        if (this.RootAtom is StyledAtom sa)
        {
            this.RootAtom = sa with { Foreground = brush };
        }
        else
        {
            RootAtom = new StyledAtom(RootAtom?.Source, RootAtom, null, brush);
        }
    }

    public void SetBackground(IBrush brush)
    {
        if (this.RootAtom is StyledAtom sa)
        {
            this.RootAtom = sa with { Background = brush };
        }
        else
        {
            this.RootAtom = new StyledAtom(this.RootAtom?.Source, this.RootAtom, brush, null);
        }
    }

    internal Box CreateBox(TexEnvironment environment)
    {
        if (this.RootAtom == null)
            return StrutBox.Empty;
        else
            return this.RootAtom.CreateBox(environment);
    }
}
