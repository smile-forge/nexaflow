using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Email.ViewModels;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Email;

/// <summary>
/// The Email viewer's AI surface, one test per tool.
/// <para>
/// The viewer is <em>deliberately</em> read-only. There is no send, reply or forward tool, and the system
/// guidance says so out loud — a model that believed it could send mail from a page showing someone's
/// message is the failure worth designing against, so "the surface is exactly these three reads" is itself
/// an assertion rather than an incidental detail.
/// </para>
/// </summary>
[TestClass]
public class EmailViewModelTests
{
    private static string SamplePath => TestSampleData.Path("email", "simple.eml");

    private static EmailViewModel Open() => new(SamplePath, Substitute.For<IShellServices>());

    private static Task<ToolResult> Run(EmailViewModel vm, string tool, JsonObject? args = null)
        => vm.GetClientTools().Single(t => t.Name == tool).InvokeAsync(args ?? [], CancellationToken.None);

    // ── The surface ───────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("email-ai-act")]
    public void TheSurfaceIsThreeReads_AndNothingThatTransmitsMail()
    {
        var vm = Open();
        try
        {
            var tools = vm.GetClientTools();

            CollectionAssert.AreEquivalent(
                new[] { "read_email", "list_attachments", "read_attachment" },
                tools.Select(t => t.Name).ToArray(),
                "the Email AI act tool surface changed — update the tree's email-ai-act leaves to match");
            Assert.IsTrue(tools.All(t => t.Safety == ToolSafety.SafeOperation),
                          "every tool is a pure read, so none of them needs an approval prompt");
        }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    [CoversNode("email-ai-act")]
    public void TheGuidanceTellsTheModelItCannotSendFromHere()
    {
        var vm = Open();
        try
        {
            StringAssert.Contains(vm.GetAiSystemPromptGuidance()!, "cannot send",
                                  "drafting a reply is useful; believing it was sent is not");
        }
        finally { vm.Dispose(); }
    }

    // ── Context ───────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("email-ai-context")]
    public void TheContextCarriesTheEnvelopeAndTheBody()
    {
        var vm = Open();
        try
        {
            var ctx = vm.GetContext();

            StringAssert.Contains(ctx, "Quarterly report");
            StringAssert.Contains(ctx, "alice@example.com");
            StringAssert.Contains(ctx, "Body:", "\"summarise this\" needs the message, not just its headers");
        }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    [CoversNode("email-ai-context")]
    public void TheContextNamesTheAttachments_SoTheModelKnowsWhatItCanAskFor()
    {
        var vm = Open();
        try
        {
            StringAssert.Contains(vm.GetContext(), "notes.txt");
        }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    [CoversNode("email-ai-context")]
    public void TwoOpenMessagesAreDistinctScopes_NotFirstWins()
    {
        var vm = Open();
        try { Assert.AreEqual(SamplePath, vm.GetSecurityContext()); }
        finally { vm.Dispose(); }
    }

    // ── Reading ───────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("email-ai-act-read-email")]
    public async Task ReadEmail_ReturnsTheHeadersAndTheUncappedBody()
    {
        var vm = Open();
        try
        {
            var r = await Run(vm, "read_email");

            Assert.IsFalse(r.IsError);
            StringAssert.Contains(r.ModelText, "Quarterly report");
            StringAssert.Contains(r.ModelText, "Please find the quarterly report",
                                  "the context truncates at 4000 chars; this is how the rest is reached");
        }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    [CoversNode("email-ai-act-list-attachments")]
    public async Task ListAttachments_EnumeratesTheRealOnes()
    {
        var vm = Open();
        try
        {
            var r = await Run(vm, "list_attachments");

            Assert.IsFalse(r.IsError);
            StringAssert.Contains(r.ModelText, "notes.txt");
        }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    [CoversNode("email-ai-act-read-attachment")]
    public async Task ReadAttachment_ReturnsATextAttachmentsContents()
    {
        var vm = Open();
        try
        {
            var r = await Run(vm, "read_attachment", new JsonObject { ["name"] = "notes.txt" });

            Assert.IsFalse(r.IsError);
            StringAssert.Contains(r.ModelText, "revenue up 12%",
                                  "content the ambient context never carried");
        }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    [CoversNode("email-ai-act-read-attachment")]
    public async Task ReadAttachment_AnUnknownNameIsReported_NotThrown()
    {
        var vm = Open();
        try
        {
            var r = await Run(vm, "read_attachment", new JsonObject { ["name"] = "does-not-exist.bin" });

            Assert.IsTrue(r.IsError);
        }
        finally { vm.Dispose(); }
    }
}
