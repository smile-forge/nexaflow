using Nexaflow.Markdown.Editing;

namespace Nexaflow.Tests.Features.Infrastructure;

/// <summary>
/// What a page's editor asks of whatever shows it, kept for a test to read: the lines it asked to be put on screen, and where. It takes what
/// the editor hands it as a view does — the caret and what is picked out stand where they were put — and draws nothing.
/// </summary>
public sealed class ScrollRecorder : IContentEditorView
{
    private readonly ContentEditor _editor;

    /// <summary>Shows <paramref name="editor"/> from now on, keeping what it asks.</summary>
    public ScrollRecorder(ContentEditor editor)
    {
        _editor = editor;
        editor.Attach(this);
    }

    /// <summary>Every line the editor asked to be put on screen, from nought, with where — in the order it asked.</summary>
    public List<(int Line, ShownPlace Place)> Shown { get; } = [];

    public void Take(EditState next, bool written) => _editor.Stood(next);

    public void ShowLine(int line, ShownPlace place) => Shown.Add((line, place));

    public IReadOnlyList<(int Start, int End)> Foldable() => [];

}
