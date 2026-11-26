using System;
using System.IO;
using FlaUI.Core.Input;
using System.Linq;
using FlaUI.Core.AutomationElements;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.CodeIntel.UI;

/// <summary>
/// The one UI journey for the "ShowCodeAction" page — the integration test registered at the feature's UI node.
/// It opens a structured C# sample via the explicit <b>"ShowCodeAction"</b> ActionStrip button and drives every
/// interactive surface in a single pass: the editor toolbar (encoding / EOL selectors, line-number toggle,
/// Save), the floating command panel, the status bar's line-operations button, and the code-map collapse /
/// reopen pair. Each check is soft, so one gap doesn't hide the rest.
///
/// Individual controls are asserted by view-model unit tests at their own leaf nodes; this proves the
/// wiring holds end-to-end. Nothing here mutates the sample: the toggles are view-only and Save is
/// presence-checked (a clean buffer leaves it disabled).
///
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("code")]
public class CodeJourneyTests : UiJourneyTestBase
{
    /// <summary>One sample of each supported language: each opens as code with its structure panel (the code map's class view).</summary>
    private static readonly string[] Languages =
    [
        "hello.cs", "app.js", "types.ts", "main.py",
        "point.rs",       // Rust
        "widget.cpp",     // C++
        "Greeter.java",   // Java
        "index.php",      // PHP
        "Counter.razor",  // Razor (@code → component box)
    ];

    /// <summary>The page's controls, then every supported language opened by double-click, then the wheel over the diagram.</summary>
    [TestMethod]
    [CoversNode("code-ui")]
    [CoversNode("code-map-class-view")]
    public void Code_Journey()
    {
        RunStep("the editor toolbar, status bar, command panel and code map", EditorControls);
        foreach (var file in Languages)
            RunStep($"{file} opens as code, with its structure panel", () => OpensAsCodeWithStructurePanel(file));
        RunStep("the wheel over the code map's diagram scrolls its panel", WheelOverDiagramScrollsThePanel);
        AssertJourney();
    }

    private void EditorControls()
    {
        // hello.cs has real structure, so the code-map panel opens and its toggle is on-screen.
        var file = "hello.cs";
        Check("code sample exists", () => TestSampleData.Files("code").Any(p => Path.GetFileName(p) == file));

        var view = OpenFileVia(TestSampleData.Path("code"), file, "ShowCodeAction", "CodeView");
        Assert.IsNotNull(view, "CodeView did not open via the 'As Code' action.");

        // ── Editor toolbar ────────────────────────────────────────────────────
        CheckPresent("Encoding selector", "Editor_Encoding");
        CheckPresent("Line-ending selector", "Editor_Eol");
        CheckInvoke("Line-numbers toggle", "Editor_LineNumbers");   // view-only, safe to flip
        CheckInvoke("Line-numbers toggle (back)", "Editor_LineNumbers");
        CheckInvoke("Whitespace toggle", "Editor_Whitespace");             // view-only, safe to flip
        CheckInvoke("Whitespace toggle (back)", "Editor_Whitespace");

        // Save is disabled on a freshly-opened, unmodified buffer — assert that rather than clicking it,
        // so the journey never writes to the shared sample file.
        var save = CheckPresent("Save", "Editor_Save");
        Check("Save is disabled while the buffer is clean", () => save is not null && !save.IsEnabled);

        // ── Status bar ────────────────────────────────────────────────────────
        CheckPresent("File size", "Editor_FileSize");
        CheckPresent("Line operations button", "Editor_LineCommands");

        // ── Floating command panel ────────────────────────────────────────────
        // Encode/Decode are selection-scoped and hidden without a selection; Checksum is always offered.
        CheckPresent("Checksum command group", "Editor_Cmd_Checksum");

        // ── Code map ──────────────────────────────────────────────────────────
        CheckDoes("Code map: collapse", "Code_MapToggle", () => WaitForId("Code_MapReopen", 3) is not null);
        CheckDoes("Code map: reopen", "Code_MapReopen", () => WaitForId("Code_MapToggle", 3) is not null);
    }

    /// <summary>
    /// Per-language render check for the code map's class diagram: the file browser's default-open route opens a real sample and
    /// the composite view loads (editor + the rendered diagram) — catching runtime XAML / resource / wiring failures a build can't.
    /// </summary>
    private void OpensAsCodeWithStructurePanel(string fileName)
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

    private void WheelOverDiagramScrollsThePanel()
    {
        // A single tall class box makes the panel taller than its viewport AND fills it with the diagram, so
        // the cursor sits over the diagram. Before the fix the diagram's ScrollViewer swallowed the wheel.
        var dir = Path.Combine(TestTemp.Root, "codeintel-wheel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("using System.Aaa;").AppendLine("using System.Bbb;").AppendLine("namespace W;").AppendLine("public class Big").AppendLine("{");
        for (int m = 0; m < 60; m++) sb.AppendLine($"    public void Method{m}() {{ }}");
        sb.AppendLine("}");
        File.WriteAllText(Path.Combine(dir, "Big.cs"), sb.ToString());

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
            Mouse.Position = new System.Drawing.Point((int)(r.X + r.Width / 2), (int)(r.Y + r.Height * 0.55));
            System.Threading.Thread.Sleep(200);
            Mouse.Scroll(-6);
            System.Threading.Thread.Sleep(400);

            Assert.IsTrue(scroll.VerticalScrollPercent.Value > before, "Wheel over the diagram did not scroll the Code map panel.");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
