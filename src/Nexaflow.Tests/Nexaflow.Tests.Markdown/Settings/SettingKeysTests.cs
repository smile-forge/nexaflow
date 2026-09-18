using System.Linq;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Plot;
using Nexaflow.Markdown.Settings;
using Nexaflow.Markdown.WordCloud;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Settings;

/// <summary>
/// The guard that says a key a block accepts is a key that does something.
///
/// <para>
/// <strong>A setting's name is a promise.</strong> Naming a key puts it in the few a parser reads as a
/// setting rather than as data, so writing it neither draws anything nor says why — the one outcome
/// worse than refusing it. Ten of a correlation plot's keys were inert exactly this way, and nothing
/// noticed, because the tests that read settings were hand-written lists that simply did not mention
/// them.
/// </para>
/// <para>
/// So this is driven off <c>Keys</c> itself rather than off a list somebody remembered to extend: a key
/// added to a block is a key this asks about the next time it runs.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("a guard over every block that reads settings")]
public class SettingKeysTests
{
    /// <summary>
    /// Every key a plot accepts changes what a plot comes to.
    ///
    /// <para>
    /// Asked by reading the block twice — once with the key written and once without — and requiring
    /// the two to differ. A key that reads as its own default is exempt only by saying so here, and no
    /// key currently is.
    /// </para>
    /// </summary>
    [TestMethod]
    public void EveryKeyAPlotAcceptsDoesSomething()
    {
        var inert = new List<string>();

        foreach (var key in PlotSetting.Keys)
        {
            var written = PlotSays(key);
            if (written is null) continue;

            var plain = PlotReader.TrySettings(PlotParser.Parse("1 2\n3 4"),
                                               PlotFence.Scatter, out var bare, out _);

            var told = PlotReader.TrySettings(PlotParser.Parse($"{key}: {written}\n1 2\n3 4"),
                                              PlotFence.Scatter, out var set, out var error);

            if (!plain || !told)
            {
                inert.Add($"{key}: {written} — could not be read ({error})");
                continue;
            }

            if (Equals(bare, set)) inert.Add($"{key}: {written} — read, and changed nothing");
        }

        Assert.AreEqual(0, inert.Count,
                        "a key a block accepts is a key that does something:\n  " + string.Join("\n  ", inert));
    }

    /// <summary>The same for a word cloud, so the next language inherits the guard rather than the hole.</summary>
    [TestMethod]
    public void EveryKeyAWordCloudAcceptsDoesSomething()
    {
        var inert = new List<string>();

        foreach (var key in WordCloudSetting.Keys)
        {
            var written = CloudSays(key);
            if (written is null) continue;

            if (!WordCloudReader.TryRead(ContentPart.Of(WordCloudParser.Parse("WPF: 40\nXAML: 25")),
                                         out var bare, out _))
                continue;

            if (!WordCloudReader.TryRead(
                    ContentPart.Of(WordCloudParser.Parse($"{key}: {written}\nWPF: 40\nXAML: 25")),
                    out var set, out var error))
            {
                inert.Add($"{key}: {written} — could not be read ({error})");
                continue;
            }

            if (Equals(bare!.Settings, set!.Settings))
                inert.Add($"{key}: {written} — read, and changed nothing");
        }

        Assert.AreEqual(0, inert.Count,
                        "a key a block accepts is a key that does something:\n  " + string.Join("\n  ", inert));
    }

    [TestMethod]
    public void AKeyIsMatchedWhateverItsCaseOrHyphens()
    {
        Assert.AreEqual("xscale", SettingKeys.Plain("x-Scale"));
        Assert.IsTrue(SettingKeys.Is("MIN-SIZE", ["minSize"]));
        Assert.IsFalse(SettingKeys.Is("sunburst", ["minSize"]));
    }

    /// <summary>
    /// Something a plot's key plainly takes, different from what it takes by default — enough to tell the
    /// two readings apart, and nothing more clever than that so the guard stays readable when a key is
    /// added.
    /// </summary>
    private static string? PlotSays(string key) => key switch
    {
        "geom" => "tile",
        "contour" => "lines",
        "method" => "spearman",
        "fit" => "lm",
        "legend" => "bottom",
        "grid" => "none",
        "shape" => "triangle",
        "xScale" or "yScale" => "log",
        "gradient" => "magma",
        "palette" => "#ff0000 #00ff00",
        "stats" => "r n",
        "xLimits" or "yLimits" or "fillLimits" => "0 10",
        "sizeRange" => "3 9",
        "alphaRange" => "0.3 0.9",
        "xBreaks" or "yBreaks" => "0 5 10",
        "bandwidth" => "2 3",
        "bins" => "12",
        "levels" => "12",
        "midpoint" => "0",

        // These are false by default, so only saying yes tells the two readings apart.
        "header" or "labels" or "points" or "flip" => "true",
        "se" => "false",

        "level" => "0.8",
        "adjust" or "aspect" => "1.5",
        "jitter" => "0.4",
        "width" or "height" => "120",

        // Everything else names a column or is free text, and either way saying it changes the reading.
        _ => "thing",
    };

    /// <summary>The same for a word cloud, whose keys are its own — its <c>fit</c> is a flag, not a line.</summary>
    private static string? CloudSays(string key) => key switch
    {
        "shape" => "star",
        "scale" => "log",
        "color" or "colour" => "#ff0000 #00ff00",
        "background" => "#101010",
        "letters" => "ABC",

        "bold" or "shuffle" or "fit" => "false",

        "ellipticity" => "0.4",
        "rotate" => "0.4",
        "gridSize" => "8",
        "gap" => "12",
        "minSize" or "maxSize" => "22",
        "minRotation" or "maxRotation" => "30",
        "rotationSteps" => "4",
        "width" or "height" or "seed" => "120",

        // A picture that is not there stops the block, which is a different test's business.
        "mask" => null,

        _ => "thing",
    };
}
