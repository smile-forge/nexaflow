using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

using Nexaflow.Markdown.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// The surface as a view of its document's <see cref="ContentEditor"/>: what a page asks of the caret, what is picked out and the page
/// goes through the editor, which the surface makes as it makes a key — and the window's own commands for the clipboard and the history
/// are answered here, so a toolbar's buttons and the menu's are enabled when they can do something.
/// </summary>
public sealed partial class NexaTextSurface
{
    /// <summary>
    /// The view of the document this shows — where its caret is, what is picked out, which lines are on its page — which a page holds and
    /// reads and moves them through, whatever draws them. Handed over, it brings its document with it; otherwise the surface has one of
    /// its own over <see cref="Document"/>.
    /// </summary>
    public static readonly DependencyProperty EditorProperty = DependencyProperty.Register(
        nameof(Editor), typeof(ContentEditor), typeof(NexaTextSurface),
        new PropertyMetadata(null, (surface, args) =>
        {
            if (args.NewValue is ContentEditor editor) ((NexaTextSurface)surface)._engine.Editor = editor;
        }));

    /// <summary>The view of the document this shows — the page's, where it handed one over, and otherwise one of the surface's own.</summary>
    public ContentEditor Editor
    {
        get => _engine.Editor;
        set => SetValue(EditorProperty, value);
    }

    /// <summary>
    /// Moves the page so line <paramref name="line"/>, counting from nought, stands where <paramref name="place"/> says — once the page is
    /// laid out, where it is not yet.
    /// </summary>
    public void ScrollToLine(int line, ShownPlace place = ShownPlace.Nearest)
    {
        if (_shown.ActualHeight <= 0)
        {
            Dispatcher.BeginInvoke(() => Scrolled(line, place), DispatcherPriority.Loaded);
            return;
        }

        Scrolled(line, place);
    }

    private void Scrolled(int line, ShownPlace place)
    {
        if (_engine.ScrollToLine(line, place, Seen.Height) is { } box) HolderBrings(box);
    }

    /// <summary>Tells the view of the document which lines are on the page, where anything holds a view to be told.</summary>
    private void Reported()
    {
        if (_engine.EditorIfAny is not { } editor) return;

        var lines = LinesShown();
        if (lines.Count > 0) editor.Showed(lines[0].Number - 1, lines[^1].Number - 1);
    }

    /// <summary>
    /// The window's commands for the clipboard and the history, answered by what the content can do now: a toolbar button or menu item
    /// aimed at this, with its keys, enabled where it would do something.
    /// </summary>
    private void Commanding()
    {
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, _) => CopySelection(), (_, args) => args.CanExecute = _engine.State.HasSelection));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Cut, (_, _) => Cut(), (_, args) => args.CanExecute = Writable && _engine.State.HasSelection));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste, (_, _) => _engine.Paste(), (_, args) => args.CanExecute = Writable));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Delete, (_, _) => _engine.Delete(), (_, args) => args.CanExecute = Writable));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Undo, (_, _) => Undo(), (_, args) => args.CanExecute = Writable && CanUndo));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Redo, (_, _) => Redo(), (_, args) => args.CanExecute = Writable && CanRedo));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.SelectAll, (_, _) => _engine.SelectAll(), (_, args) => args.CanExecute = true));
    }

    /// <summary>Whether anybody may write in what it shows.</summary>
    private bool Writable => !IsReadOnly;
}
