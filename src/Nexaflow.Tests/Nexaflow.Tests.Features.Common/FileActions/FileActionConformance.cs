using Nexaflow.Features.Common;
using NSubstitute;

namespace Nexaflow.Tests.Features.FileActions;

/// <summary>
/// Contract every viewer-opening <see cref="IFileAction"/> must satisfy, whatever it opens. Derive a
/// concrete <c>[TestClass]</c> per action and the inherited tests run against it — so a new viewer action
/// cannot ship without being held to the same rules.
/// <para>
/// The rule these exist to enforce: <c>PerformAction(p)</c> and <c>PerformAction([p])</c> are the same user
/// intent. The file browser picks the overload from how many files are selected, not from what the user
/// meant, so an action whose two overloads disagree behaves differently depending on whether the click
/// landed on one file or on a one-item selection. Nothing else in the system can detect that.
/// </para>
/// <para>
/// The metadata half of the contract — <c>StaticExperienceId</c> is declared, and the experience is mapped
/// in the bundled file map — is checked by reflection over every feature assembly in
/// <c>FeatureTouchPointTests</c>, so it is deliberately not repeated here. What reflection cannot do is
/// <em>invoke</em> the action, which is the half that had drifted.
/// </para>
/// </summary>
public abstract class ViewerActionConformanceTests
{
    /// <summary>The action under test, wired to the recording shell the base supplies.</summary>
    protected abstract IFileAction CreateAction(IShellServices shell);

    /// <summary>The <c>PageKind</c> this action's tab is registered under.</summary>
    protected abstract string ExpectedPageKind { get; }

    /// <summary>A path this action handles. Only the extension matters — nothing here touches disk.</summary>
    protected abstract string AcceptableFile { get; }

    /// <summary>
    /// A path this action does not claim. The default carries an extension no feature maps, so an action
    /// that filters by type rejects it and one that does not opens it — either is a legitimate design, and
    /// the test asserts only that both overloads make the <em>same</em> choice. Override for an action whose
    /// experience is the file map's catch-all (the hex editor opens literally anything).
    /// </summary>
    protected virtual string FileThisActionDoesNotHandle => @"C:\probe\unclaimed.qqzz";

    /// <summary>One recorded <c>OpenTab</c> call, rendered so two runs can be compared directly.</summary>
    private sealed record Opened(string PageKind, string Parameters)
    {
        public override string ToString() => $"{PageKind}({Parameters})";
    }

    private (IFileAction Action, List<Opened> Tabs) Arrange()
    {
        var tabs  = new List<Opened>();
        var shell = Substitute.For<IShellServices>();
        shell.When(s => s.OpenTab(Arg.Any<string>(), Arg.Any<Dictionary<string, string>?>(),
                                  Arg.Any<IPageView?>(), Arg.Any<bool>()))
             .Do(ci =>
             {
                 var p = ci.ArgAt<Dictionary<string, string>?>(1) ?? [];
                 tabs.Add(new Opened(ci.ArgAt<string>(0),
                                     string.Join(", ", p.Select(kv => $"{Normalise(kv.Key)}={kv.Value}")
                                                        .OrderBy(s => s))));
             });
        return (CreateAction(shell), tabs);
    }

    /// <summary>
    /// Folds the singular and plural spellings of the path parameter together. A tab that accepts both
    /// resolves them to the same one-element list — DICOM's <c>ResolvePaths</c> is the worked example — so
    /// which one an overload happens to write is the tab's business, not a behavioural difference. The
    /// values are still compared verbatim, so an overload that opens a <em>different</em> file still fails.
    /// </summary>
    private static string Normalise(string key) => key == "path" ? "paths" : key;

    [TestMethod]
    public void ItOpensItsOwnTab_OnTheFileItWasInvokedOn()
    {
        var (action, tabs) = Arrange();

        Assert.IsTrue(action.PerformAction(AcceptableFile),
            $"'{action.DisplayName}' should accept {AcceptableFile}");
        Assert.AreEqual(1, tabs.Count, "one invocation opens one tab");
        Assert.AreEqual(ExpectedPageKind, tabs[0].PageKind);
    }

    [TestMethod]
    public void TheTabParametersCarryTheFile()
    {
        var (action, tabs) = Arrange();
        action.PerformAction(AcceptableFile);

        // The key differs by action (path / paths / a '|'-joined queue), and that is the tab's business —
        // what matters is that the file the user picked actually reaches the page that opens.
        Assert.IsTrue(tabs.Single().Parameters.Contains(AcceptableFile),
            $"the opened tab must carry the file: {tabs.Single().Parameters}");
    }

    [TestMethod]
    public void AnEmptySelection_OpensNothing_AndSaysSo()
    {
        var (action, tabs) = Arrange();

        Assert.IsFalse(action.PerformAction([]),
            "returning true on an empty selection flashes the action strip's success tick over nothing");
        Assert.AreEqual(0, tabs.Count);
    }

    [TestMethod]
    public void TheTwoOverloads_AgreeOnAFileThisActionHandles() =>
        AssertOverloadsAgree(AcceptableFile);

    [TestMethod]
    public void TheTwoOverloads_AgreeOnAFileThisActionDoesNotHandle() =>
        AssertOverloadsAgree(FileThisActionDoesNotHandle);

    /// <summary>
    /// The heart of the contract. An action may filter by type or not, but it must decide once: filtering
    /// the selection overload while the single-file overload opens anything means "As Audio" on a text file
    /// queues it in the player from the file list, and silently does nothing from a one-item selection.
    /// </summary>
    private void AssertOverloadsAgree(string path)
    {
        var (single, singleTabs) = Arrange();
        var singleSaid = single.PerformAction(path);

        var (multi, multiTabs) = Arrange();
        var multiSaid = multi.PerformAction([path]);

        Assert.AreEqual(singleSaid, multiSaid,
            $"'{single.DisplayName}' answered {singleSaid} for {path} but {multiSaid} for a one-item "
            + "selection of the same file — one file selected is one file selected");

        CollectionAssert.AreEqual(
            singleTabs.Select(t => t.ToString()).ToArray(),
            multiTabs.Select(t => t.ToString()).ToArray(),
            $"'{single.DisplayName}' opened different tabs for {path} than for a one-item selection of it:\n"
            + $"  one file : {string.Join(" | ", singleTabs)}\n"
            + $"  selection: {string.Join(" | ", multiTabs)}");
    }

    [TestMethod]
    public void ASelectionOpensOneTab_WhenTheViewerHoldsOneFileAtATime()
    {
        var (action, tabs) = Arrange();
        if (action.SupportsMultipleFiles)
        {
            // The other design: one tab that holds the whole selection (the player's queue, the font set).
            Assert.IsTrue(action.PerformAction([AcceptableFile, AcceptableFile]));
            Assert.AreEqual(1, tabs.Count, "a multi-file viewer takes the selection into a single tab");
            return;
        }

        Assert.IsTrue(action.PerformAction([AcceptableFile, AcceptableFile]));
        Assert.AreEqual(1, tabs.Count,
            "SupportsMultipleFiles is false, so a selection opens the first file — not a tab per file");
    }

    [TestMethod]
    public void TheForceOverloads_MatchTheOrdinaryOnes()
    {
        // force skips confirmation prompts; a viewer action has none, so the two must be indistinguishable.
        // The interface's default implementation gives this for free — the test is here for the action that
        // overrides it and forgets that opening a file was never the destructive part.
        var (plain, plainTabs) = Arrange();
        plain.PerformAction(AcceptableFile);

        var (forced, forcedTabs) = Arrange();
        forced.PerformAction(AcceptableFile, force: true);

        CollectionAssert.AreEqual(plainTabs.Select(t => t.ToString()).ToArray(),
                                  forcedTabs.Select(t => t.ToString()).ToArray());
    }

    [TestMethod]
    public void OpeningAFileToLookAtIt_IsNeitherDestructiveNorARefresh()
    {
        var (action, _) = Arrange();

        Assert.IsTrue(action.OpensViewer, "this base is for the actions that open a viewer tab");
        Assert.IsFalse(action.IsDestructive, "opening a file to look at it changes nothing on disk");
        Assert.IsTrue(action.CanPerformAction);
    }
}
