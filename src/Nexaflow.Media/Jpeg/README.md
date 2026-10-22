# Jpeg

A JPEG decoder in memory-safe C#: baseline, extended and progressive DCT, Huffman or arithmetic coded, and lossless
Huffman coded, at 8 bits a sample (2 to 8 lossless). It decodes only; Nexaflow writes no JPEG of its own here.

## Origin

A port of the decompression side of [libjpeg-turbo](https://github.com/libjpeg-turbo/libjpeg-turbo) 3.1.1, itself based
on the Independent JPEG Group's libjpeg. Each file names the C files it ports and keeps their copyright lines, and a
method's comment names the C function it ports, as in `3.1.1 - decode_mcu_AC_refine`. libjpeg-turbo is the reference
it is measured against: every JPEG in the PDF corpus decodes to the samples its TurboJPEG library decodes it to
(`DctComparisonSweep`, `tools/bench/Run-DctBench.ps1`), and so does a fixture libjpeg-turbo's own compressor made in
every colour space, subsampling and coding it offers, sound and damaged (`JpegDecoderTests`).

It decides what libjpeg decides: the colour space from the JFIF and Adobe markers and the component identifiers, the
standard Huffman tables for a sequential image that defines none, block smoothing for a progressive image whose
coefficients are not all known, fancy upsampling, the restart-marker resynchronisation, and the zeros supplied once the
data runs out. Fatal errors become a `JpegException` with libjpeg's message; warnings are kept on the image.

## Where it departs from the C

- **The IDCT is libjpeg-turbo's AVX2 code, not jidctint.c.** On x86, libjpeg-turbo — and Pillow, which ships it —
  transforms blocks with `jsimd_idct_islow_avx2`, whose 16-bit lanes wrap and saturate where the C's 32-bit integers
  do not. The two agree on every valid image; on coefficients no valid image holds they differ, and the SIMD values are
  the ones Nexaflow is held to. `InverseDct` ports the AVX2 code instruction for instruction, and does the same
  arithmetic a value at a time where AVX2 is absent.
- **Whole images, not scanlines.** libjpeg decodes a single-scan image as its caller reads scanlines and absorbs a
  multi-scan one whole. Here a single-scan image is entropy-decoded a band of MCU rows at a time into small pooled
  coefficient buffers, each band transformed and converted on the thread pool while the next decodes; a multi-scan
  image's coefficients are held whole, as libjpeg holds them. The whole planes stand in for the main buffer controller's
  context rows: a vertical fancy upsampler reads the plane rows above and below, clamped to the component's real rows
  as jdmainct.c clamps them.
- **The fast Huffman path runs inside restart intervals too.** libjpeg keeps to the careful path whenever a restart
  interval is set. The fast path keeps an MCU only when it met no marker, and short of a marker both paths read the
  same bits to the same coefficients, so the samples are the same.
- **A fault libjpeg finds only after the image is out is a warning.** After a single-scan image's last row,
  jpeg_finish_decompress reads the markers that follow it; what it finds wrong there cannot change the image, so the
  image is kept and the fault recorded as a warning.
- **Loops are vectorised.** The IDCT, the fancy upsamplers and the colour converters run sixteen samples at a time
  through `Vector256`, each lane the scalar code's integer arithmetic exactly. Spans and vectors only: no `unsafe`, no
  `Unsafe`, no reinterpreting casts of memory.
- **Only what decoding needs.** No encoder, transcoder, scaling, cropping, raw-data or buffered-image output, colour
  quantization or 12- and 16-bit decoding: libjpeg-turbo's 8-bit API refuses 12-bit and 16-bit images, and so does
  this.

## Licence

The IJG licence and libjpeg-turbo's BSD licence: [LICENSE.md](LICENSE.md) and [README.ijg](README.ijg) beside this
file, the header of each source file, and the entry in `src/Nexaflow.Core/Assets/ThirdPartyNotices.md`. This software
is based in part on the work of the Independent JPEG Group.
