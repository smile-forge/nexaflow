namespace Nexaflow.Markdown.Prose;

/// <summary>
/// <c>[name]: where "title"</c> at the head of a paragraph — as many as are written there — each a block of its own in
/// front of what is left of the paragraph, which the stages read for what it defines.
///
/// <para>
/// Read as CommonMark reads one: a name of up to 999 characters holding no unescaped bracket, a colon, an address in
/// angle brackets or one without space whose parentheses balance, and a title in quotes or parentheses, parted from the
/// address by space or one line ending. A title followed by anything but space is no title, and the definition ends with
/// the address's line — so long as nothing but space follows the address there. The name, the address and the title are
/// spelled as a link written inline spells them (<see cref="MarkdownLinkSyntax"/>).
/// </para>
/// </summary>
internal sealed class LinkDefinitions : IParagraphReading
{
    private readonly WrittenRule _written = new();

    public void Read(MarkdownScanner scan, ScannedBlock paragraph)
    {
        var text = scan.Text;
        var lines = paragraph.Lines;
        var taken = 0;

        while (taken < lines.Count && Written(text, lines, taken) is { } definition)
        {
            var (from, _) = lines[taken];
            while (text[from] is ' ' or '\t') from++;

            var (_, to) = lines[definition.Last];
            while (to > from && text[to - 1] is ' ' or '\t') to--;

            var block = new ScannedBlock(_written, ScannedKind.Reference, from) { Line = paragraph.Line + taken, IsOpen = false };
            block.Reach(to);

            var parent = paragraph.Parent!;
            parent.Insert(parent.IndexOf(paragraph), block);
            parent.Reach(block.End);

            taken = definition.Last + 1;
        }

        if (taken == 0) return;

        lines.RemoveRange(0, taken);

        if (lines.Count > 0)
        {
            var from = lines[0].From;
            while (from < lines[0].To && text[from] is ' ' or '\t') from++;
            paragraph.Start = from;
        }
    }

    /// <summary>The definition written from the start of line <paramref name="line"/> on, or null where none is.</summary>
    public static LinkDefinition? Written(string text, IReadOnlyList<(int From, int To)> lines, int line)
    {
        var walk = new MarkdownLinkSyntax(text, lines, line, lines[line].From);
        walk.SkipSpaces();

        var named = walk.At + 1;
        if (!walk.Label()) return null;

        var label = (named, walk.At - 1);
        if (walk.Current != ':') return null;
        walk.Next();

        walk.SkipWhitespace();
        if (!walk.Destination(inline: false, out var from, out var to)) return null;

        var (afterLine, afterAt) = (walk.Line, walk.At);

        if (walk.SkipWhitespace() && walk.Title(out var opens, out var closes))
        {
            walk.SkipSpaces();
            if (walk.AtLineEnd) return new LinkDefinition(walk.Line, label, (from, to), (opens, closes));
        }

        // No title, or one followed by more: the definition is the address's line, if nothing else stands on it.
        walk.Restore(afterLine, afterAt);
        walk.SkipSpaces();

        return walk.AtLineEnd ? new LinkDefinition(walk.Line, label, (from, to)) : null;
    }

    /// <summary>What a definition is held as: written where it stands, and closed.</summary>
    private sealed class WrittenRule : MarkdownBlockRule
    {
        public override Continued Continues(MarkdownScanner scan, ScannedBlock block) => Continued.Unmatched;
    }
}

/// <summary>A link's definition as written: the line it ends on, and where its label, its address and its title stand.</summary>
/// <param name="Title">Where what its title says stands, between the marks round it — where it has one.</param>
internal readonly record struct LinkDefinition(int Last, (int From, int To) Label, (int From, int To) Destination, (int From, int To)? Title = null);
