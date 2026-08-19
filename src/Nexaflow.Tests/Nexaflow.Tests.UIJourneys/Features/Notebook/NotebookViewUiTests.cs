using FlaUI.Core.Input;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Notebook;

/// <summary>
/// End-to-end UI check for the Notebook feature: opens an <c>.ipynb</c> through the file browser's default-open
/// route and asserts it lands in the dedicated NotebookView (not the code editor) without crashing.
///
/// Requires an interactive desktop session — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("notebook")]
public class NotebookViewUiTests : FileSystemUiTestBase
{
    [TestMethod]
    [CoversNode("notebook-ui")]
    public void Ipynb_OpensInNotebookView()
    {
        NavigateFileBrowserTo(TestSampleData.Path("notebook"));

        var row = WaitForName("notebook.ipynb", 8);
        Assert.IsNotNull(row, "notebook.ipynb not found in the file list.");
        row!.DoubleClick();
        Wait.UntilInputIsProcessed();

        Assert.IsNotNull(WaitForId("NotebookView", 15), "notebook.ipynb did not open in NotebookView.");
        Assert.IsFalse(App.HasExited, "App crashed opening the notebook.");
    }
}
