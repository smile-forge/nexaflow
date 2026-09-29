using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.UIJourneys.Infrastructure;

namespace Nexaflow.Tests.Features.Hdf5.UI;

/// <summary>
/// One-pass UI journey for the HDF5 viewer: opens the sample through the <b>"ShowHdf5Action"</b> action, filters
/// the tree, then selects a 3-D dataset (table and slice bar), a wide one (column window), a group (member
/// summary) and a 1-D one (storage details), and closes the details drawer — soft-asserting each so one gap
/// does not hide the rest. What each control does to the data is asserted against the view-model in
/// <see cref="Hdf5ViewModelTests"/>.
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[CoversNode("hdf5")]
public class Hdf5JourneyTests : UiJourneyTestBase
{
    [TestMethod]
    [CoversNode("hdf5-ui")]
    public void Hdf5_Controls_RespondInOnePass()
    {
        var view = OpenFileVia(TestSampleData.Path("hdf5"), "experiment.h5", "ShowHdf5Action", "Hdf5View");
        Assert.IsNotNull(view, "Hdf5View did not open via the 'Open in HDF5 viewer' action.");

        // Structure panel.
        var tree = CheckPresent("Object tree", "Hdf5_Tree");
        Check("Tree filter takes a name and clears", () => TypeInto("Hdf5_TreeFilter", "meas") && TypeInto("Hdf5_TreeFilter", string.Empty));

        // Content pane — a 3-D dataset lies on rows and columns with its first dimension held by a slider.
        Check("A 3-D dataset opens in the table", () => SelectInTree(tree, "measurements", "frames"));
        CheckPresent("Data table", "Hdf5_Table");
        CheckPresent("Rows dimension", "Hdf5_SliceRowAxis");
        CheckPresent("Columns dimension", "Hdf5_SliceColAxis");
        CheckExists("Index slider for the held dimension", "Hdf5_SliceIndex0");

        // Wider than the table lays out: the column window appears.
        Check("A wide dataset opens", () => SelectInTree(tree, "measurements", "matrix"));
        CheckExists("Column window", "Hdf5_ColumnOffset");

        // A group shows its members by kind.
        Check("A group opens", () => SelectInTree(tree, "metadata"));
        CheckExists("Group summary", "Hdf5_GroupSummary");

        // Details drawer — open to begin with.
        Check("A 1-D dataset opens", () => SelectInTree(tree, "measurements", "temperature"));
        CheckExists("Object details", "Hdf5_ObjectDetails");
        CheckExists("Storage details", "Hdf5_StorageDetails");
        CheckExists("Attribute list", "Hdf5_Attributes");
        Check("Attribute filter takes a name", () => TypeInto("Hdf5_AttributeFilter", "units"));
        CheckDoes("Details toggle closes the drawer", "Hdf5_DetailsToggle",
                  () => WaitForFs(() => !Exists("Hdf5_AttributeFilter"), 3));
        CheckDoes("Details toggle opens it again", "Hdf5_DetailsToggle",
                  () => WaitForFs(() => Exists("Hdf5_AttributeFilter"), 3));

        AssertJourney();
    }

    /// <summary>Expands each named row down the tree and selects the last, through the rows' own patterns.</summary>
    private bool SelectInTree(AutomationElement? tree, params string[] names)
    {
        if (tree is null) return false;
        AutomationElement scope = tree;
        AutomationElement? row = null;
        for (int i = 0; i < names.Length; i++)
        {
            var name = names[i];
            var within = scope;
            row = WaitFor(() => within.FindFirstDescendant(cf => cf.ByName(name).And(cf.ByControlType(ControlType.TreeItem))), 5);
            if (row is null) return false;
            if (i < names.Length - 1)
            {
                row.Patterns.ExpandCollapse.PatternOrDefault?.Expand();
                Wait.UntilInputIsProcessed();
                scope = row;
            }
        }
        row!.Patterns.SelectionItem.Pattern.Select();
        Wait.UntilInputIsProcessed();
        return true;
    }
}
