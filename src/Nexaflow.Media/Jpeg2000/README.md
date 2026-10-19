# Jpeg2000

A JPEG 2000 decoder in memory-safe C#: Part 1 codestreams and JP2 files, with Part 15 high-throughput (HTJ2K) code
blocks. It decodes only; Nexaflow writes no JPEG 2000.

## Origin

OpenJPEG.net, notBald's C# port of [OpenJPEG](https://github.com/uclouvain/openjpeg)
([notBald/OpenJPEG.net](https://github.com/notBald/OpenJPEG.net)), taken from
[BobLd/OpenJPEG.net](https://github.com/BobLd/OpenJPEG.net) at `ff5923d`. It follows OpenJPEG 2.5 function for
function — a method's comment names the C function it ports, as in `2.5 - opj_dwt_decode_tile` — and libopenjp2's
source is the reference it is corrected and measured against (`tools/bench/Run-JpxBench.ps1`).

Where the port's loops were slow they are rewritten, and the output kept bit for bit libopenjp2's — every JPEG 2000
image in the corpus decodes to the same samples (`JpxComparisonSweep`). The MQ-coded passes are one generic method
each, the coder's registers held in a struct local for the pass (`Tier1Passes.cs`); the inverse wavelet, colour
transforms, DC shift and dequantisation run eight samples at a time through `Vector256`, each lane the scalar's
operations in its order with no fused multiply-add; code blocks and wavelet strips are shared out with
`Parallel.For`, each worker keeping one coder or one pooled buffer. Spans and vectors only: no `unsafe`, no
`Unsafe`, no reinterpreting casts of memory.

## Licence

BSD-2-Clause: [LICENSE](LICENSE) beside this file, the header of each source file, and the entry in
`src/Nexaflow.Core/Assets/ThirdPartyNotices.md`.
