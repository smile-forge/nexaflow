using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Font.ClientTools;
using Nexaflow.Features.Font.ViewModels;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Font;

/// <summary>
/// Covers the Font viewer's AI surface (headless — no WPF rendering): the page context string, the two
/// exposed client tools, font reference resolution, and the details tool. The image-rendering tool needs
/// an STA/visual host, so it's exercised manually / via the UI, not here.
/// </summary>
[TestClass]
[CoversNode("font")]
public class FontClientToolsTests
{
    private static FontViewModel LoadedVm()
    {
        var ttf = TestSampleData.Files("font").Single(p => p.EndsWith(".ttf"));
        return new FontViewModel(new[] { ttf }, Substitute.For<IShellServices>());
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("font-ai")]
    public void Exposes_TheTwoFontTools()
    {
        var names = LoadedVm().GetClientTools().Select(t => t.Name).ToList();
        CollectionAssert.AreEquivalent(new[] { "get_font_details", "render_font_preview" }, names);
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("font-ai")]
    public void GetContext_ListsFonts_PreviewText_AndSpecimen()
    {
        var vm = LoadedVm();
        var ctx = vm.GetContext();
        StringAssert.Contains(ctx, "Nexaflow");             // the loaded font's family name
        StringAssert.Contains(ctx, vm.Options.PreviewText); // the sequence being previewed
        StringAssert.Contains(ctx, "specimen");             // the alphabet/digits/symbols line
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("font-ai")]
    public void ResolveFont_ByIndex_ByName_AndDefault()
    {
        var vm = LoadedVm();
        Assert.IsNotNull(vm.ResolveFont("1"));                 // 1-based index
        Assert.IsNotNull(vm.ResolveFont("Nexaflow"));          // name substring
        Assert.IsNotNull(vm.ResolveFont(null));                // selected / first renderable
        Assert.IsNull(vm.ResolveFont("no-such-font-xyz"));     // no match
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void SystemFontFile_LoadsOnlyThatFont_NotTheWholeFontsFolder()
    {
        // WPF's Fonts.GetFontFamilies(fileUri) returns EVERY font in the file's directory — so opening one
        // file in C:\Windows\Fonts once loaded ~200 families. Guard that the file-scoped loader returns one.
        var arial = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Fonts), "arial.ttf");
        if (!File.Exists(arial)) { Assert.Inconclusive("arial.ttf not present on this machine."); return; }

        var vm = new FontViewModel(new[] { arial }, Substitute.For<IShellServices>());
        Assert.AreEqual(1, vm.Fonts.Count,
            "a single system font file must load ONE family, not the whole C:\\Windows\\Fonts directory.");
        StringAssert.Contains(vm.Fonts[0].DisplayName, "Arial");
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("font-details")]
    public async Task DetailsTool_ReturnsMetadata_ForAnyListedFont()
    {
        var tool = new FontDetailsTool(LoadedVm());
        var result = await tool.InvokeAsync(new JsonObject { ["font"] = "1" }, CancellationToken.None);

        Assert.IsTrue(result.Success);
        StringAssert.Contains(result.ModelText, "Nexaflow");   // identity
        StringAssert.Contains(result.ModelText, "Glyphs");     // a style/metrics row
    }
}
