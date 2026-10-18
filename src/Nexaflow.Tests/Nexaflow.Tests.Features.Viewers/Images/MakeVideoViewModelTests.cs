using Nexaflow.Features.Common;
using Nexaflow.Features.Images.ViewModels;
using Nexaflow.Tests.Fixtures;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Nexaflow.Tests.Features.Viewers.Images;

/// <summary>
/// Making a video from the picture on screen, the user driving: offered only where the workspace makes clips from a
/// picture, the box set from the picture and the model's lengths, and the clip handed to the workspace as it was set.
/// </summary>
[TestClass]
[CoversNode("images-make-video")]
public class MakeVideoViewModelTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47];

    private static IAIService Ai(params int[] lengths)
    {
        var ai = Substitute.For<IAIService>();
        ai.PictureClipLengths.Returns(lengths);
        return ai;
    }

    [TestMethod]
    public void WhereNoModelMakesAClipFromAPicture_NothingIsOffered()
    {
        var box = new MakeVideoViewModel(Ai(), () => false, () => Png);

        box.OpenCommand.Execute(null);

        Assert.IsFalse(box.CanMake);
        Assert.IsFalse(box.IsOpen);
        Assert.IsFalse(box.IsEnabled, "the button stays, disabled, its tooltip saying how to enable it");
        Assert.IsFalse(new MakeVideoViewModel(null, () => false, () => Png).CanMake, "no AI at all, no video either");
    }

    [TestMethod]
    public void TheBox_OpensOnThePicturesShape_AndTheLengthNearestEightSeconds()
    {
        var box = new MakeVideoViewModel(Ai(4, 8, 12), () => true, () => Png);

        box.OpenCommand.Execute(null);

        Assert.IsTrue(box.IsOpen);
        CollectionAssert.AreEqual(new[] { 4, 8, 12 }, box.Lengths.Select(l => l.Seconds).ToArray());
        Assert.AreEqual(8, box.Length!.Seconds);
        Assert.IsTrue(box.Portrait, "a tall picture starts a portrait clip");
        Assert.IsFalse(box.Landscape);
    }

    [TestMethod]
    public void AClip_IsMadeOnlyOnceSaidWhatHappens_ThenHandedOverAsSet()
    {
        var ai  = Ai(5);
        var box = new MakeVideoViewModel(ai, () => false, () => Png);
        box.OpenCommand.Execute(null);

        Assert.IsFalse(box.MakeCommand.CanExecute(null), "nothing said of what happens yet");
        box.Description = "  The fox looks up and trots away.  ";
        box.Portrait = true;
        Assert.IsTrue(box.MakeCommand.CanExecute(null));

        box.MakeCommand.Execute(null);

        ai.Received(1).MakeClipFromPicture(Arg.Is<ClipFromPicture>(c =>
            c.Png == Png && c.Description == "The fox looks up and trots away." && c.Seconds == 5 && c.Portrait));
        Assert.IsFalse(box.IsOpen, "the clip runs on in the background; the box closes");
        Assert.AreEqual("", box.Description);
    }

    [TestMethod]
    public void WhereTheWorkspaceNoLongerMakesClipsFromAPicture_TheBoxClosesAndTheButtonIsDisabled()
    {
        var ai  = Ai(5);
        var box = new MakeVideoViewModel(ai, () => false, () => Png);
        box.OpenCommand.Execute(null);
        box.Description = "Waves roll in.";
        ai.PictureClipLengths.Returns([]);
        ai.When(a => a.MakeClipFromPicture(Arg.Any<ClipFromPicture>())).Throw(new InvalidOperationException("no model"));

        box.MakeCommand.Execute(null);

        Assert.IsFalse(box.CanMake);
        Assert.IsFalse(box.IsOpen);
        Assert.IsFalse(box.IsEnabled);
    }

    [TestMethod]
    public async Task AModelWaitingOnItsDownload_EnablesTheButton_WhichOffersTheDownload()
    {
        var ai = Ai();
        ai.StatusOf(AiAbility.HighQualityVideoGeneration).Returns(new AiAbilityStatus(AiAbilityState.NeedsDownload, 20_000_000_000));
        ai.MakeReadyAsync(AiAbility.HighQualityVideoGeneration, Arg.Any<CancellationToken>()).Returns(false);
        var box = new MakeVideoViewModel(ai, () => false, () => Png);

        Assert.IsTrue(box.IsEnabled);
        await box.OpenCommand.ExecuteAsync(null);

        await ai.Received(1).MakeReadyAsync(AiAbility.HighQualityVideoGeneration, Arg.Any<CancellationToken>());
        Assert.IsFalse(box.IsOpen, "the download is offered, and the press stops there");
    }
}
