using System.Collections.Generic;
using System.IO;
using System.Text;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Web;
using Nexaflow.Features.Web.RibbonHandlers;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Web;

/// <summary>
/// The two ways a bare URL reaches a Web tab without a file behind it: dragging a link from a browser onto
/// the ribbon (<see cref="UrlRibbonPinHandler"/>) and clicking a URL object elsewhere in the app, e.g. a
/// scratchpad link (<see cref="WebObjectHandler"/>).
/// <para>
/// The drag payload is the interesting part: browsers hand a link over as a <see cref="MemoryStream"/> of
/// (usually UTF-16) bytes, Firefox appends the page title on a second line, and the buffer can be NUL
/// terminated — so the decode is exercised in the shapes real browsers actually produce.
/// </para>
/// </summary>
[TestClass]
[CoversNode("web-url-pin")]
public class WebUrlRoutingTests
{
    private static readonly UrlRibbonPinHandler Pinner = new();

    /// <summary>A drag payload as a browser supplies it: raw bytes in <paramref name="encoding"/>, optionally
    /// NUL-terminated the way CFSTR_INETURLW buffers are.</summary>
    private static MemoryStream Payload(string text, Encoding encoding, bool nulTerminated = false)
        => new(encoding.GetBytes(nulTerminated ? text + "\0" : text));

    // ── Ribbon pin: accepted formats ──────────────────────────────────────

    [TestMethod]
    [TestCategory("Unit")]
    public void AcceptedFormats_CoverTheLinkDragFormatsBrowsersAdvertise()
    {
        // Chromium/Edge/IE advertise UniformResourceLocator(W); Firefox uses text/x-moz-url. Matching only
        // these is what stops the ribbon lighting up for arbitrary dragged text.
        CollectionAssert.AreEquivalent(
            new[] { "UniformResourceLocatorW", "UniformResourceLocator", "text/x-moz-url" },
            (System.Collections.ICollection)Pinner.AcceptedFormats);
    }

    // ── Ribbon pin: payload decoding ──────────────────────────────────────

    [TestMethod]
    [TestCategory("Unit")]
    public void Pin_APlainStringUrl_ProducesAnHtmlButtonLabelledWithTheHost()
    {
        var result = Pinner.Pin("https://example.com/docs/page?q=1");

        Assert.IsNotNull(result);
        Assert.AreEqual("Html", result!.PageKind);
        Assert.AreEqual("example.com", result.Label, "the host is the readable label, not the whole URL");
        Assert.AreEqual("🌐", result.Icon);
        Assert.AreEqual("https://example.com/docs/page?q=1", result.PageParams!["path"]);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Pin_AUtf16NulTerminatedPayload_IsDecoded()
    {
        // The shape Chromium/Edge actually drop: UTF-16 bytes with a trailing NUL.
        var result = Pinner.Pin(Payload("https://example.com/page", Encoding.Unicode, nulTerminated: true));

        Assert.IsNotNull(result);
        Assert.AreEqual("https://example.com/page", result!.PageParams!["path"]);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Pin_AMozUrlPayload_TakesTheUrlLineAndDropsTheTitle()
    {
        // Firefox's text/x-moz-url is "url\ntitle".
        var result = Pinner.Pin(Payload("https://example.com/a\nExample Page Title", Encoding.Unicode));

        Assert.IsNotNull(result);
        Assert.AreEqual("https://example.com/a", result!.PageParams!["path"]);
        Assert.AreEqual("example.com", result.Label);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Pin_AnAsciiPayload_IsDecodedAsUtf8()
    {
        var result = Pinner.Pin(Payload("http://example.org/x", Encoding.UTF8));

        Assert.IsNotNull(result);
        Assert.AreEqual("http://example.org/x", result!.PageParams!["path"]);
    }

    // ── Ribbon pin: what must NOT pin ─────────────────────────────────────

    [TestMethod]
    [TestCategory("Unit")]
    public void Pin_RejectsAnythingThatIsNotAnHttpUrl()
    {
        // A non-http(s) scheme could otherwise pin a button that launches something unexpected.
        Assert.IsNull(Pinner.Pin("file:///C:/secret.txt"));
        Assert.IsNull(Pinner.Pin("javascript:alert(1)"));
        Assert.IsNull(Pinner.Pin("not a url at all"));
        Assert.IsNull(Pinner.Pin(string.Empty));
        Assert.IsNull(Pinner.Pin(42), "an unrecognised payload type pins nothing");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Pin_AUrlWithNoHost_FallsBackToTheUrlAsItsLabel()
    {
        // Uri.Host is empty for some absolute forms; the button still needs a caption.
        var result = Pinner.Pin("http://localhost:5000/app");

        Assert.IsNotNull(result);
        Assert.AreEqual("localhost", result!.Label);
    }

    // ── Clicked URL objects (e.g. a scratchpad link) ──────────────────────

    [TestMethod]
    [TestCategory("Unit")]
    public void ObjectHandler_ClaimsHttpUrls_AndNothingElse()
    {
        var handler = new WebObjectHandler(Substitute.For<IShellServices>());

        Assert.IsTrue(handler.CanHandleObject("https://example.com"));
        Assert.IsTrue(handler.CanHandleObject("http://example.com"));
        Assert.IsFalse(handler.CanHandleObject("file:///C:/x.txt"));
        Assert.IsFalse(handler.CanHandleObject("just text"));
        Assert.IsFalse(handler.CanHandleObject(42));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ObjectHandler_OpensTheUrlInAWebTab()
    {
        var shell = Substitute.For<IShellServices>();
        new WebObjectHandler(shell).Handle("https://example.com/read");

        shell.Received(1).OpenTab("Html",
            Arg.Is<Dictionary<string, string>>(p => p["path"] == "https://example.com/read"));
    }
}
