using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Views;
using Nexaflow.Visuals.Common.Generation;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>
/// The options a picture made from the New picker is drawn with: what to draw, and what the workspace's image model at
/// the quality chosen offers besides — its proportions, size and detail, a seed, what to keep out, a transparent
/// background, a LoRA — as <c>generate_image</c> offers the assistant. The workspace's models are followed while the view is
/// shown, so a model assigned or downloaded meanwhile is offered at once; one waiting on its download is offered to
/// download.
/// </summary>
public sealed partial class GenerateImageOptions : ObservableObject, IFileCreateOptions
{
    /// <summary>The rows a picture is drawn by, standard first.</summary>
    internal static readonly AiAbility[] ImageRows = [AiAbility.ImageGeneration, AiAbility.HighQualityImageGeneration];

    private readonly IAIService _ai;
    private readonly string _folder;
    private IAiAbilityGate? _models;
    private PictureMaking? _making;

    public GenerateImageOptions(IAIService ai, string folder)
    {
        _ai     = ai;
        _folder = folder;
        Refresh();
    }

    /// <summary>Whether the New picker offers a picture: a model draws one now, or will once a model of Nexaflow's own downloads.</summary>
    internal static bool Offered(IAIService ai) => ai.MakesPictures is not null || Waiting(ai).Any();

    /// <summary>The image rows whose model of Nexaflow's own waits on its download.</summary>
    private static IEnumerable<AiAbility> Waiting(IAIService ai) =>
        ImageRows.Where(r => ai.StatusOf(r).State is AiAbilityState.NeedsDownload or AiAbilityState.Downloading);

    /// <summary>Made when the picker first shows it, and following the workspace's models while it is shown.</summary>
    public FrameworkElement View => _view ??= Shown(new GenerateImageOptionsView { DataContext = this });

    private FrameworkElement? _view;

    private FrameworkElement Shown(FrameworkElement view)
    {
        view.Loaded   += (_, _) => Follow();
        view.Unloaded += (_, _) => Unfollow();
        return view;
    }

    /// <summary>Whether a model draws a picture now; until one does, the options say why and offer its download.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    private bool _isReady;

    /// <summary>Whether a model of Nexaflow's own waits on its download, which <see cref="DownloadCommand"/> offers.</summary>
    [ObservableProperty] private bool _isWaiting;

    /// <summary>What to draw, in the user's words.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    private string _prompt = string.Empty;

    /// <summary>Standard and high quality, offered where the workspace has a model for each.</summary>
    public ObservableCollection<GenerationChoice> Qualities { get; } = [];

    [ObservableProperty] private GenerationChoice? _quality;

    public ObservableCollection<GenerationChoice> Aspects { get; } = [];

    [ObservableProperty] private GenerationChoice? _aspect;

    /// <summary>The sizes the model draws at, after its own; empty where it draws at one.</summary>
    public ObservableCollection<GenerationChoice> Resolutions { get; } = [];

    [ObservableProperty] private GenerationChoice? _resolution;

    /// <summary>The levels of detail the model takes, after its own; empty where it takes none.</summary>
    public ObservableCollection<GenerationChoice> Details { get; } = [];

    [ObservableProperty] private GenerationChoice? _detail;

    [ObservableProperty] private bool _seeds;

    /// <summary>The seed as typed: empty for a new one each time.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreate), nameof(SeedIsValid))]
    private string _seed = string.Empty;

    [ObservableProperty] private bool _negativePrompts;

    /// <summary>What to keep out of the picture.</summary>
    [ObservableProperty] private string _negativePrompt = string.Empty;

    [ObservableProperty] private bool _transparentBackgrounds;

    [ObservableProperty] private bool _transparentBackground;

    /// <summary>The LoRAs the model draws with, after none; empty where it takes none.</summary>
    public ObservableCollection<GenerationChoice> Loras { get; } = [];

    [ObservableProperty] private GenerationChoice? _lora;

    /// <summary>What the LoRA chosen does to a picture; empty for none.</summary>
    [ObservableProperty] private string _loraNote = string.Empty;

    /// <summary>The model's LoRAs as it offers them, and whether it takes a negative prompt, at the quality chosen.</summary>
    private IReadOnlyList<PictureLora> _offeredLoras = [];
    private bool _modelTakesNegativePrompts;

    /// <summary>Whether <see cref="Seed"/> is empty or a whole number of 0 or more.</summary>
    public bool SeedIsValid => !Seeds || GenerationChoices.SeedValue(Seed, out _);

    public bool CanCreate => IsReady && Prompt.Trim().Length > 0 && SeedIsValid;

    private bool HighQuality => Quality?.Value == HighQualityValue;

    private const string HighQualityValue = "high";

    public string? Create(string fileName)
    {
        if (!CanCreate) return null;
        GenerationChoices.SeedValue(Seed, out var seed);
        var path = Path.Combine(_folder, fileName);
        _ai.MakePicture(new PictureAsked(Prompt.Trim(), path, HighQuality)
        {
            Aspect                = Aspect?.Value,
            Resolution            = Resolution?.Value,
            Detail                = Detail?.Value,
            Seed                  = Seeds ? seed : null,
            NegativePrompt        = NegativePrompts && NegativePrompt.Trim() is { Length: > 0 } keptOut ? keptOut : null,
            TransparentBackground = TransparentBackgrounds && TransparentBackground,
            Lora                  = Lora?.Value,
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

    /// <summary>Reads again what the workspace draws with, keeping what was chosen where the model still offers it.</summary>
    internal void Refresh()
    {
        _making   = _ai.MakesPictures;
        IsReady   = _making is not null;
        IsWaiting = !IsReady && Waiting(_ai).Any();

        var kept = Quality?.Value;
        Qualities.Clear();
        if (_making is { Standard: not null, HighQuality: not null })
        {
            Qualities.Add(new(Str.Get("Images.Generate.Quality.Standard"), "standard"));
            Qualities.Add(new(Str.Get("Images.Generate.Quality.High"), HighQualityValue));
        }
        Quality = Qualities.FirstOrDefault(q => q.Value == kept) ?? Qualities.FirstOrDefault();
        Offer();
    }

    partial void OnQualityChanged(GenerationChoice? value) => Offer();

    /// <summary>Lists what the model at the quality chosen offers.</summary>
    private void Offer()
    {
        var choices = _making is null ? null : HighQuality || _making.Standard is null ? _making.HighQuality : _making.Standard;

        Aspect = GenerationChoices.Refill(Aspects, choices?.Aspects.Select(a => new GenerationChoice(AspectLabel(a), a)), Aspect);
        Resolution = GenerationChoices.Refill(Resolutions, choices?.Resolutions.Select(r => new GenerationChoice(r, r)), Resolution, Str.Get("Images.Generate.ModelsOwn"));
        Detail = GenerationChoices.Refill(Details, choices?.Details.Select(d => new GenerationChoice(Str.Get($"Images.Generate.Detail.{Capitalised(d)}"), d)), Detail, Str.Get("Images.Generate.ModelsOwn"));
        Seeds                  = choices?.Seeds ?? false;
        TransparentBackgrounds = choices?.TransparentBackgrounds ?? false;
        _modelTakesNegativePrompts = choices?.NegativePrompts ?? false;
        _offeredLoras = choices?.Loras ?? [];
        Lora = GenerationChoices.Refill(Loras, _offeredLoras.Select(l => new GenerationChoice(l.Name, l.Id)), Lora, Str.Get("Images.Generate.NoLora"));
        OfferLora();
        OnPropertyChanged(nameof(SeedIsValid));
        OnPropertyChanged(nameof(CanCreate));
    }

    partial void OnLoraChanged(GenerationChoice? value) => OfferLora();

    /// <summary>Says what the LoRA chosen does, and offers what to keep out only where the model and its LoRA are told it.</summary>
    private void OfferLora()
    {
        var chosen = _offeredLoras.FirstOrDefault(l => l.Id == Lora?.Value);
        LoraNote        = chosen?.Description ?? string.Empty;
        NegativePrompts = _modelTakesNegativePrompts && (chosen?.NegativePrompts ?? true);
    }

    /// <summary>"Landscape · 16:9": the shape the proportions make, then the proportions.</summary>
    internal static string AspectLabel(string aspect)
    {
        var shape = GenerationChoices.ShapeOf(aspect);
        return shape is null ? aspect : Str.Format("Images.Generate.AspectFormat", Str.Get($"Images.Generate.Shape.{shape}"), aspect);
    }

    private static string Capitalised(string word) => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..];

    private void Follow()
    {
        if (_models is not null) return;
        _models = _ai.Gate(ImageRows);
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