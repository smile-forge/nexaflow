using System.Linq;

using Nexaflow.Syntax;
using Nexaflow.Tests.Fixtures;

using TreeSitter;

namespace Nexaflow.Tests.Components.Syntax;

/// <summary>
/// Code parsed from text read a stretch at a time — as a rope hands it over — colours and folds exactly as the same text parsed as one
/// string, so a reading never has to be copied into one string to be parsed.
/// </summary>
[TestClass]
[CoversNode("md-code-highlighting")]
public class ReadingParseTests
{
    private const string Source = "// alpha\n// beta\nclass C\n{\n    void M()\n    {\n        var s = \"text\"; // note\n        return;\n    }\n}\n";

    [TestMethod]
    public void TextReadAStretchAtATime_IsColouredAsTheWholeString()
    {
        using var highlighter = CodeHighlighter.TryCreate("c-sharp")!;
        using var read = highlighter.Parse(new Stretched(Source), null)!;

        var expected = highlighter.Highlight(Source).Select(span => (span.Start, span.Length, span.Capture)).ToList();
        var actual = highlighter.Highlight(read, 0, Source.Length).Select(span => (span.Start, span.Length, span.Capture)).ToList();

        CollectionAssert.AreEquivalent(expected, actual);
    }

    [TestMethod]
    public void TheFoldsOfATreeAlreadyParsed_AreTheFoldsOfTheText()
    {
        using var highlighter = CodeHighlighter.TryCreate("c-sharp")!;
        using var read = highlighter.Parse(new Stretched(Source), null)!;

        CollectionAssert.AreEquivalent(highlighter.GetFolds(Source).ToList(), highlighter.FoldsIn(read).ToList());
    }

    /// <summary>Text handed out a few characters a read, so every seam between two reads is crossed.</summary>
    private sealed class Stretched(string text) : TextSource
    {
        public override int Length => text.Length;

        public override int Read(int start, char[] buffer, int count)
        {
            var copied = System.Math.Max(0, System.Math.Min(System.Math.Min(count, 5), text.Length - start));
            text.CopyTo(start, buffer, 0, copied);
            return copied;
        }
    }
}
