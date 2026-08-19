using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Font.UI;

/// <summary>
/// End-to-end UI check for the "As Font" open path: double-clicks a real font sample in the file browser
/// and asserts it opens preloaded in <c>FontView</c> with its family name rendered in the details — for
/// both a raw <c>.ttf</c> and a <c>.woff</c> (which exercises the in-app WOFF → sfnt decode path the
/// standalone journey doesn't). Complements the generic <c>SampleFileViewerTests</c> with a content check.
/// <para>Interactive desktop only — run with <c>--filter "TestCategory=UI"</c>.</para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("font-open")]
public class FontFileOpenUiTests : FileSystemUiTestBase
{
    [TestMethod]
    [DataRow("nexaflow-test.ttf")]
    [DataRow("nexaflow-test.woff")]
    public void FontFile_OpensInFontView_WithDetails(string fileName)
    {
        NavigateFileBrowserTo(TestSampleData.Path("font"));

        var row = WaitForName(fileName, 8);
        Assert.IsNotNull(row, $"File '{fileName}' not found in the file list.");
        row!.DoubleClick();
        Wait.UntilInputIsProcessed();

        Assert.IsNotNull(WaitForId("FontView", 15), $"{fileName} did not open in FontView.");
        Assert.IsNotNull(WaitForName("Nexaflow Test", 8),
            $"The font's family name / details did not render for {fileName}.");
        Assert.IsFalse(App.HasExited, $"App crashed opening {fileName}.");
    }
}
