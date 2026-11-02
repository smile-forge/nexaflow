namespace Nexaflow.Tests.Fixtures;

/// <summary>Documents holding readings inside readings, for the tests that hold every depth to the same document read whole.</summary>
public static class NestedSamples
{
    /// <summary>
    /// A document holding markdown in a fence, which holds markdown in a fence, which holds markdown in a fence — each long enough to
    /// be read a unit at a time a few dozen characters long — and, where <paramref name="quoted"/>, the first two written in quotes, so
    /// the third stands behind the marks of both.
    /// </summary>
    public static string ThreeDeep(bool quoted)
    {
        var third = "~~~markdown\n" + Paragraphs("third", 6) + "~~~\n";
        var second = "```markdown\n" + Paragraphs("second", 5) + third + "\n" + Paragraphs("second after", 2) + "```\n";
        var first = "````markdown\n" + Paragraphs("first", 5) + (quoted ? Quote(second) : second) + "\n" + Paragraphs("first after", 2) + "````\n";

        return "# Top\n\n" + Paragraphs("top", 5) + (quoted ? Quote(first) : first) + "\nAfter it all.\n\n" + Paragraphs("end", 3);
    }

    private static string Paragraphs(string word, int count) =>
        string.Concat(Enumerable.Range(1, count).Select(n => $"The {word} {n} reads on a while here.\n\n"));

    private static string Quote(string text) =>
        string.Concat(text.Split('\n').SkipLast(1).Select(line => line.Length == 0 ? ">\n" : $"> {line}\n"));
}
