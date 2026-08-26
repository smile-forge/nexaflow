using System.Collections.Generic;

namespace XamlMath.Parsers;

/// <summary>
/// An environment in which the command parsing is performed. This environment may provide additional commands for
/// the current parser context.
/// <para/>
/// Environment may be recursive and non-recursive, it decides whether it should be recursive or provide any other
/// kind of environment for child contexts itself.
/// </summary>
internal interface ICommandEnvironment
{
    /// <summary>
    /// Where to record a part of the input that could not be read, or <c>null</c> to give up on the whole
    /// formula instead — which is what every caller but a recovering parse wants.
    /// <para>
    /// It rides on the environment because the environment is already threaded through every nested
    /// parse, so a group or a fraction's numerator reports into the same list as the formula holding it
    /// without a single signature having to change to carry it.
    /// </para>
    /// </summary>
    ICollection<TexParseDiagnostic>? Diagnostics { get; }

    /// <summary>
    /// A stretch of the input to set as the characters written rather than read as maths, or <c>null</c>
    /// to read all of it. Offsets are into the whole input, not into whatever span is being parsed.
    /// <para>
    /// For an editor, where a piece of a formula is being written and so must be seen exactly as typed
    /// while everything around it stays typeset. Recovery does the same thing for input that could not be
    /// read; this is the same treatment asked for deliberately, and it goes through the same code — so
    /// the characters take up room, wrap, hit-test and carry a span each, and the formula around them is
    /// laid out knowing they are there.
    /// </para>
    /// <para>
    /// It rides on the environment for the same reason the diagnostics do: the environment is already
    /// threaded through every nested parse, so a stretch inside a fraction's numerator is honoured
    /// without a signature having to change to carry it.
    /// </para>
    /// </summary>
    (int Start, int Length)? ShownAsWritten { get; }

    /// <summary>
    /// Whether an argument or a table cell left empty should be given a placeholder to stand in it.
    /// <para>
    /// An editing affordance, and only that. A hole is drawn so a reader can see there is something
    /// still to write and can aim at it; a formula being set for presentation has no such reader, and
    /// a box in the middle of a published equation would simply be wrong. So the parse that an editor
    /// asks for produces them and the parse a renderer asks for does not - it is the same source either
    /// way, read for a different purpose.
    /// </para>
    /// </summary>
    bool Placeholders { get; }

    /// <summary>Commands from the current environment.</summary>
    IReadOnlyDictionary<string, ICommandParser> AvailableCommands { get; }

    /// <summary>This method gets called when the environment is about to be applied recursively.</summary>
    /// <returns>
    /// A child environment that will be applied to the child parsing context (e.g. a nested element group).
    /// </returns>
    ICommandEnvironment CreateChildEnvironment();

    /// <summary>Processes an unknown character found during parsing.</summary>
    /// <param name="character">The character that wasn't resolved during parsing.</param>
    /// <returns>
    /// Should return <c>true</c> if the character was processed by this method. Otherwise, parser will throw an
    /// exception.
    /// </returns>
    /// <param name="at">Where the character is in the input, so anything the environment builds in its
    /// place can say where it came from - a matrix's empty cell is a hole standing at its separator.</param>
    bool ProcessUnknownCharacter(TexFormula formula, char character, SourceSpan at);
}
