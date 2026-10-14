using Markdig;
using Markdig.Syntax;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Prose;

namespace Nexaflow.Tests.Markdown.Prose.Differential;

/// <summary>
/// The readings the harness holds side by side, and where they part.
///
/// <para>
/// <strong>Markdig</strong> reads the whole document once, with the reading's own options — the reference. <strong>Today</strong>
/// is the reading as the app has it once what the document defines is known: the parse, then the stages that settle
/// what a name means (<see cref="WithTargets"/>, <see cref="WithReferences"/>). A reader of our own joins them as a third,
/// read through the same stages and held to the same measure.
/// </para>
/// </summary>
internal static class MarkdownReadings
{
    /// <summary>
    /// The reading's options, with every inline's place counted exactly: without it Markdig measures a word on a
    /// paragraph's later lines from where it thinks the line starts, which is not always where it does.
    /// </summary>
    public static readonly MarkdownPipeline Oracle = MarkdownParser.Reading(new MarkdownPipelineBuilder()).UsePreciseSourceLocation().Build();

    public const string MarkdigOnly = "markdig-only";
    public const string TodayOnly = "today-only";

    /// <summary>The document as Markdig reads it.</summary>
    public static MarkdownDocument Markdig(string source) => global::Markdig.Markdown.Parse(source, Oracle);

    /// <summary>The document as the app reads it, once what it defines is known.</summary>
    public static ContentNode Today(string source)
    {
        var targets = new LinkTargets();

        return new AstPipeline(new WithTargets(targets), new WithReferences(targets)).Run(MarkdownParser.Parsing()(source).Tree);
    }

    /// <summary>
    /// Where the two readings of <paramref name="input"/> part: every construct one says is there and the other does not,
    /// and which said it. A document Markdig throws on has nothing to be held to, and is said to be one.
    /// </summary>
    public static IReadOnlyList<MarkdownDivergence> Parted(MarkdownInput input)
    {
        List<MarkdownConstruct> theirs;

        try
        {
            theirs = MarkdownShape.Of(Markdig(input.Source), input.Source);
        }
        catch (Exception thrown)
        {
            return [new MarkdownDivergence(input.Id, "markdig-threw", new MarkdownConstruct(thrown.GetType().Name, 0, 0, string.Empty), input.Source)];
        }

        var ours = MarkdownShape.Of(Today(input.Source), input.Source);

        return
        [
            .. Unmatched(theirs, ours).Select(construct => new MarkdownDivergence(input.Id, MarkdigOnly, construct, input.Source)),
            .. Unmatched(ours, theirs).Select(construct => new MarkdownDivergence(input.Id, TodayOnly, construct, input.Source)),
        ];
    }

    /// <summary>What <paramref name="these"/> hold that <paramref name="those"/> do not, counting each as often as it is held.</summary>
    public static IEnumerable<MarkdownConstruct> Unmatched(IEnumerable<MarkdownConstruct> these, IEnumerable<MarkdownConstruct> those)
    {
        var left = those.GroupBy(construct => construct).ToDictionary(group => group.Key, group => group.Count());

        foreach (var construct in these.OrderBy(construct => construct.Start).ThenByDescending(construct => construct.End))
        {
            if (left.TryGetValue(construct, out var count) && count > 0) left[construct] = count - 1;
            else yield return construct;
        }
    }
}

/// <summary>A construct one reading of a document says is there and the other does not.</summary>
/// <param name="Side">Which reading said it.</param>
internal sealed record MarkdownDivergence(string Input, string Side, MarkdownConstruct Construct, string Source)
{
    /// <summary>The line the ledger holds it as: the document, the side, and the construct by what it is rather than where.</summary>
    public string Entry => $"{this.Input}\t{this.Side}\t{this.Construct.Named(this.Source)}";

    /// <summary>The same, with where it is, for whoever has to look.</summary>
    public string Shown => $"{this.Entry}    at {this.Construct.Where(this.Source)}";
}
