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

    public static IshikawaChart Read(string? block) => Of(MermaidParser.Read(block));

    public static IshikawaChart Of(ContentNode tree) => Of(MermaidBlock.Of(tree));

    public static IshikawaChart Of(MermaidBlock block)
    {
        var chart = new IshikawaChart(block, IshikawaConfig.Read(block.Config));
        var stack = new List<(int Level, IshikawaCause Cause)>();
        int? start = null;

        foreach (var part in block.Reading.Root.SelfAndDescendants().Where(part => part.Kind == IshikawaKinds.Cause))
        {
            if (part.Words() is not { Length: > 0 } says) continue;
            var cause = new IshikawaCause(part, says);

            if (chart.Effect is null)
            {
                chart.Effect = cause;
                stack.Add((0, cause));
                continue;
            }

            var indent = part.Indent();
            start ??= indent;
            var level = Math.Max(1, indent - start.Value + 1);

            while (stack.Count > 1 && stack[^1].Level >= level) stack.RemoveAt(stack.Count - 1);
            stack[^1].Cause.Add(cause);
            stack.Add((level, cause));
        }

        return chart;
    }

    public MermaidBlock Block { get; }

    public IshikawaConfig Config { get; }

    /// <summary>The event the diagram is about — the fish's head — or null where nothing is written.</summary>
    public IshikawaCause? Effect { get; private set; }
}
