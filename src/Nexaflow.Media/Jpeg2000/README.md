# Jpeg2000

A JPEG 2000 decoder in memory-safe C#: Part 1 codestreams and JP2 files, with Part 15 high-throughput (HTJ2K) code
blocks. It decodes only; Nexaflow writes no JPEG 2000.

## Origin

OpenJPEG.net, notBald's C# port of [OpenJPEG](https://github.com/uclouvain/openjpeg)
([notBald/OpenJPEG.net](https://github.com/notBald/OpenJPEG.net)), taken from
[BobLd/OpenJPEG.net](https://github.com/BobLd/OpenJPEG.net) at `ff5923d`. It follows OpenJPEG 2.5 function for
function — a method's comment names the C function it ports, as in `2.5 - opj_dwt_decode_tile` — and libopenjp2's
source is the reference it is corrected and measured against (`tools/bench/Run-JpxBench.ps1`).

## Licence

BSD-2-Clause: [LICENSE](LICENSE) beside this file, the header of each source file, and the entry in
`src/Nexaflow.Core/Assets/ThirdPartyNotices.md`.
