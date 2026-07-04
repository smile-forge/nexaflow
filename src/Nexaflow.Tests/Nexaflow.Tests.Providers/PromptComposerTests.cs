using Nexaflow.Providers.Common;

namespace Nexaflow.Tests.Providers;

/// <summary>
/// The shared, SDK-agnostic message-prep seam every provider now routes through:
/// system-prompt split, attachment partition, and the file-list text fallback.
/// </summary>
[TestClass]
public class PromptComposerTests
{
    // ── SplitSystemPrompt ────────────────────────────────────────────────────

    [TestMethod]
    public void SplitSystemPrompt_LeadingSystem_IsExtracted()
    {
        var (system, rest) = PromptComposer.SplitSystemPrompt(
        [
            new LlmMessage(LlmRole.System, "be helpful"),
            new LlmMessage(LlmRole.User, "hi"),
            new LlmMessage(LlmRole.Assistant, "yo"),
        ]);

        Assert.AreEqual("be helpful", system);
        Assert.AreEqual(2, rest.Count);
        Assert.AreEqual("hi", rest[0].Text);
    }

    [TestMethod]
    public void SplitSystemPrompt_NoSystem_ReturnsNullAndAllTurns()
    {
        var (system, rest) = PromptComposer.SplitSystemPrompt([new LlmMessage(LlmRole.User, "hi")]);
        Assert.IsNull(system);
        Assert.AreEqual(1, rest.Count);
    }

    [TestMethod]
    public void SplitSystemPrompt_SystemNotFirst_IsNotExtracted()
    {
        var (system, rest) = PromptComposer.SplitSystemPrompt(
        [
            new LlmMessage(LlmRole.User, "hi"),
            new LlmMessage(LlmRole.System, "late"),
        ]);
        Assert.IsNull(system);
        Assert.AreEqual(2, rest.Count);
    }

    [TestMethod]
    public void SplitSystemPrompt_Empty_ReturnsNull()
    {
        var (system, rest) = PromptComposer.SplitSystemPrompt([]);
        Assert.IsNull(system);
        Assert.AreEqual(0, rest.Count);
    }

    // ── PartitionAttachments ─────────────────────────────────────────────────

    [TestMethod]
    public void PartitionAttachments_Null_AreBothEmpty()
    {
        var (images, files) = PromptComposer.PartitionAttachments(null);
        Assert.AreEqual(0, images.Count);
        Assert.AreEqual(0, files.Count);
    }

    [TestMethod]
    public void PartitionAttachments_SplitsByImageness_PreservingOrder()
    {
        var (images, files) = PromptComposer.PartitionAttachments(
        [
            new LlmAttachment("a.png"),
            new LlmAttachment("notes.txt"),
            new LlmAttachment("b.jpg"),
            new LlmAttachment("data.json"),
        ]);

        CollectionAssert.AreEqual(new[] { "a.png", "b.jpg" }, images.Select(a => a.FilePath).ToArray());
        CollectionAssert.AreEqual(new[] { "notes.txt", "data.json" }, files.Select(a => a.FilePath).ToArray());
    }

    // ── AppendFileList ───────────────────────────────────────────────────────

    [TestMethod]
    public void AppendFileList_NoFiles_ReturnsPromptUnchanged()
        => Assert.AreEqual("hello", PromptComposer.AppendFileList("hello", []));

    [TestMethod]
    public void AppendFileList_ListsEachPath_AfterThePrompt()
    {
        var text = PromptComposer.AppendFileList("hello",
            [new LlmAttachment(@"C:\a.txt"), new LlmAttachment(@"C:\b.txt")]);

        StringAssert.StartsWith(text, "hello");
        StringAssert.Contains(text, "Attached files:");
        StringAssert.Contains(text, @"C:\a.txt");
        StringAssert.Contains(text, @"C:\b.txt");
    }
}
