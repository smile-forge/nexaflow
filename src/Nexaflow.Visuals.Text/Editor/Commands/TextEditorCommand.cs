using System;
using System.Text;
using Nexaflow.IO.Common;

namespace Nexaflow.Visuals.Text.Editor.Commands;

/// <summary>Whether a command acts on the current selection or the whole document.</summary>
public enum TextEditScope { Selection, Document }

/// <summary>
/// The live surface a command operates on. Deliberately plain data + callbacks (no AvalonEdit types) so
/// commands are unit-testable without a control; the editor view-model fills it from the real editor.
/// </summary>
public sealed class TextEditorContext
{
    public required string DocumentText { get; init; }
    public required string SelectedText { get; init; }
    public required bool HasSelection { get; init; }
    public required bool CanEdit { get; init; }
    public required Encoding Encoding { get; init; }
    public required LineEnding Eol { get; init; }

    /// <summary>Replace the entire document text.</summary>
    public required Action<string> ReplaceDocument { get; init; }
    /// <summary>Replace just the current selection.</summary>
    public required Action<string> ReplaceSelection { get; init; }

    /// <summary>Surface a (label, value) result — e.g. a checksum. Typically copies the value and notifies.</summary>
    public Action<string, string>? ShowResult { get; init; }
    /// <summary>Surface a non-fatal error — e.g. invalid Base64.</summary>
    public Action<string>? ShowError { get; init; }
}

/// <summary>A command shown in the editor's command-group dropdowns.</summary>
public interface ITextEditorCommand
{
    string Name { get; }
    string Group { get; }
    TextEditScope Scope { get; }
    /// <summary>True if it mutates the document (so it's unavailable in read-only mode).</summary>
    bool IsDestructive { get; }
    /// <summary>True for line-munging ops (sort/dedup/remove-empty) that only make sense on prose/data —
    /// hidden when the file is code/markup, where reordering lines is nonsensical.</summary>
    bool PlainTextOnly { get; }
    bool CanExecute(TextEditorContext ctx);
    void Execute(TextEditorContext ctx);
}

/// <summary>Base implementing the standard gating: destructive commands need an editable buffer, and
/// selection-scoped commands need a non-empty selection.</summary>
public abstract class TextEditorCommandBase : ITextEditorCommand
{
    public abstract string Name { get; }
    public abstract string Group { get; }
    public abstract TextEditScope Scope { get; }
    public abstract bool IsDestructive { get; }
    public virtual bool PlainTextOnly => false;

    public virtual bool CanExecute(TextEditorContext ctx)
        => (!IsDestructive || ctx.CanEdit)
        && (Scope != TextEditScope.Selection || ctx.HasSelection);

    public abstract void Execute(TextEditorContext ctx);
}

/// <summary>Lambda-backed command, so the built-ins stay declarative.</summary>
public sealed class DelegateEditorCommand(
    string name, string group, TextEditScope scope, bool isDestructive,
    Action<TextEditorContext> execute, bool plainTextOnly = false)
    : TextEditorCommandBase
{
    public override string Name => name;
    public override string Group => group;
    public override TextEditScope Scope => scope;
    public override bool IsDestructive => isDestructive;
    public override bool PlainTextOnly => plainTextOnly;
    public override void Execute(TextEditorContext ctx) => execute(ctx);
}
