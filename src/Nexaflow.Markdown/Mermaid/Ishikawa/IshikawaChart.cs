using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid.Ishikawa;

/// <summary>The event, or one cause of it: what it says, and the causes under it.</summary>
/// <param name="Part">The line as written — what pressing it means.</param>
/// <param name="Says">What it says.</param>
public sealed class IshikawaCause(ContentPart Part, ContentPart Says)
{
    private readonly List<IshikawaCause> _causes = [];

    public ContentPart Part { get; } = Part;

    public ContentPart Says { get; } = Says;

    /// <summary>The causes written under it, in the order they are written.</summary>
    public IReadOnlyList<IshikawaCause> Causes => _causes;

    /// <summary>How many causes are under it, however deep.</summary>
    public int Descendants => _causes.Sum(cause => 1 + cause.Descendants);

    internal void Add(IshikawaCause cause) => _causes.Add(cause);
}

/// <summary>
/// An <c>ishikawa</c> block, read: the event it is about, and the causes of it as their indentation nests them.
///
/// <para>
/// The nesting is Mermaid's. The first line is the event, however far it is indented. The first cause's indentation is where
/// causes start, so the event may be indented more or less than they are; each later line is under the nearest line before it
/// indented less, and a line indented less than the first cause is a cause of the event itself.
/// </para>
/// </summary>
public sealed class IshikawaChart
{
    private IshikawaChart(MermaidBlock block, IshikawaConfig config) => (Block, Config) = (block, config);

    

    public static IshikawaChart Of(ContentNode tree) => Of(MermaidBlock.Of(tree));

    public static IshikawaChart Of(MermaidBlock block)
    {
        var chart = new IshikawaChart(block, IshikawaConfig.Read(block.Config));

        var lines = block.Reading.Root.SelfAndDescendants()
            .Where(part => part.Kind == IshikawaKinds.Cause && part.Words() is { Length: > 0 })
            .Select(part => (part.Indent(), part))
            .ToList();

        // The event first, and every cause under the nearest cause before it indented less — the event's own indentation
        // counting for nothing, since it may be written further in than its causes.
        var nested = MermaidOutline.Nested(lines, floor: true);
        var causes = new List<IshikawaCause>(nested.Count);

        for (var at = 0; at < nested.Count; at++)
        {
            var (part, parent) = (nested[at].Item, nested[at].Parent);
            var cause = new IshikawaCause(part, part.Words()!);
            causes.Add(cause);

            if (parent is { } over) causes[over].Add(cause);
            else chart.Effect ??= cause;
        }

        return chart;
    }

    public MermaidBlock Block { get; }

    public IshikawaConfig Config { get; }

    /// <summary>The event the diagram is about — the fish's head — or null where nothing is written.</summary>
    public IshikawaCause? Effect { get; private set; }
}
