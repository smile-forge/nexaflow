using Nexaflow.Features.Audio;             // AudioConfig, AudioTabRegistration
using Nexaflow.Features.Common;            // IShellServices
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Audio;

/// <summary>Tab title/identity produced by <see cref="AudioTabRegistration.CreatePageDefinition"/>.</summary>
[TestClass]
[CoversNode("audio")]
public class AudioTabRegistrationTests
{
    private static AudioTabRegistration Make()
        => new(Substitute.For<IShellServices>(), new AudioConfig());

    [TestMethod]
    public void WholeFolderQueue_IsTitledForTheFolder_NotTheFirstTrack()
    {
        var page = Make().CreatePageDefinition(new()
        {
            ["paths"] = @"C:\Music\Best Album\01 First.mp3|C:\Music\Best Album\02 Second.mp3",
            ["index"] = "0",
            ["scope"] = "folder",
        });

        Assert.AreEqual("Best Album", page.Title);
    }

    [TestMethod]
    public void SingleFile_IsTitledForTheTrack()
    {
        var page = Make().CreatePageDefinition(new() { ["paths"] = @"C:\Music\Best Album\song.mp3" });

        Assert.AreEqual("song.mp3", page.Title);
    }

    [TestMethod]
    public void MultiSelectionWithoutFolderScope_IsTitledForTheStartingTrack()
    {
        // An explicit selection (not a whole-folder "Play folder") keeps the starting-track title.
        var page = Make().CreatePageDefinition(new()
        {
            ["paths"] = @"C:\Music\a.mp3|C:\Music\b.mp3",
            ["index"] = "1",
        });

        Assert.AreEqual("b.mp3", page.Title);
    }
}
