using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using Nexaflow.Tests.Features.WindowsFileSystem.UI;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Fixtures;

/// <summary>
/// One UI test per generated sample file: navigates the file browser to the sample's folder,
/// opens the file (the double-click "default open" route), and asserts it loads in the expected
/// in-app viewer without crashing. Together with the data-driven case generation, this guarantees
/// every fixture in <see cref="TestSampleData"/> is exercised end-to-end through the real shell.
///
/// Requires an interactive desktop session — run with --filter "TestCategory=UI". The app launches
/// against an isolated NEXAFLOW_CONFIG_DIR (see <c>UITestBase</c>), so the file-type map is seeded
/// fresh from the bundled defaults and the open routing is deterministic on any machine.
/// </summary>
[TestClass]
[TestCategory("UI")]
public class SampleFileViewerTests : FileSystemUiTestBase
{
    /// <summary>Sub-directory → the AutomationId of the viewer its files should open in.</summary>
    private static readonly (string SubDir, string ViewerId)[] ViewerBySet =
    [
        ("markdown", "MarkdownView"),
        ("tabular",  "TabularView"),
        ("text",     "TextView"),
        ("json",     "JsonView"),
        ("logs",     "LogView"),
        ("binary",   "HexView"),
        ("images",   "ImageView"),
        ("model3d",  "Model3DView"),
        ("audio",    "AudioView"),
        ("video",    "VideoView"),
    ];

    /// <summary>Yields one case per generated sample file: (subDir, fileName, expectedViewerId).</summary>
    public static IEnumerable<object[]> SampleFiles()
    {
        foreach (var (subDir, viewerId) in ViewerBySet)
            foreach (var path in TestSampleData.Files(subDir))
                yield return [subDir, Path.GetFileName(path), viewerId];
    }

    public static string CaseName(MethodInfo _, object[] data) => $"{data[0]}/{data[1]} → {data[2]}";

    [TestMethod]
    [DynamicData(nameof(SampleFiles), DynamicDataDisplayName = nameof(CaseName))]
    public void SampleFile_OpensInExpectedViewer(string subDir, string fileName, string viewerId)
    {
        NavigateFileBrowserTo(TestSampleData.Path(subDir));
        OpenFile(fileName);

        Assert.IsNotNull(WaitForId(viewerId, 15),
            $"{subDir}/{fileName} did not open in {viewerId}.");
        Assert.IsFalse(App.HasExited, $"App crashed opening {subDir}/{fileName}.");
    }

    /// <summary>Opens a file by double-clicking its row in the file list (the default-open route).</summary>
    private void OpenFile(string fileName)
    {
        var row = WaitForName(fileName, 8);
        Assert.IsNotNull(row, $"File '{fileName}' not found in the file list.");
        row!.DoubleClick();
        Wait.UntilInputIsProcessed();
    }
}
