# AI tools

An AI tool runs a neural network Nexaflow holds on this machine to do one thing — restore a picture, read text from one,
find a face — for a person from a page, and for an assistant, local or in the cloud, from its tools. It is not an AI
provider: nothing in the cloud stands in for it, a workspace's ability grid never names it, and it has no `ModelKind`.
Its models are fetched once, app-wide, when a person asks, and run in the AI host on the same plan of its GPUs as the
owned models.

Five are here: the picture tools — Real-ESRGAN and GFPGAN (`real-esrgan-gfpgan`) — picture selection, SAM 3.1
(`sam3.1`), picture erasing, LaMa (`big-lama`), page reading, Unlimited-OCR with PP-OCRv5's text detector
(`unlimited-ocr`), and picture checking, OwenElliott's image safety classifier (`image-safety-classifier-m`), which
Core runs itself rather than a page.

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
| Assistant | client tools over the same contract, on the page that shows what the tool works on — acting only on what that page has open, never on a file of the model's choosing | the page's `GetClientTools` |
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

**For the assistant.** `restore_picture`, on an image tab, restores the picture on screen as the editor does — the recipe's
restore section set, its scale, faces and strength as asked — and answers once it is made; the user undoes or saves it.

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
part is taken from the registry for an answer and given back after it, so an open page holds nothing between asks: a
part stays loaded only until another model needs its room, or the host closes for having stood idle. Masks go back by file, at the size each ask names: the picture's own for a cut-out, smaller for showing one.

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

**For the assistant.** On an image tab, `select_in_picture` picks out everything a phrase names in the picture on screen
and shows it in the editor's Cut out; `cut_out` picks it the same way, or what a box holds, into a PNG opened for the
user as a draft in a new tab, its Save as starting beside the picture — an image tab gives the assistant its pictures,
never their folder, so nothing is written beside them. The assistant is told only that the tab opened, and works on the
new picture once the user gives it that tab.

## Picture erasing

**What it does.** What a mask covers erased from a picture and filled from what is round it — scratches, dust, wires, a
person or a sign rubbed out of a photo. Only the mask's pixels change.

**The graph.** LaMa's big-lama, fp32, one graph that is LaMa's predict around its generator. `tools/ai-models/big-lama`
traces it from the makers' own modules, the checkpoint read by an unpickler that keeps tensors alone, and holds it to the
original on CPU and CUDA to 8×10⁻⁵. Its Fourier units take their FFTs as matrix products with cosine and sine matrices
built from the input's size (`fourier_matmul.py`): ONNX Runtime's CUDA provider has no DFT, and its CPU one loops in
O(n²) off powers of two — over two minutes for a 1800×1200 picture. A width's parity is a tensor comparison in the
graph, since a test of it in Python is settled once, at the size the graph was traced at.

**The port.** `ErasePicture` works each part of the mask in a region of its surroundings (`InpaintRegions` in
`IO.Common`): as much picture again on every side, at least 512 pixels each way, regions whose surroundings overlap
joined, a region past 1536 square worked smaller — down by PIL's bilinear resize, which averages over the reduction, its
mask marking every pixel a hole reaches — and its fill brought back up by Lanczos. `ErasingHandover` hands a region over
as LaMa's predict does: RGB over 255, the mask taken where it is not 0, symmetric padding to a multiple of 8, the result
clipped and truncated to 8 bits. `PictureErasingTests` holds the host to the makers' predict on SAM 3's sample pictures
with drawn masks — 58–62 dB inside the mask on a GPU, 85 dB on a CPU, nothing outside it changed.

**Memory.** The graph peaks at 4 GB at 1536 square, 0.1 s on a GPU and under 5 s on 16 CPU threads; the download is
209 MB.

**For features.** `IPictureErasing.EraseAsync` takes a picture and a mask by file and writes the erased picture.

**In the image editor.** The Erase tool marks what to erase by picking it as Cut out picks it, or by brushing over it —
a thing picked and its shadow brushed are one step — and fills it three ways: AI (picture erasing), content-aware fill,
or the healing brush from where the user Alt-clicks. Content-aware fill and the healing brush are algorithms in
`IO.Common` (`ContentAwareFill`, `HealingBrush`) and need nothing downloaded. Each Erase is a step of the recipe's
erasures (`RecipeErase`, version 4 of the recipe): `PhotoEraser` finds the photo as erased kept on disk
(`ErasedPictures`, every run of first steps kept, so a step added makes only itself) or makes it in the background — a
picked thing grown past its edge (`EraseMasks`), the strokes painted over it — needing picture erasing only for AI steps
and picture selection only for picked ones (`ToolEdit.Needs`). The erased photo is drawn in the photo's place, and a
restoration is of the photo as erased (`ToolEdit.Input`), so erasing comes before every other edit.

**For the assistant.** `erase_in_picture`, on an image tab, erases what a phrase names or a box holds from the picture on
screen as a step of its edits, filled by AI where picture erasing is here, else content-aware, and answers once it is made.

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
- **Pages read together.** Every reply a model is writing advances in one llama.cpp call a token (`TokenRounds`): each
  conversation is a sequence of the model's one cache, and a round reads the token each chose last into its own
  sequence and chooses each one's next from its own row of scores — its window and its repeat ban its own — the
  likeliest read straight from the scores (`LlamaScores`). A round starts once every reply in it has chosen, and is one
  GPU step at the highest priority among them. `PageReader` keeps `IPageReading.PagesAtOnce` pages in flight — three
  for the reader, whose pictures and windows then fill under two thirds of its cache. A 368-page textbook reads in 5.2
  minutes on an RTX PRO 6000, 0.85 s a page, where one page at a time takes 7.7. Each page's picture is a step of its
  own, and is best left one: the projector encodes a page's five tiles in about 220 ms of the card's compute, batched
  or one at a time, and encoded alongside the rounds it only shares the card with them — each tile takes nearly three
  times as long, and the whole reads no faster.
  Read together, a page's text is the same, though a box's edge can move by a thousandth or a near tie tip the other
  way: the step's sums are taken in another order for three than for one (`PagesReadTogether_ReadAsEachDoesAlone`).

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
`Visuals.Text`'s `PageReader` reads pages into Markdown (`PageMarkdown`), `IPageReading.PagesAtOnce` of them at once and
written in page order, cuts the figures (`PageFigures`), numbered in page order too, and asks
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



## Picture checking

**What it does.** How fit a picture is for a general audience — the chances it shows gore, sexual content, or neither —
from OwenElliott's image safety classifier, M (SwiftFormer-M, 12.1M parameters, MIT), on four CPU threads in a few
milliseconds. While Enhanced AI Protection Mode is on, `generate_image` asks it of every picture made, before it is
saved or shown — by a person from the New picker as by the assistant — and withholds a picture whose chances of gore
and of sexual content together reach `PictureChecking.UnfitFrom` (0.5), or one whose check could not run.

**The model.** Its author's own ONNX graph, pinned to its commit — the classifier is published as one graph with its
normalisation and softmax in it, so there is nothing for a recipe of ours to build. It takes the whole picture squeezed
to 224 × 224 in RGB values of 0 to 255 (`PictureSafetyClassifier.Planes`), so nothing at a wide picture's edges goes
unseen; the host reads the picture as it was encoded, whatever Windows reads (`PictureDecoder`). Chosen over Marqo's
`nsfw-image-detection-384` for its gore class and its speed, and over its own S size for being the more cautious with
suggestive pictures; neither has been measured here on what it is meant to catch, only on pictures it should pass.

**Fetched when first needed.** Unlike the other tools, it is fetched the first time a picture is checked
(`LocalToolModels.ReadyAsync`): while the mode is on, asking for a picture is asking for its check. It still has its row
on the Local AI page. `PictureCheckingTests` runs the host with it on the CPU; `OwnedPictureCheckingTests` and
`GenerationToolsTests` hold the front and the withholding.

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
5. The pages that use it, and their client tools: on the page that shows what the tool works on, acting only on what
   that page has open — the assistant reaches no further than the tabs the user opened. An edit a tool makes to a photo
   is a recipe section and a `ToolEdit` — naming the tools each ask needs, and the picture it is made from where that is
   what another edit makes — its results kept by `KeptFiles` and named to the renderer and the exporter in `MadeByTools`.
