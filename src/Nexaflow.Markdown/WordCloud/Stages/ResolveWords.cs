using System.Globalization;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.WordCloud.Stages;

/// <summary>
/// Says what each word of a cloud counts for, the size that sets it at and when it is packed (<see cref="WordCloudWordNode"/>) —
/// on the word as written, which is what is drawn, pressed and typed into.
///
/// <para>
/// A weight that will not read is not a fault in the block. The block is well formed, that word is the part being edited, and it
/// is wrong every time somebody is halfway through changing it; so it keeps its line and is marked where it is wrong, and the
/// other words are still a cloud. A block with no words at all says what it needs.
/// </para>
/// </summary>
public sealed class ResolveWords : IAstStage
{
    /// <summary>What a block with no words says for itself.</summary>
    public const string Empty = "An empty word cloud. It takes a `word: weight` line for each word, and settings written the same way.";

    public string Name => "wordcloud:words";

    public ContentNode Run(ContentNode tree)
    {
        // The sizes are the settings', so a block whose settings will not read has nothing to size its words by.
        if (tree is not WordCloudBlockNode block) return tree;

        var entries = tree.SelfAndDescendants().Where(node => node.Kind == WordCloudKinds.Entry).ToList();
        if (entries.Count == 0)
            return tree.SelfAndDescendants().Any(node => node.Trouble is not null && !node.IsDerived) ? tree : tree.Saying(Empty);

        var said = new Dictionary<ContentNode, Func<ContentNode, ContentNode>>(ReferenceEqualityComparer.Instance);
        var counted = new List<(ContentNode Word, double Weight)>();

        foreach (var entry in entries)
        {
            var (word, weight, wrong, trouble) = Read(entry);

            if (trouble is null) counted.Add((word, weight));
            else said[wrong!] = node => node.Saying(trouble);
        }

        // Heaviest first, ties in the order written.
        var ranked = counted.Select((word, written) => (word.Word, word.Weight, Written: written))
                            .OrderByDescending(word => word.Weight).ThenBy(word => word.Written).ToList();

        for (var rank = 0; rank < ranked.Count; rank++)
        {
            var (word, weight, _) = ranked[rank];
            var size = Size(block.Settings, weight, ranked[0].Weight, ranked[^1].Weight);
            var at = rank;
            said[word] = node => new WordCloudWordNode(node, weight, size, at);
        }

        return AstRewrite.Each(tree, node => said.TryGetValue(node, out var saying) ? saying(node) : node);
    }

    /// <summary>
    /// One <c>word: weight</c> line: the word as written and what it counts for — or the part of it that is wrong, and why.
    /// </summary>
    private static (ContentNode Word, double Weight, ContentNode? Wrong, string? Trouble) Read(ContentNode entry)
    {
        var written = entry.Part(WordCloudRoles.Word)!;
        var word = written.IsLeaf ? written : written.Part(WordCloudRoles.Word) ?? written;
        var number = entry.Part(WordCloudRoles.Weight);

        if (word.Text.Trim().Length == 0)
            return (word, 0, number ?? word, "A word of a cloud cannot be blank.");

        if (number is null)
            return (word, 0, word, $"{word.Text} has no weight. Write how much it counts for after the colon.");

        if (!double.TryParse(number.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var weight))
            return (word, 0, number, WordCloudSetting.Is(word.Text)
                ? $"`{word.Text}` is a setting, but the settings are written above the words — below them it is read as a word, and `{number.Text}` is not a weight."
                : $"`{number.Text}` is not a weight, and `{word.Text}` is not a setting — so this line is neither.");

        if (weight <= 0 || double.IsNaN(weight) || double.IsInfinity(weight))
            return (word, 0, number, $"`{number.Text}` is not a weight. A word counts for more than nothing or it is not in the cloud.");

        return (word, weight, null, null);
    }

    /// <summary>
    /// The size a weight is set at: the lightest word at <see cref="WordCloudSettings.MinSize"/>, the heaviest at
    /// <see cref="WordCloudSettings.MaxSize"/>, and the rest between them by <see cref="WordCloudSettings.Scale"/>.
    ///
    /// <para>
    /// A mapping rather than <c>wordcloud2.js</c>'s <c>weightFactor</c>, which is a multiplier — and a multiplier is no use to a
    /// block of text, where the weights are whatever somebody counted: the same factor that suits counts in the tens makes a wall
    /// of ink of counts in the thousands. Stating the two ends instead means a cloud of percentages and a cloud of word counts are
    /// both drawn at a size somebody can read. Where every word weighs the same there is no range to spread them over, and they are
    /// all set at the largest size.
    /// </para>
    /// </summary>
    private static double Size(WordCloudSettings settings, double weight, double heaviest, double lightest)
    {
        if (heaviest - lightest < double.Epsilon) return settings.MaxSize;

        var share = (Spread(settings, weight) - Spread(settings, lightest)) / (Spread(settings, heaviest) - Spread(settings, lightest));
        return settings.MinSize + share * (settings.MaxSize - settings.MinSize);
    }

    /// <summary>A weight on the scale the settings ask for, which is the axis the sizes are spread along.</summary>
    private static double Spread(WordCloudSettings settings, double weight) => settings.Scale switch
    {
        WordCloudScale.Root => Math.Sqrt(Math.Max(weight, 0)),

        // Shifted by one so that a weight of nought is the bottom of the scale rather than an infinity, which is what a count of
        // nothing has to be for a cloud counting things.
        WordCloudScale.Logarithmic => Math.Log(Math.Max(weight, 0) + 1),

        _ => weight,
    };
}
