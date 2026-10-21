using Nexaflow.Core.ViewModels;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// Which Options pages count as holding unsaved edits — what closing the panel asks about.
/// </summary>
/// <remarks>
/// A custom page with no tracker counted as changed whether or not anything was touched, so opening Options and
/// closing it again always asked to save.
/// </remarks>
[TestClass]
[CoversNode("opt-save-validate")]
public class OptionsUnsavedChangesTests
{
    /// <summary>Shows something and saves nothing, like About.</summary>
    public sealed class ReadOnlyPage;

    /// <summary>Saves, and says when it has something to save.</summary>
    public sealed class TrackedPage : ICustomConfigApply, IConfigChangeTracker
    {
        private bool _hasChanges;

        public bool HasChanges => _hasChanges;
        public event EventHandler? HasChangesChanged;

        public void Edit()  { _hasChanges = true;  HasChangesChanged?.Invoke(this, EventArgs.Empty); }
        public void Apply() { _hasChanges = false; HasChangesChanged?.Invoke(this, EventArgs.Empty); }
    }

    /// <summary>Saves, but never says whether it has anything to save.</summary>
    public sealed class UntrackedPage : ICustomConfigApply
    {
        public void Apply() { }
    }

    [CustomControl(typeof(ReadOnlyPage))]
    public sealed class ReadOnlyConfig : IFeatureConfig
    {
        public string ConfigName => "test-read-only";
        public string FriendlyName => "Read only";
    }

    [CustomControl(typeof(TrackedPage))]
    public sealed class TrackedConfig : IFeatureConfig
    {
        public string ConfigName => "test-tracked";
        public string FriendlyName => "Tracked";
    }

    [CustomControl(typeof(UntrackedPage))]
    public sealed class UntrackedConfig : IFeatureConfig
    {
        public string ConfigName => "test-untracked";
        public string FriendlyName => "Untracked";
    }

    [TestMethod]
    public void A_page_that_saves_nothing_never_holds_unsaved_edits()
    {
        var section = new ConfigEditViewModel(new ReadOnlyConfig(), "test-read-only", "Read only");

        Assert.IsFalse(section.HasChanges);
    }

    [TestMethod]
    public void A_tracked_page_holds_unsaved_edits_only_while_its_tracker_says_so()
    {
        var section = new ConfigEditViewModel(new TrackedConfig(), "test-tracked", "Tracked");
        var page = (TrackedPage)section.CustomControlInstance!;

        Assert.IsFalse(section.HasChanges, "an untouched page has nothing to save");

        page.Edit();
        Assert.IsTrue(section.HasChanges, "an edit is something to save");

        section.ApplyToReal();
        Assert.IsFalse(section.HasChanges, "a saved page has nothing left to save");
    }

    [TestMethod]
    public void A_page_that_saves_without_a_tracker_is_taken_to_hold_unsaved_edits()
    {
        var section = new ConfigEditViewModel(new UntrackedConfig(), "test-untracked", "Untracked");

        Assert.IsTrue(section.HasChanges, "with no way to tell, closing asks rather than dropping edits");
    }
}
