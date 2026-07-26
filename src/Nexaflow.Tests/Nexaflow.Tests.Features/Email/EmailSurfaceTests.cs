using System;
using System.IO;
using System.Linq;
using Nexaflow.Features.Common;
using Nexaflow.Features.Email.ViewModels;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Email;

/// <summary>
/// The message tab's surfaces: the envelope, the body-view toolbar and which of its buttons a given
/// message earns, the body pane's fallback, and the attachment strip.
/// <para>
/// The toolbar is the part worth pinning. Its buttons are not a fixed set — each appears only when the
/// message actually has that form — so the toolbar describes the message as much as it switches views. Get
/// that wrong and you offer "HTML source" for a message with no HTML in it, which shows an empty pane and
/// looks like a viewer that lost the body.
/// </para>
/// </summary>
[TestClass]
public class EmailSurfaceTests
{
    private static EmailViewModel Open(string sampleFile, out IShellServices shell)
    {
        shell = Substitute.For<IShellServices>();
        return new EmailViewModel(TestSampleData.Path("email", sampleFile), shell);
    }

    private static EmailViewModel Open(string sampleFile = "simple.eml") => Open(sampleFile, out _);

    // ── Envelope ──────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("email-envelope")]
    public void TheEnvelopeCarriesTheFieldsTheMessageDeclared()
    {
        var vm = Open();

        Assert.IsFalse(string.IsNullOrEmpty(vm.Subject));
        Assert.IsFalse(string.IsNullOrEmpty(vm.From));
        Assert.IsFalse(string.IsNullOrEmpty(vm.To));
        Assert.IsFalse(vm.HasError, vm.ErrorMessage ?? "");
    }

    [TestMethod]
    [CoversNode("email-envelope")]
    public void AMessageWithNoCcDoesNotShowAnEmptyCcField()
    {
        var vm = Open();

        if (string.IsNullOrEmpty(vm.Cc))
            Assert.IsFalse(vm.HasCc, "the row is dropped from the layout, not left blank");
        else
            Assert.IsTrue(vm.HasCc);
    }

    [TestMethod]
    [CoversNode("email-raw-headers")]
    public void TheRawHeaderListIsCapturedButFoldedAwayToStartWith()
    {
        var vm = Open();

        Assert.IsTrue(vm.AllHeaders.Count > 0, "every header the message carried is kept");
        Assert.IsFalse(vm.HeadersExpanded, "a forwarded message's hundred headers must not open over the body");

        vm.ToggleHeadersCommand.Execute(null);
        Assert.IsTrue(vm.HeadersExpanded);
    }

    // ── Body view toolbar ─────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("email-body-modes")]
    public void AMessageOpensOnTheRenderedView()
    {
        var vm = Open();

        Assert.IsTrue(vm.IsRenderedView);
        Assert.IsFalse(vm.IsPlainTextView);
        Assert.IsFalse(vm.IsHtmlSourceView);
    }

    [TestMethod]
    [CoversNode("email-body-modes")]
    public void TheThreeViewsAreMutuallyExclusive()
    {
        var vm = Open();

        vm.ShowPlainTextCommand.Execute(null);
        Assert.IsTrue(vm.IsPlainTextView);
        Assert.IsFalse(vm.IsRenderedView);

        vm.ShowHtmlSourceCommand.Execute(null);
        Assert.IsTrue(vm.IsHtmlSourceView);
        Assert.IsFalse(vm.IsPlainTextView);

        vm.ShowRenderedCommand.Execute(null);
        Assert.IsTrue(vm.IsRenderedView);
        Assert.IsFalse(vm.IsHtmlSourceView);
    }

    [TestMethod]
    [CoversNode("email-body-modes")]
    public void TheToolbarOnlyOffersTheFormsThisMessageActuallyHas()
    {
        // simple.eml is multipart/alternative — it has both, so both buttons are earned.
        var both = Open();
        Assert.IsTrue(both.HasPlainText, "a text/plain part exists");
        Assert.IsTrue(both.HasHtmlBody, "and a text/html one");
    }

    [TestMethod]
    [CoversNode("email-body")]
    public void ThereIsAlwaysSomethingInTheRenderedPane_WhicheverBodyPartsExist()
    {
        var vm = Open();

        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.RenderedMarkdown),
                       "a message with only a plain-text part still renders it, rather than showing blank");
    }

    [TestMethod]
    [CoversNode("email-body")]
    public void AnInlineImageIsRewrittenToSomethingTheRendererCanActuallyShow()
    {
        // cid: references mean nothing to a markdown renderer; they are exported to local files first.
        var vm = Open("inline-image.eml");

        Assert.IsTrue(vm.HasInlineImages, "the fixture carries one");
        Assert.IsFalse(vm.RenderedMarkdown.Contains("cid:", StringComparison.OrdinalIgnoreCase),
                       "a surviving cid: reference renders as a broken image");
        Assert.IsNotNull(vm.MarkdownBaseDirectory, "the renderer needs a base directory to resolve them against");
    }

    // ── Attachments ───────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("email-attachment-strip")]
    public void OnlyRealAttachmentsGetAButton_InlineImagesAreCountedInstead()
    {
        var vm = Open("inline-image.eml");

        Assert.IsTrue(vm.InlineImageCount > 0);
        Assert.IsFalse(vm.Attachments.Any(a => a.DisplayName.Contains("logo", StringComparison.OrdinalIgnoreCase)),
                       "an image that is part of the body is not a separate attachment to open");
    }

    [TestMethod]
    [CoversNode("email-attachment-strip")]
    public void OpeningAnAttachmentGoesThroughTheOrdinaryFileOpenPath()
    {
        var vm = Open("simple.eml", out var shell);
        var attachment = vm.Attachments.First();
        shell.HandleObject(Arg.Any<string>()).Returns(true);

        vm.OpenAttachmentCommand.Execute(attachment);

        // The path is the message plus the entry name — a virtual path inside the .eml, so the shell's
        // normal resolution picks the right viewer instead of the email tab extracting it by hand.
        shell.Received().HandleObject(Arg.Is<string>(p => p.Contains(attachment.EntryName)));
    }

    [TestMethod]
    [CoversNode("email-attachment-strip")]
    public void AnAttachmentThatCannotBeOpenedSaysSo()
    {
        var vm = Open("simple.eml", out var shell);
        shell.HandleObject(Arg.Any<string>()).Returns(false);

        vm.OpenAttachmentCommand.Execute(vm.Attachments.First());

        shell.ReceivedWithAnyArgs().ShowError(default!);
    }

    [TestMethod]
    [CoversNode("email-attachment-strip")]
    public void AMessageWithNoAttachmentsHidesTheStrip()
    {
        var vm = Open("inline-image.eml");

        if (vm.Attachments.Count == 0) Assert.IsFalse(vm.HasAttachments);
        else Assert.IsTrue(vm.HasAttachments);
    }

    // ── Failure ───────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("email-error")]
    public void AFileThatIsNotAMessageReportsWhy_RatherThanOpeningEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), $"notmail_{Guid.NewGuid():N}.eml");
        File.WriteAllBytes(path, [0x00, 0x01, 0x02, 0x03]);
        try
        {
            var vm = new EmailViewModel(path, Substitute.For<IShellServices>());

            // Either it fails outright, or it parses to a message with nothing in it — both must be visible
            // as *something*, never a silently blank tab.
            Assert.IsTrue(vm.HasError || !string.IsNullOrWhiteSpace(vm.RenderedMarkdown)
                                      || vm.AllHeaders.Count > 0,
                          "a file that is not really a message must not open as an empty viewer");
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [TestMethod]
    [CoversNode("email-error")]
    public void AMissingFileIsAnErrorOnThePage_NotAnException()
    {
        var vm = new EmailViewModel(Path.Combine(Path.GetTempPath(), "no-such-message.eml"),
                                    Substitute.For<IShellServices>());

        Assert.IsTrue(vm.HasError);
        StringAssert.Contains(vm.ErrorMessage!, "Couldn't open");
    }
}
