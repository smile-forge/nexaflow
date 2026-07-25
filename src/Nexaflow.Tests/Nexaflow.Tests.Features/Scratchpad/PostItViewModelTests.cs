using Nexaflow.Features.Scratchpad.Models;
using Nexaflow.Features.Scratchpad.ViewModels;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Scratchpad;

[TestClass]
public class PostItViewModelTests
{
    private static PostItNote MakeNote() => new()
    {
        Content   = "hello",
        X         = 10,
        Y         = 20,
        Width     = 250,
        Height    = 250,
        Rotation  = 5,
        ZIndex    = 3,
        Color     = "Yellow",
        Shape     = "Square",
        ExpiresAt = null,
    };

    [TestMethod]
    [CoversNode("scratchpad-persistence")]
    public void Ctor_CopiesValuesFromNote()
    {
        var note = MakeNote();
        var vm = new PostItViewModel(note);

        Assert.AreEqual("hello", vm.Content);
        Assert.AreEqual(10,      vm.X);
        Assert.AreEqual(20,      vm.Y);
        Assert.AreEqual(250,     vm.Width);
        Assert.AreEqual(250,     vm.Height);
        Assert.AreEqual(5,       vm.Rotation);
        Assert.AreEqual(3,       vm.ZIndex);
        Assert.AreEqual("Yellow", vm.Color);
        Assert.AreEqual("Square", vm.Shape);
        Assert.IsTrue(vm.IsPinned);
        Assert.AreSame(note, vm.Note);
    }

    [TestMethod]
    [CoversNode("scratchpad-persistence")]
    public void PropertyChange_SyncsBackToNote()
    {
        var note = MakeNote();
        var vm = new PostItViewModel(note);

        vm.Content  = "updated";
        vm.X        = 99;
        vm.Y        = 88;
        vm.Width    = 300;
        vm.Height   = 400;
        vm.Rotation = -7;
        vm.ZIndex   = 42;
        vm.Color    = "Pink";
        vm.Shape    = "Circle";

        Assert.AreEqual("updated", note.Content);
        Assert.AreEqual(99,        note.X);
        Assert.AreEqual(88,        note.Y);
        Assert.AreEqual(300,       note.Width);
        Assert.AreEqual(400,       note.Height);
        Assert.AreEqual(-7,        note.Rotation);
        Assert.AreEqual(42,        note.ZIndex);
        Assert.AreEqual("Pink",    note.Color);
        Assert.AreEqual("Circle",  note.Shape);
    }

    [TestMethod]
    [CoversNode("scratchpad-persistence")]
    public void PropertyChange_FiresRequestSave()
    {
        var vm = new PostItViewModel(MakeNote());
        int saves = 0;
        vm.RequestSave = _ => saves++;

        vm.Content = "edit";
        vm.X       = 1;
        vm.Color   = "Blue";

        Assert.AreEqual(3, saves);
    }

    [TestMethod]
    [CoversNode("scratchpad-persistence")]
    public void DerivedProperties_DoNotFireRequestSave()
    {
        var vm = new PostItViewModel(MakeNote()) { ExpiresAt = null };
        int saves = 0;
        vm.RequestSave = _ => saves++;

        vm.RefreshTimeDisplay(); // raises TimeRemainingText only

        Assert.AreEqual(0, saves);
    }

    [TestMethod]
    [CoversNode("postit-pin-timer")]
    public void TimeRemainingText_Pinned_ShowsPin()
    {
        var vm = new PostItViewModel(MakeNote()) { ExpiresAt = null };
        Assert.AreEqual("📌", vm.TimeRemainingText);
        Assert.IsTrue(vm.IsPinned);
    }

    [TestMethod]
    [CoversNode("postit-pin-timer")]
    public void TimeRemainingText_Expired_ShowsAlarm()
    {
        var vm = new PostItViewModel(MakeNote()) { ExpiresAt = DateTimeOffset.Now.AddMinutes(-1) };
        Assert.AreEqual("⏰", vm.TimeRemainingText);
    }

    [TestMethod]
    [CoversNode("postit-pin-timer")]
    public void TimeRemainingText_MinutesOnly_FormatsMinutes()
    {
        var vm = new PostItViewModel(MakeNote()) { ExpiresAt = DateTimeOffset.Now.AddMinutes(20) };
        StringAssert.StartsWith(vm.TimeRemainingText, "⏱");
        StringAssert.Contains(vm.TimeRemainingText, "m");
        Assert.IsFalse(vm.TimeRemainingText.Contains('h'));
    }

    [TestMethod]
    [CoversNode("postit-pin-timer")]
    public void TimeRemainingText_OverAnHour_FormatsHoursAndMinutes()
    {
        var vm = new PostItViewModel(MakeNote()) { ExpiresAt = DateTimeOffset.Now.AddHours(3).AddMinutes(15) };
        StringAssert.Contains(vm.TimeRemainingText, "h");
        StringAssert.Contains(vm.TimeRemainingText, "m");
    }

    [TestMethod]
    [CoversNode("postit-colors")]
    public void ChangeColorCommand_UpdatesColor()
    {
        var vm = new PostItViewModel(MakeNote());
        vm.ChangeColorCommand.Execute("Green");
        Assert.AreEqual("Green", vm.Color);
        Assert.AreEqual("Green", vm.Note.Color);
    }

    [TestMethod]
    [CoversNode("square")]
    [CoversNode("rounded")]
    [CoversNode("diagonal")]
    [CoversNode("speech")]
    public void ChangeShapeCommand_UpdatesShape()
    {
        var vm = new PostItViewModel(MakeNote());
        vm.ChangeShapeCommand.Execute("Circle");
        Assert.AreEqual("Circle", vm.Shape);
        Assert.AreEqual("Circle", vm.Note.Shape);
    }

    [TestMethod]
    [CoversNode("postit-pin-timer")]
    public void TogglePin_FromPinned_StartsCountdownUsingLifetime()
    {
        var vm = new PostItViewModel(MakeNote())
        {
            ExpiresAt        = null,
            GetNoteLifetime  = () => TimeSpan.FromHours(3),
        };
        Assert.IsTrue(vm.IsPinned);
        vm.TogglePinCommand.Execute(null);

        Assert.IsNotNull(vm.ExpiresAt);
        Assert.IsFalse(vm.IsPinned);
        var remaining = vm.ExpiresAt!.Value - DateTimeOffset.Now;
        Assert.IsTrue(remaining > TimeSpan.FromMinutes(170) && remaining < TimeSpan.FromMinutes(190));
    }

    [TestMethod]
    [CoversNode("postit-pin-timer")]
    public void TogglePin_FromCountdown_PinsNote()
    {
        var vm = new PostItViewModel(MakeNote())
        {
            ExpiresAt = DateTimeOffset.Now.AddHours(1),
        };
        vm.TogglePinCommand.Execute(null);
        Assert.IsNull(vm.ExpiresAt);
        Assert.IsTrue(vm.IsPinned);
    }

    [TestMethod]
    [CoversNode("postit-pin-timer")]
    public void TogglePin_NoLifetimeProvider_FallsBackTo2Hours()
    {
        var vm = new PostItViewModel(MakeNote()) { ExpiresAt = null };
        vm.TogglePinCommand.Execute(null);
        Assert.IsNotNull(vm.ExpiresAt);
        var remaining = vm.ExpiresAt!.Value - DateTimeOffset.Now;
        Assert.IsTrue(remaining > TimeSpan.FromMinutes(115) && remaining < TimeSpan.FromMinutes(125));
    }

    [TestMethod]
    [CoversNode("postit-zorder")]
    public void BringToFrontCommand_UsesGetMaxZIndex()
    {
        var vm = new PostItViewModel(MakeNote())
        {
            GetMaxZIndex = () => 42,
        };
        vm.BringToFrontCommand.Execute(null);
        Assert.AreEqual(43, vm.ZIndex);
    }

    [TestMethod]
    [CoversNode("postit-zorder")]
    public void BringToFrontCommand_NoProvider_DefaultsToOne()
    {
        var vm = new PostItViewModel(MakeNote());
        vm.BringToFrontCommand.Execute(null);
        Assert.AreEqual(1, vm.ZIndex);
    }

    [TestMethod]
    [CoversNode("postit-zorder")]
    public void SendToBackCommand_ZeroesZIndex()
    {
        var vm = new PostItViewModel(MakeNote()) { ZIndex = 99 };
        vm.SendToBackCommand.Execute(null);
        Assert.AreEqual(0, vm.ZIndex);
    }

    [TestMethod]
    [CoversNode("postit-close")]
    public void RemoveCommand_FiresRequestRemoveWithSelf()
    {
        var vm = new PostItViewModel(MakeNote());
        PostItViewModel? removed = null;
        vm.RequestRemove = v => removed = v;

        vm.RemoveCommand.Execute(null);
        Assert.AreSame(vm, removed);
    }

    [TestMethod]
    [CoversNode("postit-pin-timer")]
    public void ExpiresAtChange_RaisesIsPinnedAndTimeRemaining()
    {
        var vm = new PostItViewModel(MakeNote()) { ExpiresAt = null };
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.ExpiresAt = DateTimeOffset.Now.AddHours(1);

        CollectionAssert.Contains(changed, nameof(PostItViewModel.IsPinned));
        CollectionAssert.Contains(changed, nameof(PostItViewModel.TimeRemainingText));
    }

    // ── Recycle-bin label (first 15 chars of the first non-empty line) ────

    private static PostItViewModel WithContent(string content)
        => new(new PostItNote { Content = content });

    [TestMethod]
    public void RecycleBinLabel_Empty_IsEmpty()
        => Assert.AreEqual(string.Empty, WithContent("").RecycleBinLabel);

    [TestMethod]
    public void RecycleBinLabel_ShortContent_ReturnedWhole()
        => Assert.AreEqual("hi there", WithContent("hi there").RecycleBinLabel);

    [TestMethod]
    [CoversNode("recycle-list")]
    public void RecycleBinLabel_LongContent_TruncatedTo15()
        => Assert.AreEqual("abcdefghijklmno", WithContent("abcdefghijklmnopqrstuvwxyz").RecycleBinLabel);

    [TestMethod]
    [CoversNode("recycle-list")]
    public void RecycleBinLabel_UsesFirstNonEmptyLine()
        => Assert.AreEqual("first line", WithContent("\n\n   \nfirst line\nsecond line").RecycleBinLabel);

    [TestMethod]
    public void RecycleBinLabel_FirstNonEmptyLineTrimmedThenTruncated()
        => Assert.AreEqual("the quick brown", WithContent("   the quick brown fox").RecycleBinLabel);

    [TestMethod]
    public void RecycleBinLabel_WhitespaceOnly_IsEmpty()
        => Assert.AreEqual(string.Empty, WithContent("   \n\t\n  ").RecycleBinLabel);

    [TestMethod]
    [CoversNode("recycle-list")]
    public void RecycleBinLabel_TracksContentChanges()
    {
        var vm = WithContent("old");
        vm.Content = "brand new content here";
        Assert.AreEqual("brand new conte", vm.RecycleBinLabel);
    }

    [TestMethod]
    public void StartInEdit_DefaultsFalse()
        => Assert.IsFalse(WithContent("x").StartInEdit);
}
