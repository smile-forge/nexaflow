// German letter-to-sound rules, ported from Crane (https://github.com/lucasjinreal/Crane,
// crane-core/src/models/g2p/languages/german_rules.rs), under its licence:
//
// MIT License
//
// Copyright (c) 2024 Nicholas Jela
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
// documentation files (the "Software"), to deal in the Software without restriction, including without limitation the
// rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
// persons to whom the Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
// Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
// WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
// COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

using System.Text;

namespace Nexaflow.AiHost.Speech.Phonemes;

/// <summary>
/// German words no lexicon knows, said by rule: the word split into orthographic syllables, each syllable's letters
/// read with the rules that depend on what is around them (<c>ch</c> after a back or a front vowel, <c>st</c>/<c>sp</c>
/// at a word's start, vowel length from what follows, <c>-ig</c>, final devoicing), and the stress put where prefixes
/// and suffixes say. Written in Kokoro's form, as the German voice was trained: espeak-ng's stress before the vowel and
/// its r sounds (<c>r</c>, <c>ɾ</c>, <c>ɜ</c>), affricates and diphthongs as single tokens (<c>ʦ</c>, <c>ʧ</c>, <c>I</c>,
/// <c>W</c>, <c>Y</c>), <c>ʏ</c> as <c>y</c>.
/// The last resort: a lexicon, a compound's parts or English say a word better when they know it.
/// </summary>
internal static class GermanLetterToSound
{
    private const char Stress = 'ˈ';

    /// <summary>Prefixes that put the stress on the syllable after them, longest first so the more specific wins.</summary>
    private static readonly string[] UnstressedPrefixes =
        ["entgegen", "durch", "wider", "miss", "nach", "bei", "mit", "ver", "zer", "ent", "emp", "ge", "be", "er"];

    /// <summary>Suffixes that take the stress themselves.</summary>
    private static readonly string[] StressedSuffixes = ["ung", "schaft", "tion", "ismus"];

    /// <summary>The phonemes for <paramref name="word"/>, or nothing when it has no German letters.</summary>
    public static string Phonemes(string word)
    {
        var letters = Letters(word);
        var (whole, syllables, starts) = Syllables(letters);
        if (syllables.Count == 0) return "";

        // An unstressed prefix ends where the stem starts: its st and sp are said ʃt and ʃp (verstehen, besprechen).
        int prefix = UnstressedPrefixLength(whole);
        if (prefix > 0 && prefix < starts.Length) starts[prefix] = true;

        int stressed = StressedSyllable(syllables, whole);
        var said = new StringBuilder();
        for (int i = 0; i < syllables.Count; i++)
        {
            var (start, end) = syllables[i];
            var syllable = Syllable(whole[start..end], whole, starts, start, stressed: i == stressed);
            if (i == stressed) syllable = StressBeforeVowel(syllable);
            said.Append(syllable);
        }
        return said.ToString();
    }

    private static bool IsLetter(char c) => c is (>= 'a' and <= 'z') or 'ä' or 'ö' or 'ü' or 'ß';

    private static bool IsVowel(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u' or 'y' or 'ä' or 'ö' or 'ü';

    /// <summary>The word lower-cased, with only German letters and hyphens, which mark where its morphemes meet.</summary>
    private static char[] Letters(string word)
        => [.. word.ToLowerInvariant().Where(c => IsLetter(c) || c == '-')];

    private static bool At(ReadOnlySpan<char> chars, int i, string pattern)
        => i + pattern.Length <= chars.Length && chars.Slice(i, pattern.Length).SequenceEqual(pattern);

    private static bool StartsWith(char[] chars, string pattern) => chars.Length > pattern.Length && At(chars, 0, pattern);

    private static bool EndsWith(ReadOnlySpan<char> chars, string pattern) => chars.Length >= pattern.Length && At(chars, chars.Length - pattern.Length, pattern);

    /// <summary>The vowel nuclei: diphthongs, <c>ie</c> before no other vowel, doubled vowels, single vowels.</summary>
    private static List<(int Start, int End)> Nuclei(ReadOnlySpan<char> letters)
    {
        var spans = new List<(int, int)>();
        for (int i = 0; i < letters.Length; )
        {
            if (!IsVowel(letters[i])) { i++; continue; }
            if (i + 1 < letters.Length)
            {
                char first = letters[i], second = letters[i + 1];
                bool diphthong = (first is 'a' or 'e' or 'ä' && second == 'u') || (first is 'e' or 'a' or 'o' && second == 'i') || (first == 'e' && second == 'y');
                bool ie = first == 'i' && second == 'e' && !(i + 2 < letters.Length && IsVowel(letters[i + 2]));
                bool doubled = first == second && first is 'a' or 'o' or 'e' or 'i' or 'u';
                if (diphthong || ie || doubled) { spans.Add((i, i + 2)); i += 2; continue; }
            }
            spans.Add((i, i + 1));
            i++;
        }
        return spans;
    }

    /// <summary>A hyphen-free run's syllables: each up to and including its nucleus, the consonants after it joining
    /// the next.</summary>
    private static List<(int Start, int End)> SyllablesOf(ReadOnlySpan<char> letters)
    {
        if (letters.IsEmpty) return [];
        var nuclei = Nuclei(letters);
        if (nuclei.Count == 0) return [(0, letters.Length)];
        var syllables = new List<(int, int)>(nuclei.Count);
        int start = 0;
        for (int i = 0; i < nuclei.Count; i++)
        {
            int end = i + 1 < nuclei.Count ? nuclei[i].End : letters.Length;
            syllables.Add((start, end));
            start = nuclei[i].End;
        }
        return syllables;
    }

    /// <summary>The word without hyphens, its syllables in it, and where each of its morphemes starts.</summary>
    private static (char[] Whole, List<(int Start, int End)> Syllables, bool[] Starts) Syllables(char[] word)
    {
        var whole = word.Where(c => c != '-').ToArray();
        var starts = new bool[whole.Length];
        var syllables = new List<(int, int)>();
        int at = 0;
        foreach (var segment in new string(word).Split('-', StringSplitOptions.RemoveEmptyEntries))
        {
            starts[at] = true;
            foreach (var (s, e) in SyllablesOf(segment)) syllables.Add((at + s, at + e));
            at += segment.Length;
        }
        return (whole, syllables, starts);
    }

    private static int UnstressedPrefixLength(char[] word)
        => UnstressedPrefixes.FirstOrDefault(p => StartsWith(word, p))?.Length ?? 0;

    /// <summary>The stressed syllable: the only one; the last, before a stressed suffix; the first after an unstressed
    /// prefix; otherwise the first.</summary>
    private static int StressedSyllable(List<(int Start, int End)> syllables, char[] whole)
    {
        int n = syllables.Count;
        if (n <= 1) return 0;
        if (StressedSuffixes.Any(s => EndsWith(whole, s))) return n - 1;
        // The syllable holding the first letter after the prefix: a syllable takes the consonants before its vowel, so the
        // prefix's last consonant may already be in it (ver|ste|hen is ve|rste|hen).
        int prefix = UnstressedPrefixLength(whole);
        if (prefix > 0)
            for (int i = 0; i < n; i++)
                if (syllables[i].End > prefix) return i;
        return 0;
    }

    /// <summary><c>ch</c> is the ach-sound after a back vowel (through a silent <c>h</c>: <c>ah</c>), the ich-sound
    /// otherwise.</summary>
    private static string Ch(char[] whole, int i)
    {
        if (i == 0) return "ç";
        int j = i - 1;
        char before = IsVowel(whole[j]) ? whole[j] : whole[j] == 'h' && j > 0 && IsVowel(whole[j - 1]) ? whole[j - 1] : '\0';
        return before is 'a' or 'o' or 'u' ? "x" : "ç";
    }

    private static string Long(char vowel) => vowel switch
    {
        'a' => "aː", 'e' => "eː", 'i' => "iː", 'o' => "oː", 'u' => "uː", 'ä' => "ɛː", 'ö' => "øː", _ => "yː",
    };

    /// <summary>How many consonants follow, up to the next vowel or morpheme: one at most, and the vowel before is long.</summary>
    private static int ConsonantsAfter(char[] whole, int from, bool[] starts)
    {
        int count = 0;
        for (int i = from; i < whole.Length && !IsVowel(whole[i]) && !starts[i]; i++) count++;
        return count;
    }

    /// <summary>One syllable's letters read left to right, the longest rule first.</summary>
    private static string Syllable(ReadOnlySpan<char> syllable, char[] whole, bool[] starts, int offset, bool stressed)
    {
        var said = new StringBuilder(syllable.Length * 2);
        int n = syllable.Length;
        for (int i = 0; i < n; )
        {
            int at = offset + i;
            char c = syllable[i];

            // A doubled consonant is said once: it only marks the vowel before it short.
            if (!IsVowel(c) && c != 'h' && i + 1 < n && syllable[i + 1] == c) { i++; continue; }

            // Letters that depend on what is around them.
            if (At(syllable, i, "tsch")) { said.Append('ʧ'); i += 4; continue; }
            if (At(syllable, i, "sch")) { said.Append('ʃ'); i += 3; continue; }
            if (At(syllable, i, "chs")) { said.Append("ks"); i += 3; continue; }
            if (At(syllable, i, "ch")) { said.Append(Ch(whole, at)); i += 2; continue; }
            if (At(syllable, i, "ng")) { said.Append('ŋ'); i += 2; continue; }
            if (At(syllable, i, "nk")) { said.Append("ŋk"); i += 2; continue; }
            if (At(syllable, i, "pf")) { said.Append("pf"); i += 2; continue; }
            if (At(syllable, i, "qu")) { said.Append("kv"); i += 2; continue; }
            if (At(syllable, i, "st") && starts[at]) { said.Append("ʃt"); i += 2; continue; }
            if (At(syllable, i, "sp") && starts[at]) { said.Append("ʃp"); i += 2; continue; }

            // Consonants read the same wherever they are.
            if (c == 'h') { if (starts[at]) said.Append('h'); i++; continue; }   // silent but at a morpheme's start
            if (c == 'ß') { said.Append('s'); i++; continue; }
            if (At(syllable, i, "tz")) { said.Append('ʦ'); i += 2; continue; }
            if (c == 'z') { said.Append('ʦ'); i++; continue; }
            if (At(syllable, i, "ck")) { said.Append('k'); i += 2; continue; }
            if (c == 'c' && i + 1 < n && syllable[i + 1] is 'e' or 'i') { said.Append('ʦ'); i += 2; continue; }
            if (c == 'c') { said.Append('k'); i++; continue; }
            if (c == 'x') { said.Append("ks"); i++; continue; }
            if (c == 'q') { said.Append('k'); i++; continue; }
            if (c == 'j') { said.Append('j'); i++; continue; }
            if (c == 'v') { said.Append('f'); i++; continue; }
            if (c == 'w') { said.Append('v'); i++; continue; }
            if (c == 'y') { said.Append('y'); i++; continue; }   // always short: System, Typ

            // Vowels.
            if (At(syllable, i, "au")) { said.Append('W'); i += 2; continue; }
            if (At(syllable, i, "ei") || At(syllable, i, "ai") || At(syllable, i, "ey")) { said.Append('I'); i += 2; continue; }
            if (At(syllable, i, "oi") || At(syllable, i, "eu") || At(syllable, i, "äu")) { said.Append('Y'); i += 2; continue; }
            if (At(syllable, i, "ie") && !(i + 2 < n && IsVowel(syllable[i + 2]))) { said.Append("iː"); i += 2; continue; }
            if (IsVowel(c) && i + 1 < n && syllable[i + 1] == c && c is 'a' or 'o' or 'e' or 'i' or 'u') { said.Append(Long(c)); i += 2; continue; }
            if (IsVowel(c) && i + 1 < n && syllable[i + 1] == 'h') { said.Append(Long(c)); i += 2; continue; }   // the h that lengthens
            if (IsVowel(c) && i + 1 < n && syllable[i + 1] == 'ß') { said.Append(Long(c)).Append('s'); i += 2; continue; }
            if (c == 'e' && i + 2 == n && syllable[i + 1] == 'r') { said.Append('ɜ'); i += 2; continue; }   // -er at a syllable's end, as espeak-ng writes it
            // A vowel ending an open syllable is long — an e only where it is stressed (Leben); unstressed, it is a schwa.
            if (i + 1 == n && IsVowel(c) && (c != 'e' || stressed) && ConsonantsAfter(whole, at + 1, starts) <= 1) { said.Append(Long(c)); i++; continue; }
            if (IsVowel(c))
            {
                said.Append(c switch
                {
                    'a' => "a",
                    'e' => (i == n - 1 && ConsonantsAfter(whole, at + 1, starts) <= 1) || (!stressed && i + 2 == n && syllable[i + 1] is 'n' or 'l' or 'm' or 'r' or 's') ? "ə" : "ɛ",
                    'i' => "ɪ", 'o' => "ɔ", 'u' => "ʊ", 'ä' => "ɛ", 'ö' => "ø", _ => "y",
                });
                i++;
                continue;
            }

            if (c == 'r') { said.Append(R(whole, at, starts)); i++; continue; }
            if (At(syllable, i, "ss")) { said.Append('s'); i += 2; continue; }
            if (c == 's')
            {
                bool between = i > 0 && IsVowel(syllable[i - 1]) && i + 1 < n && IsVowel(syllable[i + 1]);
                said.Append(between ? 'z' : 's');
                i++;
                continue;
            }
            said.Append(c switch { 'g' => 'ɡ', _ => c });
            i++;
        }

        // -ig at a syllable's end is the ich-sound — unless its i is part of a diphthong (steig).
        if (EndsWith(syllable, "ig") && !(n >= 3 && IsVowel(syllable[n - 3])) && said.Length > 0 && said[^1] == 'ɡ')
            said[^1] = 'ç';

        // A voiced obstruent ending a syllable is said voiceless.
        if (said.Length > 0)
            said[^1] = said[^1] switch { 'b' => 'p', 'd' => 't', 'ɡ' => 'k', 'v' => 'f', 'z' => 's', var other => other };
        return said.ToString();
    }

    /// <summary>An r as espeak-ng writes German's: <c>r</c> starting a word or between vowels, <c>ɾ</c> in a cluster or
    /// ending a syllable — the symbols the German voice was trained on.</summary>
    private static char R(char[] whole, int at, bool[] starts)
        => starts[at] || (at > 0 && IsVowel(whole[at - 1]) && at + 1 < whole.Length && IsVowel(whole[at + 1])) ? 'r' : 'ɾ';

    /// <summary>The stress mark where espeak-ng puts it: right before the syllable's vowel.</summary>
    private static string StressBeforeVowel(string syllable)
    {
        int vowel = syllable.IndexOfAny(['a', 'e', 'i', 'o', 'u', 'y', 'ɛ', 'ə', 'ɪ', 'ɔ', 'ʊ', 'ø', 'ɐ', 'I', 'W', 'Y']);
        return vowel < 0 ? Stress + syllable : syllable.Insert(vowel, Stress.ToString());
    }
}
