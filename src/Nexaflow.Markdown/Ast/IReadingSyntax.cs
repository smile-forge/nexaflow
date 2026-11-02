namespace Nexaflow.Markdown.Ast;

/// <summary>
/// What a reading's language keeps of the whole of it beside its units — code's syntax tree, which another parser builds over all of
/// the reading's characters — carried through every change as the units and the bodies they hold are (<see cref="ContentUnits.Syntax"/>).
/// Immutable: a change makes another, so every version of a reading has its own, and one handed to another thread stays as it was.
/// </summary>
public interface IReadingSyntax
{
    /// <summary>
    /// The syntax of the reading once its characters from <paramref name="from"/> to <paramref name="to"/> were written over, leaving
    /// it <paramref name="by"/> characters longer — <paramref name="edited"/> being its characters now.
    /// </summary>
    IReadingSyntax Edited(int from, int to, int by, IContentCharacters edited);
}
