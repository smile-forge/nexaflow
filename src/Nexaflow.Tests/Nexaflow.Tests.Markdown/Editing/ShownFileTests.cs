using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Nexaflow.Markdown.Editing;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Editing;

/// <summary>
/// The whole file a page shows, answered in its own lines the same whether the document is the file or a window onto it: what a line holds,
/// where it starts, every line in order — and going to a line far from the window brings the window there.
/// </summary>
[TestClass]
[CoversNode("windowed-reading")]
public class ShownFileTests
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void Begin() => _folder = TestTemp.CreateSubdirectory("shown-file").FullName;

    [TestMethod]
    public void ThroughAWindowOrNot_TheFilesLinesAreTheSame()
    {
        var text = string.Concat(Enumerable.Range(0, 3000).Select(line => $"line {line:D5} {new string('x', line % 30)}\n"));
        var path = Path.Combine(_folder, Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, text, new UTF8Encoding(false));

        using var plainEditor = new ContentEditor(new EditedDocument(text));
        using var plain = new ShownFile(plainEditor);

        var file = ContentFile.Open(path, new UTF8Encoding(false), 0, ContentFileAccess.ReadOnly, blockBytes: 4000);
        file.Scan();
        var paged = new EditedDocument();
        ContentFileWindow.Open(file, paged, at: 0, size: 10_000);
        using var windowedEditor = new ContentEditor(paged);
        using var windowed = new ShownFile(windowedEditor);

        Assert.AreEqual(plain.LineCount, windowed.LineCount);
        Assert.AreEqual(plain.Length, windowed.Length);

        foreach (var line in new long[] { 0, 1, 499, 1500, 2998, 2999, 3000 })
        {
            Assert.AreEqual(plain.OffsetOfLine(line), windowed.OffsetOfLine(line), $"line {line} starts");
            Assert.AreEqual(plain.LineText(line), windowed.LineText(line), $"line {line} holds");
        }

        var (seen, read) = (new List<string>(), new List<string>());
        plain.Lines()((_, _, each) => seen.Add(each), default);
        windowed.Lines()((_, _, each) => read.Add(each), default);
        CollectionAssert.AreEqual(seen, read);

        windowed.GoToLine(2500, ShownPlace.Centre);
        Assert.AreEqual(2500, windowed.LineOf(windowedEditor.Caret), "the caret on the line gone to");
        Assert.IsTrue(windowed.LinesBefore > 0, "the window brought there");
        Assert.AreEqual("line 02500 xxxxxxxxxx", windowed.LineText(2500));
    }
}
