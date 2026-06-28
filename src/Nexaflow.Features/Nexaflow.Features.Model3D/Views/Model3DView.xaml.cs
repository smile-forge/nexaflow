using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using Nexaflow.Features.Common;
using Nexaflow.Features.Model3D.Loaders;
using Nexaflow.Features.Model3D.ViewModels;
using WpfModel3D = System.Windows.Media.Media3D.Model3D;

namespace Nexaflow.Features.Model3D.Views;

/// <summary>
/// The 3D model viewer page: a HelixToolkit viewport (mouse rotate / zoom / pan come free) with a toolbar,
/// stats footer and inspector. The view-model loads the geometry; this code-behind drops it into the
/// viewport, builds the on-demand wireframe, and fulfils the camera / capture delegates the AI tools call.
/// </summary>
public partial class Model3DView : UserControl, IPageView
{
    private const double TurnDegreesPerPixel = 0.4;

    private readonly Model3DViewModel _vm;
    private readonly ModelVisual3D _modelVisual = new();
    private readonly LinesVisual3D _wireframe;
    private bool _built;
    private bool _turnDragging;
    private double _lastTurnX;
    private bool _panWasEnabled;
    private bool _rotateWasEnabled;

    // The on-load camera, restored by "Reset view".
    private bool _hasHome;
    private Point3D _homePosition;
    private Vector3D _homeLook;
    private Vector3D _homeUp;
    private double? _homeFieldOfView;

    IPageViewModel? IPageView.ViewModel => _vm;

    public Model3DView(Model3DViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        _wireframe = new LinesVisual3D
        {
            Thickness = 1,
            Color = ResolveColor("Model3D.WireframeColor", Color.FromRgb(0x88, 0x88, 0x88)),
        };

        WireDelegates();
        Viewport.PreviewMouseRightButtonDown += OnTurnDragStart;
        Viewport.PreviewMouseMove += OnTurnDragMove;
        Viewport.PreviewMouseRightButtonUp += OnTurnDragEnd;

        _vm.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        if (!_vm.IsLoaded)
        {
            _vm.CategoricalPalette = ResolveSwatchPalette(); // theme colours for materials the file doesn't colour
            await _vm.LoadAsync();
        }
        BuildVisuals();
    }

    /// <summary>The theme's categorical <c>Swatch.*</c> bank as colours, for tinting uncoloured materials.
    /// Falls back to the literal set if the theme resources can't be resolved.</summary>
    private IReadOnlyList<Color> ResolveSwatchPalette()
    {
        string[] keys =
        [
            "Swatch.Blue", "Swatch.Orange", "Swatch.Green", "Swatch.Purple", "Swatch.Pink", "Swatch.Cyan",
            "Swatch.Amber", "Swatch.Teal", "Swatch.Lime", "Swatch.Red", "Swatch.Yellow", "Swatch.Slate",
        ];

        var colours = new List<Color>(keys.Length);
        foreach (var key in keys)
        {
            switch (TryFindResource(key))
            {
                case SolidColorBrush brush: colours.Add(brush.Color); break;
                case Color colour: colours.Add(colour); break;
            }
        }
        return colours.Count > 0 ? colours : CategoricalPalette.Fallback;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => _vm.PropertyChanged -= OnViewModelPropertyChanged;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Model3DViewModel.ShowWireframe)) ApplyRenderMode();
    }

    // ── Building the scene ──────────────────────────────────────────────────────────────────────
    private void BuildVisuals()
    {
        if (_built || _vm.Geometry is null) return;
        _built = true;

        _modelVisual.Content = _vm.Geometry;
        ApplyRenderMode();

        if (_vm.InitialView is { } authored) ApplyCameraView(authored); // the file's own camera
        else Viewport.ZoomExtents(0);                                    // else frame the bounding box

        CaptureHome(); // snapshot whatever we landed on, so "Reset view" can return here
    }

    /// <summary>Points the camera from an authored viewpoint, aiming at the model centre and taking the
    /// framing distance from the model bounds (the file gives a position + direction, not a distance).</summary>
    private void ApplyCameraView(ModelCameraView view)
    {
        if (Viewport.Camera is not ProjectionCamera camera) return;

        var look = view.LookDirection;
        if (look.Length < 1e-9) { Viewport.ZoomExtents(0); return; }
        look.Normalize();

        var bounds = _modelVisual.Content?.Bounds ?? Rect3D.Empty;
        var distance = 1.0;
        if (!bounds.IsEmpty)
        {
            var centre = new Point3D(bounds.X + bounds.SizeX / 2, bounds.Y + bounds.SizeY / 2, bounds.Z + bounds.SizeZ / 2);
            var projected = Vector3D.DotProduct(centre - view.Position, look);
            distance = projected > 1e-3 ? projected : (centre - view.Position).Length;
        }

        camera.Position = view.Position;
        camera.LookDirection = look * Math.Max(distance, 1e-3);
        camera.UpDirection = view.UpDirection;
    }

    private void CaptureHome()
    {
        if (Viewport.Camera is not ProjectionCamera camera) return;
        _homePosition = camera.Position;
        _homeLook = camera.LookDirection;
        _homeUp = camera.UpDirection;
        _homeFieldOfView = camera is PerspectiveCamera perspective ? perspective.FieldOfView : null;
        _hasHome = true;
    }

    /// <summary>Restores the on-load camera (authored view, or the initial bounding-box framing). Falls
    /// back to a fresh zoom-to-extents if no home was captured.</summary>
    private void ResetView()
    {
        if (_hasHome && Viewport.Camera is ProjectionCamera camera)
        {
            camera.Position = _homePosition;
            camera.LookDirection = _homeLook;
            camera.UpDirection = _homeUp;
            if (camera is PerspectiveCamera perspective && _homeFieldOfView is { } fov) perspective.FieldOfView = fov;
        }
        else
        {
            Viewport.ZoomExtents(0);
        }
    }

    private void ApplyRenderMode()
    {
        if (!_built) return;
        var wireframe = _vm.ShowWireframe;

        SetChild(_modelVisual, !wireframe);

        if (wireframe && _wireframe.Points.Count == 0 && _vm.Geometry is not null)
            BuildWireframe(_vm.Geometry);
        SetChild(_wireframe, wireframe);
    }

    private void SetChild(Visual3D visual, bool present)
    {
        var has = Viewport.Children.Contains(visual);
        if (present && !has) Viewport.Children.Add(visual);
        else if (!present && has) Viewport.Children.Remove(visual);
    }

    private void BuildWireframe(Model3DGroup group)
    {
        var points = new Point3DCollection();
        AddEdges(group, points, Matrix3D.Identity);
        points.Freeze();
        _wireframe.Points = points;
    }

    private static void AddEdges(WpfModel3D? model, Point3DCollection points, Matrix3D parent)
    {
        if (model is null) return;

        var local = model.Transform?.Value ?? Matrix3D.Identity;
        var world = Matrix3D.Multiply(local, parent);

        switch (model)
        {
            case Model3DGroup group:
                foreach (var child in group.Children) AddEdges(child, points, world);
                break;

            case GeometryModel3D gm when gm.Geometry is MeshGeometry3D mesh:
                var p = mesh.Positions;
                var indices = mesh.TriangleIndices;
                if (indices.Count >= 3)
                    for (int i = 0; i + 2 < indices.Count; i += 3)
                        AddTriangleEdges(points, p, world, indices[i], indices[i + 1], indices[i + 2]);
                else
                    for (int i = 0; i + 2 < p.Count; i += 3)
                        AddTriangleEdges(points, p, world, i, i + 1, i + 2);
                break;
        }
    }

    private static void AddTriangleEdges(Point3DCollection points, Point3DCollection p, Matrix3D world,
                                         int a, int b, int c)
    {
        Point3D pa = world.Transform(p[a]), pb = world.Transform(p[b]), pc = world.Transform(p[c]);
        points.Add(pa); points.Add(pb);
        points.Add(pb); points.Add(pc);
        points.Add(pc); points.Add(pa);
    }

    // ── Toolbar buttons ─────────────────────────────────────────────────────────────────────────
    private void OnResetClick(object sender, RoutedEventArgs e) => ResetView();

    // ── Alt + right-drag = turn the model about the vertical (green / world-Y) axis ──
    // The turntable's horizontal/vertical drags rotate about Z (blue) and X (red); this exposes the
    // missing rotation about Y (green) — e.g. turning a side-on chair to show its front or back.
    private void OnTurnDragStart(object sender, MouseButtonEventArgs e)
    {
        // Alt is WPF's system key — check the key state directly rather than ModifierKeys, which can read
        // empty during the button-down.
        if (!Keyboard.IsKeyDown(Key.LeftAlt) && !Keyboard.IsKeyDown(Key.RightAlt)) return; // plain right-drag unchanged
        _turnDragging = true;
        _lastTurnX = e.GetPosition(Viewport).X;
        _panWasEnabled = Viewport.IsPanEnabled;
        _rotateWasEnabled = Viewport.IsRotationEnabled;
        Viewport.IsPanEnabled = false;      // stop Helix's own right-drag gesture (pan…
        Viewport.IsRotationEnabled = false; // …or rotate) from moving the camera while we turn
        Viewport.CaptureMouse();
        e.Handled = true;
    }

    private void OnTurnDragMove(object sender, MouseEventArgs e)
    {
        if (!_turnDragging) return;
        var x = e.GetPosition(Viewport).X;
        OrbitAboutWorldUp((x - _lastTurnX) * TurnDegreesPerPixel); // horizontal drag → turn about green/Y
        _lastTurnX = x;
        e.Handled = true;
    }

    private void OnTurnDragEnd(object sender, MouseButtonEventArgs e)
    {
        if (!_turnDragging) return;
        _turnDragging = false;
        Viewport.IsPanEnabled = _panWasEnabled;
        Viewport.IsRotationEnabled = _rotateWasEnabled;
        Viewport.ReleaseMouseCapture();
        e.Handled = true;
    }

    // ── View → ViewModel bridge: camera + capture operations the AI tools call ───────────────────
    private void WireDelegates()
    {
        _vm.CaptureViewportPngAsync = _ => Task.FromResult(CaptureViewportPng());
        _vm.OrbitAsync = (yaw, pitch, _) => { Orbit(yaw, pitch); return Task.CompletedTask; };
        _vm.ZoomAsync = (factor, _) => { Zoom(factor); return Task.CompletedTask; };
        _vm.PanAsync = (dx, dy, _) => { Pan(dx, dy); return Task.CompletedTask; };
        _vm.RollAsync = (degrees, _) => { Roll(degrees); return Task.CompletedTask; };
        _vm.ResetViewAsync = _ => { ResetView(); return Task.CompletedTask; };
    }

    private byte[]? CaptureViewportPng()
    {
        try
        {
            var background = ResolveBrush("Model3D.ViewportBg") ?? Brushes.Black;
            if (Viewport3DHelper.RenderBitmap(Viewport.Viewport, background, 1) is not { } bitmap) return null;

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private void Orbit(double yawDegrees, double pitchDegrees)
    {
        if (Viewport.Camera is not ProjectionCamera camera) return;

        var target = camera.Position + camera.LookDirection;
        var up = camera.UpDirection;
        var right = Vector3D.CrossProduct(camera.LookDirection, up);
        if (right.Length < 1e-9) right = new Vector3D(1, 0, 0);
        right.Normalize();
        up.Normalize();

        var rotation = new Quaternion(up, yawDegrees) * new Quaternion(right, pitchDegrees);
        var matrix = new Matrix3D();
        matrix.Rotate(rotation);

        var offset = matrix.Transform(camera.Position - target); // camera orbits about the target
        camera.Position = target + offset;
        camera.LookDirection = target - camera.Position;
        camera.UpDirection = matrix.Transform(up);
    }

    private void Zoom(double factor)
    {
        if (factor <= 0 || Viewport.Camera is not ProjectionCamera camera) return;

        var target = camera.Position + camera.LookDirection;
        var look = camera.LookDirection / factor; // factor > 1 → shorter look vector → closer
        camera.Position = target - look;
        camera.LookDirection = look;
    }

    private void Pan(double dx, double dy)
    {
        if (Viewport.Camera is not ProjectionCamera camera) return;

        var look = camera.LookDirection;
        var right = Vector3D.CrossProduct(look, camera.UpDirection);
        if (right.Length < 1e-9) return;
        right.Normalize();
        var up = Vector3D.CrossProduct(right, look);
        up.Normalize();

        var distance = look.Length;
        camera.Position += right * (dx * distance) + up * (dy * distance); // look unchanged → true pan
    }

    private void Roll(double degrees)
    {
        if (Viewport.Camera is not ProjectionCamera camera) return;

        var axis = camera.LookDirection; // roll about the line of sight
        if (axis.Length < 1e-9) return;
        axis.Normalize();

        var matrix = new Matrix3D();
        matrix.Rotate(new Quaternion(axis, -degrees)); // negate so +degrees looks clockwise to the viewer
        camera.UpDirection = matrix.Transform(camera.UpDirection);
    }

    // Orbit the camera about the world-up (green / Y) axis through the look-at point — the rotation the
    // Z-up turntable doesn't expose. Drives the Alt + right-drag "turn" gesture.
    private void OrbitAboutWorldUp(double degrees)
    {
        if (Viewport.Camera is not ProjectionCamera camera) return;

        var target = camera.Position + camera.LookDirection;
        var matrix = new Matrix3D();
        matrix.Rotate(new Quaternion(new Vector3D(0, 1, 0), degrees)); // green / world Y

        camera.Position = target + matrix.Transform(camera.Position - target);
        camera.LookDirection = target - camera.Position;
        camera.UpDirection = matrix.Transform(camera.UpDirection);
    }

    // ── Theme resource helpers ──────────────────────────────────────────────────────────────────
    private Color ResolveColor(string key, Color fallback) =>
        TryFindResource(key) switch
        {
            SolidColorBrush brush => brush.Color,
            Color color => color,
            _ => fallback,
        };

    private Brush? ResolveBrush(string key) => TryFindResource(key) as Brush;
}
