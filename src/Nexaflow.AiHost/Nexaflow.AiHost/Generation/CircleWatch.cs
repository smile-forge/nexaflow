namespace Nexaflow.AiHost.Generation;

/// <summary>
/// Watches a reply token by token, its reasoning included, for a model going in circles: the same stretch of tokens
/// over and over, back to back. A reply has no fixed limit — it may be as long as the context leaves room for — so this
/// is what stops one that has stopped going anywhere. A stretch of <c>p</c> tokens is going in circles once it has come
/// round three times and filled at least <see cref="ShortestCircle"/> tokens doing so: no prose, list or code says the
/// very same 256 tokens again and again. Each token is compared with the one each possible stretch back, a few hundred
/// comparisons beside a step of the model.
/// </summary>
internal sealed class CircleWatch(int longestStretch = CircleWatch.LongestStretch)
{
    /// <summary>The longest stretch watched for: a few paragraphs.</summary>
    internal const int LongestStretch = 1024;

    /// <summary>How many tokens the repeats must fill before they count as going in circles.</summary>
    internal const int ShortestCircle = 256;

    private readonly List<int> _tokens = [];

    /// <summary>For each stretch length, how many tokens running have each matched the one that length back.</summary>
    private readonly int[] _matching = new int[longestStretch + 1];

    /// <summary>The stretch it was found going round, in tokens; null until it is.</summary>
    public int? Stretch { get; private set; }

    /// <summary>Takes the next token; true once the reply is going in circles.</summary>
    public bool Take(int token)
    {
        _tokens.Add(token);
        int at = _tokens.Count - 1;
        int longest = Math.Min(longestStretch, at);
        for (int stretch = 1; stretch <= longest; stretch++)
        {
            _matching[stretch] = _tokens[at - stretch] == token ? _matching[stretch] + 1 : 0;
            // The stretch itself, then every token since matching it: round three times, and long enough to mean it.
            if (_matching[stretch] + stretch >= Math.Max(3 * stretch, ShortestCircle))
            {
                Stretch = stretch;
                return true;
            }
        }
        return false;
    }
}
