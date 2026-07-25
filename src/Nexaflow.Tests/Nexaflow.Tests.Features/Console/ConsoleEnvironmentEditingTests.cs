using System.Collections.Generic;
using Nexaflow.Features.Console;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Console;

/// <summary>
/// The rules the environments config editor enforces.
/// <para>
/// A folder binding pins a location to an environment <i>by name</i>, so renaming one is the moment every
/// pin the user made can quietly stop resolving. Nothing announces it: the folder still opens a console,
/// just with the wrong shell — which is exactly the sort of breakage that gets blamed on the folder
/// rather than on the rename.
/// </para>
/// </summary>
[TestClass]
[CoversNode("console-env-editor")]
public class ConsoleEnvironmentEditingTests
{
    // ── Renaming an environment ───────────────────────────────────────────────

    [TestMethod]
    public void RenamingAnEnvironmentCarriesItsPinnedLocationsAcross()
    {
        Assert.IsTrue(ConsoleEnvironmentEditing.ShouldMigrateBindings("Dev", "Development", ["Admin", "WSL"]));
    }

    [TestMethod]
    public void ButNotWhenAnotherEnvironmentStillAnswersToTheOldName()
    {
        // Two environments, one renamed to free up a name the other already uses — the pins belong to that
        // other one now, and moving them would silently steal them.
        Assert.IsFalse(ConsoleEnvironmentEditing.ShouldMigrateBindings("Dev", "Development", ["Dev", "WSL"]),
                       "the old name is still owned — its pins stay where they are");
    }

    [TestMethod]
    public void TheOldNameIsMatchedCaseInsensitively_LikeEverythingElseAboutAPath()
    {
        Assert.IsFalse(ConsoleEnvironmentEditing.ShouldMigrateBindings("Dev", "Development", ["DEV"]));
    }

    [TestMethod]
    public void ANonRenameMigratesNothing()
    {
        Assert.IsFalse(ConsoleEnvironmentEditing.ShouldMigrateBindings("Dev", "dev", []),
                       "a case-only edit is the same environment");
        Assert.IsFalse(ConsoleEnvironmentEditing.ShouldMigrateBindings("Dev", "", []),
                       "a half-typed empty name is not a destination to move pins to");
        Assert.IsFalse(ConsoleEnvironmentEditing.ShouldMigrateBindings(null, "Dev", []));
    }

    // ── Adding an environment ─────────────────────────────────────────────────

    [TestMethod]
    public void ANewEnvironmentTakesTheFirstFreeName()
    {
        // Duplicate names would make every binding to that name ambiguous.
        Assert.AreEqual("New Environment",
                        ConsoleEnvironmentEditing.UniqueName("New Environment", ["Dev"]));
        Assert.AreEqual("New Environment 2",
                        ConsoleEnvironmentEditing.UniqueName("New Environment", ["New Environment"]));
        Assert.AreEqual("New Environment 3",
                        ConsoleEnvironmentEditing.UniqueName("New Environment",
                                                             ["New Environment", "New Environment 2"]));
    }

    [TestMethod]
    public void TheFreeNameSearchIgnoresCase()
    {
        Assert.AreEqual("New Environment 2",
                        ConsoleEnvironmentEditing.UniqueName("New Environment", ["NEW ENVIRONMENT"]));
    }

    [TestMethod]
    public void AGapInTheNumberingIsFilled_RatherThanSkippedPast()
    {
        Assert.AreEqual("New Environment 2",
                        ConsoleEnvironmentEditing.UniqueName("New Environment",
                                                             ["New Environment", "New Environment 3"]));
    }

    // ── Removing an environment ───────────────────────────────────────────────

    [TestMethod]
    public void TheLastEnvironmentCannotBeRemoved()
    {
        Assert.IsFalse(ConsoleEnvironmentEditing.CanRemoveEnvironment(1),
                       "a shell has to launch with something");
        Assert.IsFalse(ConsoleEnvironmentEditing.CanRemoveEnvironment(0));
        Assert.IsTrue(ConsoleEnvironmentEditing.CanRemoveEnvironment(2));
    }
}
