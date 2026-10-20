using Nexaflow.Core.AI;
using Nexaflow.Providers.Common;

namespace Nexaflow.Core.Services.LocalAi;

/// <summary>
/// What fetching a set of models on this machine comes to: a row for each model with files still to come, and one for each
/// runtime they share — the CUDA pack — counted once however many models need it; the bytes still to come, each file once
/// and less what is already on disk; and the room the fetch takes on the asset store's drive, staging included, against what
/// the drive has left.
/// </summary>
public sealed record LocalDownloadPlan(IReadOnlyList<LocalDownloadPlan.Row> Rows, long Bytes, ModelRoom Room)
{
    /// <summary>One line of the plan: a model, with the option that fetches it, or a runtime models share, with none.</summary>
    /// <param name="Bytes">What it adds to the plan — a file another row lists already counts there.</param>
    public sealed record Row(string Name, ModelKind? Kind, long Bytes, LocalModelOption? Option);

    public static LocalDownloadPlan For(IReadOnlyList<LocalModelOption> options, ModelAssetStore store)
    {
        var counted = new HashSet<string>(StringComparer.Ordinal);
        long Missing(IEnumerable<ModelAsset> files)
            => files.Where(f => counted.Add(f.RelativePath) && store.Locate(f) is null).Sum(f => f.Size);

        var rows = new List<Row>();
        foreach (var option in options)
            if (Missing(option.Model.Files) is > 0 and var bytes)
                rows.Add(new Row(option.Model.DisplayName, option.Kind, bytes, option));
        foreach (var runtime in options.Select(o => o.Runtime).OfType<LocalRuntime>().DistinctBy(r => r.Id))
            if (Missing(runtime.Files) is > 0 and var bytes)
                rows.Add(new Row(runtime.DisplayName, null, bytes, null));

        return new LocalDownloadPlan(rows, rows.Sum(r => r.Bytes), store.RoomFor(options.SelectMany(o => o.Needs)));
    }

    /// <summary>The models a workspace's grid has the Local column hold for <paramref name="slots"/>, in rows that use it, that
    /// run here and are not all here yet — each once.</summary>
    public static IReadOnlyList<LocalModelOption> Awaited(AiConfig config, LocalModelShelf shelf, IEnumerable<LocalSlot> slots)
    {
        var awaited = new List<LocalModelOption>();
        foreach (var slot in slots)
        {
            var id = LocalColumns.IdOf(slot);
            if (!LocalColumns.AbilitiesOf(slot).Any(a => config.Assignments.GetValueOrDefault(a.ToString())?.ColumnIds.Contains(id) == true)) continue;
            if (config.Columns.FirstOrDefault(c => c.Id == id) is not { } chosen) continue;
            if (shelf.OptionsFor(slot).FirstOrDefault(o => o.IsIn(chosen)) is { Runs: true } option && !shelf.IsReady(option)
                && !awaited.Any(a => a.IsIn(option.Column())))
                awaited.Add(option);
        }
        return awaited;
    }
}
