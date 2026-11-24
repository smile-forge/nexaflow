using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Drawing;
using Nexaflow.AiHost.Filming;
using Nexaflow.AiHost.Speech;

namespace Nexaflow.AiHost.Models;

/// <summary>
/// A request's models as the host runs them: the model, voices or LoRA a request names by its catalog id, made into the parts it
/// runs — each where it runs for this request (<see cref="HeldModels.ResolveAsync"/>) — with the settings its catalog gives it.
/// </summary>
internal sealed class RequestModels(HeldModels held)
{
    /// <summary>The image model the catalog calls <paramref name="id"/>, and how it samples.</summary>
    /// <exception cref="HostRequestException">The catalog names no such model, or it draws nothing.</exception>
    public (HostedModel Model, HostedSampler Sampler) Drawer(string id)
    {
        var model = held.ModelOf(id);
        return (model, model.Sampler ?? throw new HostRequestException(HostErrorKind.Invalid, $"{model.DisplayName} draws nothing."));
    }

    /// <summary><paramref name="model"/> as a picture is drawn with it: its parts for its method, each where it runs, its sampling, and
    /// <paramref name="lora"/> — one of its own — as its files lie in the store.</summary>
    /// <exception cref="HostRequestException">A LoRA it does not have, or whose files are not in the store; a part that is not.</exception>
    public async Task<DrawingModel> DrawingAsync(HostedModel model, DrawingLora? lora, Func<string, Task> stage, CancellationToken ct)
    {
        var (_, sampler) = Drawer(model.Id);
        LoraFiles? files = null;
        if (lora is not null)
        {
            var hosted = model.Loras.FirstOrDefault(l => l.Id == lora.Id)
                         ?? throw new HostRequestException(HostErrorKind.NotFound, $"{model.DisplayName} has no LoRA '{lora.Id}'.");
            var (adapter, distilled) = held.FilesOf(hosted);
            files = new LoraFiles(adapter, lora.Strength, distilled);
        }
        return new DrawingModel(sampler.Method, await held.PartsAsync(model, DrawingParts.Of(sampler.Method), stage, ct),
                                sampler.Steps, sampler.Shift, sampler.Guidance, files);
    }

    /// <summary>The video model the catalog calls <paramref name="id"/>, and how it films.</summary>
    /// <exception cref="HostRequestException">The catalog names no such model, or it films nothing.</exception>
    public (HostedModel Model, HostedFilming Filming) Filmer(string id)
    {
        var model = held.ModelOf(id);
        return (model, model.Filming ?? throw new HostRequestException(HostErrorKind.Invalid, $"{model.DisplayName} films nothing."));
    }

    /// <summary><paramref name="model"/> as a clip is made with it: its parts for its method, each where it runs, its sampling, and what
    /// guidance steers away from — its makers' own, and <paramref name="keepOut"/> besides.</summary>
    public async Task<FilmingModel> FilmingAsync(HostedModel model, string? keepOut, Func<string, Task> stage, CancellationToken ct)
    {
        var (_, filming) = Filmer(model.Id);
        return new FilmingModel(filming.Method, await held.PartsAsync(model, FilmingParts.Of(filming.Method), stage, ct),
                                filming.Steps, filming.Shift, filming.Guidance, filming.Timesteps, NegativePrompt(filming, keepOut));
    }

    /// <summary>What guidance steers away from: the model's makers' own, and what the clip asks to keep out besides.</summary>
    public static string NegativePrompt(HostedFilming filming, string? keepOut)
        => string.IsNullOrWhiteSpace(keepOut) ? filming.NegativePrompt : $"{filming.NegativePrompt}, {keepOut.Trim()}";

    /// <summary>The speech model <paramref name="request"/> names, as one stretch of listening runs on it: its voice activity detector
    /// and recognizer, and its transcriber and turn detector where it has them.</summary>
    /// <exception cref="HostRequestException">The catalog names no such model; it lacks a detector or a recognizer, or the turn
    /// detector listening for a turn needs.</exception>
    public async Task<ListenModels> ListeningAsync(Listen request, Func<string, Task> stage, CancellationToken ct)
    {
        var model = held.ModelOf(request.Model);
        if (request.End == UtteranceEnd.Turn && !held.Has(model, HostedRoles.TurnDetector))
            throw new HostRequestException(HostErrorKind.Invalid, $"{model.DisplayName} has no turn detector to listen for the end of a turn with.");

        async Task<ModelSpec?> PartAsync(string role) => held.Has(model, role) ? await held.ResolveAsync(held.RefOf(model, role), stage, ct) : null;
        return new ListenModels(
            await PartAsync(HostedRoles.VoiceActivity) ?? throw new HostRequestException(HostErrorKind.NotFound, $"{model.DisplayName} has no voice activity detector."),
            await PartAsync(HostedRoles.Recognizer) ?? throw new HostRequestException(HostErrorKind.NotFound, $"{model.DisplayName} has no recognizer."),
            await PartAsync(HostedRoles.Transcriber),
            await PartAsync(HostedRoles.TurnDetector));
    }

    /// <summary>The voices of the voice model the catalog calls <paramref name="id"/>, in its catalog's order — the first reads any
    /// language none is for — each with its part where it runs, and its lexicon a file of that part's build.</summary>
    /// <exception cref="HostRequestException">The catalog names no such model, or it has no voice.</exception>
    public async Task<IReadOnlyList<VoiceSpec>> VoicesAsync(string id, Func<string, Task> stage, CancellationToken ct)
    {
        var model = held.ModelOf(id);
        if (model.Voices.Count == 0) throw new HostRequestException(HostErrorKind.Invalid, $"{model.DisplayName} has no voice to speak with.");
        var voices = new List<VoiceSpec>(model.Voices.Count);
        foreach (var voice in model.Voices)
        {
            var spec = await held.ResolveAsync(new ModelRef(model.Id, voice.Part), stage, ct);
            voices.Add(new VoiceSpec(voice.Language, spec, voice.File, voice.Lexicon is { } lexicon ? held.PathOf($"{spec.Folder}/{lexicon}") : null));
        }
        return voices;
    }
}
