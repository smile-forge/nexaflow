using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// Writing in a swimlane through the editor that hosts it: a lane's own name, in the strip at the near end of its band, is the
/// characters written on the <c>subgraph</c> line that opened it, and so is what the lane holds. A name turned a quarter turn — a
/// chart running across the page — takes a caret through the turn, which <c>MermaidBuilderContract</c> holds for every block drawn.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("swimlanes")]
public class SwimlaneEditingTests : MermaidEditing
{
    /// <inheritdoc/>
    protected override string Source =>
        "swimlane-beta TB\n  subgraph Sales\n    quote[\"Quote\"] -- sent --> won\n  end\n  subgraph Legal\n    review\n  end\n"
        + "  won --> review";

    [TestMethod]
    public void TypingInALanesNameAndInWhatItHoldsChangesThem() => UiThread.Run(() =>
        InADocument((editor, diagram) =>
        {
            Assert.IsFalse(diagram.IsReadOnly, "a swimlane's words are written in");

            PressPast(diagram, "Sales");
            Write(editor, " team");
            PressPast(diagram, "Quote");
            Write(editor, "s");
            PressPast(diagram, "sent");
            Write(editor, "!");
            PressPast(diagram, "review");
            Write(editor, "ed");

            StringAssert.Contains(diagram.Source, "subgraph Sales team", diagram.Source);
            StringAssert.Contains(diagram.Source, "quote[\"Quotes\"]", diagram.Source);
            StringAssert.Contains(diagram.Source, "-- sent! -->", diagram.Source);
            StringAssert.Contains(diagram.Source, "reviewed", diagram.Source);
            Assert.AreEqual(0, diagram.Diagnostics.Count);
            StringAssert.Contains(editor.Markdown, "Sales team", "and so does the document");
        }));
}
