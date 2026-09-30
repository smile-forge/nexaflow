using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;

using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Core.Services;

/// <summary>
/// The application's answer when a document asks for the clipboard — once, for every window and every document in it.
///
/// <para>
/// A document says what copying would put on a clipboard and asks for it to be put there; it asks for what is on one to
/// paste. The clipboard itself is the application's, shared with everything else in the window, so this is the one place
/// it is touched for a document. A host with something to say about what may leave it, or arrive, answers the same
/// events first and marks them handled.
/// </para>
/// </summary>
internal static class DocumentClipboard
{
    /// <summary>Answers every document in every window, and in anything a window pops up.</summary>
    public static void Register()
    {
        foreach (var owner in new[] { typeof(Window), typeof(Popup) })
        {
            EventManager.RegisterClassHandler(owner, MarkdownSurface.CopyingEvent, new EventHandler<ContentCopyingEventArgs>(Copied));
            EventManager.RegisterClassHandler(owner, MarkdownSurface.PastingEvent, new EventHandler<ContentPastingEventArgs>(Pasted));
        }
    }

    private static void Copied(object sender, ContentCopyingEventArgs e)
    {
        if (e.Handled) return;

        // Held by something else for the moment; the copy is not made, and saying so leaves a cut with its words.
        try { Clipboard.SetDataObject(e.Data, copy: true); e.Handled = true; }
        catch (ExternalException) { }
    }

    /// <summary>
    /// What is on the clipboard, taken off it and said in words and in markdown: a clipboard holds whatever put something there,
    /// and a document is written from text, so the translating is the application's.
    /// </summary>
    private static void Pasted(object sender, ContentPastingEventArgs e)
    {
        if (e.Handled) return;

        try
        {
            if (Clipboard.GetDataObject() is not { } data) return;

            e.Words = MarkdownClipboard.ReadPlainText(data);
            e.Markdown = MarkdownClipboard.ReadBestMarkdown(data);
            e.Handled = !string.IsNullOrEmpty(e.Words) || !string.IsNullOrEmpty(e.Markdown);
        }
        catch (ExternalException) { }
    }
}
