using System.Text;
using System.Text.RegularExpressions;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;

namespace Nexaflow.IO.Documents.Processing;

/// <summary>
/// A block's words with everything else held back, so a model working on the words is handed nothing it could break.
///
/// <para>
/// Markup round words — emphasis, a link and where it goes, a strikethrough — stands as a numbered pair of tags round the words
/// it marks, <c>&lt;1&gt;the moon&lt;/1&gt;</c>, which a model carries with those words wherever another language puts them.
/// Inline code and a formula short enough to read stand as <c>⟦n `.md`⟧</c> — what they say shown, not to be translated, so a
/// model can see what it is placing and put it where it goes in the other language's sentence. Anything else that is not words —
/// a picture, longer code, the marks a quote or a list puts at the head of a line — stands as <c>⟦n⟧</c>, unseen. What comes
/// back is checked to hold every one once, each pair opened before it is closed and inside the
/// pairs it was inside (<see cref="Problem"/>); the block is written again from it (<see cref="Rebuilt"/>), every piece of markup
/// as it was written, character for character, and the words spelled by the parser where they stand, so nothing in them is read
/// as markup; and read again (<see cref="Keeps"/>) to prove its markup still reads as the markup it was.
/// </para>
/// </summary>
public sealed partial class ProtectedText
{
    /// <summary>The longest code or formula shown in its mark; anything longer is held back unseen.</summary>
    private const int MostShown = 60;

    private readonly List<string> _held;
    private readonly List<(string Open, string Close)> _pairs;
    private readonly List<ContentPart> _slots;

    private ProtectedText(string lead, string masked, string trail, List<string> held, List<(string Open, string Close)> pairs, List<ContentPart> slots)
    {
        Lead   = lead;
        Masked = masked;
        Trail  = trail;
        _held  = held;
        _pairs = pairs;
        _slots = slots;
    }

    /// <summary>What comes before the block's first words — a heading's hashes, a list's first marker — kept as written.</summary>
    public string Lead { get; }

    /// <summary>The block's words, with a tag pair round each run of marked words, and <c>⟦n⟧</c> for anything else between them.</summary>
    public string Masked { get; }

    /// <summary>What comes after the block's last words — the line break closing it, say — kept as written.</summary>
    public string Trail { get; }

    /// <summary>Whether the block has any words at all; one that has none is copied as it was written.</summary>
    public bool HasWords => _slots.Count > 0;

    /// <summary>
    /// <paramref name="block"/>'s words with everything else held back. A break between lines and nothing else is a space —
    /// the lines a block was wrapped at are not the words'.
    /// </summary>
    public static ProtectedText Of(ContentPart block)
    {
        var tokens = new List<Token>();
        var pairs = new List<(string Open, string Close)>();
        Walk(block, tokens, pairs);

        if (string.Concat(tokens.Select(token => token.Text)) != block.Print())
            throw new InvalidOperationException($"A {block.Kind} block's parts do not print as the block does.");

        var first = tokens.FindIndex(token => token.Kind != Said.Mark);
        if (first < 0 || !tokens.Any(token => token.Kind == Said.Words)) return new(block.Print(), string.Empty, string.Empty, [], [], []);

        var last = tokens.FindLastIndex(token => token.Kind != Said.Mark);
        var held = new List<string>();
        var slots = new List<ContentPart>();
        var masked = new StringBuilder();

        for (var at = first; at <= last; at++)
        {
            var token = tokens[at];

            switch (token.Kind)
            {
                case Said.Words:
                    slots.Add(token.Slot!);
                    masked.Append(token.Text);
                    break;

                case Said.Opens:
                    masked.Append('<').Append(token.Pair).Append('>');
                    break;

                case Said.Closes:
                    masked.Append("</").Append(token.Pair).Append('>');
                    break;

                // Shown, so its place in the sentence can be judged; written back as it was whatever the answer makes of it.
                case Said.Shown:
                    masked.Append('⟦').Append(held.Count).Append(' ').Append(token.Text).Append('⟧');
                    held.Add(token.Text);
                    break;

                // A run of what is not words: one mark, however many pieces it was read as.
                default:
                    var run = new StringBuilder(token.Text);
                    while (at + 1 <= last && tokens[at + 1].Kind == Said.Mark) run.Append(tokens[++at].Text);

                    var said = run.ToString();
                    if (string.IsNullOrWhiteSpace(said) && said.Contains('\n')) masked.Append(' ');
                    else
                    {
                        masked.Append('⟦').Append(held.Count).Append('⟧');
                        held.Add(said);
                    }

                    break;
            }
        }

        return new(string.Concat(tokens.Take(first).Select(token => token.Text)), masked.ToString(),
                   string.Concat(tokens.Skip(last + 1).Select(token => token.Text)), held, pairs, slots);
    }

    /// <summary>
    /// The pieces of the block's words, each at most <paramref name="room"/> characters where it can be, cut at the ends of its
    /// sentences (<see cref="DocumentCuts.Cut"/>) but never inside a pair of tags — a pair is handed over whole, round the words it marks.
    /// </summary>
    public IReadOnlyList<string> Cut(int room)
    {
        var pieces = new List<string>();
        var piece = new StringBuilder();
        var depth = 0;

        foreach (var cut in DocumentCuts.Cut(Masked, room))
        {
            piece.Append(cut);
            foreach (Match tag in Mark().Matches(cut))
                if (tag.Groups["pair"].Success) depth += tag.Groups["closes"].Value.Length == 0 ? 1 : -1;

            if (depth != 0) continue;

            pieces.Add(piece.ToString());
            piece.Clear();
        }

        if (piece.Length > 0) pieces.Add(piece.ToString());
        return pieces;
    }

    /// <summary>
    /// What is wrong with <paramref name="answer"/> as words standing in for <paramref name="source"/>'s: a <c>⟦n⟧</c> or a tag of
    /// the source's missing from it or there twice, one the source never had, or a pair closed before it is opened or across
    /// another. Null where it holds each of the source's once, every pair opened and closed in its order.
    /// </summary>
    public static string? Problem(string source, string answer)
    {
        var wanted = Marks(source);
        var given = Marks(answer);

        var missing = wanted.Where(mark => !given.Contains(mark)).ToList();
        var twice = given.GroupBy(mark => mark).Where(group => group.Count() > 1).Select(group => group.Key).ToList();
        var strange = given.Where(mark => !wanted.Contains(mark)).Distinct().ToList();

        if (missing.Count > 0) return $"{Named(missing)} {(missing.Count == 1 ? "is" : "are")} missing";
        if (twice.Count > 0) return $"{Named(twice)} {(twice.Count == 1 ? "is" : "are")} there more than once";
        if (strange.Count > 0) return $"{Named(strange)} {(strange.Count == 1 ? "stands" : "stand")} for nothing in it";

        var open = new Stack<string>();
        foreach (Match tag in Mark().Matches(answer))
        {
            if (!tag.Groups["pair"].Success) continue;

            var pair = tag.Groups["pair"].Value;
            if (tag.Groups["closes"].Value.Length == 0)
            {
                open.Push(pair);
                continue;
            }

            if (open.Count == 0 || open.Peek() != pair) return $"</{pair}> closes what <{pair}> did not open: keep each pair round the words it marks, one inside another as they were";
            open.Pop();
        }

        return null;

        static string Named(IEnumerable<string> marks) => string.Join(", ", marks);
    }

    /// <summary>
    /// The block written again with <paramref name="answer"/> for its words: <see cref="Lead"/>, then the answer's words spelled by
    /// the parser where the words they stand in for stood, each tag and <c>⟦n⟧</c> the markup it held back, then <see cref="Trail"/>.
    /// Breaks between lines in the words are spaces, as they were handed over.
    /// </summary>
    public string Rebuilt(string answer)
    {
        var built = new StringBuilder(Lead);
        var at = 0;
        var slot = 0;

        foreach (Match mark in Mark().Matches(answer))
        {
            Words(answer[at..mark.Index]);

            built.Append(mark.Groups["hold"].Success ? _held[Number(mark.Groups["hold"])]
                       : mark.Groups["closes"].Value.Length > 0 ? _pairs[Number(mark.Groups["pair"])].Close
                       : _pairs[Number(mark.Groups["pair"])].Open);

            at = mark.Index + mark.Length;
        }

        Words(answer[at..]);
        return built.Append(Trail).ToString();

        void Words(string words)
        {
            words = Breaks().Replace(words, " ");
            if (words.Length == 0) return;

            built.Append(MarkdownParser.SpellWords(_slots[Math.Min(slot++, _slots.Count - 1)], words));
        }

        static int Number(Group group) => int.Parse(group.Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Whether <paramref name="rebuilt"/>, read as a block, is the block <paramref name="block"/> was with other words: the same
    /// kind of block, and every piece of markup still read as the markup it was rather than taken into the words — which is what
    /// fails when a link's brackets come apart. A backslash the parser spelled in front of a character is the words', and not
    /// counted. <paramref name="definitions"/> are the document's link definitions, which a link by name needs.
    /// </summary>
    public static bool Keeps(ContentPart block, string rebuilt, string definitions)
    {
        var read = DocumentReading.Of(rebuilt.TrimEnd('\r', '\n') + "\n\n" + definitions);
        var again = read.Blocks(0, read.Root.Children.Count).FirstOrDefault();

        return again is not null && again.Kind == block.Kind && Markup(again).SequenceEqual(Markup(block));
    }

    /// <summary>The markup in a block, in order: every piece that is not words, a backslash or space.</summary>
    private static List<string> Markup(ContentPart block) =>
        [.. block.SelfAndDescendants()
                 .Where(leaf => leaf.Children.Count == 0 && !leaf.Derived && !MarkdownParser.ReadsAsWords(leaf) && leaf.Kind != MarkdownKinds.Escape)
                 .Select(leaf => leaf.Print())
                 .Where(said => !string.IsNullOrWhiteSpace(said))
                 .Select(said => said.Trim())
                 .Order(StringComparer.Ordinal)];

    /// <summary>
    /// <paramref name="part"/> as what is said of it in order: its words, the markup round marked words as the opening and
    /// closing of a pair, and anything else as marks.
    /// </summary>
    private static void Walk(ContentPart part, List<Token> tokens, List<(string Open, string Close)> pairs)
    {
        if (part.Derived) return;

        // Code or a formula in a line of words, short enough to read: one mark, shown.
        if (part.Kind is MarkdownKinds.Code or Kinds.Block && part.Print() is { Length: > 0 and <= MostShown } shown
            && !shown.Contains('\n') && !shown.Contains('⟧'))
        {
            tokens.Add(new(Said.Shown, shown));
            return;
        }

        if (part.Children.Count == 0)
        {
            var text = part.Print();
            if (text.Length > 0) tokens.Add(MarkdownParser.ReadsAsWords(part) ? new(Said.Words, text, part) : new(Said.Mark, text));
            return;
        }

        if (Round(part) is { } body)
        {
            var pair = pairs.Count;
            pairs.Add((string.Empty, string.Empty));

            var before = string.Concat(part.Children.TakeWhile(child => child != body).Select(child => child.Print()));
            var after = string.Concat(part.Children.SkipWhile(child => child != body).Skip(1).Select(child => child.Print()));
            pairs[pair] = (before, after);

            tokens.Add(new(Said.Opens, before, Pair: pair));
            Walk(body, tokens, pairs);
            tokens.Add(new(Said.Closes, after, Pair: pair));
            return;
        }

        foreach (var child in part.Children) Walk(child, tokens, pairs);
    }

    /// <summary>
    /// The words <paramref name="part"/> marks, where it is markup round words — opened by a mark of its own, its words in its
    /// body, nothing but markup either side of them — or null.
    /// </summary>
    private static ContentPart? Round(ContentPart part)
    {
        var own = part.Children.Where(child => !child.Derived).ToList();
        if (own.Count < 2 || own[0].Role != Roles.Open || own.FirstOrDefault(child => child.Role == Roles.Body) is not { } body) return null;
        if (!HasWords(body)) return null;

        return own.Where(child => child != body).Any(HasWords) ? null : body;

        static bool HasWords(ContentPart part) => part.SelfAndDescendants().Any(leaf => leaf.Children.Count == 0 && MarkdownParser.ReadsAsWords(leaf));
    }

    /// <summary>
    /// Every <c>⟦n⟧</c> and tag in <paramref name="text"/>, by what it stands for: a shown mark by its number alone, since what
    /// it shows is written back as it was whatever an answer made of it.
    /// </summary>
    private static List<string> Marks(string text) =>
        [.. Mark().Matches(text).Select(match => match.Groups["hold"].Success
            ? $"⟦{match.Groups["hold"].Value}⟧"
            : $"<{match.Groups["closes"].Value}{match.Groups["pair"].Value}>")];

    /// <summary>What a block is said as, in order.</summary>
    private enum Said
    {
        Words,
        Mark,
        Opens,
        Closes,

        /// <summary>Code or a formula in a line of words, held back but shown in its mark.</summary>
        Shown,
    }

    private readonly record struct Token(Said Kind, string Text, ContentPart? Slot = null, int Pair = -1);

    [GeneratedRegex(@"⟦\s*(?<hold>\d+)(?:\s[^⟧]*)?⟧|<\s*(?<closes>/?)\s*(?<pair>\d+)\s*>")]
    private static partial Regex Mark();

    [GeneratedRegex(@"\s*\n\s*")]
    private static partial Regex Breaks();
}
