using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Drawing;

/// <summary>An image model as a picture is drawn with it: its parts by role (<see cref="DrawingParts"/>), each where it runs for this
/// picture, the method that draws with them, how it samples, and the LoRA it is drawn with — all from the catalog, for the model a
/// <see cref="Draw"/> names.</summary>
/// <param name="Steps">How many denoising steps a picture takes.</param>
/// <param name="Shift">The flow-matching shift applied to the noise levels the steps run through; 0 for a model whose
/// shift follows the picture's size (FLUX.2's).</param>
/// <param name="Guidance">How far each step steers away from the empty prompt; 0 for a model that does not.</param>
/// <param name="Lora">The LoRA it is drawn with; null for none.</param>
internal sealed record DrawingModel(DrawingMethod Method, IReadOnlyDictionary<string, ModelSpec> Parts, int Steps, float Shift, float Guidance,
                                    LoraFiles? Lora = null);

/// <summary>A LoRA a picture is drawn with, as its files lie in the store: an ONNX Runtime adapter for the denoiser, applied unmerged at
/// <paramref name="Strength"/>, and — for a distillation — the sampler it brings in place of its model's own.</summary>
internal sealed record LoraFiles(string Adapter, float Strength, string? Sampler);
