using System.Text;

namespace Nexaflow.Markdown.Scores.Rendering;

/// <summary>
/// How a chord symbol is spelled on the page: <c>Bb</c> is not a B followed by the letter b — it is B flat, and an engraver draws
/// the flat. An accidental is one that follows a note letter, or another accidental, so <c>Bb</c> is B flat and <c>Bbm</c> B flat
/// minor, while the b of a word is left alone. The signs are set at a normal weight where the letters are set heavier: ♭ and ♯ are
/// drawn from thin strokes, and a semi-bold face fills them in.
/// </summary>
internal static class ChordSpelling
{
    /// <summary>The symbol as it is drawn, and where in that each sign stands.</summary>
    public static (string Written, IReadOnlyList<int> Signs) Spelled(string text)
    {
        var written = new StringBuilder(text.Length);
        var signs = new List<int>();

        for (var at = 0; at < text.Length; at++)
        {
            var after = written.Length > 0 ? written[^1] : '\0';
            var isNote = after is >= 'A' and <= 'G' or '♭' or '♯';

            if (isNote && text[at] is 'b' or '#')
            {
                signs.Add(written.Length);
                written.Append(text[at] == 'b' ? '♭' : '♯');
                continue;
            }

            written.Append(text[at]);
        }

        return (written.ToString(), signs);
    }
}
