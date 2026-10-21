using System.Collections.Generic;
using CommunityToolkit.Mvvm.Input;

namespace Nexaflow.Visuals.Text.Editor.Commands;

/// <summary>A dropdown button in the editor toolbar: a group — by the name it is known by, and by what it says — and its commands.</summary>
public sealed class EditorCommandGroupVm(string name, string title, IReadOnlyList<EditorCommandVm> commands)
{
    public string Name { get; } = name;
    public string Title { get; } = title;
    public IReadOnlyList<EditorCommandVm> Commands { get; } = commands;
}

/// <summary>One menu entry. <see cref="Run"/> asks the owning editor view-model to build a live context and
/// execute the command (gating + result/error surfacing happen there).</summary>
public sealed class EditorCommandVm
{
    private readonly ITextEditorCommand _command;

    public EditorCommandVm(ITextEditorCommand command, FileTextEditorViewModel owner)
    {
        _command = command;
        Run = new RelayCommand(() => owner.RunEditorCommand(command));
    }

    public string Name => _command.Name;
    public string Title => _command.Title;
    public IRelayCommand Run { get; }
}
