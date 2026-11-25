using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.Features.Common;
using Nexaflow.Providers.Common;
using Nexaflow.Providers.Local;

namespace Nexaflow.Session.Services.LocalAi;

/// <summary>A model of Nexaflow's own a workspace uses: the model, and the provider its column runs it through —
/// which says what the model needs placed.</summary>
internal sealed record OwnedModelInUse(string ModelId, string Name, ModelKind Kind, IAiProvider Provider, IHostModelCatalog Catalog)
{
    /// <summary>A part's name as a person reads it: the model's, and the part's where the model has several.</summary>
    public string NameOf(PlacementNeed part, int parts)
        => parts == 1 ? Name : $"{Name} — {part.Id[(part.Id.IndexOf('/') + 1)..]}";
}

/// <summary>
/// Where a workspace's models of Nexaflow's own run: as the host holds them where it holds every one, as they would be placed on
/// the host's GPUs before it does — on the GPUs the adapters promise, while the host cannot be reached; a picture's parts where
/// each would load beside them.
/// </summary>
internal static class LocalAiSetupReader
{
    /// <param name="expected">The GPUs the adapters promise, for when the host cannot be reached.</param>
    public static async Task<LocalAiSetup?> DescribeAsync(HostHolds holds, IReadOnlyList<OwnedModelInUse> models, IReadOnlyList<HostGpu> expected,
                                                          CancellationToken ct)
    {
        if (models.Count == 0) return null;
        HostPlacement? held = null;
        IReadOnlyList<HostGpu> gpus;
        try
        {
            held = HostPlacement.Of(await holds.DescribeAsync(ct));
            gpus = held.Gpus;
        }
        catch (AiHostException)
        {
            gpus = expected;
        }

        var resident = models.Where(m => !m.Catalog.LoadsOnDemand).SelectMany(m => m.Catalog.NeedsOf(m.ModelId)).ToList();
        var placed = held is not null && resident.All(n => held.Of(n.Id) is not null || held.IsUnplaced(n.Id))
            ? PlacedParts.Of(held)
            : PlacedParts.Of(ModelPlacement.Plan(gpus, resident));

        var described = new List<LocalAiModel>();
        foreach (var model in models)
        {
            // Parts built for the CPU alone hold nothing on a GPU, and run there whatever the machine.
            var parts = model.Catalog.NeedsOf(model.ModelId).Where(n => !n.CpuOnly).ToList();
            foreach (var part in parts)
            {
                bool onDemand = model.Catalog.LoadsOnDemand;
                var spot = placed.Of(part, onDemand);
                described.Add(spot is null
                    ? new LocalAiModel(model.NameOf(part, parts.Count), null, 0, 0, onDemand, Displaces: false, Fits: false)
                    : new LocalAiModel(model.NameOf(part, parts.Count), spot.Device, spot.MemoryBytes, spot.ContextTokens, onDemand,
                                       Displaces: spot.Displaces, Fits: true));
            }
        }

        var language = models.FirstOrDefault(m => m.Kind == ModelKind.Language);
        IReadOnlyList<string> fitting = language?.Provider is ILocalModelCatalog catalog
            ? [.. catalog.Catalog.Where(c => c.Id != language.ModelId).Select(c => c.DisplayName)]
            : [];
        return new LocalAiSetup([.. gpus.Select(g => new LocalAiGpu(g.Device, g.Name, g.TotalBytes, g.BudgetBytes))], described, fitting);
    }
}

/// <summary>Where a part runs, as a report reads it: its GPU or the CPU, what it holds there, its context, and — for one loaded on
/// demand — whether held models make way for it.</summary>
internal sealed record PartSpot(int? Device, long MemoryBytes, int ContextTokens, bool Displaces);

/// <summary>Where a workspace's parts run: the held ones as the residents place them, one loaded on demand beside them.</summary>
internal sealed class PlacedParts(IResidents residents, Func<string, PartSpot?> held)
{
    /// <summary>As the host holds them.</summary>
    public static PlacedParts Of(HostPlacement placement)
        => new(placement, id => placement.Of(id) is { } p ? new PartSpot(p.Device, p.MemoryBytes, p.ContextTokens, false) : null);

    /// <summary>As a plan would place them.</summary>
    public static PlacedParts Of(Placement plan)
        => new(plan, id => plan.Of(id) is { } s ? new PartSpot(s.Device, s.MemoryBytes, s.ContextTokens, false) : null);

    public IResidents Residents => residents;

    /// <summary>Where <paramref name="part"/> runs; null where it fits nowhere.</summary>
    public PartSpot? Of(PlacementNeed part, bool onDemand)
        => !onDemand ? held(part.Id)
           : ModelPlacement.PlaceOnDemand(residents, part) is { } spec
               ? new PartSpot(spec.Device, spec.MemoryBytes, spec.ContextTokens, residents.Displaces(spec))
               : null;
}
