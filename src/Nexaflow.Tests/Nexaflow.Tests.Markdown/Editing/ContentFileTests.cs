using System;
using System.IO;
using System.Linq;
using System.Text;

using Nexaflow.Markdown.Editing;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Editing;

/// <summary>
/// A document over a file, read a block at a time: answering every question as the same characters held as one string answer it, wherever a
/// block cuts a line break or a character, and reading only what is asked for.
/// </summary>
[TestClass]
[CoversNode("content-file")]
public class ContentFileTests
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void Begin() => Directory.CreateDirectory(_folder = Path.Combine(Path.GetTempPath(), "nexaflow-content-file-" + Guid.NewGuid().ToString("N")));

    [TestCleanup]
    public void End()
    {
        try { Directory.Delete(_folder, recursive: true); }
        catch (IOException) { }
    }

    [TestMethod]
    public void AFileDocument_AnswersAsTheSameCharactersInOneStringDo()
    {
        var text = Lines(4000);
        using var file = Opened(text, new UTF8Encoding(false), blockBytes: 999);

        var document = ContentDocument.Of(file);
        var flat = ContentDocument.Of(text);

        Assert.IsTrue(document.Roped);
        Assert.AreEqual(text.Length, document.Length);
        Assert.AreEqual(flat.LineCount, document.LineCount);
        Assert.AreEqual(flat.LineEnding, document.LineEnding);

        foreach (var line in Enumerable.Range(0, flat.LineCount).Where(line => line % 37 == 0 || line == flat.LineCount - 1))
            Assert.AreEqual(flat.OffsetOf(line), document.OffsetOf(line), $"line {line}");

        foreach (var offset in Enumerable.Range(0, text.Length).Where(offset => offset % 997 == 0))
        {
            Assert.AreEqual(flat.LineOf(offset), document.LineOf(offset), $"offset {offset}");
            Assert.AreEqual(flat.LineAt(offset), document.LineAt(offset), $"offset {offset}");
        }

        Assert.AreEqual(text, document.ToString());
        Assert.IsTrue(document.Equals(flat));
    }

    [TestMethod]
    public void ABlockCutBetweenTheTwoCharactersOfALineBreak_StillReadsOneBreak()
    {
        // Every line ends \r\n, and blocks of an odd number of bytes come to cut between the two over and over.
        var text = string.Concat(Enumerable.Range(0, 30000).Select(line => $"row {line}\r\n"));
        using var file = Opened(text, new UTF8Encoding(false), blockBytes: 333);

        var document = ContentDocument.Of(file);

        Assert.AreEqual(30001, document.LineCount);
        Assert.AreEqual("\r\n", document.LineEnding);
        Assert.AreEqual(ContentDocument.Of(text).OffsetOf(12345), document.OffsetOf(12345));
    }

    [TestMethod]
    [DataRow("utf-8")]
    [DataRow("utf-16")]
    [DataRow("utf-16BE")]
    [DataRow("utf-32")]
    public void Blocks_AreCutWhereACharacterStarts_InEveryEncodingThatCanBeCut(string name)
    {
        var encoding = Encoding.GetEncoding(name);
        var text = string.Concat(Enumerable.Range(0, 20000).Select(line => $"é中😀 {line}\n"));
        using var file = Opened(text, encoding, blockBytes: 1001);

        Assert.IsTrue(file.BlockCount > 10);
        Assert.AreEqual(text, ContentDocument.Of(file).ToString());
    }

    [TestMethod]
    public void Writing_InAFileDocument_ComesToWhatWritingInTheTextComesTo()
    {
        var text = Lines(3000);
        using var file = Opened(text, new UTF8Encoding(false), blockBytes: 777);

        var document = ContentDocument.Of(file);
        var flat = ContentDocument.Of(text);

        foreach (var change in new[]
                 {
                     new ContentChange([new ContentWrite(10, 0, "inserted\n")], 0),
                     new ContentChange([new ContentWrite(5000, 40000, string.Empty)], 0),
                     new ContentChange([new ContentWrite(100, 3, "x"), new ContentWrite(90000, 2, "line\r\nbreak")], 0),
                 })
        {
            (document, flat) = (document.Apply(change), flat.Apply(change));

            Assert.AreEqual(flat.Length, document.Length);
            Assert.AreEqual(flat.LineCount, document.LineCount);
        }

        Assert.AreEqual(flat.ToString(), document.ToString());
    }

    [TestMethod]
    public void MakingADocument_ReadsNothing_AndReadingAllOfIt_KeepsOnlySoManyBlocks()
    {
        using var file = Opened(Lines(4000), new UTF8Encoding(false), blockBytes: 500);

        var document = ContentDocument.Of(file);
        Assert.AreEqual(0, file.CachedCount, "a document over the file is made from the scan alone");

        _ = document.ToString();
        Assert.IsTrue(file.BlockCount > ContentFile.CachedBlocks);
        Assert.AreEqual(ContentFile.CachedBlocks, file.CachedCount);
    }

    [TestMethod]
    public void WhatAnotherProgramWritesToTheEnd_IsTakenUpByTheNextScan()
    {
        var path = Written("first\n", new UTF8Encoding(false));
        using var file = ContentFile.Open(path, new UTF8Encoding(false), 0, ContentFileAccess.ReadOnly);
        file.Scan();

        File.AppendAllText(path, "second\n");

        Assert.AreEqual(1, file.Scan());
        Assert.AreEqual("first\nsecond\n", ContentDocument.Of(file).ToString());
    }

    [TestMethod]
    public void AReadOnlyFile_IsNeverHeld()
    {
        using var file = ContentFile.Open(Written("log\n", Encoding.UTF8), Encoding.UTF8, 3, ContentFileAccess.ReadOnly);
        file.Scan();

        Assert.AreEqual(ContentFileHold.ReadOnly, file.HoldForEdit());
    }

    [TestMethod]
    public void AnEditableFile_HeldForAnEdit_KeepsOtherWritersOut_ButNotReaders()
    {
        var path = Written("text\n", new UTF8Encoding(false));
        using var file = ContentFile.Open(path, new UTF8Encoding(false), 0, ContentFileAccess.Editable);
        file.Scan();

        // Shared with writers until it is held.
        using (new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { }

        Assert.AreEqual(ContentFileHold.Held, file.HoldForEdit());
        Assert.ThrowsExactly<IOException>(() => new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete).Dispose());

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) { }
        Assert.AreEqual("text\n", ContentDocument.Of(file).ToString());
    }

    [TestMethod]
    public void AFileChangedSinceItWasRead_IsNotHeld_AndOneAnotherProgramWrites_IsNot_Either()
    {
        var path = Written("text\n", new UTF8Encoding(false));
        using var file = ContentFile.Open(path, new UTF8Encoding(false), 0, ContentFileAccess.Editable);
        file.Scan();

        File.AppendAllText(path, "more\n");
        Assert.AreEqual(ContentFileHold.Changed, file.HoldForEdit());

        var other = Written("text\n", new UTF8Encoding(false));
        using var again = ContentFile.Open(other, new UTF8Encoding(false), 0, ContentFileAccess.Editable);
        again.Scan();

        using (new FileStream(other, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            Assert.AreEqual(ContentFileHold.WrittenElsewhere, again.HoldForEdit());
    }

    private ContentFile Opened(string text, Encoding encoding, int blockBytes)
    {
        var file = ContentFile.Open(Written(text, encoding), encoding, encoding.GetPreamble().Length, ContentFileAccess.ReadOnly, blockBytes);
        file.Scan();

        return file;
    }

    private string Written(string text, Encoding encoding)
    {
        var path = Path.Combine(_folder, Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, text, encoding);

        return path;
    }

    /// <summary>Lines of every length up to a long one, ending every way a line can.</summary>
    private static string Lines(int count) =>
        string.Concat(Enumerable.Range(0, count).Select(line => new string((char)('a' + line % 26), line % 97) + (line % 3) switch
        {
            0 => "\n",
            1 => "\r\n",
            _ => "\r",
        }));
}
