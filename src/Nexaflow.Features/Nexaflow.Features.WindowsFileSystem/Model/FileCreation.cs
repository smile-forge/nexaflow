using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Features.Common;
using Nexaflow.IO.Common;
using Nexaflow.Localization;
using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.WindowsFileSystem.Model;

/// <summary>
/// Making a new item in the open folder: the kind chosen, the name it is given — prefilled per kind, its extension
/// following the kind — whether that name is free there, the options a kind asks for first, and the making itself.
/// </summary>
public sealed partial class FileCreation : ObservableObject
{
    private readonly FolderLocation _location;
    private readonly IVirtualFileSystem _vfs;
    private readonly IShellServices _shell;

    public FileCreation(FolderLocation location, IVirtualFileSystem vfs, IShellServices shell)
    {
        _location = location;
        _vfs      = vfs;
        _shell    = shell;
    }

    /// <summary>The kind of item chosen, or null with none.</summary>
    public IFileCreateEntry? Chosen { get; private set; }

    /// <summary>The name typed for the item; its extension is the kind's unless it has one of its own.</summary>
    [ObservableProperty] private string _fileName = string.Empty;

    /// <summary>The open folder already holds an item by the name typed.</summary>
    [ObservableProperty] private bool _nameExists;

    /// <summary>The options the chosen kind asks for, opened on the open folder while it is chosen; null for a kind a
    /// name is enough for.</summary>
    [ObservableProperty] private IFileCreateOptions? _options;

    /// <summary>The name is free and the kind's options say enough, so the item can be made.</summary>
    [ObservableProperty] private bool _canCreate;

    /// <summary>The newest name-taken question, answered once <see cref="CanCreate"/> says what it found.</summary>
    public Task NameChecked { get; private set; } = Task.CompletedTask;

    /// <summary>Chooses <paramref name="kind"/>: its options are opened and the name prefilled for it.</summary>
    public void Choose(IFileCreateEntry? kind)
    {
        Chosen   = kind;
        Options  = kind is IFileCreateWithOptions asks ? asks.OpenOptions(RealFolder(_location.CurrentPath)) : null;
        FileName = DefaultNameFor(kind);   // checks the name again when it differs
    }

    /// <summary>Asks again whether the name typed can be made — for a list of kinds just offered, the empty one included.</summary>
    public void Recheck() => RecomputeCreateState();

    /// <summary>Nothing is being made any more: the options shown are closed.</summary>
    public void Close() => Options = null;

    /// <summary>Makes the item, reporting a failure; false when there was nothing to make (no kind, no name).</summary>
    public async Task<bool> CreateAsync()
    {
        var entry = Chosen;
        if (entry is null) return false;

        var name = ResolveName();
        if (name.Length == 0) return false;

        try
        {
            if (Options is { } options) options.Create(name);
            else if (entry is IFileCreateAction action && CreateFolder() is { } folder) await action.CreateItemAsync(folder, name);
        }
        catch (Exception ex)
        {
            _shell.ShowError(Str.Format("WindowsFileSystem.Create.FailedFormat", name, ex.Message));
        }
        return true;
    }

    /// <summary>Options left behind are closed; those now shown are followed, so making waits on them.</summary>
    partial void OnOptionsChanged(IFileCreateOptions? oldValue, IFileCreateOptions? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnOptionsPropertyChanged;
            oldValue.Dispose();
        }
        if (newValue is not null) newValue.PropertyChanged += OnOptionsPropertyChanged;
        ApplyCreateGate();
    }

    private void OnOptionsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IFileCreateOptions.CanCreate) or null) ApplyCreateGate();
    }

    partial void OnFileNameChanged(string value) => RecomputeCreateState();

    private static string DefaultNameFor(IFileCreateEntry? kind)
    {
        if (kind is null) return string.Empty;
        // Extensionless kinds name the item after the kind ("New Folder", "New Product"); files use "New File.ext".
        return string.IsNullOrEmpty(kind.FileExtension)
            ? Str.Format("WindowsFileSystem.Create.NewItemFormat", kind.DisplayName)
            : Str.Get("WindowsFileSystem.Create.NewFileName") + kind.FileExtension;
    }

    /// <summary>Applies the extension rule: keep the user's extension, else append the kind's.</summary>
    private string ResolveName()
    {
        var name = (FileName ?? string.Empty).Trim();
        if (name.Length == 0 || Chosen is null) return name;
        return Path.HasExtension(name) ? name : name + Chosen.FileExtension;
    }

    /// <summary>The real directory behind a (possibly mounted) browse location, or the path itself when
    /// there isn't one. Creation and collision checks work in real paths; the mount is only a view.</summary>
    private string RealFolder(string path) => _vfs.TryResolveReal(path) ?? path;

    /// <summary>The folder the chosen kind makes its item in, as it names it: a kind made from a name alone goes through
    /// the file system, so on a location it is the location's folder and takes the location's names; a kind with options
    /// writes where Windows reaches the folder, and takes Windows' names. Null with no folder open.</summary>
    private string? CreateFolder()
        => string.IsNullOrEmpty(_location.CurrentPath) ? null
         : Chosen is IFileCreateWithOptions ? RealFolder(_location.CurrentPath)
         : new Services.ShellPath(_vfs).RealForMutation(_location.CurrentPath);

    private void RecomputeCreateState()
    {
        var name   = ResolveName();
        var folder = CreateFolder();
        bool valid = Chosen is not null && folder is not null && _vfs.CanName(folder, name);

        _nameCheck?.Cancel();
        if (!valid || string.IsNullOrEmpty(_location.CurrentPath))
        {
            NameExists = false;
            _nameFree  = valid;
            ApplyCreateGate();
            return;
        }

        _nameFree = false;   // until the folder says the name is free
        ApplyCreateGate();
        var check = _nameCheck = new CancellationTokenSource();
        NameChecked = CheckNameAsync(_vfs.ChildPath(folder!, name), check.Token);
    }

    /// <summary>Whether the folder last said the name typed is valid and free; the options the kind asks for are the
    /// other half of <see cref="CanCreate"/>.</summary>
    private bool _nameFree;

    /// <summary>Making waits on both halves: a free name, and options that say enough. Either changing re-gates it without
    /// asking the folder again.</summary>
    private void ApplyCreateGate() => CanCreate = _nameFree && (Options?.CanCreate ?? true);

    /// <summary>The newest name-taken question; an older one still waiting is cancelled.</summary>
    private CancellationTokenSource? _nameCheck;

    /// <summary>Whether the name typed is taken, asked of the folder. On a location that is a round trip, so the newest
    /// question wins and an older answer arriving late is dropped; a folder that cannot answer leaves the making to
    /// report why.</summary>
    private async Task CheckNameAsync(string full, CancellationToken ct)
    {
        bool exists;
        try
        {
            exists = await _vfs.ExistsAsync(full, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (IOException)
        {
            exists = false;
        }
        if (ct.IsCancellationRequested) return;

        NameExists = exists;
        _nameFree  = !exists;
        ApplyCreateGate();
    }
}
