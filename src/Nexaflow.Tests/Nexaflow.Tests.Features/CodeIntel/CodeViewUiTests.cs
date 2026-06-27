using FlaUI.Core.Input;
using Nexaflow.Tests.Features.WindowsFileSystem.UI;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.CodeIntel;

/// <summary>
/// End-to-end UI check for the "As Code" page: opens a real code sample through the file browser's
/// default-open route and asserts the composite view loads (editor + the code-intelligence panel) without
/// crashing — catching runtime XAML / resource / wiring failures a build can't.
///
/// Requires an interactive desktop session — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[TestCategory("UI")]
public class CodeViewUiTests : FileSystemUiTestBase
{
    [DataTestMethod]
    [DataRow("hello.cs")]
    [DataRow("app.js")]
    [DataRow("types.ts")]
    [DataRow("main.py")]
    [DataRow("point.rs")]       // Rust
    [DataRow("widget.cpp")]     // C++
    [DataRow("Greeter.java")]   // Java
    [DataRow("index.php")]      // PHP
    [DataRow("Counter.razor")]  // Razor (@code → component box)
    [DataRow("notebook.ipynb")] // Jupyter (decoded cell structure)
    public void CodeFile_OpensAsCode_WithStructurePanel(string fileName)
    {
        NavigateFileBrowserTo(TestSampleData.Path("code"));

        var row = WaitForName(fileName, 8);
        Assert.IsNotNull(row, $"File '{fileName}' not found in the file list.");
        row!.DoubleClick();
        Wait.UntilInputIsProcessed();

        Assert.IsNotNull(WaitForId("CodeView", 15), $"{fileName} did not open in CodeView.");
        Assert.IsNotNull(WaitForName("Code map", 8), $"The code-intelligence panel did not render for {fileName}.");
        Assert.IsFalse(App.HasExited, $"App crashed opening {fileName}.");
    }

    [TestMethod]
    public void WheelOverDiagram_ScrollsThePanel()
    {
        // A single tall class box makes the panel taller than its viewport AND fills it with the diagram, so
        // the cursor sits over the diagram. Before the fix the diagram's ScrollViewer swallowed the wheel.
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "codeintel-wheel-" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("using System.Aaa;").AppendLine("using System.Bbb;").AppendLine("namespace W;").AppendLine("public class Big").AppendLine("{");
        for (int m = 0; m < 60; m++) sb.AppendLine($"    public void Method{m}() {{ }}");
        sb.AppendLine("}");
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "Big.cs"), sb.ToString());

        try
        {
            NavigateFileBrowserTo(dir);
            var row = WaitForName("Big.cs", 8);
            Assert.IsNotNull(row);
            row!.DoubleClick();
            Wait.UntilInputIsProcessed();

            var codeView = WaitForId("CodeView", 15);
            Assert.IsNotNull(codeView);
            // Diagram member rows render as real TextBlocks (Name == text), unlike the FlowDocument prose.
            var member = WaitForName("+Method0()", 8);
            Assert.IsNotNull(member, "panel/diagram did not render");
            double y0 = member!.BoundingRectangle.Y;

            // Hover over the diagram (centre of the right-hand panel) and spin the wheel down.
            var r = codeView!.BoundingRectangle;
            FlaUI.Core.Input.Mouse.Position = new System.Drawing.Point((int)(r.Right - 180), (int)(r.Y + r.Height * 0.55));
            System.Threading.Thread.Sleep(200);
            FlaUI.Core.Input.Mouse.Scroll(-6);
            System.Threading.Thread.Sleep(400);

            var after = MainWindow.FindFirstDescendant(cf => cf.ByName("+Method0()"));
            bool scrolled = after is null || after.IsOffscreen || System.Math.Abs(after.BoundingRectangle.Y - y0) > 10;
            Assert.IsTrue(scrolled, "Wheel over the diagram did not scroll the Code map panel.");
        }
        finally { try { System.IO.Directory.Delete(dir, true); } catch { } }
    }
}
