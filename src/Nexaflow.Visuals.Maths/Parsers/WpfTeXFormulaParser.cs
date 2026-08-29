using System;
using System.Collections.Generic;
using WpfMath.Rendering;
using XamlMath;

namespace WpfMath.Parsers;

public static class WpfTeXFormulaParser
{
    /// <summary>
    /// The typesetter's tables: which symbol a name means, which face, what it has a drawing for.
    ///
    /// <para>
    /// Built with no predefined formulas, which is what it used to be mostly made of. Those were 107
    /// definitions in XML, each a scrap of LaTeX the engine's own parser read at start-up — and reading
    /// them was the last thing keeping that parser alive. Ninety-one of them are macros now, expanded
    /// while the formula is read; the rest are lengths in mu with no LaTeX spelling, built beside the
    /// symbols. The six multiple integrals were last, because their definition was LaTeX <em>plus</em> a
    /// call typing the result as a big operator, which no macro can say — until <c>\mathop</c> could.
    /// </para>
    /// </summary>
    public static TexFormulaParser Instance { get; } =
        new(WpfBrushFactory.Instance, new Dictionary<string, Func<SourceSpan, TexFormula?>>());
}
