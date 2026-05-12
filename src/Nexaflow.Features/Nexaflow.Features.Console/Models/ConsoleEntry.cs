using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text;

namespace Nexaflow.Features.Console.Models;

/// <summary>
/// Represents a single command entered in the ConsoleView, together with
/// all of its output lines (stdout and stderr).
/// </summary>
public partial class ConsoleEntry : ObservableObject
{
    public string Command { get; init; } = string.Empty;

    /// <summary>Streamed output lines in the order they arrived.</summary>
    public ObservableCollection<ConsoleOutputLine> Lines { get; } = [];

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private int  _exitCode = -1;

    /// <summary>
    /// All output lines joined into a single string for display in a
    /// selectable TextBox.  Updated automatically when Lines changes.
    /// </summary>
    [ObservableProperty] private string _outputText = string.Empty;

    public ConsoleEntry()
    {
        Lines.CollectionChanged += OnLinesChanged;
    }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var sb = new StringBuilder();
        foreach (var line in Lines)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(line.Text);
        }
        SetProperty(ref _outputText, sb.ToString(), nameof(OutputText));
    }
}

/// <summary>One output line from a command's stdout or stderr.</summary>
public class ConsoleOutputLine
{
    public string Text    { get; init; } = string.Empty;
    /// <summary>True when this line came from stderr (tinted red in the UI).</summary>
    public bool   IsError { get; init; }
}

/// <summary>A name/value pair from the process environment, shown in the left panel.</summary>
public partial class EnvVar : ObservableObject
{
    public string Name  { get; init; } = string.Empty;
    [ObservableProperty] private string _value = string.Empty;
}
