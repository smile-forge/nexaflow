using Nexaflow.AiHost.Provisioning;
using Nexaflow.Providers.Common;
using Nexaflow.Session.Services.Tools;

namespace Nexaflow.Session.Services.LocalAi;

/// <summary>What something on this machine's disk is: a model, a tool's own model, or a runtime models share.</summary>
public enum LocalDiskItemSort { Model, Tool, Runtime }

/// <summary>A model, a tool's model or a runtime on disk: the bytes of it here, the workspaces that use it, and its files.</summary>
/// <param name="Kind">The kind of model; for a tool or a runtime, none.</param>
/// <param name="UsedBy">The workspaces whose ability grid uses it — through a model, for a runtime. A tool belongs to none.</param>
public sealed record LocalDiskItem(string Name, LocalDiskItemSort Sort, ModelKind? Kind, long Bytes, IReadOnlyList<string> UsedBy,
                                   IReadOnlyList<ModelAsset> Files)
{
    /// <summary>A workspace uses it: deleting it would leave their jobs with nothing to run, so it stays.</summary>
    public bool InUse => UsedBy.Count > 0;
}

/// <summary>
/// The models on this machine's disk, the tools' models and the runtimes they share, each with what it takes and who uses it, and
/// what they take together. Two builds of one model can share files: each counts them in its own size, the total counts them once,
/// and a build whose own files are gone — the parts it shared staying for the other — is not listed, having nothing of its own left.
/// </summary>
/// <param name="TotalBytes">What they take on disk together, each file once.</param>
public sealed record LocalModelUsage(IReadOnlyList<LocalDiskItem> Items, long TotalBytes)
{
    /// <param name="shelf">The models on this machine.</param>
    /// <param name="workspaces">Every workspace's ability grid, by name — who uses what.</param>
    /// <param name="tools">The tools with models of their own.</param>
    public static LocalModelUsage Of(LocalModelShelf shelf, IReadOnlyList<(string Workspace, AiConfig Config)> workspaces,
                                     IReadOnlyList<ILocalTool> tools)
    {
        long Here(IEnumerable<ModelAsset> files) => files.Where(f => shelf.Store.Has(f)).Sum(f => f.Size);

        var options = shelf.All;
        var usedBy  = options.ToDictionary(o => o, o => (IReadOnlyList<string>)[.. workspaces.Where(w => Uses(w.Config, o)).Select(w => w.Workspace)]);
        var onDisk  = options.ToDictionary(o => o, o => o.Model.Files.Where(f => shelf.Store.Has(f))
                                                                     .Select(f => f.RelativePath).ToHashSet(StringComparer.Ordinal));
        var counted = new HashSet<string>(StringComparer.Ordinal);   // a file two builds share takes its room once
        var items   = new List<LocalDiskItem>();
        long total  = 0;

        foreach (var option in options)
        {
            var here = Here(option.Model.Files);
            if (here == 0 || Covered(option, onDisk)) continue;
            total += Here(option.Model.Files.Where(f => counted.Add(f.RelativePath)));
            items.Add(new LocalDiskItem(option.Model.DisplayName, LocalDiskItemSort.Model, option.Kind, here, usedBy[option], option.Model.Files));
        }
        foreach (var tool in tools)
        {
            var here = Here(tool.OwnFiles);
            if (here == 0) continue;
            total += Here(tool.OwnFiles.Where(f => counted.Add(f.RelativePath)));
            items.Add(new LocalDiskItem(tool.Name, LocalDiskItemSort.Tool, null, here, [], tool.OwnFiles));
        }
        foreach (var runtime in shelf.Runtimes)
        {
            var here = Here(runtime.Files);
            if (here == 0) continue;
            total += Here(runtime.Files.Where(f => counted.Add(f.RelativePath)));
            IReadOnlyList<string> users = [.. options.Where(o => o.Runtime?.Id == runtime.Id).SelectMany(o => usedBy[o]).Distinct()];
            items.Add(new LocalDiskItem(runtime.DisplayName, LocalDiskItemSort.Runtime, null, here, users, runtime.Files));
        }
        return new LocalModelUsage(items, total);
    }

    /// <summary>
    /// Whether all of <paramref name="option"/>'s files here are another model's that is here beyond them — a build whose own
    /// files are gone, the parts it shared with another build staying for that one.
    /// </summary>
    private static bool Covered(LocalModelOption option, IReadOnlyDictionary<LocalModelOption, HashSet<string>> onDisk)
        => onDisk.Any(other => !ReferenceEquals(other.Key, option) && other.Value.Except(onDisk[option]).Any()
                               && onDisk[option].All(path => other.Key.Model.Files.Any(f => f.RelativePath == path)));

    /// <summary>Whether a grid has some ability use the model: a column holding it that a chain names.</summary>
    private static bool Uses(AiConfig config, LocalModelOption option)
    {
        var assigned = config.AssignedColumnIds();
        return config.Columns.Any(c => assigned.Contains(c.Id) && c.Kind == option.Kind && option.IsIn(c));
    }
}
