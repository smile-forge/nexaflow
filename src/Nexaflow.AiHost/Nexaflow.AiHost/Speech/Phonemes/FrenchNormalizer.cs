namespace Nexaflow.AiHost.Speech.Phonemes;

/// <summary>
/// What French says for what is written: numbers in words, <c>un</c> or <c>une</c> by what they count; ordinals
/// (<c>1er</c>, <c>21e</c>); hours (<c>15h30</c>, <c>21 h</c>) and dates; units and currencies, singular below two; letters
/// by their French names; abbreviations in full.
/// </summary>
internal sealed class FrenchNormalizer : SpeechNormalizer
{
    private static readonly string[] Ones =
    [
        "zéro", "un", "deux", "trois", "quatre", "cinq", "six", "sept", "huit", "neuf", "dix",
        "onze", "douze", "treize", "quatorze", "quinze", "seize", "dix-sept", "dix-huit", "dix-neuf",
    ];

    private static readonly string[] Tens = ["", "", "vingt", "trente", "quarante", "cinquante", "soixante"];

    private static readonly string[] MonthNames =
        ["janvier", "février", "mars", "avril", "mai", "juin", "juillet", "août", "septembre", "octobre", "novembre", "décembre"];

    /// <summary>Nouns counted often enough to need <c>une</c>: the feminine ones a reply is likely to count.</summary>
    private static readonly HashSet<string> FeminineNouns = new(StringComparer.OrdinalIgnoreCase)
    {
        "heure", "heures", "minute", "minutes", "seconde", "secondes", "semaine", "semaines", "année", "années", "journée",
        "journées", "fois", "personne", "personnes", "page", "pages", "ligne", "lignes", "image", "images", "photo", "photos",
        "vidéo", "vidéos", "version", "versions", "colonne", "colonnes", "cellule", "cellules", "tâche", "tâches", "réunion",
        "réunions", "étape", "étapes", "erreur", "erreurs", "option", "options", "application", "applications", "fenêtre",
        "fenêtres", "touche", "touches", "chose", "choses", "idée", "idées", "question", "questions", "réponse", "réponses",
        "valeur", "valeurs", "entrée", "entrées", "adresse", "adresses", "clé", "clés", "carte", "cartes", "machine", "machines",
        "fonction", "fonctions", "classe", "classes", "variable", "variables", "table", "tables", "requête", "requêtes",
        "commande", "commandes", "partition", "partitions", "branche", "branches", "pièce", "pièces", "nuit", "nuits",
        "livre", "livres", "année-lumière", "dizaine", "centaine", "liste", "listes", "règle", "règles", "note", "notes",
    };

    private static readonly Dictionary<string, UnitWords> Units = new(StringComparer.Ordinal)
    {
        ["Go"] = new("gigaoctet", "gigaoctets"), ["GB"] = new("gigaoctet", "gigaoctets"), ["Gio"] = new("gibioctet", "gibioctets"), ["GiB"] = new("gibioctet", "gibioctets"),
        ["Mo"] = new("mégaoctet", "mégaoctets"), ["MB"] = new("mégaoctet", "mégaoctets"), ["Mio"] = new("mébioctet", "mébioctets"), ["MiB"] = new("mébioctet", "mébioctets"),
        ["Ko"] = new("kilooctet", "kilooctets"), ["KB"] = new("kilooctet", "kilooctets"), ["kB"] = new("kilooctet", "kilooctets"),
        ["To"] = new("téraoctet", "téraoctets"), ["TB"] = new("téraoctet", "téraoctets"),
        ["km"] = new("kilomètre", "kilomètres"), ["m"] = new("mètre", "mètres"), ["cm"] = new("centimètre", "centimètres"), ["mm"] = new("millimètre", "millimètres"),
        ["kg"] = new("kilo", "kilos"), ["g"] = new("gramme", "grammes"), ["mg"] = new("milligramme", "milligrammes"),
        ["l"] = new("litre", "litres"), ["L"] = new("litre", "litres"), ["ml"] = new("millilitre", "millilitres"), ["mL"] = new("millilitre", "millilitres"), ["cl"] = new("centilitre", "centilitres"),
        ["h"] = new("heure", "heures", Feminine: true), ["min"] = new("minute", "minutes", Feminine: true), ["s"] = new("seconde", "secondes", Feminine: true),
        ["ms"] = new("milliseconde", "millisecondes", Feminine: true),
        ["Hz"] = new("hertz", "hertz"), ["kHz"] = new("kilohertz", "kilohertz"), ["MHz"] = new("mégahertz", "mégahertz"), ["GHz"] = new("gigahertz", "gigahertz"),
        ["°C"] = new("degré Celsius", "degrés Celsius"), ["°"] = new("degré", "degrés"),
        ["W"] = new("watt", "watts"), ["kW"] = new("kilowatt", "kilowatts"), ["kWh"] = new("kilowattheure", "kilowattheures"), ["V"] = new("volt", "volts"),
        ["mAh"] = new("milliampère-heure", "milliampères-heures"), ["px"] = new("pixel", "pixels"), ["fps"] = new("image par seconde", "images par seconde", Feminine: true),
        ["km/h"] = new("kilomètre par heure", "kilomètres par heure"), ["Mbit/s"] = new("mégabit par seconde", "mégabits par seconde"),
        ["Mo/s"] = new("mégaoctet par seconde", "mégaoctets par seconde"), ["MB/s"] = new("mégaoctet par seconde", "mégaoctets par seconde"),
        ["Go/s"] = new("gigaoctet par seconde", "gigaoctets par seconde"), ["%"] = new("pour cent", "pour cent"),
        ["€"] = new("euro", "euros"), ["EUR"] = new("euro", "euros"), ["$"] = new("dollar", "dollars"), ["USD"] = new("dollar", "dollars"),
        ["£"] = new("livre", "livres", Feminine: true), ["CHF"] = new("franc suisse", "francs suisses"), ["x"] = new("fois", "fois", Feminine: true),
    };

    /// <summary>Each letter's French name, as espeak-ng says it in Kokoro's form.</summary>
    private static readonly Dictionary<char, string> Letters = new()
    {
        ['a'] = "ˈa", ['b'] = "bˈe", ['c'] = "sˈe", ['d'] = "dˈe", ['e'] = "ˈø", ['f'] = "ˈɛf", ['g'] = "ʒˈe", ['h'] = "ˈaʃ",
        ['i'] = "ˈi", ['j'] = "ʒˈi", ['k'] = "kˈa", ['l'] = "ˈɛl", ['m'] = "ˈɛm", ['n'] = "ˈɛn", ['o'] = "ˈo", ['p'] = "pˈe",
        ['q'] = "kˈy", ['r'] = "ˈɛʁ", ['s'] = "ˈɛs", ['t'] = "tˈe", ['u'] = "ˈy", ['v'] = "vˈe", ['w'] = "dubləvˈe", ['x'] = "ˈiks",
        ['y'] = "iɡʁˈɛk", ['z'] = "zˈɛd", ['é'] = "ˈe", ['è'] = "ˈɛ",
    };

    private static readonly Dictionary<string, string> Abbreviated = new(StringComparer.Ordinal)
    {
        ["m."] = "monsieur", ["mme"] = "madame", ["mmes"] = "mesdames", ["mlle"] = "mademoiselle", ["dr"] = "docteur", ["pr"] = "professeur",
        ["etc."] = "et cetera", ["env."] = "environ", ["p.ex."] = "par exemple", ["ex."] = "exemple", ["cf."] = "voir", ["n°"] = "numéro",
        ["av."] = "avenue", ["bd"] = "boulevard", ["c.-à-d."] = "c'est-à-dire", ["c.à.d."] = "c'est-à-dire", ["càd"] = "c'est-à-dire",
        ["max."] = "maximum", ["min."] = "minimum", ["approx."] = "approximativement", ["tél."] = "téléphone", ["réf."] = "référence",
        ["rdv"] = "rendez-vous", ["qqn"] = "quelqu'un", ["qqch"] = "quelque chose", ["svp"] = "s'il vous plaît", ["stp"] = "s'il te plaît",
        ["vs"] = "versus", ["vs."] = "versus", ["j.-c."] = "Jésus-Christ",
    };

    private static readonly HashSet<string> Read = new(StringComparer.Ordinal)
    {
        "json", "yaml", "nasa", "otan", "onu", "unesco", "ram", "rom", "sim", "pin", "ovni", "sida", "insee", "ascii", "gif", "jpeg",
        "jpg", "zip", "covid", "laser", "radar", "wifi", "fifa", "uefa", "cac", "erasmus", "apec",
    };

    protected override IReadOnlyDictionary<string, string> Abbreviations => Abbreviated;
    protected override IReadOnlySet<string> ReadAcronyms => Read;
    protected override IReadOnlySet<string> Months { get; } = new HashSet<string>(MonthNames, StringComparer.OrdinalIgnoreCase);
    protected override bool HourMarks => true;

    protected override UnitWords? Unit(string written) => Units.GetValueOrDefault(written);

    protected override bool Many(decimal value) => Math.Abs(value) >= 2;

    protected override string? Letter(char letter) => Letters.GetValueOrDefault(letter);

    protected override string Say(SaidSymbol symbol) => symbol switch
    {
        SaidSymbol.Point => "point", SaidSymbol.Decimal => "virgule", SaidSymbol.Percent => "pour cent", SaidSymbol.At => "arobase",
        SaidSymbol.Slash => "slash", SaidSymbol.Minus => "moins", SaidSymbol.Plus => "plus", SaidSymbol.Times => "fois",
        SaidSymbol.Equals => "égale", SaidSymbol.And => "et", _ => "",
    };

    protected override IEnumerable<SpokenWord> Cardinal(long n, NumberUse use)
        => Words(Number(n, Feminine(use.Counted)));

    protected override IEnumerable<SpokenWord> Fraction(string digits)
    {
        // Zeros first are said one by one (zéro virgule zéro cinq), the rest as a number.
        int zeros = digits.TakeWhile(d => d == '0').Count();
        var words = Enumerable.Repeat("zéro", zeros).ToList();
        if (zeros < digits.Length) words.Add(Number(long.Parse(digits[zeros..]), feminine: false));
        return Words(string.Join(' ', words));
    }

    protected override IEnumerable<SpokenWord> Ordinal(long n, string? before) => Words(OrdinalWords(n));

    protected override IEnumerable<SpokenWord> Time(int hours, int minutes)
    {
        var words = $"{Number(hours, feminine: true)} {(hours >= 2 ? "heures" : "heure")}";
        return Words(minutes > 0 ? $"{words} {Number(minutes, feminine: true)}" : words);
    }

    protected override IEnumerable<SpokenWord> Date(int day, int month, int? year, string? before)
    {
        var words = $"{(day == 1 ? "premier" : Number(day, false))} {MonthNames[month - 1]}";
        return Words(year is { } y ? $"{words} {Number(y, false)}" : words);
    }

    private static bool Feminine(string? counted)
    {
        if (counted is null) return false;
        if (FeminineNouns.Contains(counted) || Units.TryGetValue(counted, out var unit) && unit.Feminine) return true;
        var word = counted.ToLowerInvariant();
        return word.EndsWith("tion") || word.EndsWith("sion") || word.EndsWith("té") || word.EndsWith("ure") || word.EndsWith("ette")
            || word.EndsWith("ence") || word.EndsWith("ance") || word.EndsWith("ise") || word.EndsWith("ude");
    }

    /// <summary>A whole number in French words: <c>vingt et un</c>, <c>quatre-vingts</c>, <c>deux cent un</c>,
    /// <c>deux mille vingt-six</c>.</summary>
    internal static string Number(long n, bool feminine)
    {
        if (n < 0) return "moins " + Number(-n, feminine);
        if (n == 0) return "zéro";
        var parts = new List<string>();
        foreach (var (size, one, many) in new[] { (1_000_000_000_000L, "billion", "billions"), (1_000_000_000L, "milliard", "milliards"), (1_000_000L, "million", "millions") })
        {
            if (n < size) continue;
            long count = n / size;
            parts.Add($"{Number(count, false)} {(count >= 2 ? many : one)}");
            n %= size;
        }
        if (n >= 1000)
        {
            long thousands = n / 1000;
            // mille is said alone for one thousand, and makes cent and vingt before it lose their plural s.
            parts.Add(thousands == 1 ? "mille" : $"{Hundreds((int)thousands, false, beforeThousand: true)} mille");
            n %= 1000;
        }
        if (n > 0) parts.Add(Hundreds((int)n, feminine, beforeThousand: false));
        return string.Join(' ', parts);
    }

    private static string Hundreds(int n, bool feminine, bool beforeThousand)
    {
        int hundreds = n / 100, rest = n % 100;
        string tail = rest > 0 ? BelowHundred(rest, feminine, beforeThousand) : "";
        if (hundreds == 0) return tail;
        string head = hundreds == 1 ? "cent" : $"{Ones[hundreds]} cent{(rest == 0 && !beforeThousand ? "s" : "")}";
        return rest > 0 ? $"{head} {tail}" : head;
    }

    private static string BelowHundred(int n, bool feminine, bool beforeThousand)
    {
        if (n == 1) return feminine ? "une" : "un";
        if (n < 20) return Ones[n];
        int tens = n / 10, ones = n % 10;
        if (tens <= 6)
            return ones == 0 ? Tens[tens] : ones == 1 ? $"{Tens[tens]} et {(feminine ? "une" : "un")}" : $"{Tens[tens]}-{Ones[ones]}";
        if (tens == 7)
            return ones == 1 ? "soixante et onze" : $"soixante-{Ones[10 + ones]}";
        if (tens == 8)
            return ones == 0 ? (beforeThousand ? "quatre-vingt" : "quatre-vingts") : $"quatre-vingt-{(ones == 1 && feminine ? "une" : Ones[ones])}";
        return $"quatre-vingt-{Ones[10 + ones]}";
    }

    /// <summary>An ordinal in French words: <c>premier</c>, <c>deuxième</c>, <c>vingt et unième</c>, <c>centième</c>.</summary>
    internal static string OrdinalWords(long n)
    {
        if (n == 1) return "premier";
        var words = Number(n, false).Split(' ');
        var last = words[^1];
        var hyphen = last.LastIndexOf('-');
        var head = hyphen >= 0 ? last[..(hyphen + 1)] : "";
        var word = hyphen >= 0 ? last[(hyphen + 1)..] : last;
        word = word switch
        {
            "un" => "unième", "cinq" => "cinquième", "neuf" => "neuvième",
            "cents" => "centième", "vingts" => "vingtième",
            _ when word.EndsWith('e') => word[..^1] + "ième",
            _ => word + "ième",
        };
        words[^1] = head + word;
        return string.Join(' ', words);
    }

    private static IEnumerable<SpokenWord> Words(string words)
        => words.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => new SpokenWord(w));
}

