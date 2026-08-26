using System.Collections.Generic;
using XamlMath.Atoms;

namespace XamlMath.Parsers.Matrices;

/// <summary>
/// <c>\hline</c> inside an <c>array</c>. It draws nothing itself: it records which row boundary it
/// stood at, and the rule is drawn once the whole grid has been laid out and its row heights are
/// known.
/// </summary>
internal sealed class HlineCommand : ICommandParser
{
    private readonly List<List<Atom>> _rows;
    private readonly HashSet<int> _boundaries;

    public HlineCommand(List<List<Atom>> rows, HashSet<int> boundaries)
    {
        _rows = rows;
        _boundaries = boundaries;
    }

    public CommandProcessingResult ProcessCommand(CommandContext context)
    {
        // The boundary is above the row being built: one \hline before any content is the top rule,
        // and one straight after a row separator sits between that row and the next.
        _boundaries.Add(_rows.Count - 1);
        return new CommandProcessingResult(null, context.ArgumentsStartPosition);
    }
}
