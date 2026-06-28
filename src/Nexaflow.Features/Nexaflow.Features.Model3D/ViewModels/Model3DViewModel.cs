using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Model3D.ClientTools;
using Nexaflow.Features.Model3D.Loaders;
using Nexaflow.Visuals.Common.Formatting;

namespace Nexaflow.Features.Model3D.ViewModels;

/// <summary>
/// The 3D viewer page's view-model. Loads a model file through the <see cref="ModelLoaderRegistry"/> off the
/// UI thread, exposes the geometry + stats the view and footer bind to, and surfaces AI tools that drive the
/// live viewport. Camera moves and viewport capture need the actual <c>HelixViewport3D</c>, so the view wires
/// those operations in as delegates (the same View→ViewModel bridge the web viewer uses for its page tools).
/// </summary>
public sealed partial class Model3DViewModel : ObservableObject, IPageViewModel
{
    private readonly ModelLoaderRegistry _registry;

    public Model3DViewModel(string filePath, ModelLoaderRegistry registry)
    {
        FilePath = filePath;
        _registry = registry;
    }

    public string FilePath { get; }
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>Distinct colours used to tint materials the file doesn't colour itself. The view sets this
    /// from the theme's <c>Swatch.*</c> bank before loading; defaults to a literal fallback for tests.</summary>
    public IReadOnlyList<Color> CategoricalPalette { get; set; } = Loaders.CategoricalPalette.Fallback;

    /// <summary>The loaded, frozen geometry the view drops into the viewport. Null until loaded / on error.</summary>
    [ObservableProperty] private Model3DGroup? _geometry;

    /// <summary>The file's authored camera, if any — the view uses it as the initial view and reset home.</summary>
    public ModelCameraView? InitialView { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsContextReady))]
    private bool _isLoaded;

    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string _errorMessage = string.Empty;

    [ObservableProperty] private string _formatName = string.Empty;
    [ObservableProperty] private int _triangleCount;
    [ObservableProperty] private int _vertexCount;
    [ObservableProperty] private int _meshCount;
    [ObservableProperty] private string _fileSizeText = string.Empty;

    /// <summary>True when the model renders only as edges. The view watches this and swaps the visuals;
    /// the toolbar toggle and the AI <c>set_render_mode</c> tool both flip it.</summary>
    [ObservableProperty] private bool _showWireframe;

    /// <summary>Right-hand inspector visibility (materials + unsupported elements). Defaults on when there's
    /// content to show.</summary>
    [ObservableProperty] private bool _showInspector;

    [ObservableProperty] private bool _hasInspectorContent;
    [ObservableProperty] private bool _hasMaterials;
    [ObservableProperty] private bool _hasUnsupported;

    public ObservableCollection<MaterialRowViewModel> Materials { get; } = [];
    public ObservableCollection<string> UnsupportedElements { get; } = [];

    // ── View → ViewModel bridge: set by the view once the live viewport exists ──────────────────
    /// <summary>Renders the current viewport to PNG bytes (null if unavailable).</summary>
    public Func<CancellationToken, Task<byte[]?>>? CaptureViewportPngAsync { get; set; }
    /// <summary>Orbits the camera by (yaw, pitch) degrees about the model.</summary>
    public Func<double, double, CancellationToken, Task>? OrbitAsync { get; set; }
    /// <summary>Zooms by a factor &gt;1 (in) or between 0 and 1 (out).</summary>
    public Func<double, CancellationToken, Task>? ZoomAsync { get; set; }
    /// <summary>Pans the camera by signed fractions of the viewport (right, up).</summary>
    public Func<double, double, CancellationToken, Task>? PanAsync { get; set; }
    /// <summary>Rolls the camera about the view axis by degrees (positive = clockwise to the viewer).</summary>
    public Func<double, CancellationToken, Task>? RollAsync { get; set; }
    /// <summary>Re-frames the model (zoom-to-extents).</summary>
    public Func<CancellationToken, Task>? ResetViewAsync { get; set; }

    /// <summary>Reads + parses the model off the UI thread, then populates geometry, stats and inspector.
    /// Safe to call from the view's Loaded handler — the continuation runs on the UI thread.</summary>
    public async Task LoadAsync()
    {
        if (IsLoaded) return;

        try
        {
            var loader = _registry.LoaderFor(FilePath);
            if (loader is null)
            {
                Fail($"No loader is registered for '{Path.GetExtension(FilePath)}' files.");
                return;
            }

            long size = 0;
            try { size = new FileInfo(FilePath).Length; } catch { /* size is cosmetic */ }

            var palette = CategoricalPalette;
            var loaded = await Task.Run(() => loader.Load(FilePath, palette)).ConfigureAwait(true);

            Geometry = loaded.Geometry;
            InitialView = loaded.InitialView;
            FormatName = loaded.FormatName;
            TriangleCount = loaded.TriangleCount;
            VertexCount = loaded.VertexCount;
            MeshCount = loaded.MeshCount;
            FileSizeText = SizeFormatter.FormatBytes(size);

            Materials.Clear();
            foreach (var m in loaded.Materials) Materials.Add(new MaterialRowViewModel(m));

            UnsupportedElements.Clear();
            foreach (var u in loaded.UnsupportedElements) UnsupportedElements.Add(u);

            HasMaterials = Materials.Count > 0;
            HasUnsupported = UnsupportedElements.Count > 0;
            HasInspectorContent = HasMaterials || HasUnsupported;
            ShowInspector = HasInspectorContent;

            if (TriangleCount == 0)
                Fail("The file loaded but contained no renderable mesh.");
        }
        catch (Exception ex)
        {
            Fail($"Couldn't open this model: {ex.Message}");
        }
        finally
        {
            IsLoaded = true; // releases the AI send gate even on failure
        }
    }

    private void Fail(string message)
    {
        HasError = true;
        ErrorMessage = message;
    }

    /// <summary>Multi-line summary of the model for the AI's <c>get_model_info</c> tool.</summary>
    public string DescribeInfo()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{FileName} — {FormatName}");
        sb.AppendLine($"Triangles: {TriangleCount:N0}, vertices: {VertexCount:N0}, meshes: {MeshCount:N0}.");
        sb.AppendLine($"Render mode: {(ShowWireframe ? "wireframe" : "solid")}.");

        if (Materials.Count > 0)
        {
            sb.AppendLine($"Materials ({Materials.Count}):");
            foreach (var m in Materials)
                sb.AppendLine($"  - {m.Name}{(string.IsNullOrEmpty(m.Detail) ? "" : $" ({m.Detail})")}");
        }

        if (UnsupportedElements.Count > 0)
        {
            sb.AppendLine("Declared but not rendered:");
            foreach (var u in UnsupportedElements) sb.AppendLine($"  - {u}");
        }

        return sb.ToString().TrimEnd();
    }

    // ── IPageViewModel ──────────────────────────────────────────────────────────────────────────
    public bool IsContextReady => IsLoaded;

    public string GetContext()
    {
        if (!IsLoaded) return $"3D model \"{FileName}\": still loading…";
        if (HasError) return $"3D model \"{FileName}\": failed to load — {ErrorMessage}";

        var sb = new StringBuilder(
            $"3D model \"{FileName}\" ({FormatName}): {TriangleCount:N0} triangles, {VertexCount:N0} vertices, " +
            $"{MeshCount:N0} mesh(es), {Materials.Count} material(s). Rendered {(ShowWireframe ? "as wireframe" : "solid")}.");
        if (UnsupportedElements.Count > 0)
            sb.Append($" Not rendered: {string.Join("; ", UnsupportedElements)}.");
        return sb.ToString();
    }

    public IReadOnlyList<IClientTool> GetClientTools() => Model3DTools.For(this);

    public string? GetSecurityContext() => FilePath;

    public string? GetAiSystemPromptGuidance() =>
        "This is a 3D model viewer. Use capture_viewport_image to see the model, orbit_model / roll_model / " +
        "zoom_model / pan_model / reset_view to change the camera, set_render_mode to toggle wireframe, and " +
        "get_model_info for exact counts and materials. After moving the camera, capture the viewport again to " +
        "see the result.";
}
