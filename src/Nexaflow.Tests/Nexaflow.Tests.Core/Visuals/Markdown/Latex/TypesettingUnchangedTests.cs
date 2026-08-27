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

    /// <summary>The shape of each construct formula's layout, as it has been set since the engine was ingested.</summary>
    private static readonly Dictionary<string, string> Settled = new(StringComparer.Ordinal)
    {
        ["fractions and binomials"] = "093C8B671663EC23",
        ["roots, bars and boxes"] = "1B5987CEEF3F4B4D",
        ["scripts, primes and big operators"] = "16B46332D2DCEDC7",
        ["accents and arrows"] = "BB786D2EE93EE6C5",
        ["fences and delimiters"] = "3CDEE57B6DB62DBD",
        ["matrices and environments"] = "5D10C08247631C88",
        ["aligned and gathered blocks"] = "05D2446EFFA9FD4F",
        ["stacked and gathered"] = "8FFDDCED224E1EBA",
        ["text styles and fonts"] = "26D26A2A43BAEA3C",
        ["spacing, dots and modular arithmetic"] = "B569C2F93B07F9F3",
        ["colour, phantoms and overlap"] = "2E801F2B5D97DD23",
        ["styles and sizes"] = "EDE1860CA4A4B90C",
        ["greek, relations and symbols"] = "5055B9A7A962A131",
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
    /// </summary>
    private static string Shape(LatexLayout layout)
    {
        var text = new StringBuilder();
        text.Append(Number(layout.Size.Width)).Append('x').Append(Number(layout.Size.Height)).Append('\n');

        foreach (var node in layout.Tree.Root.SelfAndDescendants())
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
