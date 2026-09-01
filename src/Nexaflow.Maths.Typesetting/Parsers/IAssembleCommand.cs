using System.Collections.Generic;
using XamlMath.Atoms;

namespace XamlMath.Parsers;

/// <summary>
/// A command that can also be built from arguments somebody else has already read.
///
/// <para>
/// <see cref="ICommandParser.ProcessCommand"/> reads its arguments out of the source as it goes, because
/// when the source is all there is that is the only thing to do. A reading done from a parse tree arrives
/// with the arguments in hand and wants the other half — <em>what this command makes of them</em> — and
/// that half is the same either way. So it is asked for rather than copied: a second table of "what
/// <c>\binom</c> is" would be a second table to keep in step, and the first three of these were each
/// written out by hand before it was obvious they were one question.
/// </para>
/// <para>
/// The arguments come in the order this command reads them, which is the order they were written. Nothing
/// here counts them for meaning — where two arguments mean different things, the parse tree has already
/// named which is which, and a command that needs that distinction takes it from the roles instead.
/// </para>
/// </summary>
internal interface IAssembleCommand
{
    /// <summary>The atom this command stands for, or null when these arguments do not suit it.</summary>
    Atom? Assemble(IReadOnlyList<Atom> arguments, TexFormulaParser knowledge, Nexaflow.Maths.Latex.TexPart? origin);
}
