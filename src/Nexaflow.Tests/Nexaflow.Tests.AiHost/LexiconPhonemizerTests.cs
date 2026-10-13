using Nexaflow.AiHost.Speech;
using Nexaflow.AiHost.Speech.Phonemes;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// French and German said from a lexicon: each word found — whole, through an elided clitic, by its hyphenated parts,
/// in English, as a compound, by rule — then changed by the words around it: liaison, numbers voiced by what follows,
/// function words said without stress. In Kokoro's form, as the voices were trained.
/// </summary>
[TestClass]
[CoversNode("owned-speech")]
public class LexiconPhonemizerTests
{
    private static readonly EnglishPhonemizer English = new();

    private static LexiconPhonemizer French(params (string, string)[] entries)
        => new(PhonemeLexicon.Of(entries), new FrenchLanguage(), English);

    private static LexiconPhonemizer German(params (string, string)[] entries)
        => new(PhonemeLexicon.Of(entries), new GermanLanguage(), English);

    [TestMethod]
    public void AWord_IsFoundAsWritten_OrLowerCased_AndAPauseFollowsTheWordBeforeIt()
    {
        var said = French(("bonjour", "bɔ̃ʒˈuʁ"), ("monde", "mˈɔ̃d")).Phonemize("Bonjour, monde.");

        Assert.AreEqual("bɔ̃ʒˈuʁ, mˈɔ̃d.", said);
    }

    [TestMethod]
    public void AnElidedClitic_IsSaidWithTheWordItHangsOn()
    {
        var said = French(("ami", "amˈi"), ("inclue", "ɛ̃klˈy")).Phonemize("l'ami j'inclue");

        Assert.AreEqual("lamˈi ʒɛ̃klˈy", said, "a clitic is never spelled as its letter");
    }

    [TestMethod]
    public void Liaison_SoundsTheConsonantBeforeAVowel_AndNotBeforeAConsonantOrAPause()
    {
        var french = French(("les", "lˈe"), ("amis", "amˈi"), ("livres", "lˈivʁ"), ("vous", "vˈu"), ("avez", "avˈe"), ("héros", "eʁˈo"));

        Assert.AreEqual("lez amˈi", french.Phonemize("les amis"));
        Assert.AreEqual("le lˈivʁ", french.Phonemize("les livres"));
        Assert.AreEqual("vuz avˈe", french.Phonemize("vous avez"));
        Assert.AreEqual("le eʁˈo", french.Phonemize("les héros"), "an h aspiré blocks it");
        Assert.AreEqual("vˈu, avˈe", french.Phonemize("vous, avez"), "nothing carries across a pause");
    }

    [TestMethod]
    public void SixAndDix_AreSaidByWhatFollows()
    {
        var french = French(("six", "sˈis"), ("heures", "ˈœʁ"), ("jours", "ʒˈuʁ"));

        Assert.AreEqual("sˈiz ˈœʁ", french.Phonemize("six heures"));
        Assert.AreEqual("sˈi ʒˈuʁ", french.Phonemize("six jours"));
        Assert.AreEqual("sˈis.", french.Phonemize("six."));
    }

    [TestMethod]
    public void AHyphenatedFrenchWord_IsStressedAtItsEnd()
        => Assert.AreEqual("ʁɑ̃devˈu", French(("rendez", "ʁɑ̃dˈe"), ("vous", "vˈu")).Phonemize("rendez-vous"));

    [TestMethod]
    public void AFunctionWord_LosesItsStressInsideAPhrase_ButNotAtItsEnd()
    {
        var german = German(("ich", "ˈɪç"), ("sehe", "zˈeːə"), ("es", "ˈɛs"));

        Assert.AreEqual("ɪç zˈeːə ˈɛs.", german.Phonemize("Ich sehe es."));
    }

    [TestMethod]
    public void AGermanCompound_IsSaidAsItsWords_StressedOnTheFirst_WithTheLinkingSoundBetween()
    {
        var german = German(("arbeit", "ˈaɾbaIt"), ("zimmer", "ʦˈɪmɐ"));

        Assert.AreEqual("ˈaɾbaItsʦˌɪmɐ", german.Phonemize("Arbeitszimmer"));
    }

    [TestMethod]
    public void ACompoundEndingOnASuffix_IsNotSplit()
    {
        var german = German(("ord", "ˈɔʁt"), ("nung", "nˈʊŋ"), ("ordn", "ˈɔʁdn"), ("ung", "ˈʊŋ"));

        CollectionAssert.AreEqual(null, new GermanLanguage().Compound("Ordnung", w => PhonemeLexicon.Of(("ordn", "x"), ("ung", "y")).Find(w)) as System.Collections.ICollection);
    }

    [TestMethod]
    public void AGermanNumber_NotInTheLexiconWhole_IsSaidAsItsParts()
    {
        var german = German(("ein", "ˈaIn"), ("und", "ˈʊnt"), ("zwanzig", "ʦvˈanʦɪç"));

        Assert.AreEqual("ˈaInˌʊntʦvˌanʦɪç", german.Phonemize("21"));
    }

    [TestMethod]
    public void AnEnglishWord_NoLexiconKnows_IsSaidInEnglish()
    {
        var said = German().Phonemize("workflow");

        StringAssert.Contains(said, "wˈɜɹk");
    }

    [TestMethod]
    public void TheProductsName_IsSaidTheSameWayInEveryReply()
        => Assert.AreEqual("nɛksaflˈo", French().Phonemize("Nexaflow"));

    [TestMethod]
    public void ASpelledAcronym_IsSaidByItsLetters_StressedOnTheLast()
        => Assert.AreEqual("ˌyˌɛsbˈe", French().Phonemize("USB"));

    [TestMethod]
    [DataRow("Schublade", "ʃ")]
    [DataRow("Buch", "x")]
    [DataRow("ich", "ç")]
    [DataRow("Haus", "W")]
    [DataRow("mein", "I")]
    [DataRow("heute", "Y")]
    [DataRow("Katze", "ʦ")]
    [DataRow("deutsch", "ʧ")]
    [DataRow("Tag", "k")]
    [DataRow("fertig", "ç")]
    [DataRow("Wasser", "vˈasɜ")]
    [DataRow("rot", "rˈ")]
    [DataRow("Brot", "bɾ")]
    public void AGermanWordNothingKnows_IsSaidByRule_InKokorosForm(string word, string sound)
        => StringAssert.Contains(GermanLetterToSound.Phonemes(word), sound);

    [TestMethod]
    public void TheRules_StressTheVowel_AsEspeakDoes_AndAfterAnUnstressedPrefix()
    {
        StringAssert.StartsWith(GermanLetterToSound.Phonemes("klettern"), "klˈɛt", "the stress before the vowel, the doubled t said once");
        StringAssert.Contains(GermanLetterToSound.Phonemes("verstehen"), "ʃtˈeː");
        StringAssert.Contains(GermanLetterToSound.Phonemes("Leben"), "lˈeː");
    }
}
