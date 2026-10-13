using Nexaflow.IO.Common.Audio;
using Nexaflow.IO.Common.Speech;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Common.Speech;

/// <summary>What a voice conversation reads from text: its language, and whether an answer is yes or no.</summary>
[TestClass]
[CoversNode("voice-session")]
public class SpokenTextTests
{
    [TestMethod]
    [DataRow("The build failed because a file is missing from the project.", "en")]
    [DataRow("La compilation a échoué parce qu'il manque un fichier dans le projet.", "fr")]
    [DataRow("Der Build ist fehlgeschlagen, weil eine Datei im Projekt fehlt.", "de")]
    [DataRow("Über Größe.", "de")]
    public void ASentence_IsInTheLanguageItsCommonWordsAre(string text, string language)
        => Assert.AreEqual(language, SpokenLanguage.Detect(text));

    [TestMethod]
    [DataRow("OK.")]
    [DataRow("Nexaflow 2.0")]
    [DataRow("")]
    public void TextTooShortOrMixedToTell_HasNoLanguage(string text)
        => Assert.IsNull(SpokenLanguage.Detect(text));

    [TestMethod]
    [DataRow("Yes, go ahead.", "en", true)]
    [DataRow("Go ahead and delete it.", "en", true)]
    [DataRow("No, wait.", "en", false)]
    [DataRow("Oui, vas-y.", "fr", true)]
    [DataRow("Non, pas maintenant.", "fr", false)]
    [DataRow("Ja, mach das.", "de", true)]
    [DataRow("Nein, lieber nicht.", "de", false)]
    [DataRow("Nein.", null, false)]
    [DataRow("What would it delete?", "en", null)]
    [DataRow("Delete the other one.", "en", null)]
    public void AnAnswer_IsYesOrNo_ByHowItStarts(string answer, string? language, bool? yes)
        => Assert.AreEqual(yes, SpokenAnswers.Classify(answer, language));

    [TestMethod]
    public void Pcm16_RoundTrips_AndClipsAtFullScale()
    {
        var pcm = Pcm16.FromSamples([0f, 0.5f, -1f, 2f]);
        var back = Pcm16.ToSamples(pcm);

        Assert.AreEqual(0.5f, back[1], 1e-4);
        Assert.AreEqual(-1f, back[2], 1e-4);
        Assert.AreEqual(32767 / 32768f, back[3], 1e-6, "beyond full scale is clipped, not wrapped");
        Assert.AreEqual(2, Pcm16.ToSamples(pcm.AsSpan(0, 5)).Length, "a trailing odd byte is not a sample");
    }
}
