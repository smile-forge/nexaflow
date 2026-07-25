using System;
using System.Linq;
using System.Windows.Input;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// Formatting actions for the active (source) block, driven by the right-click
/// <see cref="MarkdownEditToolbar"/>. Each operates on the model and rebuilds via
/// <see cref="Activate(int,int)"/> — WPF never edits the document itself.
/// </summary>
public partial class InlineMarkdownEditor
{
    private void OnEditAction(MarkdownEditAction action)
    {
        if (_active < 0) { CloseEditBar(); return; }

        switch (action)
        {
            case MarkdownEditAction.Cut:   DoCut();   break;
            case MarkdownEditAction.Copy:  DoCopy();  break;
            case MarkdownEditAction.Paste: DoPaste(); break;

            case MarkdownEditAction.Bold:          WrapInline("**"); break;
            case MarkdownEditAction.Italic:        WrapInline("*");  break;
            case MarkdownEditAction.Strikethrough: WrapInline("~~"); break;
            case MarkdownEditAction.InlineCode:    WrapInline("`");  break;

            case MarkdownEditAction.H1: SetHeading(1); break;
            case MarkdownEditAction.H2: SetHeading(2); break;
            case MarkdownEditAction.H3: SetHeading(3); break;

            case MarkdownEditAction.Quote:     ToggleLinePrefix("> "); break;
            case MarkdownEditAction.CodeBlock: ToggleCodeFence();      break;
        }

        CloseEditBar();
    }

    /// <summary>Wraps the active-block selection in <paramref name="marker"/>; with no selection, inserts
    /// the markers at the caret and parks the caret between them.</summary>
    private void WrapInline(string marker)
    {
        var (from, to) = SelectionInActiveBlock();
        var block = _blocks[_active];
        if (from == to)
            Apply(MarkdownBlockFormat.InsertMarkers(block, from, marker));
        else
            EditActive(from, to, MarkdownBlockFormat.WrapSelection(block.Substring(from, to - from), marker));
    }

    /// <summary>Sets the active block's first line to a heading of <paramref name="level"/>, or removes the
    /// heading when it is already that level.</summary>
    private void SetHeading(int level) => Apply(MarkdownBlockFormat.SetHeading(_blocks[_active], level));

    /// <summary>Adds <paramref name="prefix"/> to every line of the active block, or strips it when every
    /// line already has it (toggle). Used for block quotes.</summary>
    private void ToggleLinePrefix(string prefix) =>
        Apply(MarkdownBlockFormat.ToggleLinePrefix(_blocks[_active], prefix));

    /// <summary>Wraps the active block in a <c>```</c> fence, or unwraps it when already fenced.</summary>
    private void ToggleCodeFence() => Apply(MarkdownBlockFormat.ToggleCodeFence(_blocks[_active]));

    /// <summary>Applies a <see cref="MarkdownBlockFormat"/> result to the active block.</summary>
    private void Apply((string Text, int Caret) result) => SetActiveBlock(result.Text, result.Caret);

    /// <summary>Replaces the whole active block and re-activates it with the caret at <paramref name="caret"/>.</summary>
    private void SetActiveBlock(string text, int caret)
    {
        Snapshot();
        _blocks[_active] = text;
        PushMarkdown();
        Activate(_active, Math.Clamp(caret, 0, text.Length));
    }

    // ── Edit toolbar popup ────────────────────────────────────────────────

    private void OpenEditBar()
    {
        _editBar.SetClipboardState(canCut: !_rtb.Selection.IsEmpty, canPaste: ClipboardHasContent());
        _editBarPopup.IsOpen = false;   // re-anchor at the current mouse point if already open
        _editBarPopup.IsOpen = true;
    }

    private void CloseEditBar() => _editBarPopup.IsOpen = false;
}
