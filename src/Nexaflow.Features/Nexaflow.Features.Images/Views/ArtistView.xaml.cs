using Nexaflow.Features.Common;
using Nexaflow.Features.Images.ViewModels;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Nexaflow.Features.Images.Views;

/// <summary>Artist's page: the picture on the editing canvas with the tools beside it, or the start screen while it holds none.</summary>
public partial class ArtistView : UserControl, IPageView
{
    public ArtistViewModel ViewModel { get; }

    public ArtistView(ArtistViewModel viewModel)
    {
        InitializeComponent();
        ViewModel   = viewModel;
        DataContext = viewModel;
        viewModel.Host = this;

        // The view's Loaded/Unloaded is the active-tab signal (the shell hosts the active page in one ContentPresenter, so an
        // inactive tab's content leaves the visual tree).
        Loaded   += (_, _) => { ViewModel.OnActivated(); Focus(); };
        Unloaded += (_, _) => ViewModel.OnDeactivated();
    }

    // ── IPageView ─────────────────────────────────────────────────────────

    IPageViewModel? IPageView.ViewModel => ViewModel;

    void IPageView.Reinitialize(Dictionary<string, string> pageParams)
    {
        if (ArtistTabRegistration.ParseTool(pageParams.GetValueOrDefault("tool")) is { } tool && ViewModel.IsEditing)
            ViewModel.Editor.Tool = tool;
    }

    // ── Keyboard: history and saving, painting and arranging, what a selection does to a layer ──

    private void OnViewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBoxBase) return;   // a text box's own keys are its own
        if (!ViewModel.IsEditing) return;              // the start screen's keys are its controls'
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control), shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        bool bare = Keyboard.Modifiers == ModifierKeys.None;
        var editor = ViewModel.Editor;
        bool selects = editor.TakesSelection || ViewModel.IsLayered;

        switch (e.Key)
        {
            case Key.Z when ctrl && shift:
            case Key.Y when ctrl:
                ViewModel.RedoEditCommand.Execute(null);
                break;
            case Key.Z when ctrl:
                ViewModel.UndoEditCommand.Execute(null);
                break;
            case Key.S when ctrl && shift:
                editor.SaveCopyCommand.Execute(null);
                break;
            case Key.S when ctrl:
                editor.SaveCommand.Execute(null);
                break;
            case Key.P when ctrl && !shift:
                if (ViewModel.Printing.CanPrint) ViewModel.Printing.IsOpen = true;
                break;
            // Painting and arranging: B the brush, E the eraser, I the eyedropper, G the fill, V move, Ctrl+T transform.
            case Key.B when bare:
                ChoosePaint(PaintTool.Brush);
                break;
            case Key.E when bare:
                ChoosePaint(PaintTool.Eraser);
                break;
            case Key.I when bare:
                ChoosePaint(PaintTool.Eyedropper);
                break;
            case Key.G when bare:
                ChoosePaint(PaintTool.Fill);
                break;
            case Key.V when bare:
                editor.Tool = EditTool.Move;
                break;
            case Key.T when ctrl && !shift && ViewModel.IsLayered:
                editor.Tool = EditTool.Move;
                ViewModel.Arranging.TransformCommand.Execute(null);
                break;
            case Key.Enter when ViewModel.Arranging.IsTransforming:
                ViewModel.Arranging.Apply();
                break;
            case Key.Escape when ViewModel.Arranging.IsTransforming:
                ViewModel.Arranging.Cancel();
                break;
            case Key.Enter when ViewModel.Arranging.HasFloating:
                ViewModel.Arranging.Drop();
                break;
            // A layered picture: what a selection does to the active layer, wherever the tools are.
            case Key.Delete or Key.Back when ViewModel.IsLayered && editor.RegionMarks.Draft.Count == 0
                                             && !(editor.IsMarkupTool && editor.HasSelection):
                ViewModel.Layers.DeleteSelectedCommand.Execute(null);
                break;
            case Key.F5 when shift && ViewModel.IsLayered:
                ViewModel.Layers.FillCommand.Execute(null);
                break;
            case Key.C when ctrl && ViewModel.IsLayered:
                (shift ? ViewModel.Layers.CopyMergedCommand : ViewModel.Layers.CopyCommand).Execute(null);
                break;
            case Key.X when ctrl && ViewModel.IsLayered:
                ViewModel.Layers.CutCommand.Execute(null);
                break;
            case Key.V when ctrl:
                if (ViewModel.IsLayered) ViewModel.Layers.PasteCommand.Execute(null);
                else ViewModel.PasteAsLayerCommand.Execute(null);
                break;
            case Key.Delete when editor.IsMarkupTool:
                editor.DeleteSelectedCommand.Execute(null);
                break;
            case Key.M when bare && selects:
                editor.ChooseMarquee();
                break;
            case Key.L when bare && selects:
                editor.ChooseLasso();
                break;
            case Key.W when bare && selects:
                editor.ChooseColour();
                break;
            case Key.A when ctrl && !shift && selects:
                editor.RegionMarks.SelectAllCommand.Execute(null);
                break;
            case Key.D when ctrl && !shift && selects:
                editor.RegionMarks.DeselectCommand.Execute(null);
                break;
            case Key.I when ctrl && shift && selects:
                editor.RegionMarks.InvertCommand.Execute(null);
                break;
            case Key.Back when editor.RegionMarks.Draft.Count > 0:
                editor.RegionMarks.RemoveDraftPoint();
                break;
            case Key.Enter when editor.RegionMarks.Draft.Count > 0:
                editor.RegionMarks.CloseDraft();
                break;
            case Key.Enter when editor.IsCropTool:
                editor.Tool = EditTool.Adjust;   // the crop is applied: show the cropped picture
                break;
            case Key.Escape:
                if (editor.RegionMarks.CancelDraft()) break;   // an outline half drawn goes first
                if (editor.IsCropTool) editor.CancelCrop();
                else if (editor.RegionMarks.HasSelection) editor.RegionMarks.DeselectCommand.Execute(null);   // then the selection, as Ctrl+D lets it go
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    /// <summary>The Paint tool out, in the way <paramref name="tool"/> says.</summary>
    private void ChoosePaint(PaintTool tool)
    {
        ViewModel.Editor.Tool = EditTool.Paint;
        ViewModel.Painting.Tool = tool;
    }

    // ── The save options' quality slider: a drag is one undo step, as in the tool panel ──

    private void SaveOptions_DragStarted(object sender, DragStartedEventArgs e) => ViewModel.Editor.BeginGesture();

    private void SaveOptions_DragCompleted(object sender, DragCompletedEventArgs e) => ViewModel.Editor.EndGesture();
}
