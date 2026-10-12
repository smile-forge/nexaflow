using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Nexaflow.IO.Common.Speech;

namespace Nexaflow.Core.Services.Voice;

/// <summary>What a reply becomes when it is spoken: the next words to say, or a block to show instead.</summary>
public abstract record SpokenPart;

/// <summary>Words to say, in the language they are written in (null when it cannot be told).</summary>
public sealed record SpokenSegment(string Text, string? Language) : SpokenPart;

/// <summary>A diagram, table or code that speech cannot carry: shown over the page while a short reference to it is said.</summary>
public sealed record ShownBlock(string Markdown, SpokenSegment Reference) : SpokenPart;

/// <summary>
/// Cuts a reply into what is spoken, as it is written. The model's draft arrives whole each time, a little longer; the
/// segmenter keeps its place in it and gives out each piece as soon as it is complete — the first ending at a clause,
/// so speech starts after a few words rather than a whole sentence (1.9 s to the first word became 1.3 s), the rest at
/// sentences. A fenced block or a table is not read out: it is given as a <see cref="ShownBlock"/>, with a sentence that
/// says what it is.
/// </summary>
public sealed partial class ReplySegmenter(string? sessionLanguage)
{
    /// <summary>The first piece may end at a clause only once it has this much to say.</summary>
    private const int FirstClauseMinimum = 24;

    private string _draft = "";
    private int _at;
    private bool _first = true;
    private string? _lastLanguage = sessionLanguage;

    /// <summary>Everything said or shown so far, as written.</summary>
    public string Consumed => _draft[.._at];

    /// <summary>The draft as it now stands. A draft that does not continue the last one starts afresh: a new step.</summary>
    public IReadOnlyList<SpokenPart> Update(string draft)
    {
        if (!draft.StartsWith(_draft[.._at], StringComparison.Ordinal)) Restart();
        _draft = draft;
        return Take(final: false);
    }

    /// <summary>The draft is finished: whatever is left of it is complete.</summary>
    public IReadOnlyList<SpokenPart> Finish(string final)
    {
        if (!final.StartsWith(_draft[.._at], StringComparison.Ordinal)) Restart();
        _draft = final;
        return Take(final: true);
    }

    /// <summary>A new step: its draft is a new text, and the place in the old one no longer means anything.</summary>
    public void Restart()
    {
        _draft = "";
        _at = 0;
    }

    private List<SpokenPart> Take(bool final)
    {
        var parts = new List<SpokenPart>();
        while (_at < _draft.Length)
        {
            var rest = _draft.AsSpan(_at);
            int lineStart = _at == 0 || _draft[_at - 1] == '\n' ? 0 : -1;

            if (lineStart == 0 && rest.TrimStart(' ').StartsWith("```"))
            {
                int close = FenceEnd(_at);
                if (close < 0 && !final) break;
                close = close < 0 ? _draft.Length : close;
                parts.Add(Block(_draft[_at..close]));
                _at = close;
                continue;
            }
            if (lineStart == 0 && rest.TrimStart(' ').StartsWith("|"))
            {
                int end = TableEnd(_at, final);
                if (end < 0) break;
                parts.Add(Block(_draft[_at..end]));
                _at = end;
                continue;
            }

            int stop = SegmentEnd(_at, final);
            if (stop < 0) break;
            string spoken = SpeechText.Of(_draft[_at..stop]);
            _at = stop;
            if (spoken.Length == 0) continue;
            parts.Add(Segment(spoken));
            _first = false;
        }
        return parts;
    }

    /// <summary>Where the prose starting at <paramref name="from"/> may be cut: after a sentence, or for the first
    /// piece a clause; at a paragraph break or a block; at the end of a finished draft. -1 to wait for more.</summary>
    private int SegmentEnd(int from, bool final)
    {
        for (int i = from; i < _draft.Length; i++)
        {
            char c = _draft[i];
            if (c == '\n' && i + 1 < _draft.Length && (_draft[i + 1] == '\n' || StartsBlock(i + 1) || StartsListItem(i + 1))) return i + 1;
            if (i + 1 >= _draft.Length) break;
            bool spaceAfter = char.IsWhiteSpace(_draft[i + 1]);
            if (c is '.' or '!' or '?' or '…' && spaceAfter && !Abbreviation(from, i)) return i + 1;
            if (_first && c is ',' or ';' or ':' && spaceAfter && i - from >= FirstClauseMinimum) return i + 1;
        }
        return final ? _draft.Length : -1;
    }

    private bool StartsListItem(int at) => ListItem().IsMatch(_draft.AsSpan(at));

    private bool StartsBlock(int at)
    {
        var line = _draft.AsSpan(at).TrimStart(' ');
        return line.StartsWith("```") || line.StartsWith("|");
    }

    private static readonly HashSet<string> Titles = new(StringComparer.OrdinalIgnoreCase) { "mr", "mrs", "ms", "dr", "prof", "st", "mme", "nr", "vs" };

    /// <summary>A full stop that ends "e.g.", "z.B." or a title, not a sentence.</summary>
    private bool Abbreviation(int from, int dot)
    {
        int word = dot;
        while (word > from && !char.IsWhiteSpace(_draft[word - 1])) word--;
        string token = _draft[word..dot];
        return token.Contains('.') || Titles.Contains(token);
    }
    /// <summary>The end of the fenced block opening at <paramref name="from"/>, after its closing fence's line; -1 while open.</summary>
    private int FenceEnd(int from)
    {
        int firstLineEnd = _draft.IndexOf('\n', from);
        if (firstLineEnd < 0) return -1;
        int close = _draft.IndexOf("```", firstLineEnd, StringComparison.Ordinal);
        if (close < 0) return -1;
        int after = _draft.IndexOf('\n', close);
        return after < 0 ? _draft.Length : after + 1;
    }

    /// <summary>The end of the table starting at <paramref name="from"/>: the first line after it that is not a row.</summary>
    private int TableEnd(int from, bool final)
    {
        int at = from;
        while (at < _draft.Length)
        {
            int lineEnd = _draft.IndexOf('\n', at);
            if (lineEnd < 0) return final ? _draft.Length : -1;
            if (!_draft.AsSpan(at, lineEnd - at).TrimStart(' ').StartsWith("|")) return at;
            at = lineEnd + 1;
        }
        return final ? _draft.Length : -1;
    }

    private SpokenSegment Segment(string text)
    {
        string? language = SpokenLanguage.Detect(text) ?? _lastLanguage;
        _lastLanguage = language;
        return new SpokenSegment(text, language);
    }

    private ShownBlock Block(string markdown)
    {
        string kind = markdown.TrimStart().StartsWith('|') ? "table"
                    : FenceLanguage().Match(markdown) is { Success: true } m && m.Groups[1].Value.Equals("mermaid", StringComparison.OrdinalIgnoreCase) ? "diagram"
                    : "code";
        var reference = new SpokenSegment(Reference(kind, _lastLanguage), _lastLanguage);
        _first = false;
        return new ShownBlock(markdown.Trim('\n'), reference);
    }

    /// <summary>What is said in place of a block: that it is on the screen.</summary>
    internal static string Reference(string kind, string? language) => (language, kind) switch
    {
        ("fr", "diagram") => "Voici le diagramme.",
        ("fr", "table")   => "Voici le tableau.",
        ("fr", _)         => "Voici le code.",
        ("de", "diagram") => "Hier ist das Diagramm.",
        ("de", "table")   => "Hier ist die Tabelle.",
        ("de", _)         => "Hier ist der Code.",
        (_, "diagram")    => "Here's the diagram.",
        (_, "table")      => "Here's the table.",
        _                 => "Here's the code.",
    };

    [GeneratedRegex(@"^\s*```\s*([\w+-]*)")]
    private static partial Regex FenceLanguage();

    [GeneratedRegex(@"^\s*([-*+]|\d+[.)])\s")]
    private static partial Regex ListItem();
}

/// <summary>Markdown as it is read aloud: the words, without the marks that format them.</summary>
public static partial class SpeechText
{
    public static string Of(string markdown)
    {
        string text = markdown;
        text = Image().Replace(text, "");
        text = Link().Replace(text, "$1");
        text = Url().Replace(text, "");
        text = InlineCode().Replace(text, "$1");
        text = Emphasis().Replace(text, "$2");
        text = LineMarker().Replace(text, "");
        text = Math().Replace(text, "");
        var spoken = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.OtherSymbol or UnicodeCategory.Surrogate || rune.Value is 0xFE0F or 0x200D) continue;   // emoji
            spoken.Append(rune.ToString());
        }
        return SpaceBeforePunctuation().Replace(Whitespace().Replace(spoken.ToString(), " "), "$1").Trim();
    }

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")]                  private static partial Regex Image();
    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]                  private static partial Regex Link();
    [GeneratedRegex(@"https?://\S+")]                           private static partial Regex Url();
    [GeneratedRegex(@"`([^`]*)`")]                              private static partial Regex InlineCode();
    [GeneratedRegex(@"(\*\*|__|\*|_|~~)(\S(?:.*?\S)?)\1")]      private static partial Regex Emphasis();
    [GeneratedRegex(@"(?m)^\s*(#{1,6}\s+|>\s?|[-*+]\s+|\d+[.)]\s+)")] private static partial Regex LineMarker();
    [GeneratedRegex(@"\$\$.*?\$\$|\$[^$\n]+\$", RegexOptions.Singleline)] private static partial Regex Math();
    [GeneratedRegex(@"\s+")]                                    private static partial Regex Whitespace();

    [GeneratedRegex(@"\s+([.,!?;:])")]                          private static partial Regex SpaceBeforePunctuation();
}
