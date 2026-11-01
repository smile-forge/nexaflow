using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// The heading resolution behind the Markdown tab's scroll-to-heading deep link: a snaplink carries a '&gt;'-joined heading path, and
/// the outline of the document laid finds the heading that path names (<see cref="ContentEngine.Titled"/>). Matching is on the whole
/// ancestor hierarchy, so repeated heading names under different parents stay distinct — the case a document-wide text match gets wrong.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("markdown-heading-scroll")]
public class MarkdownHeadingPathTests
{
    private const string Doc =
        "# Guide\n\nIntro text.\n\n## Setup\n\nInstall it.\n\n### Overview\n\nSetup overview.\n\n## Usage\n\n### Overview\n\nUsage overview.\n";

    [TestMethod]
    public void FindsATopLevelHeading() => UiThread.Run(() => Assert.AreEqual("# Guide", Found(Doc, ["Guide"])));

    [TestMethod]
    public void FindsANestedHeading_ByItsFullPath() => UiThread.Run(() => Assert.AreEqual("## Setup", Found(Doc, ["Guide", "Setup"])));

    [TestMethod]
    public void DuplicateHeadingNames_ResolveByTheirParent() => UiThread.Run(() =>
    {
        var engine = Laid(Doc);
        var underSetup = engine.Titled(["Guide", "Setup", "Overview"])!.Value.Start;
        var underUsage = engine.Titled(["Guide", "Usage", "Overview"])!.Value.Start;

        Assert.AreEqual(Doc.IndexOf("### Overview"), underSetup);
        Assert.AreEqual(Doc.LastIndexOf("### Overview"), underUsage);
    });

    [TestMethod]
    public void MatchIsCaseAndWhitespaceInsensitive() => UiThread.Run(() =>
        Assert.AreEqual(Found(Doc, ["Guide", "Setup"]), Found(Doc, [" guide ", "SETUP"])));

    [TestMethod]
    public void PartialPath_DoesNotMatch() => UiThread.Run(() =>
        Assert.IsNull(Found(Doc, ["Setup"]), "A leaf name alone must not match a heading that has ancestors."));

    [TestMethod]
    public void UnknownHeading_IsNowhere() => UiThread.Run(() => Assert.IsNull(Found(Doc, ["Guide", "Nope"])));

    [TestMethod]
    public void EmptyPath_IsNowhere() => UiThread.Run(() => Assert.IsNull(Found(Doc, [])));

    [TestMethod]
    public void ClosedAtxHeading_IsMatchedOnItsText() => UiThread.Run(() =>
        Assert.AreEqual("## Closed ##", Found("## Closed ##\n\nbody\n", ["Closed"])));

    private static ContentEngine Laid(string source) => Laying.LaidBy(null, source, 480).Engine;

    private static string? Found(string source, IReadOnlyList<string> titles) =>
        Laid(source).Titled(titles) is { } at ? source.Substring(at.Start, at.Length).TrimEnd('\r', '\n') : null;
}
