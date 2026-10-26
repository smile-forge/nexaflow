using Nexaflow.IO.Common;
using System;
using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Features.Email.ViewModels;
using Nexaflow.Markdown.Rtf;
using Nexaflow.Search;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using NSubstitute;

namespace Nexaflow.Tests.Features.Email;

/// <summary>
/// A message Outlook kept as rich text, in the viewer: its body drawn as the RTF it is, in the theme's colours with the ones
/// its writer set kept, and read as plain text, by the view and by the assistant, as the words it draws.
/// </summary>
[TestClass]
public class EmailRichTextTests
{
    private const string Rtf = @"{\rtf1\ansi{\colortbl ;\red255\green0\blue0;}\pard Hello {\b Bob},\par See the {\cf1 figures}.\par}";

    [TestMethod]
    [CoversNode("email-body")]
    public async Task ARichTextBodyIsDrawnAsRtf()
    {
        await OpenedAsync(new MsgBuilder().String(0x0037, "Figures").Binary(0x1009, MsgBuilder.Stored(Rtf)), vm =>
        {
            Assert.AreEqual(RtfParser.Language, vm.RenderedLanguage);
            Assert.AreEqual(Rtf, vm.RenderedMarkdown, "drawn from the RTF itself, not from anything made of it");
            Assert.IsTrue(vm.IsRichText);
            Assert.IsFalse(vm.HasHtmlBody, "there is no HTML to show the source of, or to open in a browser");
        });
    }

    [TestMethod]
    [CoversNode("email-body")]
    public async Task WithNoPlainTextOfItsOwn_ItReadsAsTheWordsItsRtfDraws()
    {
        await OpenedAsync(new MsgBuilder().Binary(0x1009, MsgBuilder.Stored(Rtf)), vm =>
        {
            Assert.AreEqual("Hello Bob,\nSee the figures.", vm.PlainText);
            StringAssert.Contains(vm.GetContext(), "See the figures.");
            Assert.IsFalse(vm.GetContext().Contains(@"\rtf1"), "the assistant is given the words, not the RTF");
        });
    }

    [TestMethod]
    [CoversNode("email-body")]
    public async Task AnHtmlBodyIsDrawnAsMarkdown()
    {
        await OpenedAsync(new MsgBuilder().Binary(0x1013, System.Text.Encoding.UTF8.GetBytes("<p>Hello</p>")), vm =>
        {
            Assert.IsNull(vm.RenderedLanguage);
            Assert.IsFalse(vm.IsRichText);
        });
    }

    [TestMethod]
    [CoversNode("email-find")]
    public async Task ARichTextBodyIsSearchedInTheWordsItDraws_NeverItsRtf()
    {
        await OpenedAsync(new MsgBuilder().Binary(0x1009, MsgBuilder.Stored(Rtf)), vm =>
        {
            var words = vm.SearchAsync(new SearchRequest("figures"), display: false, CancellationToken.None).GetAwaiter().GetResult();
            var control = vm.SearchAsync(new SearchRequest("cf1"), display: false, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(1, words.MatchCount);
            Assert.AreEqual(0, control.MatchCount, "a control word is how the body is written, not what it says");
        });
    }

    private static async Task OpenedAsync(MsgBuilder message, Action<EmailViewModel> check)
    {
        var path = Path.Combine(TestTemp.Root, $"nexaflow-{Guid.NewGuid():N}.msg");
        File.WriteAllBytes(path, message.Build());

        var vm = new EmailViewModel(path, Substitute.For<IShellServices>(), VirtualFileSystem.Instance);
        await vm.Loaded;

        try
        {
            Assert.IsFalse(vm.HasError, vm.ErrorMessage);
            check(vm);
        }
        finally
        {
            vm.Dispose();
            File.Delete(path);
        }
    }
}
