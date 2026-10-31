using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.TreeView;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Visuals.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Visuals.Markdown.TreeView;

/// <summary>
/// A tree view drawn on the shared layout tree: a row for each node, top to bottom, set in by how deep it stands — a list, as it is
/// drawn, whatever the nodes nest as — with the lines joining each folder to what is in it apart from the rows.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("treeview")]
public class TreeViewBuilderTests : MermaidBuilderContract
{
    private const string Project =
        "treeView-beta\n    my-project/\n        src/\n            App.tsx :::highlight ## main component\n            index.js ## entry point\n        .env ## environment variables\n        package.json";

    /// <summary>A tree view is a language of its own: its fence names it, though a Mermaid header naming it is read too.</summary>
    public override MermaidDiagram Diagram => MermaidDiagram.TreeView;

    /// <inheritdoc/>
    public override string Language => TreeViewParser.Language;

    /// <inheritdoc/>
    protected override bool FitsTheRoom => true;

    /// <inheritdoc/>
    protected override IEnumerable<(string What, string Source)> Drawn { get; } =
    [
        ("the documented project, highlighted and described", Project),
        ("a tree drawn with a box drawing", "treeView-beta\n├── packages/\n│   ├── mermaid/\n│   │   └── src/\n│   └── parser/\n└── README.md"),
        ("quoted labels", "treeView-beta\n    \"my project\"\n        \"folder with spaces\"\n            \"file.js\""),
        ("icons shown, and mapped", "---\nconfig:\n  treeView:\n    showIcons: true\n    filenameIcons:\n      Dockerfile: docker\n    extensionIcons:\n      .txt: none\n---\ntreeView-beta\n    src/\n        App.tsx icon(logos:react)\n    Dockerfile\n    notes.txt"),
        ("icons from the material icon theme", "---\nconfig:\n  treeView:\n    showIcons: true\n    defaultIconPack: material-icon-theme\n    filenameIcons:\n      Dockerfile: docker\n    extensionIcons:\n      .ts: typescript\n      .txt: none\n---\ntreeView-beta\n    src/\n        index.ts\n        App.tsx icon(material-icon-theme:react-ts)\n    Dockerfile\n    notes.txt"),
        ("its own geometry and colours", "---\nconfig:\n    treeView:\n        rowIndent: 40\n        lineThickness: 3\n    themeVariables:\n        treeView:\n            labelFontSize: '20px'\n            labelColor: '#FF0000'\n            lineColor: '#00FF00'\n---\ntreeView-beta\n    \"packages\"\n        \"mermaid\"\n        \"parser\""),
        ("no header", "src/\n    index.js"),
        ("still being written", "treeView-beta\n├── src/\n│   └── \"\"\n└── README.md ##"),
        ("a description far wider than the room", "treeView-beta\nsrc/ ## " + string.Concat(Enumerable.Repeat("a long description ", 12))),
        ("rules broken", "treeView-beta\n├── src/\n    a.ts\n\"never closed"),
        ("nothing to draw", "treeView-beta\ntitle Empty"),
    ];

    [TestMethod]
    public void EveryNodeIsARowStandingForItWithEverythingUnderIt_TheRowsOneUnderAnother() => UiThread.Run(() =>
    {
        const string source = "treeView-beta\nsrc/\n    a.ts\nb.ts";
        var laid = Lay(source);
        var rows = Pieces(laid, TreeViewPiece.Row);

        CollectionAssert.AreEqual(new[] { "src/\n    a.ts\n", "    a.ts\n", "b.ts" }, rows.Select(row => Written(source, row.Part)).ToArray());
        Assert.IsTrue(rows.All(row => row.Parent.Kind == TreeViewPiece.Rows), "a list, as it is drawn: no row is inside another");
        Assert.IsTrue(rows[0].Bounds.Bottom <= rows[1].Bounds.Top + 0.5 && rows[1].Bounds.Bottom <= rows[2].Bounds.Top + 0.5, "top to bottom");
    });

    [TestMethod]
    public void ANodeIsSetInByHowDeepItStands_AndEveryRowRunsTheWidthOfTheTree() => UiThread.Run(() =>
    {
        var laid = Lay("treeView-beta\nsrc/\n    a.ts\n        deeper.ts");
        var labels = Pieces(laid, TreeViewPiece.Label);
        var rows = Pieces(laid, TreeViewPiece.Row);

        Assert.IsTrue(labels[0].Bounds.Left < labels[1].Bounds.Left && labels[1].Bounds.Left < labels[2].Bounds.Left);
        Assert.IsTrue(rows.All(row => Math.Abs(row.Bounds.Width - rows[0].Bounds.Width) < 0.5), "so a press anywhere along it means its node");
    });

    [TestMethod]
    public void TheLinesStandForNothing_ApartFromTheRows() => UiThread.Run(() =>
    {
        var laid = Lay("treeView-beta\nsrc/\n    a.ts\n    b.ts");
        var lines = Pieces(laid, TreeViewPiece.Lines).Single();

        Assert.IsNull(lines.Part);
        Assert.IsTrue(lines.Marks.Length >= 3, "down from the folder, and across to each thing in it");
    });

    [TestMethod]
    public void AFoldersNameIsBold_AndItsSlashIsNotDrawn() => UiThread.Run(() =>
    {
        var labels = Pieces(Lay("treeView-beta\nsrc/\n    a.ts"), TreeViewPiece.Label);

        Assert.AreEqual("src", labels[0].Words!.Glyphs.Text);
        Assert.IsTrue(labels[0].Words!.Maps, "what is drawn is what is written, so it is typed into");
        Assert.IsTrue(labels[0].Words!.Glyphs.Width > 0);
    });

    [TestMethod]
    public void AnIconIsDrawnWhereANodeNamesOne_OrIconsAreShown_StandingForTheNode() => UiThread.Run(() =>
    {
        Assert.AreEqual(0, Pieces(Lay("treeView-beta\nsrc/\n    a.ts"), TreeViewPiece.Icon).Count, "icons are not shown unless asked for");

        const string shown = "---\nconfig:\n  treeView:\n    showIcons: true\n---\ntreeView-beta\nsrc/\n    a.ts icon(none)\n    b.ts";
        var icons = Pieces(Lay(shown), TreeViewPiece.Icon);

        Assert.AreEqual(2, icons.Count, "a folder's and a file's, but none where a node says so");
        Assert.IsTrue(icons.All(icon => icon.Part is not null && !icon.Words!.Writes), "pressed as its node, never typed into");
    });

    [TestMethod]
    public void APacksIconIsDrawnAsItsOutlines_StandingForItsNode_ABareNameInTheDefaultPack() => UiThread.Run(() =>
    {
        const string source = "---\nconfig:\n  treeView:\n    showIcons: true\n    defaultIconPack: material-icon-theme\n    extensionIcons:\n      .rs: rust\n---\n"
                              + "treeView-beta\nsrc/\n    main.rs\n    App.tsx icon(material-icon-theme:react-ts)\n    notes.txt";
        var icons = Pieces(Lay(source), TreeViewPiece.Icon);
        var drawn = icons.Where(icon => icon.Words is null).ToList();

        Assert.AreEqual(4, icons.Count);
        Assert.AreEqual(2, drawn.Count, "main.rs and App.tsx are drawn by the pack, src and notes.txt as a folder and a file");
        Assert.IsTrue(drawn.All(icon => icon.Part is not null && icon.Marks.ToArray().OfType<GeometryMark>().Any()), "outlines, standing for the node");
    });

    [TestMethod]
    public void APacksGradientsAreMeasuredWhereItsOutlinesAre() => UiThread.Run(() =>
    {
        var brushes = Pieces(Lay("treeView-beta\nsrc/ icon(material-icon-theme:aurelia)\na.vfl icon(material-icon-theme:vfl)"), TreeViewPiece.Icon)
            .SelectMany(icon => icon.Marks.ToArray().OfType<GeometryMark>())
            .Select(mark => (mark.Fill as WpfInk)?.Brush)
            .OfType<GradientBrush>()
            .ToList();

        Assert.IsTrue(brushes.OfType<LinearGradientBrush>().Any(), "aurelia is graded along lines");
        Assert.IsTrue(brushes.OfType<RadialGradientBrush>().Any(), "vfl is graded out from a point");
        Assert.IsTrue(brushes.All(brush => brush.MappingMode == BrushMappingMode.Absolute), "in the outline's own coordinates, not its box");
    });

    [TestMethod]
    public void AFilesIconIsMappedByItsNameOrItsLastExtension_InLowerCase_AndAFoldersNever() => UiThread.Run(() =>
    {
        const string source = "---\nconfig:\n  treeView:\n    showIcons: true\n    defaultIconPack: material-icon-theme\n    filenameIcons:\n      .env: tune\n"
                              + "    extensionIcons:\n      md: markdown\n      .ts: typescript\n---\ntreeView-beta\nlib.ts/\n    README.MD\n    app.test.ts\n    .env\n    .ts";
        var icons = Pieces(Lay(source), TreeViewPiece.Icon);

        CollectionAssert.AreEqual(new[] { true, false, false, false, true }, icons.Select(icon => icon.Words is not null).ToArray(),
                                  "the folder and a file named only by a dot keep Mermaid's own icons; the rest are the pack's");
    });
}
