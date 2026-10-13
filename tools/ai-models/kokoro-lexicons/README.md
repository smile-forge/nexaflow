---
license: cc-by-sa-4.0
language:
  - fr
  - de
tags:
  - kokoro
  - g2p
  - phonemes
  - lexicon
  - text-to-speech
---

# French and German lexicons for Kokoro

Word → phonemes for the 200,000 most used French words and the 400,000 most used German words, in the form
[Kokoro](https://huggingface.co/hexgrad/Kokoro-82M)'s voices read: espeak-ng 1.52.0's IPA, post-processed as
[misaki](https://github.com/hexgrad/misaki) does for Kokoro v1.0 — the convention Kokoro's French voice (`ff_siwis`) and
the kikiri-tts German fine-tune were trained on. Built for [Nexaflow](https://github.com/smile-forge/nexaflow), which
looks words up here instead of running espeak-ng; usable by anything that feeds Kokoro.

| File | Words | Entries | Size |
|---|---|---|---|
| `fr.tsv.gz` | 200,000 | 202,488 | 2.0 MB |
| `de.tsv.gz` | 400,000 | 401,395 | 5.1 MB |

Each is a gzip of UTF-8 lines `word<TAB>phonemes`, most used first.

- **Keys are lower-case**, except where a word's spellings differ in case and in sound (German `Weg` /ˈveːk/ and `weg`
  /ˈvɛk/): then each spelling has its own entry. Look a word up as written, then lower-cased.
- **French elided clitics are not part of a key**: `l'ordinateur` is `l` + `ordinateur`. Words whose apostrophe
  belongs to them (`aujourd'hui`) are keyed whole.
- **Each word was said alone.** What changes between words — French liaison, number voicing (`six heures`), phrase
  stress on function words — is for the reader to apply.

## How it was made

Three frequency lists per language, merged by mean frequency per million words, words seen only once dropped:

- [hermitdave/FrequencyWords](https://github.com/hermitdave/FrequencyWords) 2018 (OpenSubtitles), CC BY-SA 4.0;
- the [Leipzig Corpora Collection](https://wortschatz-leipzig.de/en/download)'s news 2023 and Wikipedia 2021 corpora of
  one million sentences each, CC BY (D. Goldhahn, T. Eckart & U. Quasthoff: Building Large Monolingual Dictionaries at
  the Leipzig Corpora Collection: From 100 to 200 Languages. LREC 2012).

Each word went through `espeak-ng -q --ipa --tie=^` on its own, then misaki's post-processing: language-switch flags
removed, tied pairs mapped to single Kokoro tokens (`t^s` → `ʦ`, `a^ɪ` → `I`), ties and hyphens deleted, and for German
`ʏ` → `y`, as kikiri-tts does (`ʏ` is not in Kokoro's vocabulary).

`Build-KokoroLexicons.ps1` pins every source by hash and drives `build_lexicon.py` (Python's standard library only);
rerunning it with espeak-ng 1.52.0 reproduces these files byte for byte, in about a minute.

## Licence

CC BY-SA 4.0, as the OpenSubtitles frequency lists the ranking draws on. espeak-ng (GPL-3.0) was run as a tool to
produce the phonemes; none of it is included.
