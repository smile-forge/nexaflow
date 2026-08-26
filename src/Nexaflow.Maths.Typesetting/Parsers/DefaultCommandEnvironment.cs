using System.Collections.Generic;

namespace XamlMath.Parsers;

internal sealed class DefaultCommandEnvironment : ICommandEnvironment
{
    public static readonly ICommandEnvironment Instance = new DefaultCommandEnvironment();

    /// <summary>Nothing is recorded: without a recovering parse, a fault ends the parse.</summary>
    public ICollection<TexParseDiagnostic>? Diagnostics => null;

    /// <summary>All of it is read: showing a stretch as written is an editor's request, not a parse.</summary>
    public (int Start, int Length)? ShownAsWritten => null;

    /// <summary>No holes: a plain parse is a reading of what is there, not a surface being written on.</summary>
    public bool Placeholders => false;

    public IReadOnlyDictionary<string, ICommandParser> AvailableCommands { get; } =
        new Dictionary<string, ICommandParser>();

    public ICommandEnvironment CreateChildEnvironment() => Instance;

    public bool ProcessUnknownCharacter(TexFormula formula, char character, SourceSpan at) => false;
}
