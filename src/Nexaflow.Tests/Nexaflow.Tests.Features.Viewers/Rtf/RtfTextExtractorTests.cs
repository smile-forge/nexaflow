using System.IO;
using System.Text;
using Nexaflow.Features.Rtf.Search;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Rtf;

/// <summary>
/// Rich-text documents in a folder search: found by the words they draw, the way the page reads them — never by their
/// control words — and "couldn't tell" rather than "no match" whenever a file can't be read as RTF.
/// </summary>
[TestClass]
[CoversNode("rtf-text-extractor")]
public class RtfTextExtractorTests
{
    private readonly RtfTextExtractor _extractor = new();
    private readonly List<string> _paths = [];

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var path in _paths)
            if (File.Exists(path)) File.Delete(path);
    }

    private string Written(string content)
    {
        var path = Path.Combine(TestTemp.Root, $"nexaflow-{Guid.NewGuid():N}.rtf");
        File.WriteAllText(path, content, Encoding.Latin1);
        _paths.Add(path);
        return path;
    }

    [TestMethod]
    public void ItClaimsRtfByItsExtension_AndNothingElse()
    {
        Assert.IsTrue(_extractor.CanExtract(@"C:\docs\letter.rtf"));
        Assert.IsTrue(_extractor.CanExtract(@"C:\docs\LETTER.RTF"));
        Assert.IsFalse(_extractor.CanExtract(@"C:\docs\letter.txt"));
        Assert.IsFalse(_extractor.CanExtract(string.Empty));
    }

    [TestMethod]
    public async Task TheWordsAreTheOnesTheDocumentDraws()
    {
        var path = Written(@"{\rtf1\ansi\ansicpg1252\pard The {\b caf\'e9} opens.\par Closed on Sundays.\par}");

        var text = await _extractor.ExtractAsync(path, long.MaxValue, CancellationToken.None);

        Assert.AreEqual("The caf\u00E9 opens.\nClosed on Sundays.", text);
    }

    [TestMethod]
    public async Task AFileThatIsNotRtf_IsCouldNotTell_NotNoWords()
    {
        var path = Written("plain words in a file named .rtf");

        Assert.IsNull(await _extractor.ExtractAsync(path, long.MaxValue, CancellationToken.None),
            "an empty string would be taken as proof the file has no words; null sends the verifier to read it itself");
    }

    [TestMethod]
    public async Task AFileThatCannotBeRead_IsCouldNotTell()
    {
        var missing = Path.Combine(TestTemp.Root, $"nexaflow-{Guid.NewGuid():N}.rtf");

        Assert.IsNull(await _extractor.ExtractAsync(missing, long.MaxValue, CancellationToken.None));
    }

    [TestMethod]
    public async Task NothingPastTheByteBudgetIsRead()
    {
        const string First = @"{\rtf1\ansi\pard early\par ";
        var path = Written(First + @"late\par}");

        Assert.AreEqual("early", await _extractor.ExtractAsync(path, First.Length, CancellationToken.None));
        Assert.IsNull(await _extractor.ExtractAsync(path, 0, CancellationToken.None), "no budget, nothing read");
    }
}
