using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.FileActions;
using Nexaflow.Features.Images.ViewModels;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Localization;
using NSubstitute;

namespace Nexaflow.Tests.Features.Viewers.Images;

/// <summary>
/// Gen Image in the New picker: offered where the workspace draws pictures or will once its model downloads, its options
/// what the model at the quality chosen offers, and the picture handed to the workspace as it was set up.
/// </summary>
[TestClass]
[CoversNode("images-gen-image")]
public class GenerateImageOptionsTests
{
    private const string Folder = @"C:\pictures";

    private static readonly PictureChoices Standard = new(["1:1", "16:9", "9:16"]) { Resolutions = ["1K", "2K"], Seeds = true };

    private static readonly PictureChoices High = new(["1:1", "3:2"]) { Details = ["low", "high"], TransparentBackgrounds = true };

    private static IAIService Ai(PictureMaking? making, AiAbilityState state = AiAbilityState.Ready)
    {
        var ai = Substitute.For<IAIService>();
        ai.MakesPictures.Returns(making);
        ai.StatusOf(Arg.Any<AiAbility>()).Returns(new AiAbilityStatus(state));
        return ai;
    }

    [TestMethod]
    public void ItIsOffered_WhereAModelDraws_OrOneOfNexaflowsOwnWaitsOnItsDownload()
    {
        Assert.IsTrue(new GenerateImageCreateAction(Ai(new(Standard, null))).IsOffered);
        Assert.IsFalse(new GenerateImageCreateAction(Ai(null, AiAbilityState.Unassigned)).IsOffered, "nothing draws, nor will");

        var waiting = new GenerateImageCreateAction(Ai(null, AiAbilityState.NeedsDownload));
        Assert.IsTrue(waiting.IsOffered);
        var options = (GenerateImageOptions)waiting.OpenOptions(Folder);
        Assert.IsFalse(options.IsReady);
        Assert.IsTrue(options.IsWaiting, "its download is offered");
        Assert.IsFalse(options.CanCreate);
    }

    [TestMethod]
    public void TheOptions_AreWhatTheModelOffers_AndChange_WithTheQualityChosen()
    {
        var options = new GenerateImageOptions(Ai(new(Standard, High)), Folder);

        CollectionAssert.AreEqual(new[] { "standard", "high" }, options.Qualities.Select(q => q.Value).ToArray());
        Assert.AreEqual("standard", options.Quality!.Value);
        CollectionAssert.AreEqual(new[] { "1:1", "16:9", "9:16" }, options.Aspects.Select(a => a.Value).ToArray());
        CollectionAssert.AreEqual(new[] { null, "1K", "2K" }, options.Resolutions.Select(r => r.Value).ToArray(), "the model's own first");
        Assert.AreEqual(0, options.Details.Count);
        Assert.IsTrue(options.Seeds);
        Assert.IsFalse(options.TransparentBackgrounds);

        options.Aspect  = options.Aspects.Single(a => a.Value == "1:1");
        options.Quality = options.Qualities.Single(q => q.Value == "high");

        CollectionAssert.AreEqual(new[] { "1:1", "3:2" }, options.Aspects.Select(a => a.Value).ToArray());
        Assert.AreEqual("1:1", options.Aspect!.Value, "a choice the model still offers is kept");
        Assert.AreEqual(0, options.Resolutions.Count);
        CollectionAssert.AreEqual(new[] { null, "low", "high" }, options.Details.Select(d => d.Value).ToArray());
        Assert.IsFalse(options.Seeds);
        Assert.IsTrue(options.TransparentBackgrounds);
    }

    [TestMethod]
    public void APicture_IsMadeOnlyOnceSaidWhatToDraw_ThenHandedOverAsSet()
    {
        var ai      = Ai(new(Standard, null));
        var options = new GenerateImageOptions(ai, Folder);
        Assert.AreEqual(0, options.Qualities.Count, "one quality: nothing to choose");

        Assert.IsFalse(options.CanCreate, "nothing said of what to draw yet");
        Assert.IsNull(options.Create("fox.png"));
        options.Prompt = "  A fox asleep in the snow  ";
        options.Seed   = "seven";
        Assert.IsFalse(options.CanCreate, "a seed is a number");
        Assert.IsFalse(options.SeedIsValid);
        options.Seed       = "7";
        options.Aspect     = options.Aspects.Single(a => a.Value == "16:9");
        options.Resolution = options.Resolutions.Single(r => r.Value == "2K");
        Assert.IsTrue(options.CanCreate);

        var path = options.Create("fox.png");

        Assert.AreEqual(Path.Combine(Folder, "fox.png"), path);
        ai.Received(1).MakePicture(Arg.Is<PictureAsked>(p =>
            p.Prompt == "A fox asleep in the snow" && p.Path == path && !p.HighQuality
            && p.Aspect == "16:9" && p.Resolution == "2K" && p.Detail == null && p.Seed == 7 && !p.TransparentBackground));
    }

    [TestMethod]
    public void EachAspect_IsLabelledWithTheShapeItMakes()
    {
        var options = new GenerateImageOptions(Ai(new(Standard, null)), Folder);

        CollectionAssert.AreEqual(new[]
        {
            Str.Format("Images.Generate.AspectFormat", Str.Get("Images.Generate.Shape.Square"), "1:1"),
            Str.Format("Images.Generate.AspectFormat", Str.Get("Images.Generate.Shape.Landscape"), "16:9"),
            Str.Format("Images.Generate.AspectFormat", Str.Get("Images.Generate.Shape.Portrait"), "9:16"),
        }, options.Aspects.Select(a => a.Label).ToArray());
    }
}
