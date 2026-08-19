using System.Collections.Generic;
using Nexaflow.Features.Audio.FileActions;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Audio;

/// <summary>
/// The "As Audio" file action — the difference between opening one track and opening a selection as the
/// queue. The tab parameters are the whole contract: a dropped path or a wrong start index opens the player
/// on the wrong track with nothing to show for it.
/// <para>
/// The "Play folder" action and its 30% gate are covered by <see cref="AudioFileTypesTests"/>; tab titling
/// by <see cref="AudioTabRegistrationTests"/>.
/// </para>
/// </summary>
[TestClass]
public class AudioOpenActionTests
{
    private static (IShellServices Shell, List<Dictionary<string, string>> Opened) Shell()
    {
        var shell = Substitute.For<IShellServices>();
        var opened = new List<Dictionary<string, string>>();
        shell.When(s => s.OpenTab("Audio", Arg.Any<Dictionary<string, string>>()))
             .Do(ci => opened.Add(ci.Arg<Dictionary<string, string>>()));
        return (shell, opened);
    }

    [TestMethod]
    [CoversNode("audio-open-asaudio")]
    public void ASingleFileOpensAlone_NotAsTheWholeFolder()
    {
        var (shell, opened) = Shell();

        Assert.IsTrue(new ShowAudioAction(shell).PerformAction(@"C:\music\track.mp3"));

        Assert.AreEqual(@"C:\music\track.mp3", opened.Single()["paths"],
                        "opening one file must not quietly queue its neighbours — that is Play folder's job");
        Assert.AreEqual("0", opened.Single()["index"]);
    }

    [TestMethod]
    [CoversNode("audio-open-asaudio")]
    public void ASelectionBecomesExactlyThatQueue_InTheOrderGiven()
    {
        var (shell, opened) = Shell();

        var handled = new ShowAudioAction(shell).PerformAction(
            [@"C:\music\b.flac", @"C:\music\cover.jpg", @"C:\music\a.mp3"]);

        Assert.IsTrue(handled);
        CollectionAssert.AreEqual(new[] { @"C:\music\b.flac", @"C:\music\a.mp3" },
                                  opened.Single()["paths"].Split('|'),
                                  "the selection's own order is the queue order, non-audio dropped");
    }

    [TestMethod]
    [CoversNode("audio-open-asaudio")]
    public void ASelectionWithNoAudioIsDeclined()
    {
        var (shell, opened) = Shell();

        Assert.IsFalse(new ShowAudioAction(shell).PerformAction([@"C:\docs\a.txt"]));
        Assert.AreEqual(0, opened.Count);
    }
}
