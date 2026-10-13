"""Builds the French or German lexicon Nexaflow's Kokoro voices read: word -> phonemes, in the form the voices were
trained on, most used words first.

  python -I build_lexicon.py <lang> <sentences dir> <espeak dir> <keys> <out file>

<sentences dir> is Mozilla Common Voice's server/data folder (CC0): <lang>/*.txt, a sentence a line; <espeak dir>
holds espeak-ng.exe and espeak-ng-data. Only the standard library is used.

Words: each word of the sentences as written, the punctuation around it taken off; words seen once (mostly typos and
tokenisation debris) are left out, and the <keys> most used kept.
Keys are lower-cased, except where a word's case variants are each a sizeable share of its uses and espeak-ng says
them differently (German Weg / weg): then each is kept under its own spelling, so lookup is case-sensitive.
French elided clitics (l', d', qu', ...) are split off; the lexicon holds the host word.
Phonemes: espeak-ng --ipa --tie=^ on the word alone, then misaki's post-processing for Kokoro v1.0.
"""
import collections
import gzip
import os
import re
import subprocess
import sys
import unicodedata
from concurrent.futures import ThreadPoolExecutor

# What is taken off a word's ends: punctuation, quotes, brackets and the markup a sentence may carry.
EDGES = ".,;:!?\u2026\"\u00ab\u00bb\u201c\u201d\u201e()[]{}*_`\u2013\u2014/"

# French clitics that elide before a vowel or mute h. The lexicon keys the host word: l'ordinateur -> ordinateur.
# Words whose apostrophe is part of them (aujourd'hui) are keyed whole.
FR_CLITICS = {"l", "d", "j", "qu", "n", "s", "c", "m", "t", "jusqu", "lorsqu", "puisqu", "quoiqu", "presqu", "quelqu"}
WORD = re.compile(r"^[^\W\d_]+(?:['\-][^\W\d_]+)*$")

# misaki's EspeakG2P post-processing (hexgrad/misaki espeak.py, Kokoro v1.0), how Kokoro's French voice and the
# kikiri-tts German fine-tune were trained: language-switch flags removed, tied pairs as single Kokoro tokens, ties and
# hyphens deleted. kikiri-tts also maps ʏ, which is not in Kokoro's vocabulary, to y for German.
FLAGS = re.compile(r"\((?:\^?[a-z]){2,3}(?:-(?:\^?[a-z])+)?\)")
TIED = sorted({
    "a^ɪ": "I", "a^ʊ": "W", "d^z": "ʣ", "d^ʒ": "ʤ", "e^ɪ": "A", "o^ʊ": "O", "ə^ʊ": "Q", "s^s": "S",
    "t^s": "ʦ", "t^ʃ": "ʧ", "ɔ^ɪ": "Y",
}.items())


def kokoro(ipa, lang):
    ps = FLAGS.sub("", ipa)
    for tied, token in TIED:
        ps = ps.replace(tied, token)
    ps = ps.replace("^", "").replace("-", "")
    if lang == "de":
        ps = ps.replace("ʏ", "y")
    return " ".join(ps.split())


def norm(s):
    return unicodedata.normalize("NFC", s).replace("’", "'").replace("ʼ", "'")


def espeak(exe, env, lang, words, chunk=4000, workers=16):
    """Each word's IPA, said alone. A word is its own sentence ("word.") so espeak-ng writes a line for each; a chunk
    whose line count does not match is halved down to single words."""
    voice = {"fr": "fr-fr", "de": "de"}[lang]

    def run(ws):
        p = subprocess.run([exe, "-q", "--ipa", "--tie=^", "-v", voice, "--stdin"], input="\n".join(w + "." for w in ws) + "\n",
                           capture_output=True, text=True, encoding="utf-8", env=env, check=True)
        out = [" ".join(line.split()) for line in p.stdout.splitlines()]
        if len(out) == len(ws):
            return out
        if len(ws) == 1:
            return [" ".join(out)]
        mid = len(ws) // 2
        return run(ws[:mid]) + run(ws[mid:])

    chunks = [words[i:i + chunk] for i in range(0, len(words), chunk)]
    with ThreadPoolExecutor(workers) as pool:
        return [ipa for done in pool.map(run, chunks) for ipa in done]


def main():
    lang, sources, espeak_dir, keys, out = sys.argv[1], sys.argv[2], sys.argv[3], int(sys.argv[4]), sys.argv[5]
    exe = os.path.join(espeak_dir, "espeak-ng.exe")
    env = dict(os.environ, ESPEAK_DATA_PATH=os.path.join(espeak_dir, "espeak-ng-data"))

    written = collections.Counter()   # each word as written, case kept
    folder = os.path.join(sources, lang)
    for name in sorted(os.listdir(folder)):
        if not name.endswith(".txt"):
            continue
        with open(os.path.join(folder, name), encoding="utf-8") as f:
            for line in f:
                for raw in norm(line).split():
                    word = raw.strip(EDGES)
                    if word:
                        written[word] += 1

    def host(word):
        if lang == "fr" and "'" in word:
            head, _, rest = word.partition("'")
            if head.lower() in FR_CLITICS and rest:
                return rest
        return word

    seen = collections.Counter()
    variants = collections.defaultdict(collections.Counter)
    for w, n in written.items():
        w = host(w)
        if not w or len(w) > 40 or not WORD.match(w) or w.endswith("'"):
            continue
        k = w.lower()
        seen[k] += n
        variants[k][w] += n
    ranked = sorted((k for k in seen if seen[k] >= 2), key=lambda k: (-seen[k], k))[:keys]

    # The spellings to say for each key: the usual one, and any other with at least 15% of the case-kept uses.
    spellings = {}
    for k in ranked:
        v = variants[k]
        total = sum(v.values())
        forms = [k] if total == 0 else [w for w, n in sorted(v.items(), key=lambda x: (-x[1], x[0])) if n >= 0.15 * total][:3]
        if lang == "fr" and k not in forms:
            forms.append(k)
        spellings[k] = forms
    said = sorted({w for forms in spellings.values() for w in forms})
    ipa = dict(zip(said, espeak(exe, env, lang, said)))

    lines = []
    for k in ranked:
        forms = spellings[k]
        if len({ipa[w] for w in forms}) == 1:
            lines.append(f"{k}\t{kokoro(ipa[forms[0]], lang)}\n")
        else:
            lines.extend(f"{w}\t{kokoro(ipa[w], lang)}\n" for w in forms)

    os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
    with open(out, "wb") as raw, gzip.GzipFile(filename="", mode="wb", fileobj=raw, compresslevel=9, mtime=0) as gz:
        gz.write("".join(lines).encode("utf-8"))
    print(f"{lang}: {len(ranked)} keys, {len(lines)} entries -> {out}")


if __name__ == "__main__":
    main()
