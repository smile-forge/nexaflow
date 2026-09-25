using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid.Cynefin;

/// <summary>The five sense-making domains, in the order their words are listed (<see cref="CynefinGrammar.Domains"/>).</summary>
public enum CynefinDomain { Clear, Complicated, Complex, Chaotic, Confusion }

/// <summary>Text somebody wrote: what it says, without its quotes, and the hole standing where it is still to write.</summary>
/// <param name="Part">The line it was written on — what pressing it means.</param>
public sealed record CynefinText(ContentPart Part, ContentPart Says, ContentPart? Hole);

/// <summary>Where a domain is opened: the line, and the word opening it — which is what is typed into where it is named.</summary>
public sealed record CynefinOpening(ContentPart Part, ContentPart Word);

/// <summary>One item: what it says, and the domain it sits in.</summary>
/// <param name="Part">The item as written — what pressing it means.</param>
public sealed record CynefinItem(ContentPart Part, CynefinText Says, CynefinDomain Domain, int Order);

/// <summary>A movement from one domain to another, with what it says where anything does.</summary>
/// <param name="To">The domain it goes to, or null where that is still to write.</param>
public sealed record CynefinMove(ContentPart Part, CynefinDomain From, CynefinDomain? To, CynefinText? Label, int Order);

/// <summary>
/// A <c>cynefin-beta</c> block, read: the domains it opens, the items sitting in each, and the movements between them. Its
/// title is the block's (<see cref="MermaidBlock.Title"/>).
///
/// <para>
/// A domain opened twice is the one domain, its items in the order they are written. An item sitting in no domain — written
/// before any is opened — is nowhere to draw, and says so where it is written (<see cref="Stages.ResolveDomains"/>).
/// </para>
/// </summary>
public sealed class CynefinDiagram
{
    /// <summary>
    /// How each domain is worked, which it says under its name: the decision model it asks for, and the kind of practice that
    /// comes of it — as the Cynefin framework names them. Disorder is where a thing sits while nobody knows.
    /// </summary>
    private static readonly Dictionary<CynefinDomain, IReadOnlyList<string>> Practices = new()
    {
        [CynefinDomain.Clear] = ["Sense → Categorise → Respond", "Best Practices"],
        [CynefinDomain.Complicated] = ["Sense → Analyse → Respond", "Good Practices"],
        [CynefinDomain.Complex] = ["Probe → Sense → Respond", "Emergent Practices"],
        [CynefinDomain.Chaotic] = ["Act → Sense → Respond", "Novel Practices"],
        [CynefinDomain.Confusion] = ["Disorder"],
    };

    private readonly Dictionary<CynefinDomain, CynefinOpening> _opened = [];

    private CynefinDiagram(MermaidBlock block, CynefinConfig config) => (Block, Config) = (block, config);

    

    public static CynefinDiagram Of(ContentNode tree) => Of(MermaidBlock.Of(tree));

    public static CynefinDiagram Of(MermaidBlock block)
    {
        var diagram = new CynefinDiagram(block, CynefinConfig.Read(block.Config));
        var items = new List<CynefinItem>();
        var moves = new List<CynefinMove>();

        foreach (var part in block.Reading.Root.SelfAndDescendants())
        {
            switch (part.Kind)
            {
                case CynefinKinds.Domain when Key(part, 0) is { } word && Domain(word.Text) is { } opened:
                    diagram._opened.TryAdd(opened, new CynefinOpening(part, word));
                    break;

                case CynefinKinds.Item when Text(part, CynefinRoles.Says) is { } says && Domain(part.Fact(CynefinRoles.In)) is { } sits:
                    items.Add(new CynefinItem(part, says, sits, items.Count));
                    break;

                case CynefinKinds.Move when Domain(Key(part, 0)?.Text) is { } from:
                    moves.Add(new CynefinMove(part, from, Domain(Key(part, 1)?.Text), Text(part, CynefinRoles.Label), moves.Count));
                    break;
            }
        }

        diagram.Items = items;
        diagram.Moves = moves;
        return diagram;
    }

    public MermaidBlock Block { get; }

    public CynefinConfig Config { get; }

    public IReadOnlyList<CynefinItem> Items { get; private set; } = [];

    public IReadOnlyList<CynefinMove> Moves { get; private set; } = [];

    /// <summary>Whether nothing is written for the diagram to draw.</summary>
    public bool Empty => _opened.Count == 0 && Items.Count == 0 && Moves.Count == 0;

    /// <summary>Where a domain is opened — what pressing it means — or null for a domain nothing opens.</summary>
    public CynefinOpening? Opened(CynefinDomain domain) => _opened.GetValueOrDefault(domain);

    /// <summary>The items sitting in a domain, in the order they are written.</summary>
    public IReadOnlyList<CynefinItem> ItemsIn(CynefinDomain domain) => [.. Items.Where(item => item.Domain == domain)];

    /// <summary>How a domain is worked: what it says under its name where the front matter asks for descriptions.</summary>
    public static IReadOnlyList<string> Practice(CynefinDomain domain) => Practices[domain];

    /// <summary>The domain a word opens, ignoring case — or null for a word that opens none.</summary>
    public static CynefinDomain? Domain(string? word) => word?.Trim().ToLowerInvariant() switch
    {
        CynefinGrammar.Clear => CynefinDomain.Clear,
        CynefinGrammar.Complicated => CynefinDomain.Complicated,
        CynefinGrammar.Complex => CynefinDomain.Complex,
        CynefinGrammar.Chaotic => CynefinDomain.Chaotic,
        CynefinGrammar.Confusion => CynefinDomain.Confusion,
        _ => null,
    };

    /// <summary>The word a line names, of the ones it names — a domain's, or a transition's ends in the order written.</summary>
    private static ContentPart? Key(ContentPart line, int which) =>
        line.Children.Where(child => child.Kind == MermaidKinds.Key).ElementAtOrDefault(which);

    private static CynefinText? Text(ContentPart line, string role) =>
        line.Children.FirstOrDefault(child => child.Kind == CynefinKinds.Text && child.Role == role) is { } text && text.Words() is { } says
            ? new CynefinText(line, says, text.Hole())
            : null;
}
