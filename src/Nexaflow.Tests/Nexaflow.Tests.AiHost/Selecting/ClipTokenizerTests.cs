using System.Text.Json.Nodes;
using Nexaflow.AiHost.Selecting;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost.Selecting;

/// <summary>
/// SAM 3.1's tokenizer as the host runs it, held to the makers' <c>SimpleTokenizer</c>: its cleaning against what the
/// makers' <c>lower</c> cleaning made of the same phrases, and its tokens against the reference set's
/// (tools/ai-models/sam3.1/sam_reference.py). <c>NEXAFLOW_TEST_SAM</c> names the published graphs' folder, whose
/// tokenizer/ holds the vocabulary, and <c>NEXAFLOW_TEST_SAM_REFERENCE</c> the reference set; without them the token tests
/// are inconclusive.
/// </summary>
[TestClass]
[CoversNode("picture-selection")]
public class ClipTokenizerTests
{
    [TestMethod]
    [DataRow("Door Handle", "door handle")]
    [DataRow("the man\u2019s hat!", "the man's hat!")]
    [DataRow("  lots   of   spaces ", "lots of spaces")]
    [DataRow("HTML &amp; things", "html & things")]
    [DataRow("&amp;amp; twice", "& twice")]
    [DataRow("\uff26\uff35\uff2c\uff2cwidth", "fullwidth")]
    [DataRow("\u201cquoted\u201d \u2014 dash", "\"quoted\" \u2014 dash")]
    [DataRow("\ufb01ne ligature", "fine ligature")]
    [DataRow("tab\tand\nnewline", "tab and newline")]
    [DataRow("\u001b[31mred\u001b[0m car", "red car")]
    [DataRow("\u0093smart\u0094 quotes", "\"smart\" quotes")]
    [DataRow("\uff21\uff22\uff23\u3000\uff44\uff45\uff46", "abc def")]
    [DataRow("\u0133ssel", "ijssel")]
    [DataRow("line\r\nbreak\u2028here", "line break here")]
    [DataRow("zero\u200bwidth", "zero\u200bwidth")]
    [DataRow("\ufeffbom", "bom")]
    [DataRow("<b>bold &amp; html</b>", "<b>bold & html</b>")]
    [DataRow("caf\u00e9 cr\u00e8me", "caf\u00e9 cr\u00e8me")]
    public void APhrase_IsCleanedAsTheMakersCleanIt(string phrase, string cleaned)
        => Assert.AreEqual(cleaned, ClipText.Clean(phrase));

    [TestMethod]
    public void EveryReferencePhrase_IsTokenizedAsTheMakersTokenizeIt()
    {
        string? graphs = Environment.GetEnvironmentVariable("NEXAFLOW_TEST_SAM");
        string? reference = Environment.GetEnvironmentVariable("NEXAFLOW_TEST_SAM_REFERENCE");
        if (string.IsNullOrEmpty(graphs) || string.IsNullOrEmpty(reference))
            Assert.Inconclusive("NEXAFLOW_TEST_SAM and NEXAFLOW_TEST_SAM_REFERENCE are not both set.");
        var tokenizer = ClipTokenizer.Load(Path.Combine(graphs!, "tokenizer", ClipTokenizer.VocabularyFile));
        var set = JsonNode.Parse(File.ReadAllText(Path.Combine(reference!, "reference.json")))!;

        var phrases = set["tokens"]!.AsObject().Select(p => (p.Key, p.Value!))
                         .Concat(set["pictures"]!.AsObject().SelectMany(p => p.Value!["finds"]!.AsArray())
                                     .Select(f => (f!["phrase"]!.GetValue<string>(), f["tokens"]!)))
                         .ToList();
        Assert.IsTrue(phrases.Count > 20);
        foreach (var (phrase, expected) in phrases)
        {
            long[] makers = [.. expected.AsArray().Select(t => t!.GetValue<long>())];
            CollectionAssert.AreEqual(makers, tokenizer.Tokens(phrase), $"'{phrase}'");
        }
    }
}
