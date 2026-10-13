using System.Text.Json;
using Nexaflow.AiHost.Speech;
using Nexaflow.AiHost.Speech.Phonemes;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// The French and German phonemisers on real replies, against espeak-ng reading the same sentences whole — the
/// convention the voices were trained on: how many sentences come out with the same sounds. Runs where
/// <c>NEXAFLOW_TEST_LEXICONS</c> names a folder holding the built lexicons (<c>fr.tsv.gz</c>, <c>de.tsv.gz</c>) and the
/// espeak-ng readings (<c>context-fr-sentences.jsonl</c>, <c>context-de-sentences.jsonl</c>: each a sentence and its
/// <c>espeak</c> phonemes).
/// </summary>
[TestClass]
[CoversNode("owned-speech")]
public class PhonemizerAgainstEspeakTests
{
    private static string? Folder => Environment.GetEnvironmentVariable("NEXAFLOW_TEST_LEXICONS");

    private static string Bare(string phonemes) => new([.. phonemes.Where(c => c is not ('ˈ' or 'ˌ' or ' ' or ',' or '.' or '!' or '?' or ';' or ':' or '—' or '…'))]);

    private static (int Sentences, int Same, List<string> Differing) Compare(IPhonemizer phonemizer, string readings)
    {
        int n = 0, same = 0;
        var differing = new List<string>();
        foreach (var line in File.ReadLines(readings))
        {
            var row = JsonDocument.Parse(line).RootElement;
            string sentence = row.GetProperty("sentence").GetString()!, espeak = row.GetProperty("espeak").GetString()!;
            string ours = phonemizer.Phonemize(sentence);
            n++;
            if (Bare(ours) == Bare(espeak)) same++;
            else differing.Add($"{sentence}\n    espeak {espeak}\n    ours   {ours}");
        }
        return (n, same, differing);
    }

    [TestMethod]
    [DataRow("fr")]
    [DataRow("de")]
    public void RealReplies_SoundAsEspeakSaysThemWhole(string language)
    {
        var folder = Folder;
        if (folder is null) Assert.Inconclusive("NEXAFLOW_TEST_LEXICONS names no lexicons on this machine.");
        PhonemeLanguage rules = language == "fr" ? new FrenchLanguage() : new GermanLanguage();
        var phonemizer = new LexiconPhonemizer(PhonemeLexicon.Load(Path.Combine(folder, $"{language}.tsv.gz")), rules, new EnglishPhonemizer());

        var (sentences, same, differing) = Compare(phonemizer, Path.Combine(folder, $"context-{language}-sentences.jsonl"));

        Console.WriteLine($"{language}: {same}/{sentences} sentences with espeak-ng's sounds");
        foreach (var d in differing.Take(25)) Console.WriteLine(d);
        Assert.IsTrue(same >= sentences * 0.5, $"{same}/{sentences}");
    }
}
