using System.Collections.Generic;
using System.Linq;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Parser.Parts;
using UglyToad.PdfPig.Tokenization.Scanner;
using UglyToad.PdfPig.Tokens;

namespace Nexaflow.Features.Pdf.Document;

/// <summary>
/// Which of a document's optional content (layers) shows, by its default configuration. Content in a hidden layer —
/// a print-only watermark, an alternative language, a CAD drawing's switched-off overlay — is not drawn.
/// </summary>
internal sealed class PdfOptionalContent
{
    /// <summary>A document with no layers: everything shows.</summary>
    public static PdfOptionalContent None { get; } = new([], baseOn: true, []);

    private static readonly NameToken OcProperties = NameToken.Create("OCProperties");
    private static readonly NameToken BaseState    = NameToken.Create("BaseState");
    private static readonly NameToken On           = NameToken.Create("ON");
    private static readonly NameToken Off          = NameToken.Create("OFF");
    private static readonly NameToken Ocgs         = NameToken.Create("OCGs");
    private static readonly NameToken Ocmd         = NameToken.Create("OCMD");
    private static readonly NameToken Policy       = NameToken.Create("P");
    private static readonly NameToken Expression   = NameToken.Create("VE");
    private static readonly NameToken AllOn        = NameToken.Create("AllOn");
    private static readonly NameToken AnyOff       = NameToken.Create("AnyOff");
    private static readonly NameToken AllOff       = NameToken.Create("AllOff");
    private static readonly NameToken Default      = NameToken.Create("D");

    private readonly HashSet<DictionaryToken> _on;
    private readonly bool _baseOn;
    private readonly HashSet<DictionaryToken> _off;
    private readonly IPdfTokenScanner? _scanner;

    private PdfOptionalContent(HashSet<DictionaryToken> on, bool baseOn, HashSet<DictionaryToken> off, IPdfTokenScanner? scanner = null)
    {
        _on      = on;
        _baseOn  = baseOn;
        _off     = off;
        _scanner = scanner;
    }

    /// <summary>The default configuration of the document whose catalog is <paramref name="catalog"/>.</summary>
    public static PdfOptionalContent Read(DictionaryToken catalog, IPdfTokenScanner scanner)
    {
        if (!catalog.TryGet(OcProperties, scanner, out DictionaryToken? properties) ||
            !properties.TryGet(Default, scanner, out DictionaryToken? configuration))
            return None;

        var baseOn = !(configuration.TryGet(BaseState, scanner, out NameToken? state) && state.Equals(Off));
        return new PdfOptionalContent(Groups(configuration, On, scanner), baseOn, Groups(configuration, Off, scanner), scanner);
    }

    /// <summary>
    /// Whether content marked with <paramref name="membership"/> — an optional content group, or a membership
    /// dictionary over several — shows.
    /// </summary>
    public bool Shows(DictionaryToken? membership)
    {
        if (membership is null || _scanner is null) return true;

        if (!membership.TryGet(NameToken.Type, _scanner, out NameToken? type) || !type.Equals(Ocmd))
            return GroupShows(membership);

        if (membership.TryGet(Expression, _scanner, out ArrayToken? expression))
            return Evaluates(expression);

        var groups = Members(membership).ToList();
        if (groups.Count == 0) return true;

        var policy = membership.TryGet(Policy, _scanner, out NameToken? named) ? named : null;
        if (policy?.Equals(AllOn) == true)  return groups.All(GroupShows);
        if (policy?.Equals(AnyOff) == true) return groups.Any(g => !GroupShows(g));
        if (policy?.Equals(AllOff) == true) return groups.All(g => !GroupShows(g));
        return groups.Any(GroupShows);
    }

    private bool GroupShows(DictionaryToken group) =>
        _baseOn ? !_off.Contains(group) : _on.Contains(group);

    private IEnumerable<DictionaryToken> Members(DictionaryToken membership)
    {
        if (!membership.TryGet(Ocgs, out var token)) yield break;

        if (DirectObjectFinder.TryGet(token, _scanner!, out DictionaryToken? single))
        {
            yield return single;
            yield break;
        }

        if (!DirectObjectFinder.TryGet(token, _scanner!, out ArrayToken? many)) yield break;
        foreach (var item in many.Data)
            if (DirectObjectFinder.TryGet(item, _scanner!, out DictionaryToken? group))
                yield return group;
    }

    /// <summary>A visibility expression: <c>[/And …]</c>, <c>[/Or …]</c>, <c>[/Not x]</c>, its leaves groups.</summary>
    private bool Evaluates(ArrayToken expression)
    {
        if (expression.Length == 0 || expression.Data[0] is not NameToken op) return true;

        var operands = expression.Data.Skip(1).Select(Operand).ToList();
        return op.Data switch
        {
            "And" => operands.All(static x => x),
            "Or"  => operands.Any(static x => x),
            "Not" => operands.Count > 0 && !operands[0],
            _     => true,
        };
    }

    private bool Operand(IToken token)
    {
        if (DirectObjectFinder.TryGet(token, _scanner!, out ArrayToken? nested)) return Evaluates(nested);
        if (DirectObjectFinder.TryGet(token, _scanner!, out DictionaryToken? group)) return GroupShows(group);
        return true;
    }

    private static HashSet<DictionaryToken> Groups(DictionaryToken configuration, NameToken key, IPdfTokenScanner scanner)
    {
        var groups = new HashSet<DictionaryToken>();
        if (!configuration.TryGet(key, scanner, out ArrayToken? list)) return groups;

        foreach (var item in list.Data)
            if (DirectObjectFinder.TryGet(item, scanner, out DictionaryToken? group))
                groups.Add(group);

        return groups;
    }
}
