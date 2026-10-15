# AI tools

An AI tool runs a neural network Nexaflow holds on this machine to do one thing — restore a picture, read text from one,
find a face — for a person from a page, and for an assistant, local or in the cloud, from its tools. It is not an AI
provider: nothing in the cloud stands in for it, a workspace's ability grid never names it, and it has no `ModelKind`.
Its models are fetched once, app-wide, when a person asks, and run in the AI host on the same plan of its GPUs as the
owned models.

The picture tools are the first: Real-ESRGAN and GFPGAN (`real-esrgan-gfpgan`).

## How a tool fits

| Layer | What | Where |
|---|---|---|
| Graphs | built, checked against the makers' own code and published by a recipe | `tools/ai-models/<tool>/`, a smile-forge Hugging Face repository |
| Catalog | the tool's parts and pinned files, in the owned catalog's `tools` section | `owned-models.json` → `OwnedTool` (`OwnedModels.Tools`) |
| Runtime | fetching, placing on demand, holding — shared with the owned models through `OwnedEntry` | `OwnedModelRuntime` |
| Host | a request family of its own, with runners for its graphs | `Nexaflow.AiHost/<Family>/`, its messages in `Nexaflow.AiHost.Contracts` |
| Front | what the app asks of it, no provider types in sight | `Nexaflow.Providers.AiHost/Owned<Tool>.cs` |
| Contract | what a feature asks of it: `IToolModels` (where its models stand, download) and the tool's own | `Features.Common/ToolModels.cs` |
| Core | the contract over the front, the download app-wide; `ILocalTool` for the settings pages | `Core/Services/Tools/` |
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

## Adding a tool

1. A recipe under `tools/ai-models/<tool>/`: fetch the makers' code and weights pinned, build each graph and hold it to its
   original, run the makers' own pipeline on their own samples into a reference set; publish to smile-forge.
2. A kind in `OwnedToolKind`, its roles in `OwnedRoles.Of(OwnedToolKind)`, and its entry in the catalog's `tools` section,
   each part's `memoryBytes` its measured peak.
3. The host's request family and runners, registered in `HostMessages`, `ModelRunners.Default` and `HostProgram`; a GPU
   test held to the reference set.
4. Its front in `Providers.AiHost`, its contract in `Features.Common` (extending `IToolModels`), and Core's service
   implementing it and `ILocalTool`, built in `App.InitializeApp`, passed to `ProviderManager.Initialize` and given to
   features with `FeatureManager.ProvideTool`.
5. The client tool, and the pages that use it.
