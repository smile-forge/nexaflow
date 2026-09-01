## Computer Modern

This software contains TTF files of Computer Modern font by Donald E. Knuth:
- `cmex10.ttf`
- `cmmi10.ttf`
- `cmr10.ttf`
- `cmsy10.ttf`

Original font sources may be [retrieved from CTAN][ctan.cm].

The files are distributed under the terms of the Knuth License, reproduced below.

> This software is copyrighted. Unlimited copying and redistribution of this package and/or its individual files are permitted as long as there are no modifications. Modifications, and redistribution of modifications, are also permitted, but only if the resulting package and/or files are renamed.

[ctan.cm]: https://ctan.org/tex-archive/fonts/cm

## `jlm_msam10.ttf` and `jlm_msbm10.ttf`

This software contains the font files `jlm_msam10.ttf` and `jlm_msbm10.ttf` ([taken from JLaTeXMath project][jlatexmath.fonts]) that are licensed under the Open Font License, reproduced below. The licence text names `msam10` and `msbm10` among its Reserved Font Names, which is why the converted files carry the `jlm_` prefix.

```
Copyright (c) 1997, 2009, American Mathematical Society (http://www.ams.org).
All Rights Reserved.

"eufb10" is a Reserved Font Name for this Font Software.
"eufm10" is a Reserved Font Name for this Font Software.
"msam10" is a Reserved Font Name for this Font Software.
"msbm10" is a Reserved Font Name for this Font Software.

This Font Software is licensed under the SIL Open Font License, Version 1.1.
This license is copied below, and is also available with a FAQ at:
http://scripts.sil.org/OFL

-----------------------------------------------------------
SIL OPEN FONT LICENSE Version 1.1 - 26 February 2007
-----------------------------------------------------------

PREAMBLE
The goals of the Open Font License (OFL) are to stimulate worldwide
development of collaborative font projects, to support the font creation
efforts of academic and linguistic communities, and to provide a free and
open framework in which fonts may be shared and improved in partnership
with others.

The OFL allows the licensed fonts to be used, studied, modified and
redistributed freely as long as they are not sold by themselves. The
fonts, including any derivative works, can be bundled, embedded,
redistributed and/or sold with any software provided that any reserved
names are not used by derivative works. The fonts and derivatives,
however, cannot be released under any other type of license. The
requirement for fonts to remain under this license does not apply
to any document created using the fonts or their derivatives.

DEFINITIONS
"Font Software" refers to the set of files released by the Copyright
Holder(s) under this license and clearly marked as such. This may
include source files, build scripts and documentation.

"Reserved Font Name" refers to any names specified as such after the
copyright statement(s).

"Original Version" refers to the collection of Font Software components as
distributed by the Copyright Holder(s).

"Modified Version" refers to any derivative made by adding to, deleting,
or substituting -- in part or in whole -- any of the components of the
Original Version, by changing formats or by porting the Font Software to a
new environment.

"Author" refers to any designer, engineer, programmer, technical
writer or other person who contributed to the Font Software.

PERMISSION & CONDITIONS
Permission is hereby granted, free of charge, to any person obtaining
a copy of the Font Software, to use, study, copy, merge, embed, modify,
redistribute, and sell modified and unmodified copies of the Font
Software, subject to the following conditions:

1) Neither the Font Software nor any of its individual components,
   in Original or Modified Versions, may be sold by itself.

2) Original or Modified Versions of the Font Software may be bundled,
   redistributed and/or sold with any software, provided that each copy
   contains the above copyright notice and this license. These can be
   included either as stand-alone text files, human-readable headers or
   in the appropriate machine-readable metadata fields within text or
   binary files as long as those fields can be easily viewed by the user.

3) No Modified Version of the Font Software may use the Reserved Font
   Name(s) unless explicit written permission is granted by the corresponding
   Copyright Holder. This restriction only applies to the primary font name as
   presented to the users.

4) The name(s) of the Copyright Holder(s) or the Author(s) of the Font
   Software shall not be used to promote, endorse or advertise any
   Modified Version, except to acknowledge the contribution(s) of the
   Copyright Holder(s) and the Author(s) or with their explicit written
   permission.

5) The Font Software, modified or unmodified, in part or in whole,
   must be distributed entirely under this license, and must not be
   distributed under any other license. The requirement for fonts to
   remain under this license does not apply to any document created
   using the Font Software.

TERMINATION
This license becomes null and void if any of the above conditions are
not met.

DISCLAIMER
THE FONT SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO ANY WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT
OF COPYRIGHT, PATENT, TRADEMARK, OR OTHER RIGHT. IN NO EVENT SHALL THE
COPYRIGHT HOLDER BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
INCLUDING ANY GENERAL, SPECIAL, INDIRECT, INCIDENTAL, OR CONSEQUENTIAL
DAMAGES, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
FROM, OUT OF THE USE OR INABILITY TO USE THE FONT SOFTWARE OR FROM
OTHER DEALINGS IN THE FONT SOFTWARE.
```

## The `xm_*.ttf` faces

This software contains eight font files converted from the Type 1 outlines in [the AMS `amsfonts` distribution][ctan.amsfonts]:

| File | Converted from | Backs |
|---|---|---|
| `xm_cmbx10.ttf` | `cmbx10` | `\mathbf`, `\textbf` |
| `xm_cmss10.ttf` | `cmss10` | `\mathsf`, `\textsf` |
| `xm_cmtt10.ttf` | `cmtt10` | `\mathtt`, `\texttt` |
| `xm_cmti10.ttf` | `cmti10` | `\textit` |
| `xm_cmcsc10.ttf` | `cmcsc10` | `\textsc` |
| `xm_eufm10.ttf` | `eufm10` | `\mathfrak` |
| `xm_cmmib10.ttf` | `cmmib10` | `\boldsymbol` (maths italic) |
| `xm_cmbsy10.ttf` | `cmbsy10` | `\boldsymbol` (symbols) |

They carry the same American Mathematical Society copyright and Open Font License as `jlm_msam10.ttf` and `jlm_msbm10.ttf` above, and that licence text names every one of `cmbx10`, `cmss10`, `cmtt10`, `cmti10`, `cmcsc10`, `eufm10`, `cmmib10` and `cmbsy10` as a Reserved Font Name. Converting Type 1 outlines to TrueType makes a Modified Version, so each file and the font name inside it were renamed (`xm_cmbx10.ttf` holding `XMCMBX10`, and so on) as clause 3 of the licence requires. Nothing else about the fonts was altered: the glyphs, the advance widths and the encoding are the originals, checked against the AFM files shipped alongside the Type 1 sources.

[ctan.amsfonts]: https://ctan.org/pkg/amsfonts

## `jlm_rsfs10.ttf`

This software contains the font file `jlm_rsfs10.ttf` — Ralph Smith's Formal Script ([taken from JLaTeXMath project][jlatexmath.fonts]). The original `rsfs` fonts are by Ralph Smith and are [distributed from CTAN][ctan.rsfs], where the accompanying README grants:

> You are welcome to use and distribute these files; if you modify them,
> please change the name but give credit to the original author!

>          - Ralph Smith

The converted file carries the `jlm_` prefix rather than the original name, as that grant requires.

[ctan.rsfs]: https://ctan.org/pkg/rsfs

[jlatexmath.fonts]: https://github.com/opencollab/jlatexmath/tree/af77a8e80d41ff67dfe2f42f14b41f6860dfeeec/jlatexmath/src/main/resources/org/scilab/forge/jlatexmath/fonts/maths
