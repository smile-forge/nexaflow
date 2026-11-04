using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Drawing;

/// <summary>
/// One way of drawing a picture: a family of image model — the parts it has (<see cref="DrawingParts"/>) and the sampler
/// that drives them. Each takes the GPU a step at a time at the request's priority, and holds a part only while it needs
/// it, in the room the picture borrowed. A new family of image model is a new drawing method.
/// </summary>
internal interface IDrawingMethod
{
    /// <summary>What a <see cref="DrawingModel.Method"/> names it.</summary>
    DrawingMethod Method { get; }

    /// <summary>Whether it draws pictures of this size.</summary>
    bool Draws(int width, int height);

    /// <summary>Whether it draws an edit — a picture made from pictures to work from — at this size, and reads a picture to
    /// work from at it.</summary>
    bool Reads(int width, int height);

    /// <summary>Whether it is told what to keep out of a picture (<see cref="Draw.NegativePrompt"/>).</summary>
    bool TakesNegativePrompts { get; }

    /// <summary>Whether it draws with a LoRA (<see cref="Draw.Lora"/>).</summary>
    bool TakesLoras { get; }

    /// <summary>How many pictures to work from it reads (<see cref="Draw.Sources"/>); 0 for a method that draws from a
    /// prompt alone.</summary>
    int MaxSources { get; }

    /// <summary>Whether it draws again only what a mask covers over the first source (<see cref="Draw.Mask"/>).</summary>
    bool Masks { get; }

    /// <summary>Draws <paramref name="request"/>'s picture, as interleaved 8-bit RGB rows.</summary>
    /// <param name="stage">Told of a part loading, by what it is doing.</param>
    /// <param name="stepped">Told of each denoising step as it finishes: how many are done, of how many.</param>
    Task<byte[]> DrawAsync(Draw request, Func<string, Task> stage, Func<int, int, Task> stepped, ModelRegistry.Room room, CancellationToken ct);
}
