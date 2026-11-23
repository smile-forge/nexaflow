using Nexaflow.IO.Common.Speech;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Common.Speech;

/// <summary>A listener's noises over the assistant — "mm-hm", "ouais", "ja, genau" — told from words meant for it, whichever
/// provider heard them.</summary>
[TestClass]
[CoversNode("voice-session")]
public class SpokenTurnsTests
{
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
}
