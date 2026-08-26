using System.Collections.Generic;

namespace XamlMath.Parsers;

/// <summary>
/// An environment that asks the parser to carry on past what it cannot read, recording each such stretch
/// rather than abandoning the formula.
/// <para>
/// It is the same environment all the way down — <see cref="CreateChildEnvironment"/> returns itself — so
/// a fault inside a fraction's numerator is recorded in the same list as one at the top level, and the
/// caller gets one account of everything that went wrong.
/// </para>
/// </summary>
internal sealed class RecoveringCommandEnvironment : ICommandEnvironment
{
    private readonly List<TexParseDiagnostic> _diagnostics = new();

    public RecoveringCommandEnvironment(
        (int Start, int Length)? shownAsWritten = null, bool placeholders = true)
    {
        ShownAsWritten = shownAsWritten;
        Placeholders = placeholders;
    }

    public IReadOnlyDictionary<string, ICommandParser> AvailableCommands { get; } =
        new Dictionary<string, ICommandParser>();

    public ICollection<TexParseDiagnostic>? Diagnostics => _diagnostics;

    /// <inheritdoc/>
    public (int Start, int Length)? ShownAsWritten { get; }

    /// <inheritdoc/>
    public bool Placeholders { get; }

    /// <summary>What could not be read, in the order it was met.</summary>
    public IReadOnlyList<TexParseDiagnostic> Collected => _diagnostics;

    public ICommandEnvironment CreateChildEnvironment() => this;

    public bool ProcessUnknownCharacter(TexFormula formula, char character, SourceSpan at) => false;
}
