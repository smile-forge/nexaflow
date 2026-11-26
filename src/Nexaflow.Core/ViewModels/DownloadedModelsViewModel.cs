using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.AiHost.Provisioning;


using Nexaflow.Providers.Common;
using Nexaflow.Localization.Formatting;
using Nexaflow.Localization;

namespace Nexaflow.Core.ViewModels;

/// <summary>
/// The models on this machine's disk, and the runtimes they share: each with its size and the workspaces that use it, and
/// Delete for one no workspace uses — a model in use stays, naming who uses it, since removing it would leave their jobs
/// with nothing to run. Two builds of one model can share files: each shows them in its size, the total counts them
/// once, and deleting one keeps what the other still needs.
/// </summary>
public sealed partial class DownloadedModelsViewModel : ObservableObject
{
    private readonly LocalModelShelf _shelf;
    private readonly Func<IReadOnlyList<(string Workspace, AiConfig Config)>> _workspaces;

    private readonly IReadOnlyList<ILocalTool> _tools;
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;

    /// <param name="workspaces">Every workspace's grid as saved, by name — who uses what.</param>
    /// <param name="tools">The tools with models of their own: each listed while its files are here, and deleted like a model
    /// no workspace uses — a tool belongs to no workspace, and downloads again when next asked for.</param>
    public DownloadedModelsViewModel(LocalModelShelf shelf, Func<IReadOnlyList<(string Workspace, AiConfig Config)>> workspaces,
                                     IReadOnlyList<ILocalTool> tools)
    {
    _shelf      = shelf;
    _workspaces = workspaces;
    _tools      = tools;
        shelf.Store.Changed += (_, _) => _ui.InvokeAsync(Refresh);
        Refresh();
    }

    public ObservableCollection<DownloadedModelViewModel> Models { get; } = [];

    /// <summary>What they take on disk together.</summary>
    [ObservableProperty] private string _total = string.Empty;

    /// <summary>The bytes they take on disk together, each file once.</summary>
    [ObservableProperty] private long _totalBytes;

    [ObservableProperty] private bool _isEmpty;

    /// <summary>Lists what is on disk again, and who uses each.</summary>
    public void Refresh()
    {
        var usage = LocalModelUsage.Of(_shelf, _workspaces(), _tools);
        Models.Clear();
        foreach (var item in usage.Items)
            Models.Add(new DownloadedModelViewModel(this, item.Name, KindOf(item), item.Bytes, item.UsedBy, item.Files));
        TotalBytes = usage.TotalBytes;
        Total   = Str.Format("Shell.Config.Downloaded.TotalFormat", SizeFormatter.FormatBytes(usage.TotalBytes));
        IsEmpty = Models.Count == 0;
    }

    internal Task RemoveAsync(IReadOnlyList<ModelAsset> files) => _shelf.RemoveAsync(files);

    private static string KindOf(LocalDiskItem item) => item.Sort switch
    {
        LocalDiskItemSort.Tool    => Str.Get("Shell.Config.Downloaded.Tool"),
        LocalDiskItemSort.Runtime => Str.Get("Shell.Config.Downloaded.Runtime"),
        _ => item.Kind == ModelKind.Language ? Str.Get("Shell.Config.Downloaded.Language") : ProviderOffer.KindLabel(item.Kind ?? ModelKind.Language),
    };
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
