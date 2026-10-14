using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Core.AI;
using Nexaflow.Core.Services.LocalAi;
using Nexaflow.Providers.Common;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.ViewModels;

/// <summary>
/// The models on this machine's disk, and the runtimes they share: each with its size and the workspaces that use it, and
/// Delete for one no workspace uses — a model in use stays, naming who uses it, since removing it would leave their jobs
/// with nothing to run.
/// </summary>
public sealed partial class DownloadedModelsViewModel : ObservableObject
{
    private readonly LocalModelShelf _shelf;
    private readonly Func<IReadOnlyList<(string Workspace, AiConfig Config)>> _workspaces;
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;

    /// <param name="workspaces">Every workspace's grid as saved, by name — who uses what.</param>
    public DownloadedModelsViewModel(LocalModelShelf shelf, Func<IReadOnlyList<(string Workspace, AiConfig Config)>> workspaces)
    {
        _shelf      = shelf;
        _workspaces = workspaces;
        shelf.Store.Changed += (_, _) => _ui.InvokeAsync(Refresh);
        Refresh();
    }

    public ObservableCollection<DownloadedModelViewModel> Models { get; } = [];

    /// <summary>What they take on disk together.</summary>
    [ObservableProperty] private string _total = string.Empty;

    [ObservableProperty] private bool _isEmpty;

    /// <summary>Lists what is on disk again, and who uses each.</summary>
    public void Refresh()
    {
        var workspaces = _workspaces();
        var options    = _shelf.All;
        var usedBy     = options.ToDictionary(o => o, o => workspaces.Where(w => Uses(w.Config, o)).Select(w => w.Workspace).ToList());

        Models.Clear();
        long total = 0;
        foreach (var option in options)
        {
            var here = Here(option.Model.Files);
            if (here == 0) continue;
            total += here;
            Models.Add(new DownloadedModelViewModel(this, option.Model.DisplayName, KindOf(option.Kind), here, usedBy[option], option.Model.Files));
        }
        foreach (var runtime in _shelf.Runtimes)
        {
            var here = Here(runtime.Files);
            if (here == 0) continue;
            total += here;
            var users = options.Where(o => o.Runtime?.Id == runtime.Id).SelectMany(o => usedBy[o]).Distinct().ToList();
            Models.Add(new DownloadedModelViewModel(this, runtime.DisplayName, Str.Get("Shell.Config.Downloaded.Runtime"), here, users, runtime.Files));
        }
        Total   = Str.Format("Shell.Config.Downloaded.TotalFormat", SizeFormatter.FormatBytes(total));
        IsEmpty = Models.Count == 0;
    }

    internal Task RemoveAsync(IReadOnlyList<ModelAsset> files) => _shelf.RemoveAsync(files);

    /// <summary>The bytes of <paramref name="files"/> on disk.</summary>
    private long Here(IEnumerable<ModelAsset> files) => files.Where(f => _shelf.Store.Locate(f) is not null).Sum(f => f.Size);

    /// <summary>Whether a grid has some ability use the model: a column holding it that a chain names.</summary>
    private static bool Uses(AiConfig config, LocalModelOption option)
    {
        var assigned = config.AssignedColumnIds();
        return config.Columns.Any(c => assigned.Contains(c.Id) && c.Kind == option.Kind && option.IsIn(c));
    }

    private static string KindOf(ModelKind kind) => kind == ModelKind.Language
        ? Str.Get("Shell.Config.Downloaded.Language")
        : ProviderOffer.KindLabel(kind);
}

/// <summary>A model or runtime on disk.</summary>
public sealed partial class DownloadedModelViewModel(DownloadedModelsViewModel page, string name, string kind, long bytes,
                                                     IReadOnlyList<string> usedBy, IReadOnlyList<ModelAsset> files) : ObservableObject
{
    public string Name => name;
    public string Kind => kind;
    public string Size => SizeFormatter.FormatBytes(bytes);

    /// <summary>The workspaces that use it.</summary>
    public IReadOnlyList<string> Users => usedBy;

    public bool InUse => usedBy.Count > 0;

    /// <summary>Who uses it, or that nothing does.</summary>
    public string UsedBy => InUse ? Str.Format("Shell.Config.Downloaded.UsedByFormat", string.Join(", ", usedBy)) : Str.Get("Shell.Config.Downloaded.Unused");

    /// <summary>What Delete does, or why it cannot.</summary>
    public string DeleteTip => InUse ? Str.Format("Shell.Config.Downloaded.KeptFormat", string.Join(", ", usedBy)) : Str.Get("Shell.Config.Downloaded.DeleteTip");

    private bool CanDelete => !InUse;

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task Delete()
    {
        await page.RemoveAsync(files);
        page.Refresh();
    }
}
