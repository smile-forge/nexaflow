using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Nexaflow.Tests.Features.Fixtures;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Latex;

namespace Nexaflow.Tests.Core.Visuals.Markdown.Latex;

/// <summary>
/// That the typesetting engine still sets things where it used to.
///
/// <para>
/// This replaces something. The engine was a library with 762 approval tests of its own, which caught
/// every accidental change to the shape of what it built; ingesting the engine and not its test project
/// would have left the largest and most intricate part of the maths stack with nothing watching it. The
/// approvals asserted a serialized object graph, which was the right instrument while we were sending
/// changes upstream and is not what matters here.
/// </para>
/// <para>
/// What matters here is where things land. So this hashes the <em>geometry</em>: every piece of the
/// layout and the rectangle it occupies. Any change to a fraction's shift, a
/// script's position, the growth of a delimiter or a single glyph's width moves a hash and this fails
/// with the formula that moved.
/// </para>
/// <para>
/// Geometry rather than pixels on purpose. Rasterising depends on the machine — antialiasing, ClearType,
/// the display — and a golden test that fails on somebody else's laptop is worse than no test. Box
/// positions are arithmetic over font metrics: same input, same numbers, anywhere.
/// </para>
/// <para>
/// <b>When one of these changes.</b> A moved hash is either a bug or an improvement, and only a person
/// can say which. The failure prints the new value; look at the formula, decide, and paste it in.
/// </para>
///
/// Needs an STA thread for WPF's font machinery. It opens no window and takes no focus.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("latex-source-map")]
public class TypesettingUnchangedTests
{
    private const double Scale = 16;

    /// <summary>
    /// The shape of each construct formula's layout, as it has been set since the engine was ingested.
    /// <para>
    /// Rewritten once, when this stopped counting the boxes that hold things and started counting only
    /// the ones that draw something. Every hash moved and no formula did — which is the point of the
    /// change, and the reason the old numbers are not worth keeping beside the new ones.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string> Settled = new(StringComparer.Ordinal)
    {
        ["fractions and binomials"] = "1448FD4EF22417F5",
        ["roots, bars and boxes"] = "681D9E8E48FBA6D4",
        ["scripts, primes and big operators"] = "D74B5C0E5507CE0B",
        ["accents and arrows"] = "195E9AEA77EA1A0A",
        ["fences and delimiters"] = "EB24F7C32E81A8E7",
        ["matrices and environments"] = "AFE71FA801602FA1",
        ["aligned and gathered blocks"] = "BB2AC1E5F464D80A",
        ["stacked and gathered"] = "AEE4C548EF2E7028",
        ["text styles and fonts"] = "68DE1BB4CEFEF57F",
        ["spacing, dots and modular arithmetic"] = "66A99FC0BD7B6EB9",
        ["colour, phantoms and overlap"] = "E842EF1E1E46E2E6",
        ["styles and sizes"] = "CAE74BF57299F92F",
        ["greek, relations and symbols"] = "889A84AE4F160058",
    };

    [TestMethod]
    public void EveryConstructIsStillSetWhereItWas() => UiThread.Run(() =>
    {
        var moved = new List<string>();

        foreach (var (what, written) in LatexConstructs.Everything)
        {
            var layout = LatexLayout.Build(LatexConstructs.Flatten(written), Scale);
            Assert.IsNotNull(layout, $"{what} no longer typesets at all");

            var settled = Shape(layout);

            if (!Settled.TryGetValue(what, out var was))
            {
                moved.Add($"[\"{what}\"] = \"{settled}\",   // new");
                continue;
            }

            if (was != settled) moved.Add($"[\"{what}\"] = \"{settled}\",   // was {Short(was)}");
        }

        Assert.AreEqual(0, moved.Count,
            "the typesetting moved. Look at each formula, decide whether it moved for a good reason, "
            + "and paste these in:\n" + string.Join("\n", moved));
    });

    /// <summary>
    /// Where every piece of the layout went, as one string: what each piece is and the rectangle it
    /// occupies.
    /// <para>
    /// Where each piece was <em>named from</em> is deliberately not in here, though it used to be. The
    /// two are different claims and only one of them is this test's: a reading that names a construct
    /// differently has not moved anything on the page, and mixing them made a change of naming read as
    /// "the typesetting moved" — which is the one thing this exists to say. What each piece is named
    /// from is checked by the tests that use it, which is every selection and caret test there is.
    /// </para>
    /// <para>
    /// Nor are the containers, for exactly the same reason and learned the same way. A formula that
    /// starts being built from our own reading rather than the typesetter's keeps groups the parser
    /// collapsed, so it gains boxes that hold things without drawing any — and every one of those moved
    /// this hash while the page stayed pixel-identical. A reading that <em>nests</em> a construct
    /// differently has not moved anything either. So this counts the boxes that draw something, which is
    /// the same line the corpus sweep draws between a picture and a tree.
    /// </para>
    /// </summary>
    private static string Shape(LatexLayout layout)
    {
        var text = new StringBuilder();
        text.Append(Number(layout.Size.Width)).Append('x').Append(Number(layout.Size.Height)).Append('\n');

        foreach (var node in layout.Tree.Root.SelfAndDescendants().Where(node => node.Children.Count == 0))
            text.Append(node.Kind).Append(' ')
                .Append(Number(node.Bounds.X)).Append(',').Append(Number(node.Bounds.Y)).Append(' ')
                .Append(Number(node.Bounds.Width)).Append('x').Append(Number(node.Bounds.Height))
                .Append('\n');

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16];
    }

    /// <summary>
    /// To four places. The numbers are arithmetic over font metrics and come out identical run to run,
    /// but a hash has no tolerance at all and the last bit of a double is not worth a red build.
    /// </summary>
    private static string Number(double value) =>
        value.ToString("F4", CultureInfo.InvariantCulture);

    private static string Short(string hash) => hash.Length == 0 ? "nothing recorded" : hash;
}
