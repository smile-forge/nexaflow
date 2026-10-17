using FlaUI.Core.Input;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.CodeIntel;

/// <summary>
/// Per-language render check for the code map's class diagram: opens a real code sample of each supported
/// language through the file browser's default-open route and asserts the composite view loads (editor +
/// the rendered diagram) without crashing — catching runtime XAML / resource / wiring failures a build
/// can't. Distinct from <c>CodeJourneyTests</c> (the one control-by-control journey): this is a corpus
/// sweep, one launch per language, plus the wheel-scroll behaviour over the rendered diagram.
///
/// Requires an interactive desktop session — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("code-map-class-view")]
public class CodeViewUiTests : FileSystemUiTestBase
{
    [TestMethod]
    [DataRow("hello.cs")]
    [DataRow("app.js")]
    [DataRow("types.ts")]
    [DataRow("main.py")]
    [DataRow("point.rs")]       // Rust
    [DataRow("widget.cpp")]     // C++
    [DataRow("Greeter.java")]   // Java
    [DataRow("index.php")]      // PHP
    [DataRow("Counter.razor")]  // Razor (@code → component box)
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
        var dir = System.IO.Path.Combine(TestTemp.Root, "codeintel-wheel-" + System.Guid.NewGuid().ToString("N"));
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

            Assert.IsNotNull(WaitForId("CodeView", 15), "Big.cs did not open in CodeView.");
            var map = WaitForId("Code_Map", 8);
            Assert.IsNotNull(map, "The code map panel did not open.");

            // The diagram is drawn rather than built from controls, so its rows are not elements to find. What the wheel is
            // to move is the panel, and the panel is scrollable only once the diagram has been laid out taller than it.
            var scroller = WaitFor(() => map!.FindAllDescendants().FirstOrDefault(e =>
                e.Patterns.Scroll.PatternOrDefault is { VerticallyScrollable.Value: true }), 10);
            Assert.IsNotNull(scroller, "The code map never grew taller than its panel, so the diagram did not render.");
            var scroll = scroller!.Patterns.Scroll.Pattern;
            double before = scroll.VerticalScrollPercent.Value;

            // Hover over the diagram (the panel's middle, below the short Dependencies list) and spin the wheel down.
            var r = map!.BoundingRectangle;
            FlaUI.Core.Input.Mouse.Position = new System.Drawing.Point((int)(r.X + r.Width / 2), (int)(r.Y + r.Height * 0.55));
            System.Threading.Thread.Sleep(200);
            FlaUI.Core.Input.Mouse.Scroll(-6);
            System.Threading.Thread.Sleep(400);

            Assert.IsTrue(scroll.VerticalScrollPercent.Value > before, "Wheel over the diagram did not scroll the Code map panel.");
        }
        finally { try { System.IO.Directory.Delete(dir, true); } catch { } }
    }
}
