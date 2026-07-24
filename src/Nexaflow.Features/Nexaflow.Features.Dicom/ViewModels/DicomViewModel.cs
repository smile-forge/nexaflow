using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FellowOakDicom;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Dicom.Models;
using Nexaflow.Features.Dicom.Services;

namespace Nexaflow.Features.Dicom.ViewModels;

/// <summary>
/// Drives the DICOM viewer tab: loads a container off the UI thread, lists its Patient→Study→Series→
/// Instance tree, renders the selected image with window/level + cine, hosts the measurement overlay, and
/// opens encapsulated reports in their own viewer with an origin breadcrumb back here. Patient identifiers
/// are shown by default with a <see cref="HidePatientInfo"/> toggle; the AI is only ever given
/// de-identified data.
/// </summary>
public sealed partial class DicomViewModel : ObservableObject, IPageViewModel, IDisposable
{
    private readonly IReadOnlyList<string> _paths;
    private readonly IShellServices _shell;
    private readonly Dictionary<string, string> _originParams;   // params that re-open THIS tab
    private readonly DispatcherTimer _cineTimer;
    private readonly List<string> _tempReports = [];             // extracted report temp files, cleaned on dispose
    private readonly CancellationTokenSource _cts = new();

    private DicomRenderer? _renderer;
    private CancellationTokenSource? _renderCts;
    private IReadOnlyList<DicomNode>? _currentSeries;   // the series being scrolled (to persist W/L across it)
    private double _defaultWidth, _defaultCenter;        // the current image's native window (for Reset)

    public MeasurementViewModel Measure { get; } = new();

    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string _statusText = "Loading…";
    [ObservableProperty] private DicomContainer? _container;
    [ObservableProperty] private ObservableCollection<DicomNode> _patients = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    [NotifyPropertyChangedFor(nameof(HasInstance))]
    private DicomNode? _selectedNode;

    // ── DICOM tag drawer ──────────────────────────────────────────────────
    [ObservableProperty] private bool _tagsOpen;
    [ObservableProperty] private string _tagFilter = string.Empty;
    [ObservableProperty] private ObservableCollection<DicomTagItem> _tags = [];
    private IReadOnlyList<DicomTagItem> _allTags = [];

    /// <summary>True when a node with an on-disk/virtual instance is selected — gates the tag drawer.</summary>
    public bool HasInstance => SelectedNode?.FilePath is not null;

    [ObservableProperty] private BitmapSource? _currentBitmap;
    [ObservableProperty] private int _frameIndex;
    [ObservableProperty] private int _frameCount = 1;
    [ObservableProperty] private bool _isCine;
    [ObservableProperty] private double _windowWidth;
    [ObservableProperty] private double _windowCenter;
    [ObservableProperty] private bool _invert;
    [ObservableProperty] private string _activePreset = "default";   // default | bone | lung | soft | brain | custom
    [ObservableProperty] private string _probeText = string.Empty;
    [ObservableProperty] private string _techOverlay = string.Empty;
    [ObservableProperty] private string _patientOverlay = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PatientOverlayVisible))]
    private bool _hidePatientInfo;

    /// <summary>True while an image instance is selected (gates the image-pane tools).</summary>
    public bool HasImage => SelectedNode is { Kind: DicomNodeKind.Image };

    /// <summary>Whether the PHI overlay line is shown (hidden by the toggle).</summary>
    public bool PatientOverlayVisible => !HidePatientInfo;

    public bool MultiFrame => FrameCount > 1;

    public DicomViewModel(IReadOnlyList<string> paths, IShellServices shell, Dictionary<string, string> originParams)
    {
        _paths = paths;
        _shell = shell;
        _originParams = originParams;

        _cineTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        _cineTimer.Tick += (_, _) => AdvanceFrame();

        _ = LoadAsync();
    }

    // ── Loading ───────────────────────────────────────────────────────────

    private async Task LoadAsync()
    {
        DicomContainer container;
        try
        {
            container = await Task.Run(() => DicomContainerLoader.Load(_paths, _cts.Token), _cts.Token);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            IsLoading = false;
            StatusText = $"Could not read DICOM content: {ex.Message}";
            return;
        }

        Container = container;
        Patients = container.Patients;
        container.ApplyPhiMask(HidePatientInfo);
        StatusText = container.IsEmpty ? "No DICOM content found." : container.Summary;
        IsLoading = false;
        OnPropertyChanged(nameof(IsContextReady));

        // Auto-select the first image so the pane isn't blank.
        if (container.Images.Count > 0)
            SelectedNode = container.Images[0];
        else if (container.Patients.Count > 0)
            container.Patients[0].IsExpanded = true;
    }

    // ── Selection & rendering ─────────────────────────────────────────────

    partial void OnSelectedNodeChanged(DicomNode? value)
    {
        if (value is null) return;
        value.IsSelected = true;

        switch (value.Kind)
        {
            case DicomNodeKind.Image when value.FilePath is not null && value.Instance is not null:
                _ = OpenImageAsync(value);
                LoadTagsIfOpen();
                break;
            case DicomNodeKind.Report when value.FilePath is not null:
                ClearImage();
                OpenReport(value);
                LoadTagsIfOpen();
                break;
            default:
                // A Patient/Study/Series container — jump to its first image so the pane and tools are live.
                if (value.SelfAndDescendants().FirstOrDefault(n => n.Kind == DicomNodeKind.Image) is { } firstImage)
                    SelectedNode = firstImage;
                break;
        }
    }

    /// <summary>Steps the selected image within its series (scroll wheel). Clamped at the ends — no wrap.</summary>
    public void StepImage(int delta)
    {
        if (SelectedNode is not { Kind: DicomNodeKind.Image, SeriesImages: { Count: > 1 } imgs }) return;
        var i = -1;
        for (var k = 0; k < imgs.Count; k++)
            if (ReferenceEquals(imgs[k], SelectedNode)) { i = k; break; }
        if (i < 0) return;
        var next = Math.Clamp(i + delta, 0, imgs.Count - 1);
        if (next != i) SelectedNode = imgs[next];
    }

    private async Task OpenImageAsync(DicomNode node)
    {
        StopCine();
        // Keep the window/level, invert and preset while scrolling within one series; reset them for a new one.
        var sameSeries = node.SeriesImages is not null && ReferenceEquals(node.SeriesImages, _currentSeries);
        _currentSeries = node.SeriesImages;
        try
        {
            var renderer = await Task.Run(() => new DicomRenderer(node.FilePath!), _cts.Token);
            _renderer = renderer;
            FrameCount = Math.Max(1, renderer.Frames);
            FrameIndex = 0;
            _defaultWidth = renderer.WindowWidth;
            _defaultCenter = renderer.WindowCenter;
            if (!sameSeries)
            {
                WindowWidth = renderer.WindowWidth;
                WindowCenter = renderer.WindowCenter;
                Invert = false;
                ActivePreset = "default";
            }
            OnPropertyChanged(nameof(MultiFrame));

            Measure.SetFrame(node.Instance, 0);
            UpdateOverlays(node);
            await RenderAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _renderer = null;
            CurrentBitmap = null;
            TechOverlay = node.Instance is { } i
                ? $"Cannot render — {i.TransferSyntaxName} ({i.TransferSyntaxUid}): {ex.Message}"
                : ex.Message;
        }
    }

    private void ClearImage()
    {
        StopCine();
        _renderer = null;
        _currentSeries = null;
        CurrentBitmap = null;
        FrameCount = 1;
        FrameIndex = 0;
        OnPropertyChanged(nameof(MultiFrame));
        Measure.SetFrame(null, 0);
    }

    /// <summary>Renders the current frame at the current window/level off the UI thread.</summary>
    private async Task RenderAsync()
    {
        var renderer = _renderer;
        if (renderer is null) return;

        _renderCts?.Cancel();
        var cts = _renderCts = new CancellationTokenSource();
        var frame = FrameIndex;
        var ww = WindowWidth;
        var wc = WindowCenter;
        var invert = Invert;

        try
        {
            var bmp = await Task.Run(() =>
            {
                renderer.WindowWidth = ww;
                renderer.WindowCenter = wc;
                renderer.Invert = invert;
                return renderer.Render(frame);
            }, cts.Token);

            if (!cts.IsCancellationRequested)
            {
                CurrentBitmap = bmp;
                if (SelectedNode is { } n) UpdateOverlays(n);
            }
        }
        catch (OperationCanceledException) { }
        catch { /* a transient render failure leaves the previous frame up */ }
    }

    private void UpdateOverlays(DicomNode node)
    {
        var i = node.Instance;
        if (i is null) { TechOverlay = string.Empty; return; }
        var frameText = FrameCount > 1 ? $"  ·  frame {FrameIndex + 1}/{FrameCount}" : "";
        TechOverlay = $"{i.Modality}  {i.Columns}×{i.Rows}  ·  W {WindowWidth:0}/L {WindowCenter:0}{frameText}";
        PatientOverlay = BuildPatientOverlay();
    }

    private string BuildPatientOverlay()
    {
        // Only reads header PHI when actually shown — and it's never handed to the AI.
        if (SelectedNode?.FilePath is not { } path) return string.Empty;
        try
        {
            var ds = DicomIo.Open(path, FileReadOption.SkipLargeTags).Dataset;
            var name = DicomTags.PersonName(ds, DicomTag.PatientName);
            var id = DicomTags.Str(ds, DicomTag.PatientID, "—");
            var dob = DicomTags.Date(ds, DicomTag.PatientBirthDate);
            var extra = string.IsNullOrEmpty(dob) ? "" : $"  ·  DOB {dob}";
            return $"{name}  ·  ID {id}{extra}";
        }
        catch { return string.Empty; }
    }

    // ── Window / level (called by the view on right-drag) ─────────────────

    public void NudgeWindowLevel(double deltaWidth, double deltaCenter)
    {
        if (_renderer is null) return;
        WindowWidth = Math.Max(1, WindowWidth + deltaWidth);
        WindowCenter += deltaCenter;
        ActivePreset = "custom";
        _ = RenderAsync();
    }

    [RelayCommand]
    private void ApplyWindowPreset(string preset)
    {
        if (_renderer is null) return;

        // "default" restores the image's native window (the Reset control).
        if (preset is "default")
        {
            WindowWidth = _defaultWidth;
            WindowCenter = _defaultCenter;
            ActivePreset = "default";
            _ = RenderAsync();
            return;
        }

        // CT presets in HU (width/center). Ignored for an unknown key; still useful on any grayscale image.
        (double w, double c)? p = preset switch
        {
            "bone" => (2000, 400),
            "lung" => (1500, -600),
            "soft" => (400, 40),
            "brain" => (80, 40),
            _ => null,
        };
        if (p is null) return;
        WindowWidth = p.Value.w;
        WindowCenter = p.Value.c;
        ActivePreset = preset;
        _ = RenderAsync();
    }

    partial void OnInvertChanged(bool value) { if (_renderer is not null) _ = RenderAsync(); }

    // ── Cine ──────────────────────────────────────────────────────────────

    partial void OnIsCineChanged(bool value)
    {
        if (value && MultiFrame) _cineTimer.Start();
        else _cineTimer.Stop();
    }

    partial void OnFrameIndexChanged(int value)
    {
        Measure.SetFrame(SelectedNode?.Instance, value);
        _ = RenderAsync();
    }

    private void AdvanceFrame()
    {
        if (FrameCount <= 1) return;
        FrameIndex = (FrameIndex + 1) % FrameCount;
    }

    [RelayCommand] private void NextFrame() { if (MultiFrame) FrameIndex = (FrameIndex + 1) % FrameCount; }
    [RelayCommand] private void PrevFrame() { if (MultiFrame) FrameIndex = (FrameIndex - 1 + FrameCount) % FrameCount; }
    [RelayCommand] private void ToggleCine() => IsCine = !IsCine;

    private void StopCine() { IsCine = false; _cineTimer.Stop(); }

    // ── Measurement tools ─────────────────────────────────────────────────

    [RelayCommand] private void SetTool(string tool)
        => Measure.ActiveTool = Enum.TryParse<MeasurementTool>(tool, ignoreCase: true, out var t) ? t : MeasurementTool.None;

    [RelayCommand] private void ClearMeasurements() => Measure.Clear();

    // ── DICOM tag drawer ──────────────────────────────────────────────────

    [RelayCommand] private void ToggleTags() => TagsOpen = !TagsOpen;

    partial void OnTagsOpenChanged(bool value) { if (value) _ = LoadTagsAsync(); }
    partial void OnTagFilterChanged(string value) => ApplyTagFilter();

    private void LoadTagsIfOpen() { if (TagsOpen) _ = LoadTagsAsync(); }

    private async Task LoadTagsAsync()
    {
        if (SelectedNode?.FilePath is not { } path) { _allTags = []; Tags = []; return; }
        var hide = HidePatientInfo;
        try
        {
            _allTags = await Task.Run(() => DicomTagReader.Read(path, hide), _cts.Token);
            ApplyTagFilter();
        }
        catch (OperationCanceledException) { }
    }

    private void ApplyTagFilter()
    {
        var f = TagFilter?.Trim();
        IEnumerable<DicomTagItem> q = _allTags;
        if (!string.IsNullOrEmpty(f))
            q = _allTags.Where(t =>
                t.Name.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                t.Tag.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                t.Value.Contains(f, StringComparison.OrdinalIgnoreCase));
        Tags = new ObservableCollection<DicomTagItem>(q);
    }

    // ── PHI ───────────────────────────────────────────────────────────────

    partial void OnHidePatientInfoChanged(bool value)
    {
        Container?.ApplyPhiMask(value);
        LoadTagsIfOpen();   // re-read so identifying tag values mask/unmask
    }

    [RelayCommand] private void ToggleHidePatientInfo() => HidePatientInfo = !HidePatientInfo;

    // ── Reports (open elsewhere, breadcrumb back here) ────────────────────

    private void OpenReport(DicomNode node)
    {
        if (node.Instance is not { IsEncapsulatedDocument: true } || node.FilePath is null)
        {
            _shell.ShowNotification("This report has no embedded document to open.");
            return;
        }

        try
        {
            var temp = ExtractEncapsulatedDocument(node);
            if (temp is null) { _shell.ShowError("Could not extract the embedded report."); return; }

            // Register the temp file as originating from THIS DICOM page so the viewer that opens it shows a
            // breadcrumb pointing back here — not to the throwaway temp directory.
            OriginBreadcrumbs.Register(temp, DicomTabRegistration.StaticPageKind, _originParams,
                                       Container?.Title ?? "DICOM");
            _shell.HandleObject(temp);
        }
        catch (Exception ex)
        {
            _shell.ShowError($"Could not open report: {ex.Message}");
        }
    }

    private string? ExtractEncapsulatedDocument(DicomNode node)
    {
        var ds = DicomIo.Open(node.FilePath!, FileReadOption.ReadAll).Dataset;
        if (!ds.TryGetValues<byte>(DicomTag.EncapsulatedDocument, out var bytes) || bytes.Length == 0)
            return null;

        var ext = (node.Instance?.EncapsulatedMimeType) switch
        {
            var m when m?.Contains("pdf", StringComparison.OrdinalIgnoreCase) == true => ".pdf",
            var m when m?.Contains("xml", StringComparison.OrdinalIgnoreCase) == true => ".xml",
            _ => ".bin",
        };

        var dir = Path.Combine(Path.GetTempPath(), "nexaflow-dicom");
        Directory.CreateDirectory(dir);
        var safeTitle = string.Concat((Container?.Title ?? "report").Split(Path.GetInvalidFileNameChars()));
        var temp = Path.Combine(dir, $"{safeTitle}-{Guid.NewGuid():N}{ext}");
        File.WriteAllBytes(temp, bytes);
        _tempReports.Add(temp);
        return temp;
    }

    // ── Probe (called by the view on hover) ───────────────────────────────

    public void ProbeAt(System.Windows.Point imagePoint)
        => ProbeText = Measure.ProbeAt(imagePoint) ?? string.Empty;

    // ── IPageViewModel (AI surface) ───────────────────────────────────────

    public bool IsContextReady => !IsLoading;

    public string GetContext()
    {
        if (IsLoading) return "A DICOM container is still loading.";
        if (Container is null || Container.IsEmpty) return "An empty DICOM viewer (no content found).";

        // De-identified only: never patient name/ID/DOB.
        var sel = SelectedNode?.Instance;
        var selText = sel is null ? "" :
            $" Currently viewing a {sel.Modality} image {sel.Columns}×{sel.Rows}" +
            (sel.Frames > 1 ? $" ({sel.Frames} frames, frame {FrameIndex + 1})" : "") +
            $" at window {WindowWidth:0}/level {WindowCenter:0}.";
        return $"A DICOM viewer showing '{Container.Title}': {Container.Summary}.{selText} " +
               "Patient identifiers are withheld from this context.";
    }

    /// <summary>A stable per-tab scope so two pinned DICOM tabs expose distinctly-named tool contexts
    /// rather than collapsing first-wins.</summary>
    public string? GetSecurityContext()
    {
        var id = _originParams.TryGetValue("path", out var p) && !string.IsNullOrEmpty(p) ? p
               : _originParams.TryGetValue("paths", out var ps) && !string.IsNullOrEmpty(ps) ? ps
               : Container?.Title;
        return $"dicom:{id}";
    }

    public IReadOnlyList<IClientTool> GetClientTools() =>
    [
        new DelegateClientTool("dicom_list_contents",
            "List the de-identified structure of the open DICOM container (studies, series, modalities, image/report counts). No patient identifiers.",
            [], ToolSafety.SafeOperation, (_, _) => Task.FromResult(ListContentsResult())),

        new DelegateClientTool("dicom_get_current_image_info",
            "Report the currently displayed image's modality, geometry, frame position and window/level. No patient identifiers.",
            [], ToolSafety.SafeOperation, (_, _) => Task.FromResult(CurrentImageInfoResult())),

        new DelegateClientTool("dicom_next_frame", "Advance to the next frame of a multi-frame image.",
            [], ToolSafety.SafeOperation, (_, _) => { NextFrame(); return Task.FromResult(CurrentImageInfoResult()); }),

        new DelegateClientTool("dicom_prev_frame", "Go to the previous frame of a multi-frame image.",
            [], ToolSafety.SafeOperation, (_, _) => { PrevFrame(); return Task.FromResult(CurrentImageInfoResult()); }),

        new DelegateClientTool("dicom_capture_image",
            "Capture the current image at its present window/level as a PNG for you to look at.",
            [], ToolSafety.SafeOperation, (_, _) => Task.FromResult(CaptureImageResult())),
    ];

    public ContextSecurityRisk GetContextSecurityRisk() => ContextSecurityRisk.Medium;

    public string? GetAiSystemPromptGuidance() =>
        "This is a read-only medical DICOM viewer. Patient-identifying data (name, ID, date of birth, " +
        "accession) is deliberately withheld from your context and tools — describe imaging findings and " +
        "study structure only, and never claim to know the patient's identity.";

    private ToolResult ListContentsResult()
    {
        if (Container is null) return ToolResult.Error("No DICOM content loaded.");
        return ToolResult.Ok(Container.Summary,
            $"{Container.Summary}\nImages: {Container.Images.Count}, reports: {Container.Reports.Count}.");
    }

    private ToolResult CurrentImageInfoResult()
    {
        if (SelectedNode?.Instance is not { } i) return ToolResult.Error("No image is selected.");
        return ToolResult.Ok($"{i.Modality} {i.Columns}×{i.Rows}",
            $"{i.Modality} image, {i.Columns}×{i.Rows}px, {i.Frames} frame(s), showing frame {FrameIndex + 1}, " +
            $"window {WindowWidth:0}/level {WindowCenter:0}, transfer syntax {i.TransferSyntaxName}.");
    }

    private ToolResult CaptureImageResult()
    {
        if (CurrentBitmap is null) return ToolResult.Error("No image is currently displayed.");
        try
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(CurrentBitmap));
            using var ms = new MemoryStream();
            enc.Save(ms);
            var img = new ContextImage(ms.ToArray(), "image/png", SelectedNode?.Instance?.Modality);
            return ToolResult.Ok("Captured the current image.", "Captured the current DICOM image (attached).")
                with { Images = [img] };
        }
        catch (Exception ex) { return ToolResult.Error($"Capture failed: {ex.Message}"); }
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────

    public void OnActivated() { if (IsCine && MultiFrame) _cineTimer.Start(); }
    public void OnDeactivated() => _cineTimer.Stop();

    public void Dispose()
    {
        _cts.Cancel();
        _renderCts?.Cancel();
        _cineTimer.Stop();
        foreach (var t in _tempReports)
        {
            OriginBreadcrumbs.Clear(t);
            try { if (File.Exists(t)) File.Delete(t); } catch { }
        }
    }
}
