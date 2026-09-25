using Nexaflow.Markdown.Mermaid.Ishikawa;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.Ishikawa;

/// <summary>An <c>ishikawa</c> block read back into its event and the causes its indentation nests under it, and its front matter.</summary>
[TestClass]
[CoversNode("ishikawa-ast")]
public class IshikawaChartTests
{
    [TestMethod]
    public void TheDocumentedDiagramIsNestedByItsIndentation()
    {
        var effect = IshikawaChart.Of(MermaidStaged.Read(IshikawaGrammarTests.BlurryPhoto)).Effect!;

        Assert.AreEqual("Blurry Photo", effect.Says.Text);
        CollectionAssert.AreEqual(new[] { "Process", "User", "Equipment", "Environment" }, Said(effect.Causes));
        CollectionAssert.AreEqual(new[] { "LENS", "SENSOR" }, Said(effect.Causes[2].Causes));
        CollectionAssert.AreEqual(new[] { "Inappropriate lens", "Damaged lens", "Dirty lens" }, Said(effect.Causes[2].Causes[0].Causes));
        Assert.AreEqual(18, effect.Descendants);
    }

    [TestMethod]
    public void CausesStartAtTheFirstCausesIndentation_HoweverTheEventIsIndented()
    {
        foreach (var source in new[]
                 {
                     "ishikawa-beta\n    Blurry Photo\n        Process\n            Out of focus\n        User\n",
                     "ishikawa-beta\nProblem\nCause A\n  Subcause A1\nCause B\n",
                     "ishikawa-beta\n    Problem\nCause A\n  Subcause A1\nCause B\n",
                     "ishikawa-beta Problem\n  Cause A\n    Subcause A1\n  Cause B\n",
                 })
        {
            var effect = IshikawaChart.Of(MermaidStaged.Read(source)).Effect!;

            Assert.AreEqual(2, effect.Causes.Count, source);
            Assert.AreEqual(1, effect.Causes[0].Causes.Count, source);
            Assert.AreEqual(0, effect.Causes[1].Causes.Count, source);
        }
    }

    [TestMethod]
    public void ALineIndentedLessThanTheFirstCauseIsACauseOfTheEvent()
    {
        var effect = IshikawaChart.Of(MermaidStaged.Read("ishikawa\n  Problem\n    Cause A\n      Sub\n  Cause B")).Effect!;

        CollectionAssert.AreEqual(new[] { "Cause A", "Cause B" }, Said(effect.Causes));
    }

    [TestMethod]
    public void NothingWrittenIsNoEvent()
    {
        Assert.IsNull(IshikawaChart.Of(MermaidStaged.Read("ishikawa-beta")).Effect);
        Assert.AreEqual(0, IshikawaChart.Of(MermaidStaged.Read("ishikawa-beta\n  Problem")).Effect!.Causes.Count);
    }

    [TestMethod]
    public void TheFrontMatterIsRead()
    {
        var config = IshikawaChart.Of(MermaidStaged.Read("---\nconfig:\n  fontSize: 18\n  ishikawa:\n    diagramPadding: 40\n    useMaxWidth: true\n    singleBone: true\n  themeVariables:\n    lineColor: \"#ff0000\"\n    mainBkg: \"#00ff00\"\n    textColor: \"#0000ff\"\n---\nishikawa\n  Problem")).Config;

        Assert.AreEqual(new IshikawaConfig { DiagramPadding = 40, UseMaxWidth = true, SingleBone = true, FontSize = 18, LineColour = "#ff0000", Background = "#00ff00", TextColour = "#0000ff" }, config);
        Assert.AreEqual(IshikawaConfig.Default, IshikawaChart.Of(MermaidStaged.Read("ishikawa\n  Problem")).Config);
    }

    private static string[] Said(IReadOnlyList<IshikawaCause> causes) => [.. causes.Select(cause => cause.Says.Text)];
}
