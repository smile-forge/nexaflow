namespace Nexaflow.Markdown.Prose;

/// <summary>
/// The rules a scan reads with: every kind of block, in the order a line is asked whether it starts one, and what a
/// paragraph is read again for once it is whole.
///
/// <para>
/// The order is CommonMark's, with each extension where it has to stand: front matter before anything, a pipe table's
/// separator before the list item it would otherwise be, a grid table's first line before the bullet its <c>+</c> is.
/// An extension is one more rule in this list, and a reader that wants fewer kinds is read with a shorter one.
/// </para>
/// </summary>
internal sealed class MarkdownBlockRules
{
    private readonly bool[] _starters = new bool[128];

    public MarkdownBlockRules(IReadOnlyList<MarkdownBlockRule> starts, IReadOnlyList<IParagraphReading> paragraphs)
    {
        this.Starts = starts;
        this.Paragraph = new ParagraphRule(paragraphs);

        foreach (var rule in starts)
            for (var character = '\0'; character < 128; character++)
                _starters[character] |= rule.MayStart(character);
    }

    /// <summary>Everything a document is read for: CommonMark, and each extension the reading enables.</summary>
    public static MarkdownBlockRules Reading { get; } = Read();

    public MarkdownBlockRule Document { get; } = new DocumentRule();

    public MarkdownBlockRule Paragraph { get; }

    /// <summary>Every rule a line can start a block by, in the order it is asked.</summary>
    public IReadOnlyList<MarkdownBlockRule> Starts { get; }

    /// <summary>Whether any rule can start a block with <paramref name="first"/> — a line starting otherwise is text.</summary>
    public bool MayStart(char first) => first >= 128 || _starters[first];

    private static MarkdownBlockRules Read()
    {
        var definitions = new LinkDefinitions();

        return new MarkdownBlockRules(
        [
            new FrontMatterRule(),
            new AlertRule(),
            new FigureRule(),
            new QuoteRule("^^", ScannedKind.Footer),
            new HeadingRule(),
            new FenceRule(),
            new FenceRule(ScannedKind.Maths, "$", 2, 2),
            new HtmlBlockRule(),
            new PipeSeparatorRule(),
            new UnderlineRule(definitions),
            new RuleRule(),
            new AbbreviationRule(),
            new DefinitionRule(),
            new GridTableRule(),
            new ItemRule(lettered: true),
            new IndentedCodeRule(),
        ],
        [definitions, new PipeTables()]);
    }
}
