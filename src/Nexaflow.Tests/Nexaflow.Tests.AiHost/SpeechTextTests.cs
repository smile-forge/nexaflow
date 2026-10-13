using Nexaflow.AiHost.Speech;
using Nexaflow.IO.Common.Speech;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>What the transcripts and the audio are made into on their way through the host's listening.</summary>
[TestClass]
[CoversNode("ai-host")]
public class SpeechTextTests
{
    [TestMethod]
    [DataRow("What time is it in Paris?", "en")]
    [DataRow("No.", "en")]
    [DataRow("Tu peux dire ça plus simplement ?", "fr")]
    [DataRow("Nein, nicht das.", "de")]
    [DataRow("", "en")]
    public void AFinishedSentence_LooksFinished(string text, string language)
        => Assert.IsTrue(SpokenTurns.LooksFinished(text, language));

    [TestMethod]
    [DataRow("I was thinking that, um,", "en")]
    [DataRow("I was thinking that um", "en")]
    [DataRow("Can you open the", "en")]
    [DataRow("Je voudrais parce que", "fr")]
    [DataRow("Das ist die Datei, die", "de")]
    [DataRow("Ich wollte sagen…", "de")]
    public void ATurnEndingMidPhrase_DoesNot(string text, string language)
        => Assert.IsFalse(SpokenTurns.LooksFinished(text, language));

    [TestMethod]
    public void AWordThatOpensAPhraseInAnotherLanguage_CountsOnlyWhenTheLanguageIsUnknown()
    {
        Assert.IsTrue(SpokenTurns.LooksFinished("Ich habe es und", "fr"), "'und' opens nothing in French");
        Assert.IsFalse(SpokenTurns.LooksFinished("Ich habe es und", null));
    }

    [TestMethod]
    [DataRow("Mm-hmm.", "en", true)]
    [DataRow("Yeah, yeah.", "en", true)]
    [DataRow("Ouais.", "fr", true)]
    [DataRow("Ja, genau.", "de", true)]
    [DataRow("No, stop.", "en", false)]
    [DataRow("Yeah, but what about Friday?", "en", false)]
    [DataRow("", "en", false)]
    public void OnlyListenersNoises_AreABackchannel(string text, string language, bool backchannel)
        => Assert.AreEqual(backchannel, SpokenTurns.IsBackchannel(text, language));

    [TestMethod]
    public void Pcm_IsCutIntoWholeFrames_HoweverItArrives()
    {
        var frames = new PcmFrames(4);
        var pcm = Enumerable.Range(1, 10).SelectMany(i => BitConverter.GetBytes((short)(i * 1000))).ToArray();

        var cut = frames.Add(pcm[..3]).Concat(frames.Add(pcm[3..11])).Concat(frames.Add(pcm[11..])).ToList();

        Assert.AreEqual(2, cut.Count, "the last two samples wait for the rest of their frame");
        Assert.AreEqual(1000 / 32768f, cut[0][0], 1e-6);
        Assert.AreEqual(8000 / 32768f, cut[1][3], 1e-6);
    }

    [TestMethod]
    public void ALongUtterance_IsTranscribedInPiecesCutAtItsQuietestMoment()
    {
        const int rate = 16_000;
        var audio = new float[70 * rate];
        Array.Fill(audio, 0.5f);
        audio.AsSpan(25 * rate, rate / 5).Clear();   // a breath at 25 s

        var pieces = WhisperTranscriber.Pieces(audio).ToList();

        Assert.AreEqual(audio.Length, pieces.Sum(p => p.Length), "nothing is lost between pieces");
        Assert.IsTrue(pieces.All(p => p.Length <= 28 * rate));
        Assert.IsTrue(Math.Abs(pieces[0].Length - 25 * rate) < rate / 5, $"cut at the breath, not at 28 s ({pieces[0].Length / (double)rate:0.00} s)");
        Assert.AreEqual(1, WhisperTranscriber.Pieces(new float[10 * rate]).Count());
    }

    [TestMethod]
    public void TheTranscribersLanguage_IsForcedWhenKnown()
    {
        Assert.AreEqual("<|startoftranscript|><|de|><|transcribe|><|notimestamps|>", WhisperTranscriber.Prompt("de"));
        Assert.AreEqual("<|startoftranscript|>", WhisperTranscriber.Prompt(null));
        Assert.AreEqual("Nein, nicht das.", WhisperTranscriber.Clean("<|startoftranscript|><|de|><|transcribe|><|notimestamps|> Nein, nicht das.<|endoftext|>"));
    }

    [TestMethod]
    public void TheRecognizersLanguageTags_AreDropped()
        => Assert.AreEqual("Hallo zusammen. Wie geht's?", StreamingRecognizer.Clean("Hallo zusammen. <de-DE> Wie geht's? <de-DE>"));
}
