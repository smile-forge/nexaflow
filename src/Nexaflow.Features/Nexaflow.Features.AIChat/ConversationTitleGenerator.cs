using System.Text;
using System.Text.RegularExpressions;

namespace Nexaflow.Features.AIChat;

/// <summary>
/// Generates a short conversation title from the user's first message:
/// two random words of length &gt; 4, joined with hyphens, plus a 5-character
/// alphanumeric suffix — e.g. "drinks-fermat-a4f9k". Falls back to "chat"
/// when the input has too few qualifying words.
/// </summary>
public static class ConversationTitleGenerator
{
    private static readonly Random _rng = new();
    private const string Alphanumeric = "abcdefghijklmnopqrstuvwxyz0123456789";

    public static string Generate(string input)
    {
        var words = ExtractWords(input)
            .Where(w => w.Length > 4)
            .ToList();

        string a, b;
        if (words.Count >= 2)
        {
            // Shuffle pick two distinct words
            var i = _rng.Next(words.Count);
            int j;
            do { j = _rng.Next(words.Count); } while (j == i && words.Count > 1);
            a = words[i].ToLowerInvariant();
            b = words[j].ToLowerInvariant();
        }
        else if (words.Count == 1)
        {
            a = words[0].ToLowerInvariant();
            b = "chat";
        }
        else
        {
            a = "chat";
            b = "session";
        }

        return $"{a}-{b}-{RandomSuffix(5)}";
    }

    private static IEnumerable<string> ExtractWords(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) yield break;
        foreach (Match m in Regex.Matches(input, "[A-Za-z]+"))
            yield return m.Value;
    }

    private static string RandomSuffix(int length)
    {
        var sb = new StringBuilder(length);
        for (int i = 0; i < length; i++)
            sb.Append(Alphanumeric[_rng.Next(Alphanumeric.Length)]);
        return sb.ToString();
    }
}
