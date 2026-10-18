using System.Numerics;

namespace Nexaflow.AiHost.Llama;

/// <summary>
/// The scores a read leaves, chosen from directly rather than through llama.cpp's sampler chain: the likeliest token is
/// one scan of them, where the chain's greedy sampler first copies each of a vocabulary of a hundred thousand and more
/// into a list of candidates — a quarter of a millisecond a token, as long as a small model takes to read one.
/// </summary>
internal static class LlamaScores
{
    /// <summary>The index of the highest of <paramref name="scores"/>, the first where several share it.</summary>
    public static int Likeliest(ReadOnlySpan<float> scores)
    {
        if (scores.IsEmpty) throw new ArgumentException("There are no scores to choose from.", nameof(scores));
        int width = Vector<float>.Count, at = 0;
        float best = float.NegativeInfinity;
        if (scores.Length >= width)
        {
            var highest = new Vector<float>(float.NegativeInfinity);
            for (; at <= scores.Length - width; at += width) highest = Vector.Max(highest, new Vector<float>(scores[at..]));
            for (int lane = 0; lane < width; lane++) best = Math.Max(best, highest[lane]);
        }
        for (; at < scores.Length; at++) best = Math.Max(best, scores[at]);
        int first = scores.IndexOf(best);
        return first >= 0 ? first : 0;
    }
}
