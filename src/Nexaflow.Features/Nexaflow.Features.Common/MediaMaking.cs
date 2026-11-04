using CommunityToolkit.Mvvm.ComponentModel;

namespace Nexaflow.Features.Common;

/// <summary>
/// What one of the workspace's image models lets a picture be asked for with, for a person setting one up — what
/// <c>generate_image</c> offers the assistant, read from the same model.
/// </summary>
/// <param name="Aspects">Its proportions as width:height ("16:9"), the one it draws a square at first.</param>
public sealed record PictureChoices(IReadOnlyList<string> Aspects)
{
    /// <summary>The sizes it draws at, as it names them ("2K"), smallest first; empty where it draws at one.</summary>
    public IReadOnlyList<string> Resolutions { get; init; } = [];

    /// <summary>The care it can be asked to take, least first — "low", "medium", "high"; empty where it is not asked.</summary>
    public IReadOnlyList<string> Details { get; init; } = [];

    /// <summary>Whether a seed draws the same picture again from the same prompt.</summary>
    public bool Seeds { get; init; }

    /// <summary>Whether it is told what to keep out of the picture.</summary>
    public bool NegativePrompts { get; init; }

    /// <summary>Whether it draws on a transparent background when asked.</summary>
    public bool TransparentBackgrounds { get; init; }

    /// <summary>The LoRAs it draws with when asked, one at a time — one meant for adults only among them only while
    /// Enhanced AI Protection Mode is off; empty where it takes none.</summary>
    public IReadOnlyList<PictureLora> Loras { get; init; } = [];

    /// <summary>How many pictures it works from; 0 where it only draws from a prompt.</summary>
    public int Sources { get; init; }

    /// <summary>Whether it draws again only what a mask leaves transparent over the first picture.</summary>
    public bool Masks { get; init; }

    /// <summary>Whether a picture made from pictures keeps the first one's proportions, whatever
    /// <see cref="PictureAsked.Aspect"/> asks.</summary>
    public bool KeepsSourceProportions { get; init; }
}

/// <summary>A LoRA a model draws with when asked — a style, or a faster way of drawing — as it is offered.</summary>
/// <param name="Id">What a picture asks for it by.</param>
/// <param name="Name">What it is shown as.</param>
/// <param name="Description">What it does to a picture, in a sentence.</param>
/// <param name="NegativePrompts">Whether a picture drawn with it is told what to keep out.</param>
public sealed record PictureLora(string Id, string Name, string Description, bool NegativePrompts);

/// <summary>What the workspace draws a picture with now: at standard quality, at high quality, or at either.</summary>
public sealed record PictureMaking(PictureChoices? Standard, PictureChoices? HighQuality);

/// <summary>A picture the user asks for, as they set it up.</summary>
/// <param name="Prompt">What to draw, in the user's words.</param>
/// <param name="Path">The file it is saved as — its extension that of what the model draws — in a folder that exists.</param>
/// <param name="HighQuality">Drawn by the workspace's high-quality model, not its standard one.</param>
public sealed record PictureAsked(string Prompt, string Path, bool HighQuality)
{
    /// <summary>One of the model's <see cref="PictureChoices.Aspects"/>; null for a square.</summary>
    public string? Aspect { get; init; }

    /// <summary>One of its <see cref="PictureChoices.Resolutions"/>; null for its own.</summary>
    public string? Resolution { get; init; }

    /// <summary>One of its <see cref="PictureChoices.Details"/>; null for its own.</summary>
    public string? Detail { get; init; }

    /// <summary>0 or more, where it <see cref="PictureChoices.Seeds"/>; null for a new one.</summary>
    public int? Seed { get; init; }

    /// <summary>What to keep out, where it takes <see cref="PictureChoices.NegativePrompts"/>.</summary>
    public string? NegativePrompt { get; init; }

    /// <summary>On a transparent background, where it <see cref="PictureChoices.TransparentBackgrounds"/>.</summary>
    public bool TransparentBackground { get; init; }

    /// <summary>The id of one of its <see cref="PictureChoices.Loras"/>; null for none.</summary>
    public string? Lora { get; init; }



    /// <summary>Opened in the workspace's viewers once it is made, as a picture asked for in the file view is; false for one
    /// whoever asked for it takes in themselves — a canvas, adding it as a layer.</summary>
    public bool OpensWhenMade { get; init; } = true;
}

/// <summary>A picture the user asks to have changed by the workspace's Image Editing model, as they set it up.</summary>
/// <param name="Instruction">What to change, or how to combine the pictures, in the user's words.</param>
/// <param name="Sources">The pictures to work from, as files — the first the one changed — at most the model's
/// <see cref="PictureChoices.Sources"/>.</param>
/// <param name="Path">The file what it makes is saved as — its extension that of what the model makes — in a folder that exists.</param>
public sealed record PictureEditAsked(string Instruction, IReadOnlyList<string> Sources, string Path)
{
    /// <summary>A PNG the size of the first source whose transparent part is what is drawn again, where the model
    /// <see cref="PictureChoices.Masks"/>; null to change the whole picture.</summary>
    public string? Mask { get; init; }

    /// <summary>0 or more, where it <see cref="PictureChoices.Seeds"/>; null for a new one.</summary>
    public int? Seed { get; init; }

    /// <summary>Opened in the workspace's viewers once it is made; false for one whoever asked for it takes in themselves — a
    /// canvas, adding it as a layer.</summary>
    public bool OpensWhenMade { get; init; } = true;
}

/// <summary>
/// A picture being drawn for whoever asked for it — one of the workspace's background jobs, listed and stoppable with the
/// others: what its drawing is doing, a way to stop it, and the file it makes.
/// </summary>
public sealed partial class PictureJob : ObservableObject
{
    private readonly Action _stop;

    /// <param name="made">Completes with the file the picture was saved as; with null where it was stopped, withheld by the
    /// picture check, or could not be drawn.</param>
    /// <param name="stop">Stops the drawing; nothing it was making is kept.</param>
    public PictureJob(Task<string?> made, Action stop)
    {
        Made  = made;
        _stop = stop;
        _ = made.ContinueWith(_ => OnPropertyChanged(nameof(IsRunning)), TaskScheduler.Default);
    }

    /// <summary>The file the picture was saved as, once it is; null where it was stopped, withheld or could not be drawn.</summary>
    public Task<string?> Made { get; }

    /// <summary>What the drawing is doing now, as its model says — the step it has reached — set by whoever draws it; null
    /// until it says anything.</summary>
    [ObservableProperty] private string? _progress;

    /// <summary>The picture is still being drawn.</summary>
    public bool IsRunning => !Made.IsCompleted;

    /// <summary>Stops the drawing, where it still runs.</summary>
    public void Stop()
    {
        if (IsRunning) _stop();
    }
}

/// <summary>A size a clip is made at ("1080p"), and the only lengths it is made in there; empty where it is made in any.</summary>
public sealed record ClipResolution(string Name, IReadOnlyList<int> OnlyLengths);

/// <summary>
/// What the workspace's model for clips from a description lets one be asked for with, for a person setting one up — what
/// <c>generate_video</c> offers the assistant, read from the same model.
/// </summary>
/// <param name="Aspects">Its proportions as width:height ("16:9"), the one it makes a landscape clip in first.</param>
/// <param name="Lengths">The lengths it makes, in seconds, shortest first.</param>
public sealed record ClipChoices(IReadOnlyList<string> Aspects, IReadOnlyList<int> Lengths)
{
    /// <summary>The sizes it makes clips at, smallest first; empty where it makes them at one.</summary>
    public IReadOnlyList<ClipResolution> Resolutions { get; init; } = [];

    /// <summary>Whether a seed makes much the same clip again from the same prompt.</summary>
    public bool Seeds { get; init; }

    /// <summary>Whether it is told what to keep out of the clip.</summary>
    public bool NegativePrompts { get; init; }
}

/// <summary>A clip the user asks for from a description, as they set it up.</summary>
/// <param name="Prompt">What happens in it, in the user's words.</param>
/// <param name="Path">The file it is saved as — its extension that of what the model makes — in a folder that exists.</param>
/// <param name="Seconds">One of the model's <see cref="ClipChoices.Lengths"/>.</param>
public sealed record ClipAsked(string Prompt, string Path, int Seconds)
{
    /// <summary>One of the model's <see cref="ClipChoices.Aspects"/>; null for its landscape one.</summary>
    public string? Aspect { get; init; }

    /// <summary>The name of one of its <see cref="ClipChoices.Resolutions"/>; null for its own.</summary>
    public string? Resolution { get; init; }

    /// <summary>0 or more, where it <see cref="ClipChoices.Seeds"/>; null for a new one.</summary>
    public int? Seed { get; init; }

    /// <summary>What to keep out, where it takes <see cref="ClipChoices.NegativePrompts"/>.</summary>
    public string? NegativePrompt { get; init; }
}
