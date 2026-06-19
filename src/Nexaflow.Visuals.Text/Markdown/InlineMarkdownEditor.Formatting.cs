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
        {
            SetActiveBlock(block[..from] + marker + marker + block[from..], from + marker.Length);
        }
        else
        {
            var selected = block.Substring(from, to - from);
            EditActive(from, to, marker + selected + marker);
        }
    }

    /// <summary>Sets the active block's first line to a heading of <paramref name="level"/>, or removes the
    /// heading when it is already that level.</summary>
    private void SetHeading(int level)
    {
        var block = _blocks[_active];
        int nl = block.IndexOf('\n');
        string first = nl < 0 ? block : block[..nl];
        string rest  = nl < 0 ? string.Empty : block[nl..];
        string body  = first.TrimStart('#').TrimStart(' ');
        string prefix = new string('#', level) + " ";
        string newFirst = first.StartsWith(prefix, StringComparison.Ordinal) ? body : prefix + body;
        SetActiveBlock(newFirst + rest, newFirst.Length);
    }

    /// <summary>Adds <paramref name="prefix"/> to every line of the active block, or strips it when every
    /// line already has it (toggle). Used for block quotes.</summary>
    private void ToggleLinePrefix(string prefix)
    {
        var lines = _blocks[_active].Split('\n');
        bool allPrefixed = lines.All(l => l.StartsWith(prefix, StringComparison.Ordinal));
        for (int i = 0; i < lines.Length; i++)
            lines[i] = allPrefixed ? lines[i][prefix.Length..] : prefix + lines[i];
        var joined = string.Join("\n", lines);
        SetActiveBlock(joined, joined.Length);
    }

    /// <summary>Wraps the active block in a <c>```</c> fence, or unwraps it when already fenced.</summary>
    private void ToggleCodeFence()
    {
        var block = _blocks[_active];
        var lines = block.Split('\n');
        bool fenced = lines.Length >= 2
            && lines[0].StartsWith("```", StringComparison.Ordinal)
            && lines[^1].StartsWith("```", StringComparison.Ordinal);
        string result = fenced ? string.Join("\n", lines[1..^1]) : "```\n" + block + "\n```";
        SetActiveBlock(result, result.Length);
    }

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
