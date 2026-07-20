using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Font.ViewModels;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Font;

/// <summary>
/// Covers the Font viewer's AI-integration surface (headless — no WPF glyph rendering): the security
/// scope that keeps two pinned font pages distinguishable in the conversation hub, the honest page
/// context, and the read-only <c>get_font_details</c> act tool driven exactly as the hub drives it.
/// The <c>render_font_preview</c> tool encodes a WPF <c>RenderTargetBitmap</c> via <c>PngBitmapEncoder</c>,
/// which isn't reliable off an STA/interactive desktop (mirrors the images <c>capture_image</c> decision)
/// — it's exercised via the UI journey, not here.
/// </summary>
[TestClass]
public class FontAiTests
{
    private static string TtfSample() => TestSampleData.Files("font").Single(p => p.EndsWith(".ttf"));
    private static string WoffSample() => TestSampleData.Files("font").Single(p => p.EndsWith(".woff"));

    private static FontViewModel Vm(params string[] paths) =>
        new(paths, Substitute.For<IShellServices>());

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("font-ai-context")]
    public void SecurityScope_IsDistinctPerFontSet_AndContextIsHonest()
    {
        var a = Vm(TtfSample());
        var sameAsA = Vm(TtfSample());
        var different = Vm(WoffSample());
        var empty = Vm();   // blank compare mode

        // ── the core guarantee: a non-null scope, equal for the same set, distinct across sets ──
        Assert.IsNotNull(a.GetSecurityContext());
        Assert.AreEqual(a.GetSecurityContext(), sameAsA.GetSecurityContext(),
            "the same font set must produce the same scope (deterministic)");
        Assert.AreNotEqual(a.GetSecurityContext(), different.GetSecurityContext(),
            "two font pages over different sets must not collapse first-wins in the conversation hub");

        // an empty page is still non-null and distinct from a populated one
        Assert.IsNotNull(empty.GetSecurityContext());
        Assert.AreNotEqual(a.GetSecurityContext(), empty.GetSecurityContext());

        // ── context is honest about the compared set: count + the 1-based, named entries ──
        var ctx = a.GetContext();
        StringAssert.Contains(ctx, "comparing 1 font");     // honest count
        StringAssert.Contains(ctx, "1. Nexaflow");          // 1-based index + family name (matches ResolveFont)
        StringAssert.Contains(ctx, a.Options.PreviewText);  // the sample text being compared

        // the empty page's context says so honestly (no phantom fonts)
        StringAssert.Contains(empty.GetContext(), "empty");
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("font-ai-act")]
    public async Task GetFontDetails_ReturnsMetadata_ForResolvableFont()
    {
        var vm = Vm(TtfSample());
        var details = vm.GetClientTools().Single(t => t.Name == "get_font_details");

        // resolvable by 1-based index (matching the context numbering) — a pure metadata read
        var byIndex = await details.InvokeAsync(new JsonObject { ["font"] = "1" }, CancellationToken.None);
        Assert.IsFalse(byIndex.IsError, byIndex.ModelText);
        StringAssert.Contains(byIndex.ModelText, "Nexaflow");   // identity row
        StringAssert.Contains(byIndex.ModelText, "Glyphs");     // a style/metrics row proves faces resolved

        // …and by name (substring), the other reference form the tool accepts
        var byName = await details.InvokeAsync(new JsonObject { ["font"] = "Nexaflow" }, CancellationToken.None);
        Assert.IsFalse(byName.IsError, byName.ModelText);
        StringAssert.Contains(byName.ModelText, "Nexaflow");

        // an unresolvable reference is a clean error the model can recover from (not an exception)
        var missing = await details.InvokeAsync(new JsonObject { ["font"] = "no-such-font-xyz" }, CancellationToken.None);
        Assert.IsTrue(missing.IsError);
    }
}
