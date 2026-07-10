using System;
using System.IO;
using System.Linq;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

[TestClass]
[CoversNode("aichat-history-resume")]
public class ConversationStoreTests
{
    private string _dir = string.Empty;

    [TestInitialize]
    public void Setup() => _dir = Path.Combine(Path.GetTempPath(), "nexaflow-convo-" + Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Teardown()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static ConversationRecord SampleConversation(string id, string title) => new()
    {
        Id        = id,
        Title     = title,
        StartedAt = new DateTime(2024, 1, 2, 3, 4, 5),
        Messages  =
        [
            new ConversationMessage { Text = "hello",    IsUser = true,  Timestamp = new DateTime(2024, 1, 2, 3, 4, 10) },
            new ConversationMessage { Text = "hi there", IsUser = false, Timestamp = new DateTime(2024, 1, 2, 3, 5, 0)  },
        ],
    };

    // ── Save / Load round-trip ──

    [TestMethod]
    public void Save_ThenLoad_RoundTripsConversation()
    {
        var conv = SampleConversation("conv-1", "Greeting");
        ConversationStore.Save(_dir, conv);

        // Lands in its own id-named sub-folder.
        Assert.IsTrue(File.Exists(Path.Combine(_dir, "conv-1", "conversation.json")));

        var loaded = ConversationStore.Load(_dir);
        Assert.AreEqual(1, loaded.Count);
        Assert.AreEqual("conv-1",   loaded[0].Id);
        Assert.AreEqual("Greeting", loaded[0].Title);
        Assert.AreEqual(new DateTime(2024, 1, 2, 3, 4, 5), loaded[0].StartedAt);
        Assert.AreEqual(2, loaded[0].Messages.Count);
        Assert.AreEqual("hello", loaded[0].Messages[0].Text);
        Assert.IsTrue(loaded[0].Messages[0].IsUser);
        Assert.AreEqual("hi there", loaded[0].Messages[1].Text);
        Assert.IsFalse(loaded[0].Messages[1].IsUser);
        Assert.AreEqual(new DateTime(2024, 1, 2, 3, 5, 0), loaded[0].Messages[1].Timestamp);
    }

    [TestMethod]
    public void Load_MissingDirectory_ReturnsEmpty()
        => Assert.AreEqual(0, ConversationStore.Load(_dir).Count);

    [TestMethod]
    public void Load_SortsNewestFirst()
    {
        ConversationStore.Save(_dir, new ConversationRecord { Id = "old", StartedAt = new DateTime(2020, 1, 1) });
        ConversationStore.Save(_dir, new ConversationRecord { Id = "new", StartedAt = new DateTime(2024, 1, 1) });

        var loaded = ConversationStore.Load(_dir);
        Assert.AreEqual("new", loaded[0].Id);
        Assert.AreEqual("old", loaded[1].Id);
    }

    [TestMethod]
    public void Delete_RemovesConversationFolder()
    {
        ConversationStore.Save(_dir, SampleConversation("gone", "x"));
        Assert.IsTrue(Directory.Exists(Path.Combine(_dir, "gone")));

        ConversationStore.Delete(_dir, "gone");
        Assert.IsFalse(Directory.Exists(Path.Combine(_dir, "gone")));
    }

    // ── Markdown ──

    [TestMethod]
    public void ToMarkdown_ProducesExpectedDocument()
    {
        var md = ConversationStore.ToMarkdown(SampleConversation("c", "Test Chat"));

        const string expected =
            "# Test Chat\n\n" +
            "_Started 2024-01-02 03:04_\n\n" +
            "## You — 2024-01-02 03:04\n\n" +
            "hello\n\n" +
            "## Assistant — 2024-01-02 03:05\n\n" +
            "hi there\n";

        Assert.AreEqual(expected, md);
    }

    // ── ExportAll ──

    [TestMethod]
    public void ExportAll_WritesOneMarkdownFilePerConversation_WithExpectedContent()
    {
        var a = SampleConversation("a", "Alpha");
        var b = SampleConversation("b", "Beta");
        ConversationStore.Save(_dir, a);
        ConversationStore.Save(_dir, b);

        var dest  = Path.Combine(_dir, "export");
        var count = ConversationStore.ExportAll(_dir, dest);

        Assert.AreEqual(2, count);
        var files = Directory.GetFiles(dest, "*.md");
        Assert.AreEqual(2, files.Length);

        Assert.IsTrue(File.Exists(Path.Combine(dest, "Alpha.md")));
        Assert.IsTrue(File.Exists(Path.Combine(dest, "Beta.md")));
        // The written file is exactly the rendered markdown for that conversation.
        Assert.AreEqual(ConversationStore.ToMarkdown(a), File.ReadAllText(Path.Combine(dest, "Alpha.md")));
    }

    [TestMethod]
    public void ExportAll_EmptySource_WritesNothing_ReturnsZero()
    {
        var dest = Path.Combine(_dir, "export");
        Assert.AreEqual(0, ConversationStore.ExportAll(_dir, dest));
        Assert.AreEqual(0, Directory.GetFiles(dest, "*.md").Length);
    }

    [TestMethod]
    public void ExportAll_DuplicateTitles_ProduceDistinctFiles()
    {
        ConversationStore.Save(_dir, new ConversationRecord { Id = "1", Title = "Same" });
        ConversationStore.Save(_dir, new ConversationRecord { Id = "2", Title = "Same" });

        var dest = Path.Combine(_dir, "export");
        Assert.AreEqual(2, ConversationStore.ExportAll(_dir, dest));
        Assert.AreEqual(2, Directory.GetFiles(dest, "*.md").Length);
        Assert.IsTrue(File.Exists(Path.Combine(dest, "Same.md")));
        Assert.IsTrue(File.Exists(Path.Combine(dest, "Same (2).md")));
    }

    [TestMethod]
    public void ExportAll_SanitisesInvalidFileNameChars()
    {
        ConversationStore.Save(_dir, new ConversationRecord { Id = "x", Title = "a/b:c" });

        var dest = Path.Combine(_dir, "export");
        Assert.AreEqual(1, ConversationStore.ExportAll(_dir, dest));
        // Invalid path chars are replaced; exactly one file is produced and no exception thrown.
        var fileName = Path.GetFileName(Directory.GetFiles(dest, "*.md").Single());
        Assert.AreEqual("a_b_c.md", fileName);
    }
}
