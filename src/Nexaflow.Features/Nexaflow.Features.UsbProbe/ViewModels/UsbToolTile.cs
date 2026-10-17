using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Icons;
using Nexaflow.IO.Usb.Tools;
using Nexaflow.Visuals.Common.Behaviors;

namespace Nexaflow.Features.UsbProbe.ViewModels;

/// <summary>
/// One live tool on the device page: the tool, the tile around it, and when it runs. A tool runs while its tile is open
/// and the Live tools tab is in view; collapsing the tile or leaving the tab stops what is live in it.
/// </summary>
public sealed partial class UsbToolTile : ObservableObject, IDisposable
{
    readonly CancellationTokenSource _lifetime = new();
    bool _inView, _running;

    public UsbToolTile(IUsbTool tool, string? functionName, bool expanded)
    {
        Tool = tool;
        FunctionName = functionName;
        _isExpanded = expanded;
    }

    public IUsbTool Tool { get; }
    public string Key => Tool.Key;
    public bool IsWide => Tool.Size == UsbToolSize.Wide;

    /// <summary>The function the tool works through, as the Interfaces tab names it — only on a device with several.</summary>
    public string? FunctionName { get; }

    [ObservableProperty] private bool _isExpanded;

    /// <summary>Why the tool could not start, shown in the tile instead of its content.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    public bool HasError => Error is not null;

    partial void OnIsExpandedChanged(bool value) => Sync();

    /// <summary>The page says whether the tile can be seen: the page is shown and the Live tools tab is the one open.</summary>
    internal void SetInView(bool inView)
    {
        _inView = inView;
        Sync();
    }

    void Sync()
    {
        bool run = _inView && IsExpanded && !_lifetime.IsCancellationRequested;
        if (run == _running) return;
        _running = run;
        if (run) _ = ActivateAsync();
        else Tool.Deactivate();
    }

    async Task ActivateAsync()
    {
        try
        {
            Error = null;
            await Tool.ActivateAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Error = e.Message;
        }
    }

    public void Dispose()
    {
        if (_lifetime.IsCancellationRequested) return;
        _lifetime.Cancel();
        Tool.Dispose();
        _lifetime.Dispose();
    }
}

/// <summary>A function no live tool covers, with what it is and why nothing here talks to it.</summary>
public sealed record UsbUncoveredFunction(string Name, string ClassName, string Guidance);

/// <summary>The last tile on the Live tools tab: everything on the device no tool reached, and why.</summary>
public sealed class UsbUncoveredTile(IReadOnlyList<UsbUncoveredFunction> functions)
{
    public IReadOnlyList<UsbUncoveredFunction> Functions { get; } = functions;
}

/// <summary>One line of what the device's tools did, for the Diagnostics tab.</summary>
public sealed record UsbToolLogEntry(DateTimeOffset At, string Tool, string Message) : IClipboardText
{
    public string Time => At.ToLocalTime().ToString("HH:mm:ss");
    public string ClipboardText => $"{Time}\t{Tool}\t{Message}";
}

/// <summary>An interface association, interface or endpoint in the Interfaces tab's list, indented under its parent.</summary>
public sealed class UsbStructureRow(Nexaflow.IO.Usb.Browsing.UsbLocation location, int depth) : IClipboardText
{
    public string ClipboardText => $"{Path}\t{Name}\t{Summary}";
    public Nexaflow.IO.Usb.Browsing.UsbLocation Location { get; } = location;
    public string Path => Location.Path;
    public IconRef Icon => UsbIcons.Of(Location);
    public string Name => Location.Name;
    public string Summary => Location.Summary;
    public System.Windows.Thickness Indent { get; } = new(depth * 18, 0, 0, 0);
}
