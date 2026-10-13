---
license: cc0-1.0
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

Word → phonemes for the French and German words of Mozilla Common Voice's sentences, in the form
[Kokoro](https://huggingface.co/hexgrad/Kokoro-82M)'s voices read: espeak-ng 1.52.0's IPA, post-processed as
[misaki](https://github.com/hexgrad/misaki) does for Kokoro v1.0 — the convention Kokoro's French voice (`ff_siwis`) and
the kikiri-tts German fine-tune were trained on. Built for [Nexaflow](https://github.com/smile-forge/nexaflow), which
looks words up here instead of running espeak-ng; usable by anything that feeds Kokoro.

| File | Words | Entries | Size |
|---|---|---|---|
| `fr.tsv.gz` | 147,737 | 148,309 | 1.4 MB |
| `de.tsv.gz` | 268,857 | 268,965 | 3.1 MB |

Each is a gzip of UTF-8 lines `word<TAB>phonemes`, most used first.

- **Keys are lower-case**, except where a word's spellings differ in case and in sound (German `Weg` /ˈveːk/ and `weg`
  /ˈvɛk/): then each spelling has its own entry. Look a word up as written, then lower-cased.
- **French elided clitics are not part of a key**: `l'ordinateur` is `l` + `ordinateur`. Words whose apostrophe
  belongs to them (`aujourd'hui`) are keyed whole.
- **Each word was said alone.** What changes between words — French liaison, number voicing (`six heures`), phrase
  stress on function words — is for the reader to apply.

## How it was made

The words are those of the French and German sentences of [Mozilla Common Voice](https://github.com/common-voice/common-voice)
(`server/data`, CC0 1.0) at commit `1017ce69`, each counted as written with the punctuation around it taken off; words
seen only once are left out. Each went through `espeak-ng -q --ipa --tie=^` on its own, then misaki's post-processing:
language-switch flags removed, tied pairs mapped to single Kokoro tokens (`t^s` → `ʦ`, `a^ɪ` → `I`), ties and hyphens
deleted, and for German `ʏ` → `y`, as kikiri-tts does (`ʏ` is not in Kokoro's vocabulary).

`Build-KokoroLexicons.ps1` fetches the sentences at that commit and drives `build_lexicon.py` (Python's standard library
only); rerunning it with espeak-ng 1.52.0 reproduces these files byte for byte, in about a minute.

Spoken by Kokoro and heard back by Whisper large-v3-turbo, 100 real French and 100 real German assistant replies read
through these lexicons were understood as well as espeak-ng reading them whole: 3.4% of words wrong in French against
espeak-ng's 3.4%, 3.3% in German against its 3.5%.

## Licence

[CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/): no rights reserved, as the sentences the words come from.
espeak-ng (GPL-3.0) was run as a tool to produce the phonemes; none of it is included.
