using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// Writing in a structural C4 diagram where it is drawn: a card's label, a boundary's name, and what a relationship says.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("c4-diagram-writing")]
public class C4EditingTests : MermaidEditing
{
    /// <inheritdoc/>
    protected override string Source =>
        "C4Container\nPerson(customer, \"Banking Customer\")\n"
        + "System_Boundary(c1, \"Internet Banking\", \"System\") {\n  Container(spa, \"Single-Page App\", \"Angular\")\n}\n"
        + "Rel(customer, spa, \"Visits the site\", \"HTTPS\")";

    [TestMethod]
    public void TypingInALabelAndInWhatARelationshipSaysChangesThem() => UiThread.Run(() =>
        InADocument((editor, rtb, diagram) =>
        {
            Assert.IsFalse(diagram.IsReadOnly, "a C4 diagram's words are written in where the host takes edits");

            PressPast(diagram, "Banking Customer");
            Write(rtb, "s");
            PressPast(diagram, "Visits the site");
            Write(rtb, " daily");

            StringAssert.Contains(diagram.Source, "\"Banking Customers\"", diagram.Source);
            StringAssert.Contains(diagram.Source, "\"Visits the site daily\"", diagram.Source);
            Assert.AreEqual(0, diagram.Diagnostics.Count);
            StringAssert.Contains(editor.Markdown, "\"Banking Customers\"", "and so does the document");
        }));

    [TestMethod]
    public void TypingInABoundarysNameChangesIt() => UiThread.Run(() =>
        InADocument((editor, rtb, diagram) =>
        {
            PressPast(diagram, "Internet Banking");
            Write(rtb, " Ltd");

            StringAssert.Contains(diagram.Source, "\"Internet Banking Ltd\"", diagram.Source);
            Assert.AreEqual(0, diagram.Diagnostics.Count);
        }));

    /// <summary>
    /// A C4 element's own name is never drawn — its label is — so the drawing has nowhere to rename it from, and what is
    /// typed into a label is a label. Renaming an element is done in the source, where every macro naming it is in view.
    /// </summary>
    [TestMethod]
    public void WhatIsTypedIntoACardIsItsLabelAndNeverItsName() => UiThread.Run(() =>
        InADocument((editor, rtb, diagram) =>
        {
            PressPast(diagram, "Single-Page App");
            Write(rtb, "!");

            StringAssert.Contains(diagram.Source, "Container(spa, \"Single-Page App!\"", diagram.Source);
            StringAssert.Contains(diagram.Source, "Rel(customer, spa,", "the name every other macro uses is untouched");
        }));

    /// <summary>
    /// A quote would close the quotes an argument is written between, so it is written as the entity code Mermaid reads
    /// back as one — what is drawn is still the quote that was typed.
    /// </summary>
    [TestMethod]
    public void AQuoteTypedIntoAnArgumentIsWrittenAsItsEntityCode() => UiThread.Run(() =>
        InADocument((editor, rtb, diagram) =>
        {
            PressPast(diagram, "Visits the site");
            Write(rtb, "\"");

            StringAssert.Contains(diagram.Source, "\"Visits the site#quot;\"", diagram.Source);
            Assert.AreEqual(0, diagram.Diagnostics.Count);
        }));
}
