using FlaUI.Core.Input;
using Nexaflow.Tests.Features.WindowsFileSystem.UI;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Email;

/// <summary>
/// End-to-end UI smoke for the Email viewer: double-clicking a <c>.eml</c> in the file browser opens it in
/// <c>EmailView</c> (the default-open route → <c>/email</c> → <c>OpenAsEmailAction</c>) with its envelope
/// rendered. A focused companion to <see cref="Fixtures.SampleFileViewerTests"/> that opens only the email
/// samples, so it's independent of the other viewers in the full sweep.
///
/// Requires an interactive desktop session — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("email-open")]
public class EmailViewerUiTests : FileSystemUiTestBase
{
    [TestMethod]
    public void Eml_OpensInEmailViewer_WithEnvelopeRendered()
    {
        NavigateFileBrowserTo(TestSampleData.Path("email"));

        var row = WaitForName("simple.eml", 10);
        Assert.IsNotNull(row, "simple.eml not found in the file list.");
        row!.DoubleClick();
        Wait.UntilInputIsProcessed();

        Assert.IsNotNull(WaitForId("EmailView", 15),
            "Double-clicking the .eml did not open the Email viewer.");
        Assert.IsNotNull(WaitForName("Quarterly report", 8),
            "The message subject was not rendered in the Email viewer.");
        Assert.IsFalse(App.HasExited, "App crashed while opening the email.");
    }
}
