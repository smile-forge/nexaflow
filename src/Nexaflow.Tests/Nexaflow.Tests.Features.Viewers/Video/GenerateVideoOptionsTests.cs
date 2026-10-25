using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Features.Video.FileActions;
using Nexaflow.Features.Video.ViewModels;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Video;

/// <summary>
/// Gen Video in the New picker: offered where the workspace makes clips from a description, its options what that model
/// offers — a size that makes only some lengths offering only those — and the clip handed to the workspace as it was set up.
/// </summary>
[TestClass]
[CoversNode("video-gen-video")]
public class GenerateVideoOptionsTests
{
    private const string Folder = @"C:\videos";

    private static readonly ClipChoices Veo = new(["16:9", "9:16"], [4, 6, 8])
    {
        Resolutions = [new("720p", []), new("1080p", [8])],
        Seeds       = true,
    };

    private static IAIService Ai(ClipChoices? making, AiAbilityState state = AiAbilityState.Ready)
    {
        var ai = Substitute.For<IAIService>();
        ai.MakesClips.Returns(making);
        ai.StatusOf(Arg.Any<AiAbility>()).Returns(new AiAbilityStatus(state));
        return ai;
    }

    [TestMethod]
    public void ItIsOffered_WhereAModelMakesClips_OrOneOfNexaflowsOwnWaitsOnItsDownload()
    {
        Assert.IsTrue(new GenerateVideoCreateAction(Ai(Veo)).IsOffered);
        Assert.IsFalse(new GenerateVideoCreateAction(Ai(null, AiAbilityState.Unassigned)).IsOffered);
        Assert.IsTrue(new GenerateVideoCreateAction(Ai(null, AiAbilityState.Downloading)).IsOffered);
    }

    [TestMethod]
    public void TheLengths_AreThoseTheSizeChosenMakes_TheOneNearestEightSecondsFirstChosen()
    {
        var options = new GenerateVideoOptions(Ai(Veo), Folder);

        CollectionAssert.AreEqual(new[] { "4", "6", "8" }, options.Lengths.Select(l => l.Value).ToArray());
        Assert.AreEqual("8", options.Length!.Value);
        options.Length = options.Lengths.Single(l => l.Value == "4");

        options.Resolution = options.Resolutions.Single(r => r.Value == "1080p");
        CollectionAssert.AreEqual(new[] { "8" }, options.Lengths.Select(l => l.Value).ToArray(), "1080p makes 8 seconds only");
        Assert.AreEqual("8", options.Length!.Value);

        options.Resolution = options.Resolutions.Single(r => r.Value is null);
        Assert.AreEqual(3, options.Lengths.Count, "the model's own size makes every length");
    }

    [TestMethod]
    public void AClip_IsMadeOnlyOnceSaidWhatHappens_ThenHandedOverAsSet()
    {
        var ai      = Ai(Veo);
        var options = new GenerateVideoOptions(ai, Folder);
        Assert.IsFalse(options.CanCreate);

        options.Prompt     = "Rain runs down a window at night";
        options.Aspect     = options.Aspects.Single(a => a.Value == "9:16");
        options.Resolution = options.Resolutions.Single(r => r.Value == "1080p");
        options.Seed       = "12";
        Assert.IsFalse(options.NegativePrompts, "this model is told nothing to keep out");

        var path = options.Create("rain.mp4");

        Assert.AreEqual(Path.Combine(Folder, "rain.mp4"), path);
        ai.Received(1).MakeClip(Arg.Is<ClipAsked>(c =>
            c.Prompt == "Rain runs down a window at night" && c.Path == path && c.Seconds == 8
            && c.Aspect == "9:16" && c.Resolution == "1080p" && c.Seed == 12 && c.NegativePrompt == null));
    }
}
