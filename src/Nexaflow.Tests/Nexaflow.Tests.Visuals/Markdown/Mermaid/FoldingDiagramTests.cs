using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Flowchart;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

using System;
using Nexaflow.Markdown.Binding;
using System.Threading.Tasks;
using System.Windows.Threading;
using Nexaflow.Visuals.Text.Markdown.Prose;
using System.Text;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// The drawing half of nodes that fold: what is past the frontier is never drawn, what holds it carries a chip, and
/// the chip is a thing to press of its own — so a node whose body means something else goes on meaning it.
///
/// <para>
/// What a chip <em>means</em> is decided WPF-free in <c>DiagramExpansionTests</c>; this is that, drawn and pressed, in a
/// document — where a press is answered, and where what the host said about diagrams is heard.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("graph-expandable-nodes")]
public class FoldingDiagramTests
{
    private const string Src =
        """
        ---
        config:
          nexaflow:
            defaultExpansion: 1
        ---
        graph TD
          root["Root"] --> child["Child"]
          child --> hidden["Hidden"]
          click root "https://example.com/root"
        """;

    /// <summary>The diagram fenced in a document, as a reader is shown it, measured and arranged.</summary>
    private static MarkdownSurface Shown(string diagram, Action<MarkdownSurface>? host = null)
    {
        var surface = new MarkdownSurface();
        host?.Invoke(surface);
        surface.Markdown = "```mermaid\n" + diagram.TrimEnd('\n') + "\n```\n";

        return Settled(surface);
    }

    /// <summary>
    /// The page measured, arranged and settled. Pumped first: a producer answering a press says so on whatever thread
    /// it answered on, and the element moves that onto this one — so without pumping, the page is read back before
    /// anything a press set in motion has reached it.
    /// </summary>
    private static MarkdownSurface Settled(MarkdownSurface surface)
    {
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);

        surface.Measure(new Size(900, 900));
        surface.Arrange(new Rect(0, 0, 900, 900));
        surface.UpdateLayout();
        return surface;
    }

    /// <summary>Every word drawn anywhere in it.</summary>
    private static List<string> Words(MarkdownSurface surface) =>
        [.. surface.Shown.Laid.Root.SelfAndDescendants()
                   .Select(piece => piece.Words?.Glyphs.Text)
                   .OfType<string>()];

    /// <summary>Where each piece of a kind was placed, in the order they were drawn.</summary>
    private static List<Rect> Placed(MarkdownSurface surface, string kind) =>
        [.. surface.Shown.Laid.Tree.Root.Placed().Where(at => at.Piece.Kind == kind).Select(at => at.Where)];

    private static Point Middle(Rect where) => new(where.X + (where.Width / 2), where.Y + (where.Height / 2));

    /// <summary>A press at the middle of <paramref name="where"/>, and the page as it then stands.</summary>
    private static void Press(MarkdownSurface surface, Rect where)
    {
        surface.Shown.BeginPointerSelect(Middle(where));
        Settled(surface);
    }

    [TestMethod]
    public void AnOrdinaryFlowchartDrawsNoChips() => UiThread.Run(() =>
    {
        var surface = Shown("graph TD\n  a[\"A\"] --> b[\"B\"]\n");

        Assert.AreEqual(0, Placed(surface, MermaidPiece.Chip).Count,
                        "a diagram that never mentions folding must grow no chips");
    });

    [TestMethod]
    public void WhatIsPastTheFrontierIsNotDrawnAndWhatHoldsItCarriesAChip() => UiThread.Run(() =>
    {
        var surface = Shown(Src);
        var words = Words(surface);

        Assert.IsTrue(words.Contains("Root") && words.Contains("Child"), "one level down is inside the frontier");
        Assert.IsFalse(words.Contains("Hidden"), "and the grandchild is past it");

        Assert.AreEqual(2, Placed(surface, MermaidPiece.Chip).Count,
                        "the open root and the folded child each say so");
        Assert.IsTrue(words.Contains("+1"), "and the folded one says how much is behind it");
    });

    [TestMethod]
    public void PickingOutASuppliedNodeSaysWhichNode() => UiThread.Run(() =>
    {
        var surface = Shown(Src + "\n  {{More}}", host => host.DataSource = new ReflectionDataContext(new { More = "root --> other[\"Other\"]\n" }));

        ContentSelectionChange? told = null;
        surface.Selected += (_, e) => told = e.Change;

        Press(surface, Placed(surface, FlowchartPiece.Node).First(where => Says(surface, where, "Other")));

        Assert.AreEqual("other", told?.Picked.Single().Id, "what a page picking a supplied node out hears is which node");
    });

    /// <summary>Whether the node drawn at <paramref name="where"/> says <paramref name="words"/>.</summary>
    private static bool Says(MarkdownSurface surface, Rect where, string words) =>
        surface.Shown.Laid.Tree.Root.Placed().Any(at => at.Piece.Words?.Glyphs.Text == words && where.Contains(at.Where));

    [TestMethod]
    public void WhatWasOpenedIsForgottenOnlyWhenTheHostSaysSo() => UiThread.Run(() =>
    {
        var surface = Shown(Src);

        Press(surface, Placed(surface, MermaidPiece.Chip)[1]);
        surface.ResetDiagramViews();
        Settled(surface);

        Assert.IsFalse(Words(surface).Contains("Hidden"), "the diagram is drawn as its source says again");
    });

    [TestMethod]
    public void AClickLineIsFollowedOnOnePressWithNoChipInSight() => UiThread.Run(() =>
    {
        var followed = new List<string>();
        var surface = Shown("graph TD\n  a[\"A\"] --> b[\"B\"]\n  click a \"https://example.com/a\"\n",
                            host => host.LinkNavigate += (_, e) => { followed.Add(e.Url); e.Handled = true; });

        Press(surface, Placed(surface, FlowchartPiece.Node).First());

        CollectionAssert.Contains(followed, "https://example.com/a",
                                  "a flowchart says where its nodes lead, and one press follows it");
    });

    [TestMethod]
    [CoversNode("diagram-bound-content")]
    public void AndTwoPressesOnOneSayTheSameNodeAndThatThereWereTwo() => UiThread.Run(() =>
    {
        // A node is not a link. The content says which node was pressed and how often, and nothing about what either
        // comes to: one press on a module of an import tree picks it out and fills the detail pane, two open it in a
        // tab of its own, and only the page showing it could know that.
        var surface = Grown(out _);

        ContentSelectionChange? picked = null;
        ContentSelectionChange? twice = null;
        surface.Selected      += (_, e) => picked = e.Change;
        surface.DoubleClicked += (_, e) => twice = e.Change;

        var node = Placed(surface, FlowchartPiece.Node).First(where => Says(surface, where, "lib.dll"));

        Press(surface, node);

        Assert.AreEqual("lib.dll", picked?.Picked.Single(pick => pick.Id is not null).Id, "one press picks it out");
        Assert.IsNull(twice, "and one press is not two");

        surface.Shown.PointerDoubleClick(Middle(node));
        Settled(surface);

        Assert.AreEqual("lib.dll", twice?.Picked.Single(pick => pick.Id is not null).Id,
                        "two presses say the same node, and say that there were two");
    });

    [TestMethod]
    public void RightClickingOffersWhatThatOneThingCanDo() => UiThread.Run(() =>
    {
        var surface = Shown(Src);

        // The root's body leads somewhere, and says so rather than offering to fold anything.
        var body = Verbs(surface, Placed(surface, FlowchartPiece.Node).First());
        CollectionAssert.Contains(body, LayoutVerbs.Navigate);
        CollectionAssert.DoesNotContain(body, LayoutVerbs.Expand);

        // The folded child's chip offers what its press means, and nothing the node beneath it means.
        var chip = Verbs(surface, Placed(surface, MermaidPiece.Chip)[1]);
        CollectionAssert.Contains(chip, LayoutVerbs.Expand);
        CollectionAssert.DoesNotContain(chip, LayoutVerbs.Navigate);
    });

    [TestMethod]
    public void SomethingThatMeansNothingOffersNothingOfItsOwn() => UiThread.Run(() =>
    {
        var surface = Shown("graph TD\n  a[\"A\"] --> b[\"B\"]\n");

        var offered = Verbs(surface, Placed(surface, FlowchartPiece.Node).First());

        CollectionAssert.DoesNotContain(offered, LayoutVerbs.Navigate, "an ordinary node answers to nothing");
        CollectionAssert.DoesNotContain(offered, LayoutVerbs.Expand, "so only the document's own menu is offered");
    });

    /// <summary>What the menu offers where <paramref name="where"/> was pressed.</summary>
    private static List<string> Verbs(MarkdownSurface surface, Rect where) =>
        surface.Shown.BuildRibbon(Middle(where)) is DiagramRibbon ribbon
            ? [.. ribbon.Offers.Select(offer => offer.Verb)]
            : [];

    // ── Too many children ───────────────────────────────────────────────────

    /// <summary>A root with <paramref name="width"/> children, and a cap of three drawn at once.</summary>
    private static string Wide(int width)
    {
        var said = new System.Text.StringBuilder("---\nconfig:\n  nexaflow:\n    maxFanOut: 3\n---\ngraph TD\n");
        for (var at = 0; at < width; at++) said.Append($"  root[\"Root\"] --> c{at}[\"Child {at}\"]\n");
        return said.ToString();
    }

    [TestMethod]
    public void TooManyChildrenDrawTheFirstOfThemAndOneNodeOfferingTheRest() => UiThread.Run(() =>
    {
        var surface = Shown(Wide(10));
        var words = Words(surface);

        Assert.IsTrue(words.Contains("Child 0") && words.Contains("Child 2"), "the first three stay on the page");
        Assert.IsFalse(words.Contains("Child 3"), "and the rest are held back");

        Assert.AreEqual(1, Placed(surface, MermaidPiece.More).Count, "one node offers them, however many there are");
        CollectionAssert.Contains(words, "+7 more");
    });

    [TestMethod]
    public void ASetOfChildrenWithinTheCapDrawsNoSuchNode() => UiThread.Run(() =>
    {
        var surface = Shown(Wide(3));

        Assert.AreEqual(0, Placed(surface, MermaidPiece.More).Count);
        CollectionAssert.Contains(Words(surface), "Child 2");
    });

    [TestMethod]
    public void ItIsDrawnAndNobodyWroteIt() => UiThread.Run(() =>
    {
        var surface = Shown(Wide(10));
        var offering = surface.Shown.Laid.Root.SelfAndDescendants().First(piece => piece.Kind == MermaidPiece.More);

        Assert.IsNull(offering.Part, "it stands for no part of the source, because there is none to stand for");
        Assert.AreEqual(LayoutVerbs.Expand, offering.Acts?.Click?.Verb, "and a press on it means what every chip means");
    });

    // ── Pressing one ────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("graph-expandable-nodes")]
    public void PressingAChipShowsWhatIsFoldedBehindIt() => UiThread.Run(() =>
    {
        var surface = Shown(Src);
        Assert.IsFalse(Words(surface).Contains("Hidden"), "it starts folded, or there is nothing to open");

        Press(surface, Placed(surface, MermaidPiece.Chip)[1]);

        Assert.IsTrue(Words(surface).Contains("Hidden"),
                      "a diagram nobody supplied opens the node out of its own source");
    });

    [TestMethod]
    [CoversNode("graph-expandable-nodes")]
    public void AndPressingItAgainFoldsItAway() => UiThread.Run(() =>
    {
        var surface = Shown(Src);

        Press(surface, Placed(surface, MermaidPiece.Chip)[1]);
        Assert.IsTrue(Words(surface).Contains("Hidden"));

        Press(surface, Placed(surface, MermaidPiece.Chip)[1]);

        Assert.IsFalse(Words(surface).Contains("Hidden"), "the same chip closes what it opened");
    });

    [TestMethod]
    [CoversNode("graph-expandable-nodes")]
    public void AndEverythingElseOnThePageIsLeftWhereItWas() => UiThread.Run(() =>
    {
        var surface = new MarkdownSurface { Markdown = "Before it.\n\n```mermaid\n" + Src + "\n```\n\nAfter it.\n" };
        Settled(surface);

        var before = Placed(surface, MarkdownPieces.Whole)[0];

        Press(surface, Placed(surface, MermaidPiece.Chip)[1]);

        Assert.IsTrue(Words(surface).Contains("Hidden"), "the diagram opened");
        Assert.AreEqual(before, Placed(surface, MarkdownPieces.Whole)[0],
                        "and the paragraph above it was not laid out again");
    });

    // ── Pressing one where something supplied the diagram ───────────────────

    /// <summary>
    /// A producer that grows its graph a node at a time, as the PE inspector's import walk does: the lines of the
    /// diagram and the front matter saying which of them hold a subtree the lines do not carry, both written again
    /// whenever one is opened — so every id moves, and only the names it gave stay still.
    /// </summary>
    private sealed class Walker : IBoundContent
    {
        /// <summary>What is behind each module, under the name this producer knows it by.</summary>
        private static readonly Dictionary<string, string[]> Behind = new(StringComparer.Ordinal)
        {
            ["app.exe"] = ["lib.dll", "other.dll", "third.dll", "fourth.dll"],
            ["lib.dll"] = ["deep.dll"],
        };

        private const string Root = "app.exe";

        /// <summary>How many of a module's imports are drawn before the rest go behind one node offering them.</summary>
        private const int FanOut = 2;

        private readonly HashSet<string> _opened = new(StringComparer.Ordinal) { Root };

        /// <summary>Every key it was told of, in the order it was told — what a press actually hands a producer.</summary>
        public List<(string Key, bool Open)> Told { get; } = [];

        public string Text { get; private set; }

        public event EventHandler? Changed;

        public Walker() => Text = Written();

        public void Expand(string key, bool open)
        {
            Told.Add((key, open));

            if (!(open ? _opened.Add(key) : _opened.Remove(key))) return;

            Text = Written();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>The graph as far as it has been opened, said the way <c>DependencyMermaid</c> says one.</summary>
        private string Written()
        {
            var ids = new Dictionary<string, string>(StringComparer.Ordinal);
            var body = new StringBuilder();
            var folded = new List<string>();
            var open = new List<string>();

            string IdOf(string module) =>
                ids.TryGetValue(module, out var had) ? had : ids[module] = "n" + ids.Count;

            void Emit(string module)
            {
                var id = IdOf(module);
                body.Append("  ").Append(id).Append("[\"").Append(module).Append("\"]\n");

                // The root is always open and can never be closed, so it is offered no chip — the same exception the
                // import walk makes for the binary being inspected.
                if (!_opened.Contains(module) && Behind.ContainsKey(module)) folded.Add($"{id}: \"{module}\"");
                else if (_opened.Contains(module) && module != Root)         open.Add($"{id}: \"{module}\"");

                if (!_opened.Contains(module)) return;

                foreach (var child in Behind.GetValueOrDefault(module, []))
                {
                    body.Append("  ").Append(id).Append(" --> ").Append(IdOf(child)).Append('\n');
                    Emit(child);
                }
            }

            Emit(Root);

            var said = new StringBuilder($"---\nconfig:\n  nexaflow:\n    maxFanOut: {FanOut}\n");
            Section(said, "collapsed", folded);
            Section(said, "expanded", open);

            return said.Append("---\n").Append(body).ToString();
        }

        private static void Section(StringBuilder said, string name, List<string> lines)
        {
            if (lines.Count == 0) return;

            said.Append("    ").Append(name).Append(":\n");
            foreach (var line in lines) said.Append("      ").Append(line).Append('\n');
        }
    }

    /// <summary>The diagram one of those supplies the whole body of, as the PE inspector's dependency tab is.</summary>
    private static MarkdownSurface Grown(out Walker walker)
    {
        var walking = new Walker();
        walker = walking;

        return Shown("graph LR\n{{Imports}}", host => host.DataSource = new ReflectionDataContext(new { Imports = walking }));
    }

    /// <summary>What a producer was told, in order, so a test that is wrong about it says what it got.</summary>
    private static string Said(Walker walker) =>
        string.Join(", ", walker.Told.Select(one => $"{one.Key} {(one.Open ? "opened" : "folded")}"));

    [TestMethod]
    [CoversNode("graph-expandable-nodes")]
    public void AChipOnASuppliedDiagramTellsWhateverSuppliedIt() => UiThread.Run(() =>
    {
        var surface = Grown(out var walker);
        Assert.IsFalse(Words(surface).Contains("deep.dll"), "what is behind the module has not been fetched yet");

        Press(surface, Placed(surface, MermaidPiece.Chip).Single());

                Assert.AreEqual("lib.dll opened", Said(walker),
                                  "the producer is told the name it gave the module, never the diagram's own id");
        Assert.IsTrue(Words(surface).Contains("deep.dll"), "and what it then supplied is drawn");
    });

    [TestMethod]
    [CoversNode("graph-expandable-nodes")]
    public void AndTheNodeOfferingWhatIsLeftOverOpensWithoutTellingAnybody() => UiThread.Run(() =>
    {
        var surface = Grown(out var walker);
        Assert.IsFalse(Words(surface).Contains("fourth.dll"), "what is past the width it draws at is not drawn");

        Press(surface, Placed(surface, MermaidPiece.More).Single());

        Assert.IsTrue(Words(surface).Contains("fourth.dll"), "it opens out of what the diagram already holds");
        Assert.AreEqual(0, walker.Told.Count,
                        "nobody wrote that node, so no producer has a name for it and none is sent to fetch anything");
    });

    [TestMethod]
    [CoversNode("graph-expandable-nodes")]
    public void APressOnASuppliedNodeSaysTheNameItsProducerGaveIt() => UiThread.Run(() =>
    {
        var surface = Grown(out _);

        ContentSelectionChange? told = null;
        surface.Selected += (_, e) => told = e.Change;

        Press(surface, Placed(surface, FlowchartPiece.Node).First(where => Says(surface, where, "lib.dll")));

        Assert.AreEqual("lib.dll", told?.Picked.Single(pick => pick.Id is not null).Id,
                        "what the page hears is the producer's own name for the node, which is what it can resolve");
    });

    // ── Room ────────────────────────────────────────────────────────────────

    // ── Whose press it is ───────────────────────────────────────────────────

    /// <summary>A point inside the drawing that nothing drawn covers — the empty part of it.</summary>
    private static Point Nowhere(MarkdownSurface surface)
    {
        var drawn = surface.Shown.Laid.Tree.Root.Placed()
                           .Where(at => at.Piece.Region is not null || at.Piece.Words is not null)
                           .Select(at => at.Where).ToList();
        var size = surface.Shown.Laid.Size;

        for (var down = 3.0; down < size.Height; down += 4)
            for (var across = 3.0; across < size.Width; across += 4)
            {
                var at = new Point(across, down);
                if (!drawn.Any(where => where.Contains(at))) return at;
            }

        return default;
    }

    [TestMethod]
    [CoversNode("executable-dependency-viewport")]
    public void APressOnTheEmptyPartOfADrawingIsNotTheDrawings() => UiThread.Run(() =>
    {
        var surface = Grown(out _);

        ContentSelectionChange? told = null;
        surface.Selected += (_, e) => told = e.Change;

        var node = Placed(surface, FlowchartPiece.Node).First();
        Assert.IsTrue(surface.Shown.BeginPointerSelect(Middle(node), System.Windows.Input.ModifierKeys.None),
                      "a press on a node is the drawing's");
        surface.Shown.EndPointerSelect();
        Assert.IsNotNull(told, "and it says what was picked out");

        told = null;
        var nowhere = Nowhere(surface);
        Assert.AreNotEqual(default, nowhere, "there is somewhere in it nothing was drawn");

        Assert.IsFalse(surface.Shown.BeginPointerSelect(nowhere, System.Windows.Input.ModifierKeys.None),
                       $"a press at {nowhere} landed on nothing anybody drew, so it is not the drawing's");
        Assert.IsNull(told, "nothing is picked out by it, and nothing is said about a selection");
    });

    [TestMethod]
    [CoversNode("executable-dependency-viewport")]
    public void AndTheDrawingLetsGoOfAPressWhoseCaptureIsTakenAway() => UiThread.Run(() =>
    {
        // What left a selection following the pointer about: a host that decides the gesture is its own takes the
        // capture, so the button-up goes to it and never reaches the content.
        var surface = Grown(out _);
        var node = Placed(surface, FlowchartPiece.Node).First();

        Assert.IsTrue(surface.Shown.BeginPointerSelect(Middle(node), System.Windows.Input.ModifierKeys.None));

        surface.Shown.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0)
        {
            RoutedEvent = System.Windows.UIElement.LostMouseCaptureEvent,
        });

        ContentSelectionChange? after = null;
        surface.Selected += (_, e) => after = e.Change;
        surface.Shown.ExtendPointerSelect(new Point(Middle(node).X + 80, Middle(node).Y + 80));

        Assert.IsNull(after, "the press is over, so moving the pointer chooses nothing");
    });

    [TestMethod]
    [CoversNode("diagram-bound-content")]
    public void AndNothingIsCarriedAcrossADrawingNobodyMayWriteIn() => UiThread.Run(() =>
    {
        // What crashed the inspector. A drawing is only read, and most of this one was never written here at all: the
        // lines came from a binding. Picking out the header — which *is* written — and then pressing on that and
        // dragging over the supplied lines began a move, and the place it would be dropped is no offset of the source,
        // so spelling the move threw rather than coming to nothing.
        var surface = Grown(out _);
        var header = Placed(surface, MermaidPiece.Words).FirstOrDefault(where => Says(surface, where, "graph"));

        var written = surface.Shown.Source;

        surface.Shown.BeginPointerSelect(new Point(header.X + 1, header.Y + (header.Height / 2)),
                                         System.Windows.Input.ModifierKeys.None);
        surface.Shown.ExtendPointerSelect(new Point(header.Right - 1, header.Y + (header.Height / 2)));
        surface.Shown.EndPointerSelect();

        // Pressed on what was picked out, then carried out over the drawing.
        var over = Placed(surface, FlowchartPiece.Node).Last();
        surface.Shown.BeginPointerSelect(new Point(header.X + 2, header.Y + (header.Height / 2)),
                                         System.Windows.Input.ModifierKeys.None);
        surface.Shown.ExtendPointerSelect(Middle(over));
        surface.Shown.EndPointerSelect();
        Settled(surface);

        Assert.AreEqual(written, surface.Shown.Source, "not a character of it is written");
    });
}
