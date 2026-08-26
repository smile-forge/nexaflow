using System.Collections.Generic;
using System.Linq;
using XamlMath.Exceptions;

namespace XamlMath.Parsers.Matrices;

/// <summary>
/// The column preamble of an <c>array</c> environment: <c>{lcr}</c> gives each column its alignment,
/// and each <c>|</c> asks for a rule at the boundary it sits at.
/// </summary>
internal sealed class ArrayColumnSpec
{
    private ArrayColumnSpec(IReadOnlyList<TexAlignment> alignments, IReadOnlyCollection<int> verticalRules)
    {
        Alignments = alignments;
        VerticalRules = verticalRules;
    }

    /// <summary>One entry per column.</summary>
    public IReadOnlyList<TexAlignment> Alignments { get; }

    /// <summary>
    /// Boundaries carrying a rule, numbered from 0 (left of the first column) to the column count
    /// (right of the last).
    /// </summary>
    public IReadOnlyCollection<int> VerticalRules { get; }

    /// <summary>Reads a preamble such as <c>c|cc</c>.</summary>
    /// <exception cref="TexParseException">On anything this implementation cannot draw.</exception>
    public static ArrayColumnSpec Parse(string preamble)
    {
        var alignments = new List<TexAlignment>();
        var rules = new HashSet<int>();

        foreach (var c in preamble)
        {
            switch (c)
            {
                case 'l': alignments.Add(TexAlignment.Left); break;
                case 'c': alignments.Add(TexAlignment.Center); break;
                case 'r': alignments.Add(TexAlignment.Right); break;
                case '|': rules.Add(alignments.Count); break;
                case ' ': break;
                default:
                    throw new TexParseException(
                        $"Unsupported column specifier '{c}' in an array preamble: only l, c, r and | are.");
            }
        }

        if (alignments.Count == 0)
            throw new TexParseException("An array preamble must give at least one column.");

        return new ArrayColumnSpec(alignments, rules.ToList());
    }

    /// <summary>The alignment of a column, repeating the last one if the body outgrew the preamble.</summary>
    public TexAlignment AlignmentOf(int columnIndex) =>
        Alignments[columnIndex < Alignments.Count ? columnIndex : Alignments.Count - 1];
}
