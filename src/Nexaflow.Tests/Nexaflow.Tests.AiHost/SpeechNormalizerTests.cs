using Nexaflow.AiHost.Speech.Phonemes;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// Text as a French or German voice says it: what a reply writes — numbers, times, dates, versions, money, units,
/// acronyms, abbreviations, links, file names, markup, emoji — turned into the words that are said and the pauses
/// between them.
/// </summary>
[TestClass]
[CoversNode("owned-speech")]
public class SpeechNormalizerTests
{
    /// <summary>What is said, as text: words apart, pauses after them, a spelled word in angle brackets.</summary>
    private static string Said(SpeechNormalizer normalizer, string text)
        => string.Concat(normalizer.Normalize(text).Select(u => u switch
        {
            SpokenWord { Phonemes: not null } w => $" <{w.Text}>",
            SpokenWord w => " " + w.Text,
            SpokenPause p => p.Mark,
            _ => "",
        })).Trim();

    [TestMethod]
    [DataRow("Il reste 3,5 Go sur le disque.", "Il reste trois virgule cinq gigaoctets sur le disque.")]
    [DataRow("Le fichier pèse 1 Go.", "Le fichier pèse un gigaoctet.")]
    [DataRow("10 000 personnes", "dix mille personnes")]
    [DataRow("Il a 21 ans et 21 minutes.", "Il a vingt et un ans et vingt et une minutes.")]
    [DataRow("80 pages, 71 lignes, 200 euros", "quatre-vingts pages, soixante et onze lignes, deux cents euros")]
    [DataRow("Ça coûte 12,50 €.", "Ça coûte douze euros cinquante.")]
    [DataRow("C'est 100 % sûr 🙂", "C'est cent pour cent sûr")]
    [DataRow("Il fait 25 °C.", "Il fait vingt-cinq degrés Celsius.")]
    [DataRow("0,05 seconde", "zéro virgule zéro cinq seconde")]
    public void French_SaysNumbers_InTheFormForWhatTheyCount(string written, string said)
        => Assert.AreEqual(said, Said(new FrenchNormalizer(), written));

    [TestMethod]
    [DataRow("Le rendez-vous est à 15h30.", "Le rendez-vous est à quinze heures trente.")]
    [DataRow("Il est 21 h.", "Il est vingt et une heures.")]
    [DataRow("Rendez-vous le 12/03/2026.", "Rendez-vous le douze mars deux mille vingt-six.")]
    [DataRow("le 1er mars", "le premier mars")]
    [DataRow("le 21e siècle", "le vingt et unième siècle")]
    public void French_SaysTimesDatesAndOrdinals(string written, string said)
        => Assert.AreEqual(said, Said(new FrenchNormalizer(), written));

    [TestMethod]
    [DataRow("Installe la version 2.4.1.", "Installe la version deux point quatre point un.")]
    [DataRow("Branche la clé USB.", "Branche la clé <USB>.")]
    [DataRow("Le JSON est valide.", "Le json est valide.")]
    [DataRow("Ouvre le fichier rapport.pdf.", "Ouvre le fichier rapport point <PDF>.")]
    [DataRow("Écris à paul@exemple.fr.", "Écris à paul arobase exemple point fr.")]
    [DataRow("Voir github.com/smile-forge", "Voir github point com slash smile forge")]
    [DataRow("p. ex. le terminal", "par exemple le terminal")]
    [DataRow("Lance **git status** et/ou `git log`.", "Lance git status et ou git log.")]
    [DataRow("- Ouvrir le dossier\n- Supprimer le fichier", "Ouvrir le dossier, Supprimer le fichier")]
    [DataRow("Utilise Get-ChildItem.", "Utilise Get Child Item.")]
    public void French_SaysWhatIsNotWords_ByItsParts(string written, string said)
        => Assert.AreEqual(said, Said(new FrenchNormalizer(), written));

    [TestMethod]
    [DataRow("Ich habe 21 Katzen.", "Ich habe einundzwanzig Katzen.")]
    [DataRow("Es kostet 2026 Euro.", "Es kostet zweitausendsechsundzwanzig Euro.")]
    [DataRow("Das ist 1 Datei und 1 Ordner.", "Das ist eine Datei und ein Ordner.")]
    [DataRow("Seite 101", "Seite hunderteins")]
    [DataRow("Noch 10.000 Schritte.", "Noch zehntausend Schritte.")]
    [DataRow("Es sind 3,14 GB.", "Es sind drei Komma eins vier Gigabyte.")]
    [DataRow("Das kostet 12,50 €.", "Das kostet zwölf Euro fünfzig.")]
    [DataRow("2 Millionen Nutzer", "zwei Millionen Nutzer")]
    public void German_SaysNumbers_AsOneWord_AndOneByWhatItCounts(string written, string said)
        => Assert.AreEqual(said, Said(new GermanNormalizer(), written));

    [TestMethod]
    [DataRow("Das Treffen ist um 14:30 Uhr.", "Das Treffen ist um vierzehn Uhr dreißig.")]
    [DataRow("Wir sehen uns am 3. März.", "Wir sehen uns am dritten März.")]
    [DataRow("Heute ist der 1. Mai.", "Heute ist der erste Mai.")]
    [DataRow("Das war 1999.", "Das war neunzehnhundertneunundneunzig.")]
    [DataRow("Termin: 12.03.2026", "Termin: zwölfter März zweitausendsechsundzwanzig")]
    public void German_SaysTimesDatesAndOrdinals_WithTheEndingTheirArticleAsks(string written, string said)
        => Assert.AreEqual(said, Said(new GermanNormalizer(), written));

    [TestMethod]
    [DataRow("Das geht z. B. mit dem Terminal.", "Das geht zum Beispiel mit dem Terminal.")]
    [DataRow("Steck den USB-Stick ein.", "Steck den <USB> Stick ein.")]
    [DataRow("Führe npm install aus.", "Führe <NPM> install aus.")]
    [DataRow("Die Version 2.4.1 ist da.", "Die Version zwei Punkt vier Punkt eins ist da.")]
    [DataRow("Die IP ist 192.168.1.1.", "Die <IP> ist hundertzweiundneunzig Punkt hundertachtundsechzig Punkt eins Punkt eins.")]
    [DataRow("Fehler CS0246 bei x64", "Fehler <CS> null zwei vier sechs bei <x> vierundsechzig")]
    public void German_SaysWhatIsNotWords_ByItsParts(string written, string said)
        => Assert.AreEqual(said, Said(new GermanNormalizer(), written));
}
