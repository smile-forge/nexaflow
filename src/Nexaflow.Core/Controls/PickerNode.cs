using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Visuals.Common.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Nexaflow.Core.Controls;

/// <summary>
/// A row of a picker's tree: a place on disk whose contents are read the first time it is opened.
/// <para>
/// The read runs off the UI thread and lands as one change, because a folder can hold tens of thousands of entries —
/// %TEMP% routinely does — and a picker that read them on the UI thread, one notification each, showed a blank window
/// for seconds before it drew anything. Opening a folder twice reads it once.
/// </para>
/// </summary>
internal abstract partial class PickerNode<TNode> : ObservableObject where TNode : PickerNode<TNode>
{
    public string FullPath    { get; }
    public string DisplayName { get; }

    /// <summary>Holds a placeholder until the node is opened, which is what gives an unread folder its expander.</summary>
    public RangeObservableCollection<TNode> Children { get; } = [];

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isSelected;

    private Task? _reading;

    protected PickerNode(string fullPath, string displayName, TNode? placeholder)
    {
        FullPath    = fullPath;
        DisplayName = displayName;
        if (placeholder is not null) Children.Add(placeholder);
    }

    /// <summary>
    /// Reads the node's contents into <see cref="Children"/>, once. Called on the UI thread; the read itself is not, and
    /// the children land back on the caller's thread. A place that refuses to be read opens empty.
    /// </summary>
    public Task LoadAsync() => _reading ??= ReadIntoChildrenAsync();

    private async Task ReadIntoChildrenAsync()
    {
        if (FullPath.Length == 0) return;

        var path = FullPath;
        var read = await Task.Run(() =>
        {
            try { return ReadChildren(path); }
            catch (UnauthorizedAccessException) { return []; }
            catch (IOException) { return []; }
        });

        Children.ReplaceAll(read);
    }

    /// <summary>What <paramref name="path"/> holds, in the order it is shown. Runs on a worker thread.</summary>
    protected abstract IReadOnlyList<TNode> ReadChildren(string path);
}
