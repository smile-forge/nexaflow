using System.IO;
using Nexaflow.Features.Audio;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Audio;

[TestClass]
[CoversNode("audio-open")]
public class AudioFileTypesTests
{
    [TestMethod]
    public void IsAudio_RecognisesAudioExtensions_CaseInsensitively()
    {
        Assert.IsTrue(AudioFileTypes.IsAudio(@"c:\music\song.mp3"));
        Assert.IsTrue(AudioFileTypes.IsAudio(@"c:\music\SONG.FLAC"));
        Assert.IsTrue(AudioFileTypes.IsAudio("track.opus"));
        Assert.IsFalse(AudioFileTypes.IsAudio("notes.txt"));
        Assert.IsFalse(AudioFileTypes.IsAudio("image.png"));
    }

    [TestMethod]
    public void EnumerateAudio_ReturnsOnlyAudio_OrderedByName()
    {
        var dir = Path.Combine(Path.GetTempPath(), "audiofiletypes_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "b.mp3"), "");
            File.WriteAllText(Path.Combine(dir, "a.flac"), "");
            File.WriteAllText(Path.Combine(dir, "readme.txt"), "");

            var files = AudioFileTypes.EnumerateAudio(dir);

            Assert.AreEqual(2, files.Count);
            Assert.AreEqual("a.flac", Path.GetFileName(files[0]));
            Assert.AreEqual("b.mp3", Path.GetFileName(files[1]));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
