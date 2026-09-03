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

    /// <summary>
    /// The commands this was built from that nothing had a reading for, as the parts they were written
    /// as. Empty unless it came from <see cref="TexFormulaBuilder"/>, which draws nothing for one.
    ///
    /// <para>
    /// Parts rather than spans. A stretch of input is the obvious thing to report and the wrong one: the
    /// builder may not have a stretch to name, and does not hold the input to name it from. So it reports
    /// <em>what</em> it ignored and whoever shows the reader asks that part where it was written — which
    /// is also the only moment the answer can still be right, after an edit or two.
    /// </para>
    /// </summary>
    public IReadOnlyList<Nexaflow.Maths.Latex.TexPart> Ignored { get; internal set; } =
        new List<Nexaflow.Maths.Latex.TexPart>();

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
