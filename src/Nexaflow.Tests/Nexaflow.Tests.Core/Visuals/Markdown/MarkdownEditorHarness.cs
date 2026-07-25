using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Reflection;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Core.Visuals.Markdown;

/// <summary>
/// Shared harness for driving a real <see cref="InlineMarkdownEditor"/> in a shown (off-screen) window —
/// the editor only builds its document during a render pass, so its editing behaviour can't be exercised
/// on a control that was never displayed. Interactive desktop only.
/// </summary>
internal static class MarkdownEditorHarness
{
    private static readonly FieldInfo RtbField =
        typeof(InlineMarkdownEditor).GetField("_rtb", BindingFlags.NonPublic | BindingFlags.Instance)!;

    /// <summary>Shows an editor loaded with <paramref name="markdown"/>, runs <paramref name="test"/>, closes it.</summary>
    public static void Run(string markdown, Action<InlineMarkdownEditor, RichTextBox> test)
    {
        var editor = new InlineMarkdownEditor { EditOnDoubleClick = true, Width = 600, Height = 400 };
        var window = new Window { Width = 640, Height = 480, Content = editor,
                                  WindowStartupLocation = WindowStartupLocation.Manual, Left = -2000, Top = -2000 };
        try
        {
            window.Show();
            editor.UpdateLayout();               // flip IsVisible → the render pass builds the document
            editor.Markdown = markdown;
            editor.UpdateLayout();

            var rtb = (RichTextBox)RtbField.GetValue(editor)!;
            rtb.Focus();
            test(editor, rtb);
        }
        finally { window.Close(); }
    }

    /// <summary>Raises a genuine <see cref="TextCompositionManager.PreviewTextInputEvent"/> for
    /// <paramref name="text"/> on <paramref name="rtb"/> — the same event WPF fires for a keystroke, so
    /// the editor's handler runs exactly as in the app.</summary>
    public static void RaiseTextInput(RichTextBox rtb, string text)
    {
        var composition = new TextComposition(InputManager.Current, rtb, text);
        rtb.RaiseEvent(new TextCompositionEventArgs(InputManager.Current.PrimaryKeyboardDevice, composition)
        {
            RoutedEvent = TextCompositionManager.PreviewTextInputEvent,
        });
    }

    /// <summary>Types <paramref name="text"/> one character at a time.</summary>
    public static void Type(RichTextBox rtb, string text)
    {
        foreach (var ch in text) RaiseTextInput(rtb, ch.ToString());
    }

    /// <summary>Places the caret <paramref name="textOffset"/> text characters into
    /// <paramref name="para"/>, counting Run text only (skipping element edges).</summary>
    public static void PlaceCaret(RichTextBox rtb, Paragraph para, int textOffset)
    {
        var tp = para.ContentStart;
        int remaining = textOffset;
        while (tp is not null && remaining > 0)
        {
            if (tp.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                int len = tp.GetTextRunLength(LogicalDirection.Forward);
                if (len >= remaining) { tp = tp.GetPositionAtOffset(remaining, LogicalDirection.Forward)!; break; }
                remaining -= len;
                tp = tp.GetPositionAtOffset(len, LogicalDirection.Forward);
            }
            else tp = tp.GetNextContextPosition(LogicalDirection.Forward);
        }
        rtb.CaretPosition = tp ?? para.ContentEnd;
    }
}
