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
/// A message Outlook kept as rich text, in the viewer: its body drawn as the RTF it is — on paper, in the colours its writer
/// chose — and read as plain text, by the view and by the assistant, as the words it draws.
/// </summary>
[TestClass]
[CoversNode("email-body")]
public class EmailRichTextTests
{
    private const string Rtf = @"{\rtf1\ansi{\colortbl ;\red255\green0\blue0;}\pard Hello {\b Bob},\par See the {\cf1 figures}.\par}";

    [TestMethod]
    public void ARichTextBodyIsDrawnAsRtf_OnPaper()
    {
        Opened(new MsgBuilder().String(0x0037, "Figures").Binary(0x1009, MsgBuilder.Stored(Rtf)), vm =>
        {
            Assert.AreEqual(RtfParser.Language, vm.RenderedLanguage);
            Assert.AreEqual(Rtf, vm.RenderedMarkdown, "drawn from the RTF itself, not from anything made of it");
            Assert.IsTrue(vm.IsRichText);
            Assert.AreSame(StyleFormat.Light, vm.BodyPalette);
            Assert.IsFalse(vm.HasHtmlBody, "there is no HTML to show the source of, or to open in a browser");
        });
    }

    [TestMethod]
    public void WithNoPlainTextOfItsOwn_ItReadsAsTheWordsItsRtfDraws()
    {
        Opened(new MsgBuilder().Binary(0x1009, MsgBuilder.Stored(Rtf)), vm =>
        {
            Assert.AreEqual("Hello Bob,\nSee the figures.", vm.PlainText);
            StringAssert.Contains(vm.GetContext(), "See the figures.");
            Assert.IsFalse(vm.GetContext().Contains(@"\rtf1"), "the assistant is given the words, not the RTF");
        });
    }

    [TestMethod]
    public void AnHtmlBodyIsDrawnAsMarkdown_InTheThemesOwnColours()
    {
        Opened(new MsgBuilder().Binary(0x1013, System.Text.Encoding.UTF8.GetBytes("<p>Hello</p>")), vm =>
        {
            Assert.IsNull(vm.RenderedLanguage);
            Assert.IsFalse(vm.IsRichText);
            Assert.IsNull(vm.BodyPalette);
        });
    }

    [TestMethod]
    [CoversNode("email-find")]
    public void ARichTextBodyIsSearchedInTheWordsItDraws_NeverItsRtf()
    {
        Opened(new MsgBuilder().Binary(0x1009, MsgBuilder.Stored(Rtf)), vm =>
        {
            var words = vm.SearchAsync(new SearchRequest("figures"), display: false, CancellationToken.None).GetAwaiter().GetResult();
            var control = vm.SearchAsync(new SearchRequest("cf1"), display: false, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(1, words.MatchCount);
            Assert.AreEqual(0, control.MatchCount, "a control word is how the body is written, not what it says");
        });
    }

    private static void Opened(MsgBuilder message, Action<EmailViewModel> check)
    {
        var path = Path.Combine(Path.GetTempPath(), $"nexaflow-{Guid.NewGuid():N}.msg");
        File.WriteAllBytes(path, message.Build());

        var vm = new EmailViewModel(path, Substitute.For<IShellServices>());

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
