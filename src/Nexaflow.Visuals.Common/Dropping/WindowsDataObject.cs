using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Nexaflow.Interaction;

namespace Nexaflow.Visuals.Common.Dropping;

/// <summary>
/// An <see cref="IDragPayload"/> as a Windows data object: the one place <see cref="DragKinds"/> are written in the formats
/// other applications read, as <see cref="WindowsDragPayload"/> is the one place they are read back. It writes words,
/// HTML, a picture and files; a kind of the app's own that is words — the markdown a document copies — goes under its own
/// name, for the app's own paste to find, and other applications pass over a format they do not know.
/// </summary>
public static class WindowsDataObject
{
    private const string Png = "PNG";

    /// <summary>
    /// <paramref name="payload"/> in every format another application could want it in: words as Unicode text; HTML in the
    /// clipboard's own packaging; a picture as PNG, its transparency kept, and as a bitmap for what reads only that; files as
    /// a drop list, with whether a paste moves or copies them.
    /// </summary>
    public static DataObject From(IDragPayload payload)
    {
        var data = new DataObject();

        if (payload.Text() is { } text) data.SetText(text, TextDataFormat.UnicodeText);
        if (payload.Html() is { } html) data.SetData(DataFormats.Html, html);

        if (payload.Image() is { } png)
        {
            data.SetData(Png, new MemoryStream(png), autoConvert: false);
            data.SetImage(BitmapFrame.Create(new MemoryStream(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad));
        }

        if (payload.Files() is { Count: > 0 } files)
        {
            var list = new StringCollection();
            list.AddRange([.. files]);
            data.SetFileDropList(list);
            var effect = payload.Contains(DragKinds.FilesCut) ? DragDropEffects.Move : DragDropEffects.Copy;
            data.SetData(WindowsDragPayload.PreferredDropEffect, new MemoryStream(BitConverter.GetBytes((int)effect)));
        }

        if (payload is ComposedPayload composed)
            foreach (var kind in composed.Kinds)
                if (!DragKinds.Shared.Contains(kind) && kind != DragKinds.FilesCut && composed.Read(kind) is string words)
                    data.SetData(kind, words);

        return data;
    }
}
