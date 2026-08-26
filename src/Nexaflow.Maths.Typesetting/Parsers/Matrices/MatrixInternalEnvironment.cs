using System.Collections.Generic;
using XamlMath.Atoms;

namespace XamlMath.Parsers.Matrices;

internal sealed class MatrixInternalEnvironment : NonRecursiveEnvironment
{
    private static IReadOnlyDictionary<string, ICommandParser> GetCommands(
        List<List<Atom>> rows,
        HashSet<int>? hlineBoundaries)
    {
        var nextRowCommand = new NextRowCommand(rows);
        var commands = new Dictionary<string, ICommandParser>
        {
            [@"\"] = nextRowCommand,
            ["cr"] = nextRowCommand
        };

        // Only an array asks for horizontal rules, and only it collects them.
        if (hlineBoundaries != null)
            commands["hline"] = new HlineCommand(rows, hlineBoundaries);

        return commands;
    }

    private readonly List<List<Atom>> _rows;

    public MatrixInternalEnvironment(
        ICommandEnvironment parentEnvironment,
        List<List<Atom>> rows,
        HashSet<int>? hlineBoundaries = null)
        : base(parentEnvironment.CreateChildEnvironment(), GetCommands(rows, hlineBoundaries))
    {
        _rows = rows;
    }

    public override bool ProcessUnknownCharacter(TexFormula formula, char character, SourceSpan at)
    {
        if (character == '&')
        {
            NextRowCommand.NextCell(_rows, formula, at, Placeholders);
            return true;
        }

        return false;
    }
}
