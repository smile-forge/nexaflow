using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Video.Views;
using Nexaflow.Visuals.Common.Generation;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Video.ViewModels;

/// <summary>
/// The options a clip made from the New picker is made with: what happens, how long it runs, and what the workspace's
/// model for clips from a description offers besides — its proportions, size, a seed, what to keep out — as
/// <c>generate_video</c> offers the assistant. A size that makes only some lengths offers only those. The workspace's
/// models are followed while the view is shown; one waiting on its download is offered to download.
/// </summary>
public sealed partial class GenerateVideoOptions : ObservableObject, IFileCreateOptions
{
    /// <summary>The rows a clip is made by, standard first.</summary>
    internal static readonly AiAbility[] VideoRows = [AiAbility.VideoGeneration, AiAbility.HighQualityVideoGeneration];

    /// <summary>The length a clip is made at unless another is chosen: the one nearest this, the longer of two.</summary>
    private const int UsualSeconds = 8;

    private readonly IAIService _ai;
    private readonly string _folder;
    private IAiAbilityGate? _models;
    private ClipChoices? _making;

    public GenerateVideoOptions(IAIService ai, string folder)
    {
        _ai     = ai;
        _folder = folder;
        Refresh();
    }

    /// <summary>Whether the New picker offers a clip: a model makes one now, or will once a model of Nexaflow's own downloads.</summary>
    internal static bool Offered(IAIService ai) => ai.MakesClips is not null || Waiting(ai).Any();

    /// <summary>The video rows whose model of Nexaflow's own waits on its download.</summary>
    private static IEnumerable<AiAbility> Waiting(IAIService ai) =>
        VideoRows.Where(r => ai.StatusOf(r).State is AiAbilityState.NeedsDownload or AiAbilityState.Downloading);

    /// <summary>Made when the picker first shows it, and following the workspace's models while it is shown.</summary>
    public FrameworkElement View => _view ??= Shown(new GenerateVideoOptionsView { DataContext = this });

    private FrameworkElement? _view;

    private FrameworkElement Shown(FrameworkElement view)
    {
        view.Loaded   += (_, _) => Follow();
        view.Unloaded += (_, _) => Unfollow();
        return view;
    }

    /// <summary>Whether a model makes a clip now; until one does, the options say why and offer its download.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    private bool _isReady;

    /// <summary>Whether a model of Nexaflow's own waits on its download, which <see cref="DownloadCommand"/> offers.</summary>
    [ObservableProperty] private bool _isWaiting;

    /// <summary>What happens in the clip, in the user's words.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    private string _prompt = string.Empty;

    public ObservableCollection<GenerationChoice> Aspects { get; } = [];

    [ObservableProperty] private GenerationChoice? _aspect;

    /// <summary>The sizes the model makes clips at, after its own; empty where it makes them at one.</summary>
    public ObservableCollection<GenerationChoice> Resolutions { get; } = [];

    [ObservableProperty] private GenerationChoice? _resolution;

    /// <summary>The lengths the model makes at the size chosen, their values in seconds.</summary>
    public ObservableCollection<GenerationChoice> Lengths { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    private GenerationChoice? _length;

    [ObservableProperty] private bool _seeds;

    /// <summary>The seed as typed: empty for a new one each time.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreate), nameof(SeedIsValid))]
    private string _seed = string.Empty;

    [ObservableProperty] private bool _negativePrompts;

    /// <summary>What to keep out of the clip.</summary>
    [ObservableProperty] private string _negativePrompt = string.Empty;

    /// <summary>Whether <see cref="Seed"/> is empty or a whole number of 0 or more.</summary>
    public bool SeedIsValid => !Seeds || GenerationChoices.SeedValue(Seed, out _);

    public bool CanCreate => IsReady && Prompt.Trim().Length > 0 && Length?.Value is not null && SeedIsValid;

    public string? Create(string fileName)
    {
        if (!CanCreate || SecondsOf(Length) is not { } seconds) return null;
        GenerationChoices.SeedValue(Seed, out var seed);
        var path = Path.Combine(_folder, fileName);
        _ai.MakeClip(new ClipAsked(Prompt.Trim(), path, seconds)
        {
            Aspect         = Aspect?.Value,
            Resolution     = Resolution?.Value,
            Seed           = Seeds ? seed : null,
            NegativePrompt = NegativePrompts && NegativePrompt.Trim() is { Length: > 0 } keptOut ? keptOut : null,
        });
        return path;
    }

    /// <summary>Offers the download of a model of Nexaflow's own that waits on it; the options are drawn again once it lands.</summary>
    [RelayCommand]
    private async Task Download()
    {
        foreach (var row in Waiting(_ai))
            if (!await _ai.MakeReadyAsync(row)) break;
        Refresh();
    }

    /// <summary>Reads again what the workspace makes clips with, keeping what was chosen where the model still offers it.</summary>
    internal void Refresh()
    {
        _making   = _ai.MakesClips;
        IsReady   = _making is not null;
        IsWaiting = !IsReady && Waiting(_ai).Any();

        Aspect     = GenerationChoices.Refill(Aspects, _making?.Aspects.Select(a => new GenerationChoice(AspectLabel(a), a)), Aspect);
        Resolution = GenerationChoices.Refill(Resolutions, _making?.Resolutions.Select(r => new GenerationChoice(r.Name, r.Name)), Resolution,
                                              Str.Get("Video.Generate.ModelsOwn"));
        Seeds           = _making?.Seeds ?? false;
        NegativePrompts = _making?.NegativePrompts ?? false;
        OfferLengths();
        OnPropertyChanged(nameof(SeedIsValid));
        OnPropertyChanged(nameof(CanCreate));
    }

    partial void OnResolutionChanged(GenerationChoice? value) => OfferLengths();

    /// <summary>Lists the lengths the model makes at the size chosen, keeping the one chosen where it still makes it.</summary>
    private void OfferLengths()
    {
        var only = _making?.Resolutions.FirstOrDefault(r => r.Name == Resolution?.Value)?.OnlyLengths ?? [];
        var lengths = (_making?.Lengths ?? []).Where(l => only.Count == 0 || only.Contains(l)).ToList();
        var kept = SecondsOf(Length) is { } seconds && lengths.Contains(seconds)
            ? Length
            : lengths.Count > 0
                ? Seconds(lengths.OrderBy(l => Math.Abs(l - UsualSeconds)).ThenByDescending(l => l).First())
                : null;
        Length = GenerationChoices.Refill(Lengths, lengths.Select(Seconds), kept);
    }

    private static GenerationChoice Seconds(int seconds) =>
        new(Str.Format("Video.Generate.SecondsFormat", seconds), seconds.ToString(CultureInfo.InvariantCulture));

    private static int? SecondsOf(GenerationChoice? length) =>
        int.TryParse(length?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) ? seconds : null;

    /// <summary>"Landscape · 16:9": the shape the proportions make, then the proportions.</summary>
    internal static string AspectLabel(string aspect) =>
        GenerationChoices.ShapeOf(aspect) is { } shape
            ? Str.Format("Video.Generate.AspectFormat", Str.Get($"Video.Generate.Shape.{shape}"), aspect)
            : aspect;

    private void Follow()
    {
        if (_models is not null) return;
        _models = _ai.Gate(VideoRows);
        _models.PropertyChanged += OnModelsChanged;
        Refresh();
    }

    private void Unfollow()
    {
        if (_models is null) return;
        _models.PropertyChanged -= OnModelsChanged;
        _models.Dispose();
        _models = null;
    }

    private void OnModelsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Refresh();

    public void Dispose() => Unfollow();
}
