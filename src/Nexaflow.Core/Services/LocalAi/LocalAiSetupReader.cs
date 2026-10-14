using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.Features.Common;
using Nexaflow.Providers.Common;

namespace Nexaflow.Core.Services.LocalAi;

/// <summary>A model of Nexaflow's own a workspace uses: the model, and the provider its column runs it through —
/// which says what the model needs placed.</summary>
internal sealed record OwnedModelInUse(string ModelId, string Name, ModelKind Kind, IAiProvider Provider, IHostModelCatalog Catalog)
{
    /// <summary>A part's name as a person reads it: the model's, and the part's where the model has several.</summary>
    public string NameOf(PlacementNeed part, int parts)
        => parts == 1 ? Name : $"{Name} — {part.Id[(part.Id.IndexOf('/') + 1)..]}";
}

/// <summary>
/// Where a workspace's models of Nexaflow's own run: as the machine's setup holds them where it holds every one, as
/// they would be placed on the host's GPUs before it does — on the GPUs the adapters promise, while the host cannot be
/// reached; a picture's parts where each would load beside them.
/// </summary>
internal static class LocalAiSetupReader
{
    /// <param name="expected">The GPUs the adapters promise, for when the host cannot be reached.</param>
    public static async Task<LocalAiSetup?> DescribeAsync(HostModelSetup setup, IReadOnlyList<OwnedModelInUse> models, IReadOnlyList<HostGpu> expected,
                                                          CancellationToken ct)
    {
        if (models.Count == 0) return null;
        IReadOnlyList<HostGpu> gpus;
        try
        {
            gpus = (await setup.DescribeAsync(ct)).Gpus;
        }
        catch (AiHostException)
        {
            gpus = expected;
        }

        var resident = models.Where(m => !m.Catalog.LoadsOnDemand).SelectMany(m => m.Catalog.NeedsOf(m.ModelId)).ToList();
        var placement = setup.Current is { } current && resident.All(n => current.Of(n.Id) is not null || current.Unplaced.Contains(n.Id))
            ? current
            : ModelPlacement.Plan(gpus, resident);

        var described = new List<LocalAiModel>();
        foreach (var model in models)
        {
            // Parts built for the CPU alone hold nothing on a GPU, and run there whatever the machine.
            var parts = model.Catalog.NeedsOf(model.ModelId).Where(n => !n.CpuOnly).ToList();
            foreach (var part in parts)
            {
                bool onDemand = model.Catalog.LoadsOnDemand;
                var spec = onDemand ? ModelPlacement.PlaceOnDemand(placement, part) : placement.Of(part.Id);
                described.Add(spec is null
                    ? new LocalAiModel(model.NameOf(part, parts.Count), null, 0, 0, onDemand, Displaces: false, Fits: false)
                    : new LocalAiModel(model.NameOf(part, parts.Count), spec.Device, spec.MemoryBytes, spec.ContextTokens, onDemand,
                                       Displaces: onDemand && placement.Displaces(spec), Fits: true));
            }
        }

        var language = models.FirstOrDefault(m => m.Kind == ModelKind.Language);
        IReadOnlyList<string> fitting = language?.Provider is ILocalModelCatalog catalog
            ? [.. catalog.Catalog.Where(c => c.Id != language.ModelId).Select(c => c.DisplayName)]
            : [];
        return new LocalAiSetup([.. gpus.Select(g => new LocalAiGpu(g.Device, g.Name, g.TotalBytes, g.BudgetBytes))], described, fitting);
    }
}
