using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.RibbonHandlers;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// The guard a pinned ribbon button needs and the action strip already had.
///
/// <para>
/// <c>FileActionManager</c> decides "this action cannot take a multi-selection" by <em>hiding</em> the
/// action — it drops anything whose <c>SupportsMultipleFiles</c> is false when more than one file is
/// selected, so the user never gets a button to press. A pinned ribbon button cannot be hidden; it is
/// already on the ribbon. <c>FileActionRibbonPinHandler</c> resolved the action from the registry and
/// invoked it with whatever was selected, so that guard was simply absent on this path.
/// </para>
/// <para>
/// "Properties" is pinnable (<c>Pin</c> only refuses <em>destructive</em> actions) and declares
/// <c>SupportsMultipleFiles => false</c>. Pin it, select two files, click: <c>NotImplementedException</c>.
/// The action has since been made defensive, but every other single-file action was one implementation
/// detail away from the same thing, so the guard belongs at the call site.
/// </para>
/// </summary>
[TestClass]
[CoversNode("winfs-ribbon-pin")]
public class RibbonPinSelectionGuardTests
{
    private static IFileAction Action(bool supportsMultiple, string name = "Properties")
    {
        var a = Substitute.For<IFileAction>();
        a.SupportsMultipleFiles.Returns(supportsMultiple);
        a.DisplayName.Returns(name);
        return a;
    }

    [TestMethod]
    public void ASingleFileActionOnAMultiSelection_IsRefused_WithAReasonNamingIt()
    {
        var why = FileActionRibbonPinHandler.BlockedReason(Action(supportsMultiple: false), selectionCount: 2);

        Assert.IsNotNull(why, "this is the click that used to reach PerformAction(paths) and throw");
        StringAssert.Contains(why, "Properties",
            "name the action — the ribbon may hold several pinned buttons and the user has to know which refused");
    }

    [TestMethod]
    public void AMultiFileActionOnAMultiSelection_Runs()
    {
        Assert.IsNull(FileActionRibbonPinHandler.BlockedReason(Action(supportsMultiple: true), selectionCount: 2));
    }

    [TestMethod]
    public void ASingleFileActionOnOneFile_Runs()
    {
        // The guard is about the count, not the flag: one file is always a legitimate invocation.
        Assert.IsNull(FileActionRibbonPinHandler.BlockedReason(Action(supportsMultiple: false), selectionCount: 1));
    }

    [TestMethod]
    public void AnEmptySelection_IsRefused_ForEitherKindOfAction()
    {
        // Previously only the live-selection branch checked this; a pinned button carrying its own file list
        // could reach the invocation with nothing in it, which is the other half of the actions' own
        // empty-selection contract.
        foreach (var multi in new[] { true, false })
        {
            var why = FileActionRibbonPinHandler.BlockedReason(Action(multi), selectionCount: 0);
            Assert.IsNotNull(why, $"SupportsMultipleFiles={multi} must still refuse an empty selection");
            StringAssert.Contains(why, "Select files");
        }
    }

    [TestMethod]
    public void TheRefusalForNothingSelected_IsNotTheRefusalForTooMany()
    {
        // Two different mistakes; telling the user "works on one file at a time" when they selected none
        // sends them to fix the wrong thing.
        var none    = FileActionRibbonPinHandler.BlockedReason(Action(supportsMultiple: false), 0);
        var tooMany = FileActionRibbonPinHandler.BlockedReason(Action(supportsMultiple: false), 2);

        Assert.AreNotEqual(none, tooMany);
    }
}
