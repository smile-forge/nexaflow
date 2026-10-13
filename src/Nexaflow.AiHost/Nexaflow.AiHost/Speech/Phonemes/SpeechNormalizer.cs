using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Nexaflow.AiHost.Speech.Phonemes;

/// <summary>How a number is said where it stands: the word it counts, when one follows it.</summary>
internal readonly record struct NumberUse(string? Counted, bool Final);

/// <summary>A unit or currency as it is said after a number: once, and for more than one.</summary>
internal sealed record UnitWords(string One, string Many, bool Feminine = false);

/// <summary>What a language says for a symbol it reads aloud.</summary>
internal enum SaidSymbol { Point, Decimal, Percent, At, Slash, Minus, Plus, Times, Equals, And }

/// <summary>
/// Text as it is said: what a reply writes, turned into words to look up and the pauses between them. Markdown, emoji
/// and maths are not said. Numbers, times, dates, versions, money and units are said in words, in the forms for what
/// they count; acronyms are spelled unless they are read as words; abbreviations are said in full; links, file names,
/// paths and identifiers are said by their parts. What is written is read the same way for every language; what it is
/// said as is each language's (<see cref="FrenchNormalizer"/>, <see cref="GermanNormalizer"/>).
/// </summary>
internal abstract partial class SpeechNormalizer
{
    // ── what a language says ─────────────────────────────────────────────

    /// <summary>A whole number, as words: in German one word made of parts.</summary>
    protected abstract IEnumerable<SpokenWord> Cardinal(long n, NumberUse use);

    /// <summary>The digits after a decimal separator.</summary>
    protected abstract IEnumerable<SpokenWord> Fraction(string digits);

    /// <summary>An ordinal, given the word before it (German says <c>am dritten</c>, <c>der dritte</c>).</summary>
    protected abstract IEnumerable<SpokenWord> Ordinal(long n, string? before);

    protected abstract IEnumerable<SpokenWord> Time(int hours, int minutes);

    protected abstract IEnumerable<SpokenWord> Date(int day, int month, int? year, string? before);

    /// <summary>A unit or currency by its written form (<c>Go</c>, <c>km/h</c>, <c>€</c>); null when it is none.</summary>
    protected abstract UnitWords? Unit(string written);

    /// <summary>Whether <paramref name="value"/> of a unit takes its plural.</summary>
    protected abstract bool Many(decimal value);

    /// <summary>A letter's name as phonemes, for spelling.</summary>
    protected abstract string? Letter(char letter);

    protected abstract string Say(SaidSymbol symbol);

    /// <summary>Abbreviations and what they stand for, keyed lower-case with their dots and no spaces (<c>z.b.</c>).</summary>
    protected abstract IReadOnlyDictionary<string, string> Abbreviations { get; }

    /// <summary>Acronyms read as words rather than spelled, lower-case.</summary>
    protected abstract IReadOnlySet<string> ReadAcronyms { get; }

    /// <summary>Whether the language writes hours with an <c>h</c> (French <c>15h30</c>, <c>21 h</c>).</summary>
    protected virtual bool HourMarks => false;

    /// <summary>Whether the language writes an ordinal as a number and a dot (German <c>am 3. März</c>).</summary>
    protected virtual bool DotOrdinals => false;

    /// <summary>The months by name, which an ordinal date comes before.</summary>
    protected abstract IReadOnlySet<string> Months { get; }

    /// <summary>A word after a time that the time already says (German <c>Uhr</c>).</summary>
    protected virtual bool SaidByTime(string word) => false;

    // ── reading what is written ─────────────────────────────────────────

    public IReadOnlyList<SpokenUnit> Normalize(string text)
    {
        var chunks = Chunks(Unwritten(text));
        var said = new List<SpokenUnit>();
        for (int i = 0; i < chunks.Count; )
            i += Read(chunks, i, said);
        while (said.Count > 0 && said[0] is SpokenPause) said.RemoveAt(0);
        return said;
    }

    /// <summary>The text without what is not said: code, maths, markup, emoji; line breaks as pauses.</summary>
    internal static string Unwritten(string text)
    {
        text = text.Normalize(NormalizationForm.FormC).Replace('’', '\'').Replace('ʼ', '\'');
        text = Fenced().Replace(text, " ");
        text = Maths().Replace(text, " ");
        text = Image().Replace(text, "$1");
        text = Link().Replace(text, "$1");
        text = Tag().Replace(text, " ");
        text = LineMarker().Replace(text, "");
        text = text.Replace("**", "").Replace("__", "").Replace("~~", "").Replace("`", "").Replace("*", "").Replace("|", ", ");

        var kept = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value is 0xFE0F or 0x200D or 0x20E3 or (>= 0x1F3FB and <= 0x1F3FF)) continue;   // emoji joiners and modifiers
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.OtherSymbol && rune.Value >= 0x2100) continue;   // emoji, dingbats, arrows
            kept.Append(rune);
        }

        // A line that ends without punctuation still ends: a list item, a heading.
        var lines = kept.ToString().Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var joined = new StringBuilder();
        foreach (var line in lines)
        {
            if (joined.Length > 0) joined.Append(".,;:!?…".Contains(joined[^1]) ? " " : ", ");
            joined.Append(line);
        }
        return joined.ToString();
    }

    /// <summary>The text cut at its spaces, with a number written in groups of three (<c>10 000</c>) kept whole.</summary>
    private static List<string> Chunks(string text)
    {
        var parts = text.Split([' ', '\t', ' ', ' ', ' '], StringSplitOptions.RemoveEmptyEntries).ToList();
        for (int i = 0; i + 1 < parts.Count; )
        {
            if (GroupHead().IsMatch(parts[i]) && GroupTail().IsMatch(parts[i + 1]))
            {
                parts[i] += parts[i + 1];
                parts.RemoveAt(i + 1);
            }
            else i++;
        }
        return parts;
    }

    /// <summary>Reads the chunk at <paramref name="at"/> — and any after it that it says with it — and returns how many
    /// chunks it took.</summary>
    private int Read(List<string> chunks, int at, List<SpokenUnit> said)
    {
        if (Abbreviation(chunks, at, said) is > 0 and var taken) return taken;

        var (lead, core, trail) = Peel(chunks[at]);
        if (lead.Contains('—') || lead.Contains('–')) Pause(said, "—");
        string? before = said.LastOrDefault() is SpokenWord w ? w.Text.ToLowerInvariant() : null;
        string? next = at + 1 < chunks.Count ? Peel(chunks[at + 1]).Core : null;

        // German writes an ordinal as its number and a dot (am 3. März): the dot is the number's, not a full stop,
        // when the sentence goes on.
        if (DotOrdinals && trail.StartsWith('.') && DayNumber().IsMatch(core) && next is { Length: > 0 }
            && (char.IsLower(next[0]) || Months.Contains(next)))
        {
            said.AddRange(Ordinal(long.Parse(core), before));
            foreach (char mark in trail[1..]) if (Mark(mark) is { } after) Pause(said, after);
            return 1;
        }

        int used = core.Length == 0 ? 1 : Core(core, next, before, trail.StartsWith('.'), chunks, at, said);
        // What ends the reading is the punctuation after the last chunk it took (1 Go. — the unit's full stop).
        foreach (char mark in used > 1 ? Peel(chunks[at + used - 1]).Trail : trail) if (Mark(mark) is { } pause) Pause(said, pause);
        return used;
    }

    /// <summary>An abbreviation over up to three chunks (<c>p. ex.</c>, <c>z. B.</c>), said in full.</summary>
    private int Abbreviation(List<string> chunks, int at, List<SpokenUnit> said)
    {
        for (int n = Math.Min(3, chunks.Count - at); n >= 1; n--)
        {
            var written = string.Concat(chunks.Skip(at).Take(n));
            var trailing = written.Length - written.TrimEnd(',', ';', ':', '!', '?', ')', '»', '"').Length;
            var key = written[..^trailing].ToLowerInvariant();
            if (!Abbreviations.TryGetValue(key, out var full) && !(key.EndsWith('.') && Abbreviations.TryGetValue(key[..^1], out full)))
                continue;
            Words(said, full);
            // An abbreviation's own dot ends the sentence too, when it is the last thing in it.
            if (at + n == chunks.Count && key.EndsWith('.')) Pause(said, ".");
            foreach (char mark in written[^trailing..]) if (Mark(mark) is { } pause) Pause(said, pause);
            return n;
        }
        return 0;
    }

    private int Core(string core, string? next, string? before, bool dotAfter, List<string> chunks, int at, List<SpokenUnit> said)
    {
        if (Url().IsMatch(core) || Email().IsMatch(core)) { Address(core, said); return 1; }
        if (IsPath(core)) { Path(core, said); return 1; }
        if (FileName().IsMatch(core) && !WordLike().IsMatch(core)) { File(core, said); return 1; }

        if (core.Any(char.IsDigit))
            return Numeric(core, next, before, dotAfter, said);

        if (!core.Any(char.IsLetter)) { Symbols(core, said); return 1; }
        if (CodeLike().IsMatch(core)) { Identifier(core, said); return 1; }
        if (core.Contains('/'))
        {
            // A pair (et/ou, und/oder) said as its two words.
            foreach (var half in core.Split('/', StringSplitOptions.RemoveEmptyEntries)) Core(half, next, before, false, chunks, at, said);
            return 1;
        }
        if (Acronym().IsMatch(core)) { AcronymWord(core, said); return 1; }
        // A word with no vowel is an abbreviation said by its letters (npm, ls, cd).
        if (WordLike().IsMatch(core) && !core.Any(VowelLetter)) { AcronymWord(core.ToUpperInvariant(), said); return 1; }
        // An acronym in a hyphenated word is spelled where the rest is read (KI-Architektur, USB-Stick).
        if (core.Contains('-') && core.Split('-').Any(p => Acronym().IsMatch(p)) && WordLike().IsMatch(core))
        {
            var parts = core.Split('-', StringSplitOptions.RemoveEmptyEntries);
            var rest = new List<string>();
            foreach (var part in parts)
            {
                if (!Acronym().IsMatch(part)) { rest.Add(part); continue; }
                if (rest.Count > 0) { said.Add(new SpokenWord(string.Join('-', rest))); rest.Clear(); }
                AcronymWord(part, said);
            }
            if (rest.Count > 0) said.Add(new SpokenWord(string.Join('-', rest)));
            return 1;
        }
        if (Camel().IsMatch(core)) { Identifier(core, said); return 1; }
        if (WordLike().IsMatch(core)) { said.Add(new SpokenWord(core)); return 1; }
        Identifier(core, said);
        return 1;
    }

    // ── numbers ──────────────────────────────────────────────────────────

    private int Numeric(string core, string? next, string? before, bool dotAfter, List<SpokenUnit> said)
    {
        Match m;
        if ((m = Clock().Match(core)).Success || (HourMarks && (m = HourClock().Match(core)).Success))
        {
            said.AddRange(Time(int.Parse(m.Groups["h"].Value), m.Groups["m"].Value.Length > 0 ? int.Parse(m.Groups["m"].Value) : 0));
            return next is not null && SaidByTime(next) ? 2 : 1;
        }
        // A day and month, with a year or (as German writes it, 12.3.) a closing dot — never a decimal like 12.3.
        if (((m = DayMonthYear().Match(core)).Success && (m.Groups["y"].Value.Length > 0 || core.Contains('/') || dotAfter))
            || (m = IsoDate().Match(core)).Success)
        {
            int day = int.Parse(m.Groups["d"].Value), month = int.Parse(m.Groups["m"].Value);
            int? year = m.Groups["y"].Value.Length > 0 ? FullYear(m.Groups["y"].Value) : null;
            if (month is >= 1 and <= 12 && day is >= 1 and <= 31) { said.AddRange(Date(day, month, year, before)); return 1; }
        }
        // French hours written apart: 21 h, 15 h 30.
        if (HourMarks && next == "h" && Whole().IsMatch(core) && int.Parse(core) is >= 0 and <= 23 and var hours)
        {
            said.AddRange(Time(hours, 0));
            return 2;
        }
        if ((m = Version().Match(core)).Success) { Version(core, said); return 1; }
        if ((m = Ordinal().Match(core)).Success && OrdinalSuffix(m.Groups["s"].Value))
        {
            said.AddRange(Ordinal(long.Parse(m.Groups["n"].Value), before));
            return 1;
        }
        if ((m = Amount().Match(core)).Success)
        {
            string unitText = (m.Groups["pre"].Value + m.Groups["post"].Value).Trim();
            int used = 1;
            if (unitText.Length == 0 && next is not null && Unit(next) is not null) { unitText = next; used = 2; }
            Amount(m.Groups["sign"].Value, m.Groups["int"].Value, m.Groups["frac"].Value, unitText, used == 1 ? next : null, said);
            return used;
        }
        Code(core, said);
        return 1;
    }

    /// <summary>A number, and the unit or currency it counts: <c>12,50 €</c>, <c>3.5GB</c>, <c>25 %</c>.</summary>
    private void Amount(string sign, string whole, string fraction, string unitText, string? counted, List<SpokenUnit> said)
    {
        var unit = unitText.Length > 0 ? Unit(unitText) : null;
        if (unitText.Length > 0 && unit is null)
        {
            Code(sign + whole + (fraction.Length > 0 ? "." + fraction : "") + unitText, said);
            return;
        }
        long n = long.Parse(Digits().Replace(whole, ""), CultureInfo.InvariantCulture);
        decimal value = decimal.Parse(n + (fraction.Length > 0 ? "." + fraction : ""), CultureInfo.InvariantCulture);
        if (sign is "-" or "−") Words(said, Say(SaidSymbol.Minus));
        else if (sign == "+") Words(said, Say(SaidSymbol.Plus));

        bool money = unit is not null && unitText is "€" or "$" or "£" or "CHF" or "EUR" or "USD";
        if (money && fraction.Length == 2)
        {
            // Twelve euros fifty: the cents after the currency.
            said.AddRange(Cardinal(n, new NumberUse(unit!.One, Final: false)));
            Words(said, Many(n) ? unit.Many : unit.One);
            if (fraction != "00") said.AddRange(Cardinal(long.Parse(fraction), new NumberUse(null, Final: true)));
            return;
        }
        said.AddRange(Cardinal(n, new NumberUse(fraction.Length == 0 ? unit?.One ?? counted : null, Final: fraction.Length == 0 && unit is null && counted is null)));
        if (fraction.Length > 0)
        {
            Words(said, Say(SaidSymbol.Decimal));
            said.AddRange(Fraction(fraction));
        }
        if (unit is not null) Words(said, Many(value) ? unit.Many : unit.One);
    }

    private static int FullYear(string y) => y.Length == 2 ? 2000 + int.Parse(y) : int.Parse(y);

    private static bool OrdinalSuffix(string suffix) => suffix is "er" or "re" or "ère" or "e" or "ème" or "eme" or "ᵉ" or "nd" or "nde" or ".";

    /// <summary>A version or an address of numbers: its numbers, and the points between them.</summary>
    private void Version(string core, List<SpokenUnit> said)
    {
        var written = core.TrimStart('v', 'V');
        if (written.Length < core.Length) Spell("v", said);
        Code(written, said);
    }

    /// <summary>Letters and digits written together (<c>CS0246</c>, <c>x64</c>, <c>24H2</c>, <c>2.4.1</c>): runs of letters
    /// spelled or said, numbers of up to three digits said, longer ones digit by digit, points said.</summary>
    private void Code(string core, List<SpokenUnit> said)
    {
        foreach (Match run in Runs().Matches(core))
        {
            var text = run.Value;
            if (char.IsDigit(text[0]))
            {
                if (text.Length <= 3 && (text.Length == 1 || text[0] != '0')) said.AddRange(Cardinal(long.Parse(text), new NumberUse(null, Final: true)));
                else foreach (char d in text) said.AddRange(Cardinal(d - '0', new NumberUse(null, Final: true)));
            }
            else if (char.IsLetter(text[0]))
            {
                if (text.Length > 1 && !text.All(char.IsUpper) && WordLike().IsMatch(text)) said.Add(new SpokenWord(text));
                else AcronymWord(text, said);
            }
            else if (text == ".") Words(said, Say(SaidSymbol.Point));
            else if (text is "-" or "_" or ",") continue;
            else Symbols(text, said);
        }
    }

    // ── words that are not words ────────────────────────────────────────

    private void AcronymWord(string core, List<SpokenUnit> said)
    {
        var plural = core.Length > 2 && core[^1] == 's' && core[..^1].All(char.IsUpper);
        var letters = plural ? core[..^1] : core;
        if (ReadAcronyms.Contains(letters.ToLowerInvariant())) said.Add(new SpokenWord(letters.ToLowerInvariant()));
        else Spell(letters, said);
    }

    /// <summary>Letters said one by one, as one word: each name's stress light but the last's.</summary>
    private void Spell(string letters, List<SpokenUnit> said)
    {
        var names = letters.Select(c => Letter(char.ToLowerInvariant(c))).Where(n => n is not null).Select(n => n!).ToList();
        if (names.Count == 0) return;
        var phonemes = string.Concat(names.Take(names.Count - 1).Select(n => n.Replace('ˈ', 'ˌ'))) + names[^1];
        said.Add(new SpokenWord(letters) { Phonemes = phonemes });
    }

    /// <summary>An identifier by its parts (<c>Get-ChildItem</c>, <c>user_id</c>, <c>System.IO</c>, <c>JavaScript</c>):
    /// split at its punctuation and its changes of case, its points said.</summary>
    private void Identifier(string core, List<SpokenUnit> said)
    {
        foreach (Match part in IdentifierParts().Matches(core))
        {
            var text = part.Value;
            if (text == ".") Words(said, Say(SaidSymbol.Point));
            else if (text.Any(char.IsDigit)) Code(text, said);
            else if (text.Length > 1 && text.All(char.IsUpper)) AcronymWord(text, said);
            else if (text.Length == 1) Spell(text, said);
            else said.Add(new SpokenWord(text));
        }
    }

    /// <summary>A link or an address: its scheme and <c>www</c> left out, its points, slashes and <c>@</c> said.</summary>
    private void Address(string core, List<SpokenUnit> said)
    {
        var text = SchemeOrWww().Replace(core, "").TrimEnd('/');
        foreach (Match part in AddressParts().Matches(text))
        {
            switch (part.Value)
            {
                case ".": Words(said, Say(SaidSymbol.Point)); break;
                case "/": Words(said, Say(SaidSymbol.Slash)); break;
                case "@": Words(said, Say(SaidSymbol.At)); break;
                case "-" or "_": break;
                default: Identifier(part.Value, said); break;
            }
        }
    }

    /// <summary>A path: each of its folders and its file, with a pause between them.</summary>
    private void Path(string core, List<SpokenUnit> said)
    {
        var parts = core.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i].TrimEnd(':');
            if (part.Length == 0 || part is "." or ".." or "~") continue;
            if (i > 0 && said.LastOrDefault() is SpokenWord) Pause(said, ",");
            if (FileName().IsMatch(part)) File(part, said);
            else if (part.Length == 1) Spell(part, said);
            else Identifier(part, said);
        }
    }

    /// <summary>A file name: its name, then <c>point</c> and its extension, spelled unless it is read as a word.</summary>
    private void File(string core, List<SpokenUnit> said)
    {
        int dot = core.LastIndexOf('.');
        if (dot > 0) Identifier(core[..dot], said);
        Words(said, Say(SaidSymbol.Point));
        AcronymWord(core[(dot + 1)..].ToUpperInvariant(), said);
    }

    private void Symbols(string core, List<SpokenUnit> said)
    {
        foreach (char c in core)
        {
            SaidSymbol? symbol = c switch
            {
                '&' => SaidSymbol.And, '+' => SaidSymbol.Plus, '=' => SaidSymbol.Equals, '@' => SaidSymbol.At,
                '%' => SaidSymbol.Percent, '×' => SaidSymbol.Times, _ => null,
            };
            if (symbol is { } s) Words(said, Say(s));
            else if (c is '—' or '–') Pause(said, "—");
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────

    /// <summary>A chunk's leading quotes and brackets, its core, and its trailing punctuation.</summary>
    private static (string Lead, string Core, string Trail) Peel(string chunk)
    {
        int start = 0, end = chunk.Length;
        while (start < end && "\"'«»“”„‚‘(<[{¿¡—–".Contains(chunk[start])) start++;
        while (end > start && ".,;:!?…»\"”’)]}>'—–".Contains(chunk[end - 1])) end--;
        // A clitic's apostrophe belongs to the word (l', qu'); a closing quote does not.
        if (end < chunk.Length && chunk[end] == '\'' && end > start && char.IsLetter(chunk[end - 1]) && end - start <= 6 && chunk[start..end].All(char.IsLetter))
            end++;
        return (chunk[..start], chunk[start..end], chunk[end..]);
    }

    private static string? Mark(char c) => c switch
    {
        ',' => ",", '.' => ".", ';' => ";", ':' => ":", '!' => "!", '?' => "?", '…' => "…", '—' or '–' => "—",
        _ => null,
    };

    private static void Pause(List<SpokenUnit> said, string mark)
    {
        if (said.Count == 0) return;
        if (said[^1] is SpokenPause last)
        {
            // The stronger pause wins: a full stop over a comma.
            if (Strength(mark) > Strength(last.Mark)) said[^1] = new SpokenPause(mark);
            return;
        }
        said.Add(new SpokenPause(mark));

        static int Strength(string m) => m switch { "," => 0, "—" => 1, ";" or ":" => 2, "…" => 3, _ => 4 };
    }

    private static void Words(List<SpokenUnit> said, string words)
    {
        foreach (var word in words.Split(' ', StringSplitOptions.RemoveEmptyEntries)) said.Add(new SpokenWord(word));
    }

    /// <summary>A path: a backslash, a start like <c>~/</c> or <c>C:\</c>, or two slashes — with letters, so a date is not one.</summary>
    private static bool IsPath(string core)
        => core.Any(char.IsLetter)
        && (core.Contains('\\') || core.StartsWith("~/") || core.StartsWith("./") || core.StartsWith("../")
            || (core.Length > 2 && char.IsLetter(core[0]) && core[1] == ':' && core[2] is '\\' or '/')
            || (core.Count(c => c == '/') >= 2 && !core.Contains("://")));

    private static bool VowelLetter(char c) => "aeiouyàâäéèêëîïôöûüùœæAEIOUYÀÂÄÉÈÊËÎÏÔÖÛÜÙ".Contains(c);

    private const string Extensions = "txt|md|pdf|docx?|xlsx?|pptx?|csv|json|ya?ml|xml|html?|css|js|ts|tsx|jsx|py|cs|csproj|sln|slnx|exe|dll|msi|zip|7z|rar|tar|gz|tgz|iso|png|jpe?g|gif|heic|webp|svg|bmp|tiff?|mp4|mov|mkv|avi|mp3|wav|flac|ini|cfg|conf|log|bak|sql|db|sqlite|ps1|sh|bat|cmd|env|lock|toml|key|pem|crt|ics|eml|odt|ods|rtf|nupkg|apk|dmg";

    [GeneratedRegex(@"```.*?(?:```|$)", RegexOptions.Singleline)] private static partial Regex Fenced();
    [GeneratedRegex(@"\$\$.*?\$\$|\$[^$\n]*[\\^_{][^$\n]*\$", RegexOptions.Singleline)] private static partial Regex Maths();
    [GeneratedRegex(@"!\[([^\]]*)\]\([^)]*\)")] private static partial Regex Image();
    [GeneratedRegex(@"\[([^\]]+)\]\([^)]+\)")] private static partial Regex Link();
    [GeneratedRegex(@"</?[A-Za-z][^>]*>")] private static partial Regex Tag();
    [GeneratedRegex(@"(?m)^\s*(?:#{1,6}|[-+•]|\d+[.)])\s+")] private static partial Regex LineMarker();
    [GeneratedRegex(@"^[+\-−]?\d{1,3}$")] private static partial Regex GroupHead();
    [GeneratedRegex(@"^\d{3}(?:[.,]\d+)?(?:[^\d\s].*)?$")] private static partial Regex GroupTail();
    [GeneratedRegex(@"^(?:https?://|www\.)\S+$|^[\w.-]+\.(?:com|org|net|fr|de|io|dev|ch|be|eu|at|ai|app|gov|co|uk)(?:/\S*)?$", RegexOptions.IgnoreCase)] private static partial Regex Url();
    [GeneratedRegex(@"^[\w.+-]+@[\w-]+(?:\.[\w-]+)+$")] private static partial Regex Email();
    [GeneratedRegex(@"^(?:https?://)?(?:www\.)?", RegexOptions.IgnoreCase)] private static partial Regex SchemeOrWww();
    [GeneratedRegex(@"[^./@\-_]+|[./@\-_]")] private static partial Regex AddressParts();
    [GeneratedRegex(@"^[\w\-]+\.(?:" + Extensions + ")$", RegexOptions.IgnoreCase)] private static partial Regex FileName();
    [GeneratedRegex(@"^[^\W\d_]+(?:['\-][^\W\d_]+)*'?$")] private static partial Regex WordLike();
    [GeneratedRegex(@"[_=(){}\[\]<>$#|;\\]|::|^--?\w|\w\.\w+\(|^\w+\.\w+\.\w+|[a-z]\.[A-Za-z]")] private static partial Regex CodeLike();
    [GeneratedRegex(@"^[A-ZÄÖÜÉÈ]{2,7}s?$")] private static partial Regex Acronym();
    [GeneratedRegex(@"[a-zäöüéè][A-ZÄÖÜ]")] private static partial Regex Camel();
    [GeneratedRegex(@"[A-ZÄÖÜÉÈ]+(?=[A-ZÄÖÜÉÈ][a-zäöüéèàçß])|[A-ZÄÖÜÉÈ]?[a-zäöüéèàâêîôûçßœ]+|[A-ZÄÖÜÉÈ]+|\d+|\.")] private static partial Regex IdentifierParts();
    [GeneratedRegex(@"\p{L}+|\d+|[^\p{L}\d]")] private static partial Regex Runs();
    [GeneratedRegex(@"^(?<h>[01]?\d|2[0-3]):(?<m>[0-5]\d)(?::[0-5]\d)?$")] private static partial Regex Clock();
    [GeneratedRegex(@"^(?<h>[01]?\d|2[0-3])[hH](?<m>[0-5]\d)?$")] private static partial Regex HourClock();
    [GeneratedRegex(@"^\d{1,2}$")] private static partial Regex DayNumber();
    [GeneratedRegex(@"^\d+$")] private static partial Regex Whole();
    [GeneratedRegex(@"^(?<d>\d{1,2})[./](?<m>\d{1,2})(?:[./](?<y>\d{4}|\d{2})?)?$")] private static partial Regex DayMonthYear();
    [GeneratedRegex(@"^(?<y>\d{4})-(?<m>\d{2})-(?<d>\d{2})$")] private static partial Regex IsoDate();
    [GeneratedRegex(@"^v?\d+(?:\.\d+){2,}[\w\-]*$|^v\d+(?:\.\d+)?$|^\d{2}H\d$", RegexOptions.IgnoreCase)] private static partial Regex Version();
    [GeneratedRegex(@"^(?<n>\d{1,9})(?<s>er|re|ère|e|ème|eme|ᵉ|nd|nde|\.)$")] private static partial Regex Ordinal();
    [GeneratedRegex(@"^(?<pre>[€$£]|CHF|EUR|USD)?(?<sign>[+\-−]?)(?<int>\d{1,3}(?:[.']\d{3})+|\d+)(?:[.,](?<frac>\d+))?(?<post>\s?(?:[%€$£°]|°C|[A-Za-zµ]+(?:/[A-Za-z]+)?))?$")] private static partial Regex Amount();
    [GeneratedRegex(@"[.']")] private static partial Regex Digits();
}
