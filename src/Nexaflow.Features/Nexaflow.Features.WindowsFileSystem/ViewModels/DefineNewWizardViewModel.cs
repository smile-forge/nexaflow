using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Features.WindowsFileSystem.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;

namespace Nexaflow.Features.WindowsFileSystem.ViewModels;

/// <summary>What the new association opens with.</summary>
public enum DefineNewTarget { InternalViewer, ExistingExternalApp, NewExternalApp }

/// <summary>Which files the new association applies to.</summary>
public enum DefineNewScope { ThisFileOnly, ExtensionInFolder, ExtensionAnywhere, CustomGlobs }

/// <summary>One pickable file-open experience (an <see cref="IFileAction"/> grouped by its ExperienceId).</summary>
public sealed record ExperienceChoice(
    string ExperienceId, string DisplayName, string Description, string Icon, ImageSource? IconImage);

/// <summary>An entry in the existing-app picker: either a user-defined app or a system-registered handler.</summary>
public sealed record ExistingAppChoice(string DisplayName, string SubText, ExternalAppDefinition? UserApp, string? HandlerPath)
{
    public bool IsUserDefined => UserApp is not null;
}

/// <summary>
/// Backs the "Define New" file-association wizard launched from the file browser's
/// action area. Branches by <see cref="Target"/>: an internal viewer is a 2-page flow
/// (pick experience → scope) writing an <see cref="ExperienceMapping"/>; an external app
/// is a 3-page flow (pick/define app → scope) writing an <see cref="ExternalAppDefinition"/>.
/// Holds no view state beyond what the overlay binds; closing is delegated to the host.
/// </summary>
public partial class DefineNewWizardViewModel : ObservableObject
{
    private readonly IShellServices _shell;
    private readonly ExternalAppsConfig _externalApps;
    private readonly string? _selectedFilePath;
    private readonly Action _onClose;

    // ── Wizard position ──────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowModePage), nameof(ShowInternalPicker),
        nameof(ShowAppPickPage), nameof(ShowAppFieldsPage), nameof(ShowScopePage),
        nameof(IsLastPage), nameof(ShowBack), nameof(StepIndicator), nameof(AdvanceButtonText))]
    [NotifyCanExecuteChangedFor(nameof(AdvanceCommand), nameof(BackCommand))]
    private int _pageIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalPages), nameof(IsLastPage), nameof(StepIndicator),
        nameof(AdvanceButtonText), nameof(ShowInternalPicker),
        nameof(ShowAppPickPage), nameof(ShowAppFieldsPage), nameof(ShowScopePage))]
    [NotifyCanExecuteChangedFor(nameof(AdvanceCommand))]
    private DefineNewTarget _target = DefineNewTarget.InternalViewer;

    // ── Page 1: target + internal experience ─────────────────────────────────

    public ObservableCollection<ExperienceChoice> Experiences { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AdvanceCommand))]
    private ExperienceChoice? _selectedExperience;

    // ── Page 2 (existing external): pick an app ──────────────────────────────

    public IReadOnlyList<ExistingAppChoice> ExistingApps { get; }
    public bool HasExistingApps => ExistingApps.Count > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AdvanceCommand))]
    private ExistingAppChoice? _selectedApp;

    // ── Page 2 (new external): app fields ────────────────────────────────────

    public IReadOnlyList<string> MultiFileOptions { get; } = Enum.GetNames<MultiFileMode>();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AdvanceCommand))]
    private string _newAppPath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AdvanceCommand))]
    private string _newAppDisplayName = string.Empty;

    [ObservableProperty] private string _newAppArguments = "#file";
    [ObservableProperty] private string _newAppWorkingDir = string.Empty;
    [ObservableProperty] private string _newAppIconPath = string.Empty;
    [ObservableProperty] private string _newAppMultiFileName = nameof(MultiFileMode.SingleFileOnly);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AdvancedHeader))]
    private bool _advancedExpanded;

    public string AdvancedHeader => (AdvancedExpanded ? "▾ " : "▸ ") + "Advanced options";

    /// <summary>Auto-fills a display name from the chosen executable when the user hasn't typed one.</summary>
    partial void OnNewAppPathChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(NewAppDisplayName) && !string.IsNullOrWhiteSpace(value))
        {
            try { NewAppDisplayName = Path.GetFileNameWithoutExtension(value); } catch { }
        }
    }

    [RelayCommand]
    private void ToggleAdvanced() => AdvancedExpanded = !AdvancedExpanded;

    // ── Final page: scope ────────────────────────────────────────────────────

    public bool HasSelectedFile { get; }
    public string FolderPath { get; }
    public string SelectedFileName => _selectedFilePath is null ? string.Empty : Path.GetFileName(_selectedFilePath);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScopeThisFile), nameof(IsScopeExtFolder),
        nameof(IsScopeExtAnywhere), nameof(IsScopeCustom))]
    [NotifyCanExecuteChangedFor(nameof(AdvanceCommand))]
    private DefineNewScope _scope = DefineNewScope.ExtensionInFolder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExtGlob))]
    [NotifyCanExecuteChangedFor(nameof(AdvanceCommand))]
    private string _extension = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AdvanceCommand))]
    private string _customGlobs = string.Empty;

    // ── ctor ─────────────────────────────────────────────────────────────────

    public DefineNewWizardViewModel(
        IShellServices shell,
        ExternalAppsConfig externalApps,
        IReadOnlyList<IFileAction> fileActions,
        string currentFolder,
        string? selectedFilePath,
        Action onClose)
    {
        _shell            = shell;
        _externalApps     = externalApps;
        _selectedFilePath = selectedFilePath;
        _onClose          = onClose;

        Experiences = new(fileActions
            // Only actions that open an internal viewer tab are mappable file-type targets;
            // utility/external actions (delete/rename/open-with/shell verb) are excluded.
            .Where(a => a.OpensViewer && !string.IsNullOrEmpty(a.ExperienceId))
            .GroupBy(a => a.ExperienceId, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var rep = g.First();
                return new ExperienceChoice(
                    rep.ExperienceId,
                    rep.DisplayName,
                    string.IsNullOrWhiteSpace(rep.ExperienceDescription) ? rep.DisplayName : rep.ExperienceDescription,
                    rep.Icon,
                    rep.IconImage);
            })
            .OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase));

        HasSelectedFile = !string.IsNullOrEmpty(selectedFilePath);
        FolderPath = HasSelectedFile ? (Path.GetDirectoryName(selectedFilePath) ?? currentFolder) : currentFolder;
        if (HasSelectedFile)
            Extension = Path.GetExtension(selectedFilePath) ?? string.Empty;   // ".md"

        // Existing-app picker: user-defined apps, plus the system-registered handlers for the
        // selected file's type when registry-based mapping is enabled.
        var existing = new List<ExistingAppChoice>();
        foreach (var a in _externalApps.Apps)
            existing.Add(new ExistingAppChoice(a.DisplayName, a.ApplicationPath, a, null));

        if (_externalApps.UseRegistryMapping && HasSelectedFile)
        {
            foreach (var (name, path) in ShellAssocHandlers.ForExtension(Path.GetExtension(selectedFilePath!)))
                if (!existing.Any(c => string.Equals(c.SubText, path, StringComparison.OrdinalIgnoreCase)))
                    existing.Add(new ExistingAppChoice(name, path, null, path));
        }
        ExistingApps = existing;
    }

    // ── Target proxy (radio buttons) ─────────────────────────────────────────

    public bool IsInternalSelected { get => Target == DefineNewTarget.InternalViewer;  set { if (value) Target = DefineNewTarget.InternalViewer; } }
    public bool IsExistingSelected { get => Target == DefineNewTarget.ExistingExternalApp; set { if (value) Target = DefineNewTarget.ExistingExternalApp; } }
    public bool IsNewSelected      { get => Target == DefineNewTarget.NewExternalApp; set { if (value) Target = DefineNewTarget.NewExternalApp; } }

    partial void OnTargetChanged(DefineNewTarget value)
    {
        OnPropertyChanged(nameof(IsInternalSelected));
        OnPropertyChanged(nameof(IsExistingSelected));
        OnPropertyChanged(nameof(IsNewSelected));
    }

    // ── Scope proxy (radio buttons) ──────────────────────────────────────────

    public bool IsScopeThisFile    { get => Scope == DefineNewScope.ThisFileOnly;      set { if (value) Scope = DefineNewScope.ThisFileOnly; } }
    public bool IsScopeExtFolder   { get => Scope == DefineNewScope.ExtensionInFolder; set { if (value) Scope = DefineNewScope.ExtensionInFolder; } }
    public bool IsScopeExtAnywhere { get => Scope == DefineNewScope.ExtensionAnywhere; set { if (value) Scope = DefineNewScope.ExtensionAnywhere; } }
    public bool IsScopeCustom      { get => Scope == DefineNewScope.CustomGlobs;       set { if (value) Scope = DefineNewScope.CustomGlobs; } }

    // ── Derived view state ───────────────────────────────────────────────────

    // Three symmetric pages for every target: 0 = pick what, 1 = pick/define the target, 2 = scope.
    public int  TotalPages => 3;
    public bool IsLastPage => PageIndex == TotalPages - 1;
    public bool ShowBack   => PageIndex > 0;
    public string StepIndicator   => $"Step {PageIndex + 1} of {TotalPages}";
    public string AdvanceButtonText => IsLastPage ? "Finish" : "Next";

    public bool ShowModePage       => PageIndex == 0;
    public bool ShowInternalPicker => PageIndex == 1 && Target == DefineNewTarget.InternalViewer;
    public bool ShowAppPickPage    => PageIndex == 1 && Target == DefineNewTarget.ExistingExternalApp;
    public bool ShowAppFieldsPage  => PageIndex == 1 && Target == DefineNewTarget.NewExternalApp;
    public bool ShowScopePage      => PageIndex == 2;

    /// <summary>The extension glob (e.g. "*.md"), or "*.*" when no extension is set.</summary>
    public string ExtGlob => string.IsNullOrEmpty(NormalizedExtension) ? "*.*" : "*" + NormalizedExtension;

    /// <summary>User-entered extension normalised to ".ext" (accepts "md", ".md", "*.md"); "" if blank.</summary>
    private string NormalizedExtension
    {
        get
        {
            var e = (Extension ?? string.Empty).Trim();
            if (e.StartsWith("*.")) e = e[1..];                 // "*.md" → ".md"
            if (e.Length > 0 && !e.StartsWith('.')) e = "." + e; // "md"   → ".md"
            return e;
        }
    }

    private string[] CustomGlobSegments =>
        (CustomGlobs ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // ── Browse buttons (new external app) ────────────────────────────────────

    [RelayCommand]
    private async Task BrowseAppPathAsync()
    {
        var picked = await _shell.PickOpenFileAsync();
        if (!string.IsNullOrEmpty(picked)) NewAppPath = picked;
    }

    [RelayCommand]
    private async Task BrowseIconPathAsync()
    {
        var picked = await _shell.PickOpenFileAsync([".ico", ".exe", ".dll"]);
        if (!string.IsNullOrEmpty(picked)) NewAppIconPath = picked;
    }

    // ── Navigation ───────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanAdvance))]
    private void Advance()
    {
        if (IsLastPage) { Commit(); _onClose(); return; }
        PageIndex++;
    }

    private bool CanAdvance()
    {
        if (ShowScopePage)      return ScopeIsValid();
        if (ShowInternalPicker) return SelectedExperience is not null;
        if (ShowAppFieldsPage)  return !string.IsNullOrWhiteSpace(NewAppDisplayName) && !string.IsNullOrWhiteSpace(NewAppPath);
        if (ShowAppPickPage)    return SelectedApp is not null;
        // mode page (0): a target radio is always selected, so advancing is always allowed.
        return true;
    }

    [RelayCommand(CanExecute = nameof(ShowBack))]
    private void Back()
    {
        if (PageIndex > 0) PageIndex--;
    }

    [RelayCommand]
    private void Cancel() => _onClose();

    private bool ScopeIsValid() => Scope switch
    {
        DefineNewScope.ThisFileOnly      => HasSelectedFile,
        DefineNewScope.ExtensionInFolder => NormalizedExtension.Length > 0 && !string.IsNullOrEmpty(FolderPath),
        DefineNewScope.ExtensionAnywhere => NormalizedExtension.Length > 0,
        DefineNewScope.CustomGlobs       => CustomGlobSegments.Length > 0,
        _ => false,
    };

    // ── Commit ───────────────────────────────────────────────────────────────

    private void Commit()
    {
        var criteria = BuildCriteria();
        if (criteria.Count == 0) return;

        if (Target == DefineNewTarget.InternalViewer)
        {
            var expId = SelectedExperience!.ExperienceId;
            var mapping = FileMapManager.Instance.GetMapping(expId)
                          ?? new ExperienceMapping { ExperienceId = expId, Source = MappingSource.User };
            mapping.Source = MappingSource.User;
            mapping.Criteria.AddRange(criteria);
            FileMapManager.Instance.SaveMapping(mapping);
        }
        else
        {
            if (Target == DefineNewTarget.ExistingExternalApp)
            {
                var choice = SelectedApp!;
                if (choice.IsUserDefined)
                {
                    var def = choice.UserApp!;
                    // Preserve the app's existing extension match before adding the new scope, so the
                    // association is additive ("also works in the new location"), not a replacement.
                    if (def.Criteria.Count == 0 && !string.IsNullOrWhiteSpace(def.Extension))
                        def.Criteria.Add(new FileSelectionCriteria { Type = CriteriaType.Extension, Value = def.Extension });
                    def.Criteria.AddRange(criteria);
                }
                else
                {
                    // A system-registered handler isn't in our config — create an entry for it.
                    _externalApps.Apps.Add(new ExternalAppDefinition
                    {
                        DisplayName     = choice.DisplayName,
                        ApplicationPath = choice.HandlerPath ?? string.Empty,
                        Arguments       = "#file",
                        MultiFile       = MultiFileMode.SingleFileOnly,
                        Criteria        = criteria,
                    });
                }
            }
            else // NewExternalApp
            {
                _externalApps.Apps.Add(new ExternalAppDefinition
                {
                    DisplayName      = NewAppDisplayName.Trim(),
                    ApplicationPath  = NewAppPath.Trim(),
                    Arguments        = NewAppArguments ?? string.Empty,
                    WorkingDirectory = NewAppWorkingDir ?? string.Empty,
                    IconPath         = NewAppIconPath ?? string.Empty,
                    MultiFile        = Enum.TryParse<MultiFileMode>(NewAppMultiFileName, out var mf) ? mf : MultiFileMode.SingleFileOnly,
                    Criteria         = criteria,
                });
            }

            ExternalAppRegistry.Instance.Update(_externalApps);
            _shell.SaveFeatureConfig(_externalApps);
        }

        // The host rebuilds the action strip for the current selection when the wizard closes,
        // so the new action is usable immediately without a full refresh (which clears selection).
    }

    private List<FileSelectionCriteria> BuildCriteria()
    {
        var list = new List<FileSelectionCriteria>();
        switch (Scope)
        {
            case DefineNewScope.ThisFileOnly when _selectedFilePath is not null:
                list.Add(new() { Type = CriteriaType.PathPattern, Value = _selectedFilePath });
                break;
            case DefineNewScope.ExtensionInFolder:
                list.Add(new() { Type = CriteriaType.PathPattern, Value = Path.Combine(FolderPath, "**", "*" + NormalizedExtension) });
                break;
            case DefineNewScope.ExtensionAnywhere:
                list.Add(new() { Type = CriteriaType.Extension, Value = "*" + NormalizedExtension });
                break;
            case DefineNewScope.CustomGlobs:
                foreach (var seg in CustomGlobSegments)
                    list.Add(new() { Type = CriteriaType.PathPattern, Value = seg });
                break;
        }
        return list;
    }
}
