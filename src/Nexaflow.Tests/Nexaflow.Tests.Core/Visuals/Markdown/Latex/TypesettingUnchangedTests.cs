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
/// layout, what it was drawn from, and the rectangle it occupies. Any change to a fraction's shift, a
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
        ["fractions and binomials"] = "1C1C8EF04E8AB6F0",
        ["roots, bars and boxes"] = "19590174145125A1",
        ["scripts, primes and big operators"] = "F4DCCC3E66FE35FE",
        ["accents and arrows"] = "3542C09C74C54D4D",
        ["fences and delimiters"] = "7F9DE42101D0AE78",
        ["matrices and environments"] = "7768C22F121BB5A9",
        ["aligned and gathered blocks"] = "0A1C3A5EBCD41D43",
        ["stacked and gathered"] = "8ECEA82579436184",
        ["text styles and fonts"] = "6F56D776D9B7AFB6",
        ["spacing, dots and modular arithmetic"] = "F2E148340CFDB5A9",
        ["colour, phantoms and overlap"] = "5C35839D9F1D6437",
        ["styles and sizes"] = "C1E2BD1BFDD30C70",
        ["greek, relations and symbols"] = "751EDD424A26DB49",
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

    /// <summary>Everything the layout decided, as one string: what each piece is, where it came from, and where it went.</summary>
    private static string Shape(LatexLayout layout)
    {
        var text = new StringBuilder();
        text.Append(Number(layout.Size.Width)).Append('x').Append(Number(layout.Size.Height)).Append('\n');

        foreach (var node in layout.Tree.Root.SelfAndDescendants())
            text.Append(node.Kind).Append(' ')
                .Append(node.SourceStart).Append('+').Append(node.SourceLength).Append(' ')
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
