using System.Runtime.InteropServices;
using System.Windows;
using FlaUI.Core.Input;
using Point = System.Drawing.Point;
using SourceWindow = System.Windows.Window;

namespace Nexaflow.Tests.UIJourneys.Infrastructure;

/// <summary>
/// A drag begun by another application: a small window of the test host's own, pressed with the real pointer, which
/// offers a payload through OLE as Explorer or a browser does. To the app it is a drag from a different process, so what
/// it hears, and what it answers the source, are what a person's drag would get.
/// </summary>
internal static class OutsideDrag
{
    private const int SourceWidth = 90, SourceHeight = 60;
    private const uint NoActivate = 0x0010;
    private const DragDropEffects AnyEffect = DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link;
    private static readonly IntPtr TopMost = new(-1);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    /// <summary>Files, offered to be copied, moved or linked, as Explorer offers them.</summary>
    public static DragDropEffects Files(Point from, Point to, params string[] paths)
        => Carry(() => new DataObject(DataFormats.FileDrop, paths), from, to, AnyEffect);

        /// <summary>Files dragged with the right button, which asks whoever takes them what is meant.</summary>
        public static DragDropEffects FilesByTheRightButton(Point from, Point to, params string[] paths)
            => Carry(() => new DataObject(DataFormats.FileDrop, paths), from, to, AnyEffect, MouseButton.Right);

    /// <summary>Words, offered to be copied or moved, as a text editor offers its selection.</summary>
    public static DragDropEffects Words(Point from, Point to, string text)
        => Carry(() => new DataObject(DataFormats.UnicodeText, text), from, to, DragDropEffects.Copy | DragDropEffects.Move);

    /// <summary>A file that is only its content, offered as a mail's attachment is.</summary>
    public static DragDropEffects Attachment(Point from, Point to, string name, byte[] content)
        => Carry(() =>
        {
            // A FILEGROUPDESCRIPTORW of one file: its flags, size and name.
            var table = new byte[4 + 592];
            BitConverter.GetBytes(1).CopyTo(table, 0);
            BitConverter.GetBytes(0x40u).CopyTo(table, 4);
            BitConverter.GetBytes((uint)content.Length).CopyTo(table, 4 + 68);
            System.Text.Encoding.Unicode.GetBytes(name).CopyTo(table, 4 + 72);

            var data = new DataObject();
            data.SetData("FileGroupDescriptorW", new System.IO.MemoryStream(table));
            data.SetData("FileContents", new System.IO.MemoryStream(content));
            return data;
        }, from, to, DragDropEffects.Copy);

    /// <summary>Drags from a window put up at <paramref name="from"/> to <paramref name="to"/>, both in screen pixels.</summary>
    /// <returns>What the source was told was done with what it offered: a Move is leave to delete it.</returns>
    private static DragDropEffects Carry(Func<DataObject> payload, Point from, Point to, DragDropEffects offered, MouseButton button = MouseButton.Left)
    {
        var result = DragDropEffects.None;
        using var shown = new ManualResetEventSlim();
        using var done  = new ManualResetEventSlim();
        SourceWindow? source = null;

        var thread = new Thread(() =>
        {
            source = new SourceWindow
            {
                WindowStyle   = WindowStyle.None,
                ResizeMode    = ResizeMode.NoResize,
                Topmost       = true,
                ShowInTaskbar = false,
                Width         = SourceWidth,
                Height        = SourceHeight,
                Content       = "drag",
            };
            var right = button == MouseButton.Right;
            // OLE follows the left button unless the source says when its drag is over.
            if (right) source.QueryContinueDrag += (_, e) =>
            {
                e.Action = e.EscapePressed ? System.Windows.DragAction.Cancel
                         : (e.KeyStates & DragDropKeyStates.RightMouseButton) == 0 ? System.Windows.DragAction.Drop
                         : System.Windows.DragAction.Continue;
                e.Handled = true;
            };
            source.PreviewMouseDown += (_, _) =>
            {
                result = DragDrop.DoDragDrop(source, payload(), offered);
                done.Set();
            };
            source.SourceInitialized += (_, _) =>
                SetWindowPos(new System.Windows.Interop.WindowInteropHelper(source).Handle, TopMost,
                             from.X - SourceWidth / 2, from.Y - SourceHeight / 2, SourceWidth, SourceHeight, NoActivate);
            source.ContentRendered += (_, _) => shown.Set();
            source.Closed += (_, _) => source.Dispatcher.InvokeShutdown();
            source.Show();
            System.Windows.Threading.Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        try
        {
            if (!shown.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("The drag source did not appear.");
            Press(from, to, button);
            if (!done.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("The drag never ended.");
            return result;
        }
        finally
        {
            source?.Dispatcher.BeginInvoke(source.Close);
            thread.Join(TimeSpan.FromSeconds(5));
        }
    }

    /// <summary>Presses at <paramref name="from"/> and lets go over <paramref name="to"/>, hovering there long enough to be answered.</summary>
    public static void Press(Point from, Point to, MouseButton button = MouseButton.Left)
    {
        Mouse.MoveTo(from);
        Mouse.Down(button);
        try
        {
            Thread.Sleep(150);
            Mouse.MoveTo(new Point(from.X + 12, from.Y + 12));
            Thread.Sleep(150);
            Mouse.MoveTo(to);
            Mouse.MoveTo(new Point(to.X + 2, to.Y + 1));
            Thread.Sleep(350);
        }
        finally
        {
            Mouse.Up(button);
        }
    }
}
