namespace Nexaflow.AiHost.Speech.Phonemes;

/// <summary>
/// What German says for what is written: a number as one word made of its parts (<c>zweitausendsechsundzwanzig</c>),
/// <c>eins</c> alone and <c>ein</c> or <c>eine</c> before what it counts; ordinals with the ending their article asks
/// for (<c>am dritten</c>, <c>der dritte</c>); hours (<c>14:30 Uhr</c>) and dates; years by the hundred before 2000;
/// units and currencies; letters by their German names; abbreviations in full.
/// </summary>
internal sealed class GermanNormalizer : SpeechNormalizer
{
    private static readonly string[] Ones =
    [
        "null", "eins", "zwei", "drei", "vier", "fünf", "sechs", "sieben", "acht", "neun", "zehn",
        "elf", "zwölf", "dreizehn", "vierzehn", "fünfzehn", "sechzehn", "siebzehn", "achtzehn", "neunzehn",
    ];

    private static readonly string[] Tens = ["", "", "zwanzig", "dreißig", "vierzig", "fünfzig", "sechzig", "siebzig", "achtzig", "neunzig"];

    private static readonly string[] MonthNames =
        ["Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember"];

    private static readonly Dictionary<string, UnitWords> Units = new(StringComparer.Ordinal)
    {
        ["GB"] = new("Gigabyte", "Gigabyte"), ["Go"] = new("Gigabyte", "Gigabyte"), ["GiB"] = new("Gibibyte", "Gibibyte"),
        ["MB"] = new("Megabyte", "Megabyte"), ["MiB"] = new("Mebibyte", "Mebibyte"), ["KB"] = new("Kilobyte", "Kilobyte"), ["kB"] = new("Kilobyte", "Kilobyte"),
        ["TB"] = new("Terabyte", "Terabyte"),
        ["km"] = new("Kilometer", "Kilometer"), ["m"] = new("Meter", "Meter"), ["cm"] = new("Zentimeter", "Zentimeter"), ["mm"] = new("Millimeter", "Millimeter"),
        ["kg"] = new("Kilo", "Kilo"), ["g"] = new("Gramm", "Gramm"), ["mg"] = new("Milligramm", "Milligramm"),
        ["l"] = new("Liter", "Liter"), ["L"] = new("Liter", "Liter"), ["ml"] = new("Milliliter", "Milliliter"), ["mL"] = new("Milliliter", "Milliliter"),
        ["h"] = new("Stunde", "Stunden", Feminine: true), ["Std."] = new("Stunde", "Stunden", Feminine: true),
        ["min"] = new("Minute", "Minuten", Feminine: true), ["Min."] = new("Minute", "Minuten", Feminine: true),
        ["s"] = new("Sekunde", "Sekunden", Feminine: true), ["Sek."] = new("Sekunde", "Sekunden", Feminine: true),
        ["ms"] = new("Millisekunde", "Millisekunden", Feminine: true),
        ["Hz"] = new("Hertz", "Hertz"), ["kHz"] = new("Kilohertz", "Kilohertz"), ["MHz"] = new("Megahertz", "Megahertz"), ["GHz"] = new("Gigahertz", "Gigahertz"),
        ["°C"] = new("Grad Celsius", "Grad Celsius"), ["°"] = new("Grad", "Grad"),
        ["W"] = new("Watt", "Watt"), ["kW"] = new("Kilowatt", "Kilowatt"), ["kWh"] = new("Kilowattstunde", "Kilowattstunden", Feminine: true), ["V"] = new("Volt", "Volt"),
        ["mAh"] = new("Milliamperestunde", "Milliamperestunden", Feminine: true), ["px"] = new("Pixel", "Pixel"), ["fps"] = new("Bild pro Sekunde", "Bilder pro Sekunde"),
        ["km/h"] = new("Kilometer pro Stunde", "Kilometer pro Stunde"), ["Mbit/s"] = new("Megabit pro Sekunde", "Megabit pro Sekunde"),
        ["MB/s"] = new("Megabyte pro Sekunde", "Megabyte pro Sekunde"), ["GB/s"] = new("Gigabyte pro Sekunde", "Gigabyte pro Sekunde"),
        ["%"] = new("Prozent", "Prozent"), ["€"] = new("Euro", "Euro"), ["EUR"] = new("Euro", "Euro"), ["$"] = new("Dollar", "Dollar"),
        ["USD"] = new("Dollar", "Dollar"), ["£"] = new("Pfund", "Pfund"), ["CHF"] = new("Franken", "Franken"), ["x"] = new("mal", "mal"),
    };

    /// <summary>Each letter's German name, as espeak-ng says it in Kokoro's form.</summary>
    private static readonly Dictionary<char, string> Letters = new()
    {
        ['a'] = "ˈaː", ['b'] = "bˈeː", ['c'] = "ʦˈeː", ['d'] = "dˈeː", ['e'] = "ˈeː", ['f'] = "ˈɛf", ['g'] = "ɡˈeː", ['h'] = "hˈaː",
        ['i'] = "ˈiː", ['j'] = "jˈɔt", ['k'] = "kˈaː", ['l'] = "ˈɛl", ['m'] = "ˈɛm", ['n'] = "ˈɛn", ['o'] = "ˈoː", ['p'] = "pˈeː",
        ['q'] = "kˈuː", ['r'] = "ˈɛɾ", ['s'] = "ˈɛs", ['t'] = "tˈeː", ['u'] = "ˈuː", ['v'] = "fˈW", ['w'] = "vˈeː", ['x'] = "ˈɪks",
        ['y'] = "ˈypsilɔn", ['z'] = "ʦˈɛt", ['ä'] = "ˈɛː", ['ö'] = "ˈøː", ['ü'] = "ˈyː", ['ß'] = "ɛsʦˈɛt",
    };

    private static readonly Dictionary<string, string> Abbreviated = new(StringComparer.Ordinal)
    {
        ["z.b."] = "zum Beispiel", ["d.h."] = "das heißt", ["u.a."] = "unter anderem", ["bzw."] = "beziehungsweise", ["usw."] = "und so weiter",
        ["ca."] = "circa", ["dr."] = "Doktor", ["nr."] = "Nummer", ["str."] = "Straße", ["etc."] = "et cetera", ["evtl."] = "eventuell",
        ["ggf."] = "gegebenenfalls", ["inkl."] = "inklusive", ["exkl."] = "exklusive", ["vgl."] = "vergleiche", ["bspw."] = "beispielsweise",
        ["z.t."] = "zum Teil", ["u.u."] = "unter Umständen", ["o.ä."] = "oder Ähnliches", ["abs."] = "Absatz", ["hr."] = "Herr", ["fr."] = "Frau",
        ["prof."] = "Professor", ["max."] = "maximal", ["min."] = "minimal", ["mind."] = "mindestens", ["tel."] = "Telefon",
        ["u.v.m."] = "und vieles mehr", ["uvm."] = "und vieles mehr", ["v.a."] = "vor allem", ["zzgl."] = "zuzüglich", ["sog."] = "sogenannt",
        ["geb."] = "geboren", ["ggü."] = "gegenüber", ["m.e."] = "meines Erachtens", ["i.d.r."] = "in der Regel", ["o.g."] = "oben genannt",
        ["jh."] = "Jahrhundert", ["mio."] = "Millionen", ["mrd."] = "Milliarden", ["vs."] = "versus", ["mwst."] = "Mehrwertsteuer",
        ["bzgl."] = "bezüglich",
    };

    private static readonly HashSet<string> Read = new(StringComparer.Ordinal)
    {
        "json", "yaml", "nasa", "nato", "uno", "unesco", "ram", "rom", "sim", "pin", "ascii", "gif", "jpeg", "jpg", "zip", "ufo",
        "aids", "tüv", "dax", "covid", "laser", "radar", "wlan", "fifa", "uefa", "bafög", "ikea",
    };

    /// <summary>The articles and prepositions an ordinal's ending follows.</summary>
    private static readonly Dictionary<string, string> Endings = new(StringComparer.Ordinal)
    {
        ["der"] = "", ["die"] = "", ["das"] = "", ["am"] = "n", ["vom"] = "n", ["zum"] = "n", ["im"] = "n", ["beim"] = "n", ["den"] = "n",
        ["dem"] = "n", ["des"] = "n", ["zur"] = "n", ["ab"] = "m", ["seit"] = "m", ["bis"] = "n", ["ein"] = "r", ["kein"] = "r",
    };

    protected override IReadOnlyDictionary<string, string> Abbreviations => Abbreviated;
    protected override IReadOnlySet<string> ReadAcronyms => Read;
    protected override IReadOnlySet<string> Months { get; } = new HashSet<string>(MonthNames, StringComparer.Ordinal);
    protected override bool DotOrdinals => true;

    protected override bool SaidByTime(string word) => word == "Uhr";

    protected override UnitWords? Unit(string written) => Units.GetValueOrDefault(written);

    protected override bool Many(decimal value) => value != 1;

    protected override string? Letter(char letter) => Letters.GetValueOrDefault(letter);

    protected override string Say(SaidSymbol symbol) => symbol switch
    {
        SaidSymbol.Point => "Punkt", SaidSymbol.Decimal => "Komma", SaidSymbol.Percent => "Prozent", SaidSymbol.At => "at",
        SaidSymbol.Slash => "Schrägstrich", SaidSymbol.Minus => "minus", SaidSymbol.Plus => "plus", SaidSymbol.Times => "mal",
        SaidSymbol.Equals => "gleich", SaidSymbol.And => "und", _ => "",
    };

    protected override IEnumerable<SpokenWord> Cardinal(long n, NumberUse use)
    {
        if (n == 1 && use.Counted is { } counted && char.IsUpper(counted[0]))
            return [new SpokenWord(Feminine(counted) ? "eine" : "ein")];
        // A number of a thousand-odd that counts nothing is a year: neunzehnhundertneunundneunzig.
        if (use.Counted is null && use.Final && n is >= 1100 and < 2000) return Year((int)n);
        return Number(n);
    }

    protected override IEnumerable<SpokenWord> Fraction(string digits)
        => digits.Select(d => Word([Ones[d - '0']]));   // drei Komma eins vier: each digit said

    protected override IEnumerable<SpokenWord> Ordinal(long n, string? before)
    {
        // Up to nineteen the ordinal ends in -te (erste, dritte, siebte); from twenty, -ste (zwanzigste, hundertste).
        var parts = NumberParts(n, final: true);
        var last = parts[^1];
        bool small = n % 100 is > 0 and < 20;
        string stem = !small ? last + "st" : last switch { "eins" => "erst", "drei" => "dritt", "sieben" => "siebt", "acht" => "acht", _ => last + "t" };
        parts[^1] = stem + "e" + (before is not null && Endings.TryGetValue(before, out var ending) ? ending : "r");
        return [Word(parts)];
    }

    protected override IEnumerable<SpokenWord> Time(int hours, int minutes)
    {
        var words = new List<SpokenWord> { hours == 1 ? new SpokenWord("ein") : Word(NumberParts(hours, final: true)), new("Uhr") };
        if (minutes > 0) words.Add(Word(NumberParts(minutes, final: true)));
        return words;
    }

    protected override IEnumerable<SpokenWord> Date(int day, int month, int? year, string? before)
    {
        var words = Ordinal(day, before).ToList();
        words.Add(new SpokenWord(MonthNames[month - 1]));
        if (year is { } y) words.AddRange(Year(y));
        return words;
    }

    private static IEnumerable<SpokenWord> Year(int year)
        => year is >= 1100 and < 2000 && year % 100 != 0
            ? [Word([.. NumberParts(year / 100, final: false), "hundert", .. NumberParts(year % 100, final: true)])]
            : Number(year);

    private static bool Feminine(string noun)
    {
        if (Units.Values.FirstOrDefault(u => u.One == noun) is { Feminine: true }) return true;
        return noun.EndsWith("ung") || noun.EndsWith("heit") || noun.EndsWith("keit") || noun.EndsWith("schaft") || noun.EndsWith("ion")
            || noun.EndsWith("tät") || noun.EndsWith("ik") || noun.EndsWith("ei") || noun.EndsWith("ie") || noun.EndsWith('e');
    }

    /// <summary>A whole number in German words: one word, made of parts, up to a million; millions and above apart.</summary>
    internal static IEnumerable<SpokenWord> Number(long n)
    {
        if (n < 0) return [new SpokenWord("minus"), .. Number(-n)];
        var words = new List<SpokenWord>();
        foreach (var (size, one, many) in new[] { (1_000_000_000_000L, "Billion", "Billionen"), (1_000_000_000L, "Milliarde", "Milliarden"), (1_000_000L, "Million", "Millionen") })
        {
            if (n < size) continue;
            long count = n / size;
            words.AddRange(count == 1 ? [new SpokenWord("eine")] : Number(count));
            words.Add(new SpokenWord(count == 1 ? one : many));
            n %= size;
        }
        if (n > 0 || words.Count == 0) words.Add(Word(NumberParts(n, final: true)));
        return words;
    }

    /// <summary>The parts of a number below a million: <c>zwei tausend sechs und zwanzig</c>. <paramref name="final"/>
    /// says whether it ends the number, where one is <c>eins</c> rather than <c>ein</c>.</summary>
    internal static List<string> NumberParts(long n, bool final)
    {
        if (n == 0) return ["null"];
        var parts = new List<string>();
        if (n >= 1000)
        {
            long thousands = n / 1000;
            if (thousands > 1) parts.AddRange(NumberParts(thousands, final: false));
            parts.Add("tausend");
            n %= 1000;
        }
        if (n >= 100)
        {
            long hundreds = n / 100;
            if (hundreds > 1) parts.Add(Ones[hundreds]);
            parts.Add("hundert");
            n %= 100;
        }
        if (n > 0)
        {
            if (n < 20) parts.Add(n == 1 && !final ? "ein" : Ones[n]);
            else
            {
                long ones = n % 10;
                if (ones > 0) parts.AddRange([ones == 1 ? "ein" : Ones[ones], "und"]);
                parts.Add(Tens[n / 10]);
            }
        }
        return parts;
    }

    private static SpokenWord Word(IReadOnlyList<string> parts)
        => new(string.Concat(parts)) { Parts = parts.Count > 1 ? parts : null };
}
