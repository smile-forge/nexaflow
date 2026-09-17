using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.WordCloud;

/// <summary>
/// One word of a cloud: what it says, what it counts for, and where both were written.
///
/// <para>
/// <paramref name="Trouble"/> is for a line that is a <c>word: weight</c> pair and still says nothing a cloud
/// can draw — a weight that is not a number, or is not above nought. Such a word is kept rather than dropped,
/// because it is the part a reader edits and it is wrong every time they are halfway through changing it:
/// the cloud draws without it and the line is waved under where it stands.
/// </para>
/// </summary>
/// <param name="Word">The word as it is drawn — inside its quotes, where it was written in any.</param>
/// <param name="Number">Where the weight was written, or null where none was.</param>
public sealed record WordCloudEntry(
    string Text,
    double Weight,
    ContentPart Word,
    ContentPart? Number,
    string? Trouble = null)
{
    /// <summary>Whether this is a word the cloud can be made of.</summary>
    public bool Counts => Trouble is null && Text.Length > 0;
}

/// <summary>
/// A cloud as its block says it: its settings, and every word in it heaviest first.
///
/// <para>
/// The order is the cloud's rather than the block's. Packing is greedy — each word takes the best place left,
/// and there is no going back — so the heaviest has to choose first or the word the reader came for ends up
/// wherever the small ones left room. Ties keep the order they were written in, so a block whose weights are
/// all the same draws as it reads.
/// </para>
/// </summary>
public sealed record WordCloudChart(WordCloudSettings Settings, IReadOnlyList<WordCloudEntry> Words)
{
    /// <summary>The words that can be drawn, heaviest first.</summary>
    public IReadOnlyList<WordCloudEntry> Drawable { get; } =
        Words.Where(word => word.Counts).OrderByDescending(word => word.Weight).ToList();

    /// <summary>
    /// The size a weight is set at: the lightest word at <see cref="WordCloudSettings.MinSize"/>, the heaviest
    /// at <see cref="WordCloudSettings.MaxSize"/>, and the rest between them by
    /// <see cref="WordCloudSettings.Scale"/>.
    ///
    /// <para>
    /// A mapping rather than <c>wordcloud2.js</c>'s <c>weightFactor</c>, which is a multiplier — and a
    /// multiplier is no use to a block of text, where the weights are whatever somebody counted: the same
    /// factor that suits counts in the tens makes a wall of ink of counts in the thousands. Stating the two
    /// ends instead means a cloud of percentages and a cloud of word counts are both drawn at a size somebody
    /// can read, and neither had to be scaled by hand.
    /// </para>
    /// <para>
    /// Where every word weighs the same there is no range to spread them over, and they are all set at the
    /// largest size — which is what a cloud of a dozen equal words should look like.
    /// </para>
    /// </summary>
    public double SizeOf(double weight)
    {
        if (Drawable.Count == 0) return Settings.MaxSize;

        var heaviest = Drawable[0].Weight;
        var lightest = Drawable[^1].Weight;

        if (heaviest - lightest < double.Epsilon) return Settings.MaxSize;

        var share = (Spread(weight) - Spread(lightest)) / (Spread(heaviest) - Spread(lightest));
        return Settings.MinSize + share * (Settings.MaxSize - Settings.MinSize);
    }

    /// <summary>A weight on the scale the settings ask for, which is the axis the sizes are spread along.</summary>
    private double Spread(double weight) => Settings.Scale switch
    {
        WordCloudScale.Root => System.Math.Sqrt(System.Math.Max(weight, 0)),

        // Shifted by one so that a weight of nought is the bottom of the scale rather than an infinity, which
        // is what a count of nothing has to be for a cloud counting things.
        WordCloudScale.Logarithmic => System.Math.Log(System.Math.Max(weight, 0) + 1),

        _ => weight,
    };
}
