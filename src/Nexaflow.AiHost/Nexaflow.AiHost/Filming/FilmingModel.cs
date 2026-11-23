using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Filming;

/// <summary>A video model as a clip is made with it: its parts by role (<see cref="FilmingParts"/>), each where it runs for this clip, the
/// method that films with them, and how it samples — all from the catalog, for the model a <see cref="Film"/> names.</summary>
/// <param name="Steps">How many denoising steps a clip takes.</param>
/// <param name="Shift">The flow-matching shift of the noise levels the steps run through.</param>
/// <param name="Guidance">How far each step steers away from <see cref="NegativePrompt"/>; 0 for a model that does not.</param>
/// <param name="Timesteps">The timesteps a distilled model steps through, as it was trained to; empty for a model whose
/// steps follow from <see cref="Steps"/> and <see cref="Shift"/>.</param>
/// <param name="NegativePrompt">What guidance steers away from, as the model's makers give it; empty for a model that
/// is not guided.</param>
internal sealed record FilmingModel(FilmingMethod Method, IReadOnlyDictionary<string, ModelSpec> Parts, int Steps, float Shift, float Guidance,
                                    IReadOnlyList<int> Timesteps, string NegativePrompt);
