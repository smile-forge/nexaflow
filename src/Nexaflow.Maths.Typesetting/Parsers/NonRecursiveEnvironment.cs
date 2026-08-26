using System.Collections.Generic;

namespace XamlMath.Parsers;

internal abstract class NonRecursiveEnvironment : ICommandEnvironment
{
    private readonly ICommandEnvironment _environment;

    public NonRecursiveEnvironment(
        ICommandEnvironment environment,
        IReadOnlyDictionary<string, ICommandParser> availableCommands)
    {
        _environment = environment;
        AvailableCommands = availableCommands;
    }

    public IReadOnlyDictionary<string, ICommandParser> AvailableCommands { get; }

    /// <summary>Whatever the environment this one wraps records into — recovery is not scoped.</summary>
    public ICollection<TexParseDiagnostic>? Diagnostics => _environment.Diagnostics;

    /// <summary>Likewise the wrapped environment's — where the input is being written is not scoped either.</summary>
    public (int Start, int Length)? ShownAsWritten => _environment.ShownAsWritten;

    /// <summary>The wrapped environment's - what a parse is for does not change inside a group.</summary>
    public bool Placeholders => _environment.Placeholders;

    public ICommandEnvironment CreateChildEnvironment() => _environment;

    public abstract bool ProcessUnknownCharacter(TexFormula formula, char character, SourceSpan at);
}
