using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Visuals.Common.Controls;

/// <summary>
/// Samples one pixel from anywhere on screen. The whole desktop is photographed first and shown back full-screen
/// on every monitor, so what the user points at cannot move under them and the colour returned is exactly the
/// pixel they chose — whatever each monitor's scale. A loupe follows the cursor with the magnified pixels and the
/// hex under it. Click to take it; Esc or a right-click cancels (null).
/// </summary>
public static class ScreenEyedropper
{
    public static Task<Color?> PickAsync()
    {
        var shot    = DesktopShot.Capture();
        var result  = new TaskCompletionSource<Color?>();
        var windows = new List<Window>();

        void Finish(Color? color)
        {
            if (!result.TrySetResult(color)) return;
            foreach (var w in windows) w.Close();
        }

        foreach (var monitor in Native.MonitorBounds())
            windows.Add(new EyedropperWindow(shot, monitor, Finish));

        foreach (var w in windows) w.Show();
        windows.FirstOrDefault()?.Activate();
        return result.Task;
    }

    /// <summary>The virtual desktop as one BGRA bitmap, and where its top-left sits in screen pixels.</summary>
    private sealed class DesktopShot
    {
        private readonly byte[] _pixels;
        private readonly int _stride;

        private DesktopShot(BitmapSource image, int left, int top)
        {
            Image   = image;
            Left    = left;
            Top     = top;
            _stride = image.PixelWidth * 4;
            _pixels = new byte[_stride * image.PixelHeight];
            image.CopyPixels(_pixels, _stride, 0);
        }

        public BitmapSource Image { get; }
        public int Left { get; }
        public int Top { get; }

        public Color? At(int screenX, int screenY)
        {
            int x = screenX - Left, y = screenY - Top;
            if (x < 0 || y < 0 || x >= Image.PixelWidth || y >= Image.PixelHeight) return null;
            var i = y * _stride + x * 4;
            return Color.FromRgb(_pixels[i + 2], _pixels[i + 1], _pixels[i]);
        }

        public static DesktopShot Capture()
        {
            var (left, top, width, height) = Native.VirtualScreen();
            var screenDc = Native.GetDC(IntPtr.Zero);
            var memDc    = Native.CreateCompatibleDC(screenDc);
            var bitmap   = Native.CreateCompatibleBitmap(screenDc, width, height);
            var old      = Native.SelectObject(memDc, bitmap);
            try
            {
                Native.BitBlt(memDc, 0, 0, width, height, screenDc, left, top, Native.SRCCOPY | Native.CAPTUREBLT);
                var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
                bgra.Freeze();
                return new DesktopShot(bgra, left, top);
            }
            finally
            {
                Native.SelectObject(memDc, old);
                Native.DeleteObject(bitmap);
                Native.DeleteDC(memDc);
                Native.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }
    }

    /// <summary>One monitor's share of the frozen desktop, with the loupe.</summary>
    private sealed class EyedropperWindow : Window
    {
        private const int LoupePixels = 11;
        private const double LoupeSize = 110;

        private readonly DesktopShot _shot;
        private readonly Int32Rect _monitor;
        private readonly Action<Color?> _finish;
        private readonly Border _loupe;
        private readonly Image _loupeImage;
        private readonly Border _loupeSwatch;
        private readonly TextBlock _loupeHex;
        private readonly Canvas _canvas;

        public EyedropperWindow(DesktopShot shot, Int32Rect monitor, Action<Color?> finish)
        {
            _shot    = shot;
            _monitor = monitor;
            _finish  = finish;

            WindowStyle        = WindowStyle.None;
            ResizeMode         = ResizeMode.NoResize;
            ShowInTaskbar      = false;
            Topmost            = true;
            ShowActivated      = true;
            Cursor             = Cursors.Cross;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Title              = Str.Get("Common.ColorPicker.Eyedropper");

            var crop = new CroppedBitmap(shot.Image,
                new Int32Rect(monitor.X - shot.Left, monitor.Y - shot.Top, monitor.Width, monitor.Height));
            crop.Freeze();

            _loupeImage = new Image { Width = LoupeSize, Height = LoupeSize, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(_loupeImage, BitmapScalingMode.NearestNeighbor);

            var cell = LoupeSize / LoupePixels;
            var marker = new Rectangle
            {
                Width = cell, Height = cell, StrokeThickness = 1.5,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(cell * (LoupePixels / 2), cell * (LoupePixels / 2), 0, 0),
            };
            marker.SetResourceReference(Shape.StrokeProperty, "AccentBrush");

            _loupeSwatch = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 6, 0) };
            _loupeHex    = new TextBlock { FontSize = 12, FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center };
            _loupeHex.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

            var readout = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 4, 6, 4) };
            readout.Children.Add(_loupeSwatch);
            readout.Children.Add(_loupeHex);

            var stack = new StackPanel();
            stack.Children.Add(new Grid { Children = { _loupeImage, marker } });
            stack.Children.Add(readout);

            _loupe = new Border
            {
                Child = stack, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(2), IsHitTestVisible = false, Visibility = Visibility.Collapsed,
            };
            _loupe.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
            _loupe.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

            _canvas = new Canvas { Children = { _loupe } };
            Content = new Grid { Children = { new Image { Source = crop, Stretch = Stretch.Fill }, _canvas } };

            SourceInitialized += (_, _) => Native.PlaceOver(new WindowInteropHelper(this).Handle, monitor);
            MouseMove         += (_, e) => Track(e.GetPosition(_canvas));
            MouseLeftButtonUp += (_, _) => _finish(Sample());
            MouseRightButtonUp += (_, _) => _finish(null);
            KeyDown           += (_, e) => { if (e.Key == Key.Escape) _finish(null); };
            Closed            += (_, _) => _finish(null);
        }

        private Color? Sample()
        {
            var p = Native.CursorPosition();
            return _shot.At(p.X, p.Y);
        }

        private void Track(Point inWindow)
        {
            var p = Native.CursorPosition();
            var half = LoupePixels / 2;
            var x = Math.Clamp(p.X - _shot.Left - half, 0, _shot.Image.PixelWidth  - LoupePixels);
            var y = Math.Clamp(p.Y - _shot.Top  - half, 0, _shot.Image.PixelHeight - LoupePixels);
            _loupeImage.Source = new CroppedBitmap(_shot.Image, new Int32Rect(x, y, LoupePixels, LoupePixels));

            if (_shot.At(p.X, p.Y) is { } color)
            {
                _loupeSwatch.Background = new SolidColorBrush(color);
                _loupeHex.Text          = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            }

            // Sit below-right of the cursor, flipping to the other side near an edge.
            _loupe.Visibility = Visibility.Visible;
            _loupe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = _loupe.DesiredSize;
            var left = inWindow.X + 20 + size.Width  > ActualWidth  ? inWindow.X - 20 - size.Width  : inWindow.X + 20;
            var top  = inWindow.Y + 20 + size.Height > ActualHeight ? inWindow.Y - 20 - size.Height : inWindow.Y + 20;
            Canvas.SetLeft(_loupe, left);
            Canvas.SetTop(_loupe, top);
        }
    }

    private static class Native
    {
        public const int SRCCOPY = 0x00CC0020;
        public const int CAPTUREBLT = 0x40000000;
        private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;
        private const uint SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;
        private static readonly IntPtr HWND_TOPMOST = new(-1);

        public static (int Left, int Top, int Width, int Height) VirtualScreen()
            => (GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN),
                GetSystemMetrics(SM_CXVIRTUALSCREEN), GetSystemMetrics(SM_CYVIRTUALSCREEN));

        public static List<Int32Rect> MonitorBounds()
        {
            var list = new List<Int32Rect>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr _, ref RECT _, IntPtr _) =>
            {
                var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(monitor, ref info))
                {
                    var r = info.rcMonitor;
                    list.Add(new Int32Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top));
                }
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public static void PlaceOver(IntPtr hwnd, Int32Rect bounds)
            => SetWindowPos(hwnd, HWND_TOPMOST, bounds.X, bounds.Y, bounds.Width, bounds.Height, SWP_SHOWWINDOW | SWP_NOACTIVATE);

        public static POINT CursorPosition()
        {
            GetCursorPos(out var p);
            return p;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, ref RECT rect, IntPtr data);

        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc callback, IntPtr data);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dest, int x, int y, int cx, int cy, IntPtr src, int sx, int sy, int rop);
    }
}
