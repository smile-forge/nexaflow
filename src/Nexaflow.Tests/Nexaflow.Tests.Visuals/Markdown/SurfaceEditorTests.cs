using System.Linq;
using System.Windows;
using System.Windows.Controls;

using Nexaflow.Markdown.Editing;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// A page's view of its document handed to the surface that shows it: where the page put the caret before anything showed it is where the
/// surface shows it, however the surface is set up around it — told its language, made again for it — and the page opens where that is.
/// <para>Interactive desktop only — the surface lays its document during a render pass.</para>
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("content-editor")]
public class SurfaceEditorTests
{
    private static readonly string Code = string.Join("\n", Enumerable.Range(1, 400).Select(line => $"var line{line} = {line};"));

    [TestMethod]
    public void TheCaretAPagePut_StaysWhereItPutIt_TheLanguageToldAfterTheView() => UiThread.Run(() =>
        Shown(surface => surface.WrittenIn = "c-sharp"));

    [TestMethod]
    public void TheCaretAPagePut_StaysWhereItPutIt_TheLanguageToldBeforeTheView() => UiThread.Run(() =>
        Shown(surface => surface.WrittenIn = "c-sharp", editorLast: true));

    /// <summary>
    /// A page's document and view, the caret at the start of its fourth line, shown on a surface set up by <paramref name="setUp"/> — the view
    /// handed over before it, or after where <paramref name="editorLast"/> says — and the caret and the page checked once it is laid.
    /// </summary>
    private static void Shown(System.Action<NexaTextSurface> setUp, bool editorLast = false)
    {
        var document = new EditedDocument();
        document.Load(Code);
        using var editor = new ContentEditor(document);
        var put = document.Current.OffsetOf(3);
        editor.MoveCaretTo(put);

        var surface = new NexaTextSurface { IsReadOnly = false, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Width = 600, Height = 400 };
        if (!editorLast) surface.Editor = editor;
        setUp(surface);
        if (editorLast) surface.Editor = editor;

        var window = new Window { Width = 640, Height = 480, Content = surface, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -2000, Top = -2000 };

        try
        {
            window.Show();
            surface.UpdateLayout();
            MarkdownEditorHarness.Pump();

            Assert.AreEqual(put, editor.Caret, "the caret the page put was moved by setting up what shows it");
            Assert.AreEqual(put, surface.Engine.State.Caret, "the surface shows the caret somewhere else than the page put it");
            Assert.AreEqual(0, surface.VerticalOffset, "the page opened away from where its caret is");
        }
        finally { window.Close(); }
    }
}
