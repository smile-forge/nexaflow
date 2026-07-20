using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Email.ViewModels;
using NSubstitute;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Email;

/// <summary>
/// The Email viewer's AI surface, driven headless through <see cref="EmailViewModel.GetClientTools"/>: the
/// read-only act tools (read_email / list_attachments / read_attachment), the honesty of the ambient
/// context, and the security scope. The viewer is <em>deliberately</em> read-only — there is no send / reply
/// / forward tool, and every tool is a <see cref="ToolSafety.SafeOperation"/> that auto-runs.
/// </summary>
[TestClass]
public class EmailViewModelTests
{
    [TestMethod]
    [CoversNode("email-ai-act")]
    [CoversNode("email-ai-context")]
    public async Task AiTools_ReadEmailAndAttachments_ThroughToolSurface()
    {
        var path = TestSampleData.Path("email", "simple.eml");
        var vm = new EmailViewModel(path, Substitute.For<IShellServices>());
        try
        {
            // Scope: the .eml path is what keeps two email tabs distinguishable when pinned together.
            Assert.AreEqual(path, vm.GetSecurityContext());

            // Context is honest about the message it is summarising.
            var ctx = vm.GetContext();
            StringAssert.Contains(ctx, "Quarterly report");
            StringAssert.Contains(ctx, "alice@example.com");

            // Read-only surface only — the act tools, and nothing that transmits mail.
            var tools = vm.GetClientTools();
            CollectionAssert.AreEquivalent(
                new[] { "read_email", "list_attachments", "read_attachment" },
                tools.Select(t => t.Name).ToArray(),
                "the Email AI act tool surface changed — update the tree's email-ai-act leaves to match");
            Assert.IsTrue(tools.All(t => t.Safety == ToolSafety.SafeOperation),
                "the Email viewer is deliberately read-only; every tool must auto-run as a safe read");

            // read_email returns the full message: headers + the complete (uncapped) body.
            var readEmail = tools.Single(t => t.Name == "read_email");
            var re = await readEmail.InvokeAsync(new JsonObject(), CancellationToken.None);
            Assert.IsFalse(re.IsError);
            StringAssert.Contains(re.ModelText, "Quarterly report");             // a header
            StringAssert.Contains(re.ModelText, "Please find the quarterly report"); // body text

            // list_attachments enumerates the non-inline parts.
            var list = tools.Single(t => t.Name == "list_attachments");
            var la = await list.InvokeAsync(new JsonObject(), CancellationToken.None);
            Assert.IsFalse(la.IsError);
            StringAssert.Contains(la.ModelText, "notes.txt");

            // read_attachment reads a text attachment's contents by name — data the context never carried.
            var readAtt = tools.Single(t => t.Name == "read_attachment");
            var ra = await readAtt.InvokeAsync(new JsonObject { ["name"] = "notes.txt" }, CancellationToken.None);
            Assert.IsFalse(ra.IsError);
            StringAssert.Contains(ra.ModelText, "revenue up 12%");

            // An unknown attachment is reported as an error result, not thrown.
            var missing = await readAtt.InvokeAsync(new JsonObject { ["name"] = "does-not-exist.bin" }, CancellationToken.None);
            Assert.IsTrue(missing.IsError);
        }
        finally { vm.Dispose(); }
    }
}
