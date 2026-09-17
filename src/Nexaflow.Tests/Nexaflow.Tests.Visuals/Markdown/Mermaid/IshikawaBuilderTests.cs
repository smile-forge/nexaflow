using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Ishikawa;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// An <c>ishikawa</c> block drawn on the shared layout tree: the head and the spine standing for the event, a bone for every
/// cause standing for its line, and what each says typed into where it is drawn.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("ishikawa")]
public class IshikawaBuilderTests : MermaidBuilderContract
{
    private const string Photo =
        "ishikawa-beta\n  Blurry Photo\n  Process\n    Out of focus\n    Shutter speed too slow\n  User\n    Shaky hands\n"
        + "  Equipment\n    LENS\n      Dirty lens\n      Damaged lens\n    SENSOR\n      Dirty sensor\n  Environment\n    Too dark";

    public override MermaidDiagram Diagram => MermaidDiagram.Ishikawa;

    protected override IEnumerable<(string What, string Source)> Drawn =>
    [
        ("the documented photo", Photo),
        ("causes four deep", "ishikawa\n  Late\n  People\n    Staff\n      Sick\n        Flu\n          Winter"),
        ("the event on the header line, and an odd count of causes", "ishikawa Problem\n  A\n  B\n  C"),
        ("only the event", "ishikawa-beta\n  Problem"),
        ("config and theme of its own",
            "---\ntitle: Photos\nconfig:\n  fontSize: 16\n  ishikawa:\n    diagramPadding: 30\n  themeVariables:\n    lineColor: \"#ff0000\"\n    mainBkg: \"#202020\"\n    textColor: \"#ffffff\"\n---\nishikawa\n  Problem\n  Cause\n    Sub"),
        ("nothing to draw", "ishikawa-beta"),
    ];

    private static Laid Build(string source, double room = 700) =>
        IshikawaBuilder.Build(EditState.For(source), MarkdownPalette.Dark, 1.0, room);

    private static List<Piece> Pieces(Laid laid, string kind) => [.. laid.Root.SelfAndDescendants().Where(piece => piece.Kind == kind)];

    private static string Written(string source, ISourcePart? part) => part is null ? "" : source.Substring(part.Start, part.Length);

    private static Point Middle(Rect rect) => new(rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2));

    [TestMethod]
    public void EveryCauseHasABoneStandingForItsLine_AndTheEventTheHeadAndTheSpine() => UiThread.Run(() =>
    {
        var laid = Build(Photo);

        Assert.AreEqual("Blurry Photo", Written(Photo, Pieces(laid, IshikawaPiece.Head).Single().Part));
        Assert.AreEqual("Blurry Photo", Written(Photo, Pieces(laid, IshikawaPiece.Spine).Single().Part));
        CollectionAssert.AreEquivalent(
            new[] { "Process", "Out of focus", "Shutter speed too slow", "User", "Shaky hands", "Equipment", "LENS", "Dirty lens", "Damaged lens", "SENSOR", "Dirty sensor", "Environment", "Too dark" },
            Pieces(laid, IshikawaPiece.Bone).Select(bone => Written(Photo, bone.Part)).ToArray());
    });

    [TestMethod]
    public void TheEventsCausesAreBoxed_TheRestAreWords_AndAllAreTheCharactersWritten() => UiThread.Run(() =>
    {
        var laid = Build(Photo);
        var labels = Pieces(laid, IshikawaPiece.Label);

        CollectionAssert.AreEqual(new[] { "Process", "User", "Equipment", "Environment" }, Pieces(laid, IshikawaPiece.Cause).Select(cause => Written(Photo, cause.Part)).ToArray());
        Assert.IsTrue(labels.All(label => label.Words is { Maps: true }), "every cause typed into where it is drawn");

        // A long cause is wrapped, as Mermaid wraps one, so it is drawn as more than one line — each line its own characters.
        var said = string.Concat(labels.Select(label => Written(Photo, label.Part)));
        foreach (var cause in new[] { "Out of focus", "Shutter speed too slow", "Shaky hands", "LENS", "Dirty lens", "Damaged lens", "SENSOR", "Dirty sensor", "Too dark" })
            StringAssert.Contains(said.Replace(" ", ""), cause.Replace(" ", ""), cause);

        Assert.IsTrue(labels.Count > 9, "the longest cause takes more than one line");
    });

    [TestMethod]
    public void TheHeadIsRightOfTheSpine_AndTheCausesTakeTurnsAboveAndBelowIt() => UiThread.Run(() =>
    {
        var laid = Build(Photo);
        var spine = Pieces(laid, IshikawaPiece.Spine).Single().Bounds;
        var head = Pieces(laid, IshikawaPiece.Head).Single().Bounds;
        var causes = Pieces(laid, IshikawaPiece.Cause).Select(cause => Middle(cause.Bounds)).ToList();
        var spineAt = Middle(spine).Y;

        Assert.IsTrue(head.Left >= spine.Right - DiagramConnector.Reach, "the head at the spine's end");
        Assert.IsTrue(causes[0].Y < spineAt && causes[2].Y < spineAt, "the first and third above");
        Assert.IsTrue(causes[1].Y > spineAt && causes[3].Y > spineAt, "the second and fourth below");
        Assert.IsTrue(causes[2].X < causes[0].X, "each pair further from the head");
    });

    [TestMethod]
    public void TheFrontMattersPaddingIsRoomRoundTheDiagram() => UiThread.Run(() =>
    {
        const string plain = "ishikawa\n  Problem\n  Cause";
        var padded = "---\nconfig:\n  ishikawa:\n    diagramPadding: 30\n---\n" + plain;

        var (narrow, wide) = (Pieces(Build(plain), MermaidPiece.Diagram).Single().Bounds, Pieces(Build(padded), MermaidPiece.Diagram).Single().Bounds);

        Assert.AreEqual(narrow.Width + 60, wide.Width, 0.5);
        Assert.AreEqual(narrow.Height + 60, wide.Height, 0.5);
    });
}
