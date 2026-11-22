using System.Collections.Generic;
using System.IO;
using System.Text;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Web;
using Nexaflow.Features.Web.RibbonHandlers;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Icons;
using System.Linq;
using System.Windows;
using Nexaflow.Features.Common.Ribbon;
using Nexaflow.Interaction;
using Nexaflow.Visuals.Common.Dropping;

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

    /// <summary>What the pinner makes of <paramref name="carried"/> as a browser drags it, in <paramref name="format"/>.</summary>
    private static RibbonPinResult? Pinned(object carried, string format = "UniformResourceLocatorW")
        => Pinner.Pin(new WindowsDragPayload(new DataObject(format, carried))).SingleOrDefault();

    // ── Ribbon pin: accepted formats ──────────────────────────────────────

    [TestMethod]
    [TestCategory("Unit")]
    public void ALink_IsTakenInEveryFormatBrowsersAdvertise_AndWordsAreNot()
    {
        // Chromium/Edge/IE advertise UniformResourceLocator(W); Firefox uses text/x-moz-url. Taking only a link
        // is what stops the ribbon lighting up for arbitrary dragged text.
        foreach (var format in new[] { "UniformResourceLocatorW", "UniformResourceLocator", "text/x-moz-url" })
            Assert.IsTrue(new WindowsDragPayload(new DataObject(format, "https://example.com")).ContainsAny(Pinner.AcceptedKinds), format);

        Assert.IsFalse(new WindowsDragPayload(new DataObject(DataFormats.UnicodeText, "some words")).ContainsAny(Pinner.AcceptedKinds));
    }

    // ── Ribbon pin: payload decoding ──────────────────────────────────────

    [TestMethod]
    [TestCategory("Unit")]
    public void Pin_APlainStringUrl_ProducesAnHtmlButtonLabelledWithTheHost()
    {
        var result = Pinned("https://example.com/docs/page?q=1");

        Assert.IsNotNull(result);
        Assert.AreEqual("Html", result!.PageKind);
        Assert.AreEqual("example.com", result.Label, "the host is the readable label, not the whole URL");
        Assert.AreEqual(IconRef.Emoji("🌐"), result.Icon);
        Assert.AreEqual("https://example.com/docs/page?q=1", result.PageParams!["path"]);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Pin_AUtf16NulTerminatedPayload_IsDecoded()
    {
        // The shape Chromium/Edge actually drop: UTF-16 bytes with a trailing NUL.
        var result = Pinned(Payload("https://example.com/page", Encoding.Unicode, nulTerminated: true));

        Assert.IsNotNull(result);
        Assert.AreEqual("https://example.com/page", result!.PageParams!["path"]);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Pin_AMozUrlPayload_TakesTheUrlLineAndDropsTheTitle()
    {
        // Firefox's text/x-moz-url is "url\ntitle".
        var result = Pinned(Payload("https://example.com/a\nExample Page Title", Encoding.Unicode));

        Assert.IsNotNull(result);
        Assert.AreEqual("https://example.com/a", result!.PageParams!["path"]);
        Assert.AreEqual("example.com", result.Label);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Pin_AnAsciiPayload_IsDecodedAsUtf8()
    {
        var result = Pinned(Payload("http://example.org/x", Encoding.UTF8));

        Assert.IsNotNull(result);
        Assert.AreEqual("http://example.org/x", result!.PageParams!["path"]);
    }

    // ── Ribbon pin: what must NOT pin ─────────────────────────────────────

    [TestMethod]
    [TestCategory("Unit")]
    public void Pin_RejectsAnythingThatIsNotAnHttpUrl()
    {
        // A non-http(s) scheme could otherwise pin a button that launches something unexpected.
        Assert.IsNull(Pinned("file:///C:/secret.txt"));
        Assert.IsNull(Pinned("javascript:alert(1)"));
        Assert.IsNull(Pinned("not a url at all"));
        Assert.IsNull(Pinned(string.Empty));
        Assert.IsNull(Pinned(42), "an unrecognised payload type pins nothing");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Pin_AUrlWithNoHost_FallsBackToTheUrlAsItsLabel()
    {
        // Uri.Host is empty for some absolute forms; the button still needs a caption.
        var result = Pinned("http://localhost:5000/app");

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
