using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid;

/// <summary>
/// The links Mermaid writes between two nodes, read from the characters they are drawn as: what is one at all, whether it is the
/// whole of one or the opening of a labelled one, and what each draws.
/// </summary>
[TestClass]
[CoversNode("mermaid-diagram-kit")]
public class MermaidLinksTests
{
    [TestMethod]
    public void EveryLinkSaysWhatItDraws()
    {
        foreach (var (written, drawn) in new (string Written, MermaidLinks.Drawn)[]
                 {
                     ("-->", new MermaidLinks.Drawn(MermaidHead.None, MermaidHead.Arrow, MermaidLineStyle.Solid, 1)),
                     ("--->", new MermaidLinks.Drawn(MermaidHead.None, MermaidHead.Arrow, MermaidLineStyle.Solid, 2)),
                     ("----->", new MermaidLinks.Drawn(MermaidHead.None, MermaidHead.Arrow, MermaidLineStyle.Solid, 4)),
                     ("---", new MermaidLinks.Drawn(MermaidHead.None, MermaidHead.None, MermaidLineStyle.Solid, 1)),
                     ("----", new MermaidLinks.Drawn(MermaidHead.None, MermaidHead.None, MermaidLineStyle.Solid, 2)),
                     ("--o", new MermaidLinks.Drawn(MermaidHead.None, MermaidHead.Circle, MermaidLineStyle.Solid, 1)),
                     ("--x", new MermaidLinks.Drawn(MermaidHead.None, MermaidHead.Cross, MermaidLineStyle.Solid, 1)),
                     ("==>", new MermaidLinks.Drawn(MermaidHead.None, MermaidHead.Arrow, MermaidLineStyle.Thick, 1)),
                     ("====>", new MermaidLinks.Drawn(MermaidHead.None, MermaidHead.Arrow, MermaidLineStyle.Thick, 3)),
                     ("-.->", new MermaidLinks.Drawn(MermaidHead.None, MermaidHead.Arrow, MermaidLineStyle.Dotted, 1)),
                     ("-..->", new MermaidLinks.Drawn(MermaidHead.None, MermaidHead.Arrow, MermaidLineStyle.Dotted, 2)),
                     ("-.-", new MermaidLinks.Drawn(MermaidHead.None, MermaidHead.None, MermaidLineStyle.Dotted, 1)),
                     ("<-->", new MermaidLinks.Drawn(MermaidHead.Arrow, MermaidHead.Arrow, MermaidLineStyle.Solid, 1)),
                     ("o--o", new MermaidLinks.Drawn(MermaidHead.Circle, MermaidHead.Circle, MermaidLineStyle.Solid, 1)),
                     ("x--x", new MermaidLinks.Drawn(MermaidHead.Cross, MermaidHead.Cross, MermaidLineStyle.Solid, 1)),
                     ("~~~", new MermaidLinks.Drawn(MermaidHead.None, MermaidHead.None, MermaidLineStyle.Invisible, 1)),
                     ("", new MermaidLinks.Drawn(MermaidHead.None, MermaidHead.None, MermaidLineStyle.Solid, 1)),
                 })
        {
            Assert.AreEqual(drawn, MermaidLinks.Of(written), written.Length == 0 ? "nothing at all" : written);
        }
    }

    [TestMethod]
    public void AWholeLinkIsToldFromTheOpeningOfALabelledOne()
    {
        foreach (var (text, length) in new[] { ("-->", 3), ("---", 3), ("--x", 3), ("<-->", 4), ("-.->", 4), ("==>", 3), ("~~~", 3) })
        {
            var link = MermaidLinks.At(text, 0);

            Assert.IsNotNull(link, text);
            Assert.IsTrue(link!.Value.Whole, $"{text} is the whole of a link");
            Assert.AreEqual(length, link.Value.Length, text);
        }

        foreach (var (text, length) in new[] { ("-- yes -->", 2), ("== yes ==>", 2), ("-. yes .->", 2) })
        {
            var link = MermaidLinks.At(text, 0);

            Assert.IsNotNull(link, text);
            Assert.IsFalse(link!.Value.Whole, $"{text} opens a link whose label and closing are still to come");
            Assert.AreEqual(length, link.Value.Length, text);
        }
    }

    [TestMethod]
    public void WhatIsNoLinkIsNoLink()
    {
        foreach (var text in new[] { "", "a", "-", "=", ".", "~", "~~", "->", "x", "o", "<" })
            Assert.IsNull(MermaidLinks.At(text, 0), text.Length == 0 ? "nothing at all" : text);

        Assert.IsTrue(MermaidLinks.At(".-", 0)!.Value.Whole, "the dash before a dotted link's dots is Mermaid's to leave out");
    }

    [TestMethod]
    public void ALinkIsReadWhereverItIsWritten()
    {
        Assert.AreEqual(3, MermaidLinks.At("a-->b", 1)!.Value.Length);
        Assert.AreEqual(4, MermaidLinks.At("a <--> b", 2)!.Value.Length);
        Assert.IsNull(MermaidLinks.At("a-->b", 0), "an id is not a link, however close one is written to it");
    }
}
