# AI tools

An AI tool runs a neural network Nexaflow holds on this machine to do one thing — restore a picture, read text from one,
find a face — for a person from a page, and for an assistant, local or in the cloud, from its tools. It is not an AI
provider: nothing in the cloud stands in for it, a workspace's ability grid never names it, and it has no `ModelKind`.
Its models are fetched once, app-wide, when a person asks, and run in the AI host on the same plan of its GPUs as the
owned models.

Three are here: the picture tools — Real-ESRGAN and GFPGAN (`real-esrgan-gfpgan`) — picture selection, SAM 3.1
(`sam3.1`), and page reading, Unlimited-OCR with PP-OCRv5's text detector (`unlimited-ocr`).

## How a tool fits

| Layer | What | Where |
|---|---|---|
| Graphs | built, checked against the makers' own code and published by a recipe | `tools/ai-models/<tool>/`, a smile-forge Hugging Face repository |
| Catalog | the tool's parts and pinned files, in the owned catalog's `tools` section | `owned-models.json` → `OwnedTool` (`OwnedModels.Tools`) |
| Runtime | fetching, placing on demand, holding — shared with the owned models through `OwnedEntry` | `OwnedModelRuntime` |
| Host | a request family of its own, with runners for its graphs | `Nexaflow.AiHost/<Family>/`, its messages in `Nexaflow.AiHost.Contracts` |
| Front | what the app asks of it, no provider types in sight; whether it runs here, its files and readiness from `OwnedToolFront` | `Nexaflow.Providers.AiHost/Owned<Tool>.cs` |
| Contract | what a feature asks of it: `IToolModels` (where its models stand, download) and the tool's own | `Features.Common/ToolModels.cs` |
| Core | the contract over the front; where its models stand, their download app-wide and `ILocalTool` for the settings pages from `LocalToolModels` | `Core/Services/Tools/` |
| Features | given the contract by `FeatureManager.ProvideTool`, by constructor | the feature |
| Assistant | a client tool over the same contract, offered wherever the machine can run it | `AIService.GatherContext` |
| Settings | the Local page's Tools section offers each download; the Downloaded page lists each for removal | `LocalToolViewModel`, `DownloadedModelsViewModel` |

A tool offered to the assistant while its models are not downloaded yet answers with where to get them, rather than
being missing, so a request for it is never met with silence.

## The picture tools

**What they do.** A picture made twice or four times its size, or kept at its size and cleaned of softness, noise and
JPEG blocks, and the faces in it found and remade:

- Real-ESRGAN's 2x and 4x upscalers — at its own size, through the 2x one and brought back with Lanczos;
- RetinaFace finds the faces, GFPGAN v1.4 remakes each aligned to its 512-pixel template, and ParseNet's mask, blurred,
  pastes it back.

Real-ESRGAN's smaller general model was tried and left out: on photographs it was softer and kept more of the damage.

**The port.** The makers' Python pipelines — `RealESRGANer` and `GFPGANer` — are ported whole to the host
(`RestoreHandler`, `PictureUpscaling`, `FaceAlignment`, `RetinaFaceDecoding`), and OpenCV's resizes, warps, blurs and
similarity fit with them (`PictureResampling`, `Affine2D` in `IO.Common`), the warps as OpenCV 5 makes them, at each
pixel's exact source position. `PictureRestorationTests` holds the host to the pipelines' own output on their own sample
pictures — 54–66 dB, every face found — and `PictureResamplingTests` holds the ports to what OpenCV makes of small
pictures, without OpenCV.

**Memory.** Every part peaks under 3 GB of GPU memory, so the tools run on a 4 GB card, and on the CPU where there is
none: the 4x upscaler works at its tile's full size, so it takes tiles of 256 pixels; the 2x one folds each 2×2 into
channels and takes 512. A picture is refused at a scale whose upscaled result passes 128 megapixels
(`RestorePicture.Restores`). Pictures go to and from the host by file, as raw RGBA, never through the pipe.

**In the image editor.** The recipe's restore section (`RecipeRestore`) — AI enhance, its strength and Repair faces in the
Adjust panel; AI upscale in the Resize panel — restores the photo before every other edit. A restoration is the same
picture larger, so every other section still speaks of the source's pixels. `PhotoRestorer` finds it in the restorations
kept on disk (`RestoredPictures`, keyed by the photo's content, the restoration and `IPictureTools.ModelsVersion`) or makes
it in the background; the viewer's banner says how far it has got, offers the tools' download, or offers to try again.
The render pipeline lays it over the photo at its strength in the geometry pass, so a strength drag redraws one pass,
and Save renders from it at full size once it is made.

**For the assistant.** `restore_picture` restores a picture file into a new one beside it — upright as its EXIF says —
and opens it; the original is never touched.

## Picture selection

**What it does.** A thing in a picture picked out — by clicks on it and off it, a box drawn round it, or everything a
phrase names ("wheel", "child in a red vest"), helped by example boxes of what is and is not meant — and its mask given
back, so it can be shown, cut out of its background or kept.

**The graphs.** SAM 3.1's click and phrase paths as five graphs: the picture encoder (fp16), read once for both paths;
the click decoder; the text encoder (fp16); the exemplar encoder, which reads example boxes; and the finder (fp16 under
autocast), which finds everything a phrase and its examples name. `tools/ai-models/sam3.1` traces them from the makers'
own modules and holds them to the makers' paths on their own sample pictures; the SAM License travels with the weights.

**A session a picture.** `SelectInPicture` is a request held for as long as its caller selects from one picture — a
page holds one while it is open on the picture, and ends it when it closes or moves on. The host reads the picture
once as it arrives, then answers each `SelectByPoints` and `SelectByPhrase` written to the request from that reading,
so a click costs the decoder alone: 4 ms on a GPU, 11 on a CPU, against 45 ms (7 s on a CPU) to read the picture. Each
part is taken from the registry for an answer and given back after it, so an open page holds no GPU memory between
asks. Masks go back by file, at the size each ask names: the picture's own for a cut-out, smaller for showing one.

**The port.** `SelectHandler` and `PictureSelecting` run the makers' paths around the graphs: the picture squashed to
1008 square by PIL's bilinear resize, as SAM 3.1's predictor prepares a frame (`PictureResampling.ResizeBilinearAsPil`,
matched to PIL exactly); the click decoder's best mask chosen by predicted IoU for one click; a phrase's finds kept above
a score of 0.5 joined with the presence score; masks brought to size by PyTorch's bilinear resize
(`ResizePlaneBilinear`). `ClipTokenizer` is the makers' CLIP tokenizer over their vocabulary, its cleaning ftfy's but for
mojibake repair, which only undoes text decoded with the wrong code page. `PictureSelectionTests` holds the host to the
makers' masks and scores; `BilinearResamplingTests` and `ClipTokenizerTests` hold the ports to PIL, PyTorch and the
makers' tokenizer.

**Memory.** Every part peaks under 2.6 GB of GPU memory; the download is 1.8 GB.

**For features.** `IPictureSelection.OpenAsync` takes a picture's pixels and gives a session; its masks come back as
bytes, the files between Core and the host kept in a folder of the session's own.

**In the image editor.** The Cut out panel picks with clicks, a box or a phrase, shown at up to 1280 pixels
(`PictureCutter`). Remove background makes what is picked the recipe's cut-out section (`RecipeCutOut`, version 3 of
the recipe): `BackgroundRemover` finds its mask kept on disk (`CutOutMasks`, keyed by the photo's content, the cut-out
and `IPictureSelection.ModelsVersion`) or picks it again at full size in the background, as an 8-bit grey PNG. The display
decodes it beside the photo, and the render pipeline clears what it does not keep at the end of the geometry pass. A
photo whose format holds no transparency is saved only as a copy.

**For the assistant.** `cut_out` cuts what a phrase names, or what a box holds, out of a picture file into a new PNG
beside it — transparent around it, cropped to it — and opens it; the original is never touched.

## Page reading

**What it does.** Whether, and where, a picture holds writing — PP-OCRv5's mobile text detector, tens of milliseconds
on four CPU threads, so a page can ask it of every picture it shows — and a page read into one Markdown document by
Baidu's Unlimited-OCR: its columns as one text, headings at levels from their numbering, maths as LaTeX, tables, its
figures cut out of it, and a diagram drawn again as Mermaid by the workspace's image model.

**The models.** `tools/ai-models/unlimited-ocr` converts the makers' weights with llama.cpp's own converter at the
release the host runs (b11606), quantises the language model to 8-bit — 4-bit read nothing of a page of a few lines —
and sets the makers' chat template after the start token their code puts first; the detector goes to ONNX through
paddle2onnx and is held to Paddle's predictor to 1e-4. The reference is the makers' transformers code and Paddle's
predictor on pages of the Unlimited-OCR paper, the paper set in small print on one page, and in two scanned columns.

**On llama.cpp.** The reader is a language model with a projector, run as any other through `Generate`, with what its
makers decode it with in `ReplyDecoding`:

- **Reference sliding-window attention.** Each token was trained to see the whole page and only the last 128 tokens
  written before it; llama.cpp lets it see all it wrote, which reads a short page the same and loses its place past
  about 1,500 tokens. `LiveConversation.KeepWritten` forgets each written token's KV cell as it leaves the window,
  positions kept, so a page of 3,100 tokens reads as the makers' code reads it. Its cache holds the page and 128
  tokens, so 8,192 cells serve any page; the reply stops when its positions reach what the model was trained to read.
- **The repeat ban.** No 35-token run written again within 128 (`RepeatBan`), struck from the logits before each choice
  (`LlamaSampler.Banned`).
- **Its marks kept.** Each block opens with special tokens naming its kind and box, sent as text
  (`WritesControlTokens`).

The detector is a request of its own, `FindTextInPicture`, on the CPU — its catalog part says `runsOnCpu` — prepared and
read into regions as PaddleOCR does (`TextFinding`). `PageReadingTests` holds the host to the reference: the same
blocks and kinds, boxes within 15 thousandths, the text within 2 % — the 8-bit build's distance from the makers' bf16 —
and the regions over 0.7 found.

A small picture of a few large lines — a note, one line of a bill — reads nothing on llama.cpp, at any precision and
on the CPU too, where the makers' code reads it: llama.cpp's preparation of the picture, not Nexaflow's. The same lines
on a page read; the viewer offers Convert to Markdown only for a page.

**Memory.** The reader peaks at 6.8 GB with 8,192 cells, the vision encoder's work on the page's tiles the most of it
(32 tiles no more than 24); the download is 4 GB. A dense page takes 2–5 s on a GPU, about 30 s on a 32-thread CPU.

**For features.** `IPageReading` finds text (`TextOnPicture`: its regions, and whether they make a page —
`PageReading.IsPage`, 15 regions over 3 % of the picture) and reads a page into `PageBlock`s as it goes (`PageMarks`).
`Visuals.Text`'s `PageReader` reads pages into Markdown (`PageMarkdown`), cuts the figures (`PageFigures`) and asks
`IAIService.AskAboutPictureAsync` for each as Mermaid, kept only when `MermaidDiagrams.Troubles` finds nothing wrong
(`DiagramDrawing`). `PageConversion` converts a feature's pages (`PageSource`): the reading kept in the temp folder by
what was read and the models, a draft written from it — `<source>.md`, only the figures its text shows in
`<source>.figures` — and opened in the Markdown editor in the right-hand pane (`IShellServices.OpenTab`, `inRightPane`)
with a `draft` param, so its first save asks where, a Markdown Package by default; `OriginBreadcrumbs` leads it back to
its source. `PageConversionBar` says how far it has got, offers the download and stops it; what was asked waits for the
download and goes on when it arrives.

**In the image viewer.** `ReadPageViewModel` looks at each picture once it is decoded, at up to 1,280 pixels, and offers
Convert to Markdown for a page; it reads the picture as shown, edits and all, at full size (`ImageExporter`). `read_page`
reads it for the assistant.

**In the PDF reader.** `PdfScans` finds the scanned pages — no text, one picture over each — and their pictures, JPEG as
stored or decoded to PNG, turned as the page is shown; a notice above the document says so and converts them.
`pdf_read_scans` reads them for the assistant, and `pdf_read_text` and `pdf_find_text` include their text once read.

**For the assistant.** `read_page_file` reads a picture file of a page and returns its text; saved as Markdown when
asked, its figures beside it.

## Adding a tool

1. A recipe under `tools/ai-models/<tool>/`: fetch the makers' code and weights pinned, build each graph and hold it to its
   original, run the makers' own pipeline on their own samples into a reference set; publish to smile-forge.
2. A kind in `OwnedToolKind`, its roles in `OwnedRoles.Of(OwnedToolKind)`, and its entry in the catalog's `tools` section,
   each part's `memoryBytes` its measured peak — or, for a part so small a GPU would only keep it waiting, `runsOnCpu` —
   and every file in one of its parts' folders.
3. The host's request family and runners, registered in `HostMessages`, `ModelRunners.Default` and `HostProgram`; a GPU
   test held to the reference set.
4. Its front in `Providers.AiHost`, deriving `OwnedToolFront`; its contract in `Features.Common`, extending
   `IToolModels`; and Core's service, deriving `LocalToolModels` and implementing the contract, built in
   `App.InitializeApp`, passed to `ProviderManager.Initialize` and given to features with `FeatureManager.ProvideTool`.
5. The client tool, and the pages that use it. An edit a tool makes to a photo is a recipe section and a `ToolEdit`,
   its results kept by `KeptFiles` and named to the renderer and the exporter in `MadeByTools`.
