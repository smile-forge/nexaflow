Nexaflow is built on the following open-source libraries. We're grateful to their
authors and contributors. Each is the property of its respective owner and is used
under the license noted below.

| Library | License |
|---------|---------|
| [Anthropic SDK](https://github.com/tghamm/Anthropic.SDK) | MIT |
| [AvalonEdit](https://github.com/icsharpcode/AvalonEdit) | MIT |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | MIT |
| [Concentus](https://github.com/lostromb/concentus) (+ Oggfile) | MIT |
| [DiscUtils](https://github.com/LTRData/DiscUtils) (built from source) | MIT |
| [fo-dicom](https://github.com/fo-dicom/fo-dicom) (+ fo-dicom.Codecs, bundled native codecs) | MS-PL |
| [Google.GenAI](https://github.com/googleapis/dotnet-genai) | Apache-2.0 |
| [HelixToolkit.Wpf](https://github.com/helix-toolkit/helix-toolkit) | MIT |
| [HtmlAgilityPack](https://github.com/zzzprojects/html-agility-pack) | MIT |
| [JsonPath.Net](https://github.com/json-everything/json-everything) | MIT |
| [K4os.Compression.LZ4](https://github.com/MiloszKrajewski/K4os.Compression.LZ4) | MIT |
| [LibGit2Sharp](https://github.com/libgit2/libgit2sharp) | MIT |
| [LibVLCSharp](https://github.com/videolan/libvlcsharp) (+ LibVLCSharp.WPF) | LGPL-2.1-or-later |
| [Markdig](https://github.com/xoofx/markdig) | BSD-2-Clause |
| [Microsoft.Web.WebView2](https://developer.microsoft.com/microsoft-edge/webview2/) | Microsoft Software License |
| [MimeKit](https://github.com/jstedfast/MimeKit) | MIT |
| [MsgReader](https://github.com/Sicos1977/MSGReader) | MIT |
| [NAudio](https://github.com/naudio/NAudio) | MIT |
| [NVorbis](https://github.com/NVorbis/NVorbis) | MIT |
| [OllamaSharp](https://github.com/awaescher/OllamaSharp) | MIT |
| [OpenAI .NET](https://github.com/openai/openai-dotnet) | MIT |
| [PdfPig](https://github.com/UglyToad/PdfPig) | Apache-2.0 |
| [PdfPig.Filters.Dct.JpegLibrary](https://github.com/BobLd/UglyToad.PdfPig.Filters.Dct.JpegLibrary) | Apache-2.0 |
| [PdfPig.Filters.Jbig2.PdfboxJbig2](https://github.com/BobLd/UglyToad.PdfPig.Filters.Jbig2.PdfboxJbig2) | Apache-2.0 |
| [PdfPig.Filters.Jpx.OpenJpeg](https://github.com/BobLd/UglyToad.PdfPig.Filters.Jpx.OpenJpeg) (bundled OpenJPEG) | BSD-2-Clause |
| [SharpAssimp](https://github.com/JeremyAnsel/SharpAssimp) | MIT (bundled native Assimp: BSD-3-Clause) |
| [SharpCompress](https://github.com/adamhathcock/sharpcompress) | MIT |
| [SharpGLTF.Toolkit](https://github.com/vpenades/SharpGLTF) | MIT |
| [SharpVectors](https://github.com/ElinamLLC/SharpVectors) | BSD-3-Clause |
| [SharpZipLib](https://github.com/icsharpcode/SharpZipLib) | MIT |
| [TagLibSharp](https://github.com/mono/taglib-sharp) | LGPL-2.1 |
| [tree-sitter](https://github.com/tree-sitter/tree-sitter) | MIT |
| [TreeSitter.DotNet](https://github.com/mariusgreuel/tree-sitter-dotnet-bindings) (+ bundled grammars) | MIT |
| [Updatum](https://github.com/sn4k3/Updatum) | MIT |
| [VideoLAN libVLC](https://www.videolan.org/vlc/libvlc.html) (native, via VideoLAN.LibVLC.Windows) | LGPL-2.1-or-later |
| [Whisper.net](https://github.com/sandrohanea/whisper.net) | MIT |
| [XAML-Math / WpfMath](https://github.com/ForNeVeR/xaml-math) (vendored — see below) | MIT |
| [PDF417 Barcode Encoder](https://github.com/Uzi-Granot/PDF417BarcodeEncoder) (ingested — see below) | CPOL-1.02 |
| [ZstdSharp.Port](https://github.com/oleg-st/ZstdSharp) | MIT |
| [.NET runtime libraries](https://github.com/dotnet/runtime) | MIT |

Full license texts are available at each project's repository linked above.

**XAML-Math is vendored, not referenced.** Its source — TeX's typesetting engine, the Computer Modern
metrics, and the Computer Modern faces themselves — was copied into this repository and is built as part
of it, under `src/Nexaflow.Maths.Typesetting` and `src/Nexaflow.Visuals.Maths`, with its own test suite as
`src/Nexaflow.Tests/Nexaflow.Tests.Typesetting`. Modified: Nexaflow reads LaTeX with its own parser and
builds the engine's boxes from that reading. The MIT licence and the copyright notices travelled with the
code; the fonts carry their own licences in `src/Nexaflow.Visuals.Maths/Fonts/LICENSES.md` (OFL-1.1 and
others), and are redistributed under them.

The syntax-highlighting consistency tests use a reference corpus vendored from
[bat](https://github.com/sharkdp/bat) (MIT / Apache-2.0) under
`src/Nexaflow.Tests/Nexaflow.Tests.Fixtures/syntax-tests/`. It is test-only reference data — not
compiled into any assembly and not shipped in the product.

**The PDF417 symbol-character table is ingested, not referenced.** PDF417 cannot be encoded without it
and it cannot be derived: every legal character is 17 modules of four bars and four spaces each one to
six wide, but that admits 1,484 characters in cluster 0 where the standard uses 929, and the ones it
picks are in no order that can be computed. The table — 3 clusters × 929 patterns — was taken from Uzi
Granot's PDF417 Barcode Encoder, which is licensed under CPOL 1.02, and is held as
`src/Nexaflow.Visuals.Text/Markdown/Matrix/Pdf417/Pdf417Codewords.cs`. Nothing else was taken: the
compaction, the Reed–Solomon parity, the row indicators and the layout are Nexaflow's own, written
against ISO/IEC 15438.

CPOL 1.02 permits use in commercial applications, redistribution, and derivative works. The data is
verified rather than trusted — `Pdf417EncoderTests` asserts every one of the 2,787 entries really is a
legal symbol character in the cluster it is filed under, and symbols produced by two other generators
decode straight out of it.
