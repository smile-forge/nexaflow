using Nexaflow.Features.Images.Editing.Layers;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>A colour mode offered for a layered picture, with its name.</summary>
public sealed record ColourModelChoice(ColourModel Model, string Label)
{
    /// <summary>Colour and grey, as a picker offers them.</summary>
    public static IReadOnlyList<ColourModelChoice> All { get; } =
    [
        new(ColourModel.Rgb, Str.Get("Images.Colour.Mode.Rgb")),
        new(ColourModel.Grey, Str.Get("Images.Colour.Mode.Grey")),
    ];

    public static ColourModelChoice Of(ColourModel model) => All.First(c => c.Model == model);
}

/// <summary>A depth offered for a layered picture's channels, with its name.</summary>
public sealed record ChannelDepthChoice(ChannelDepth Depth, string Label)
{
    /// <summary>8 and 16 bits, and 32-bit float, as a picker offers them.</summary>
    public static IReadOnlyList<ChannelDepthChoice> All { get; } =
    [
        new(ChannelDepth.U8, Str.Get("Images.Colour.Depth.U8")),
        new(ChannelDepth.U16, Str.Get("Images.Colour.Depth.U16")),
        new(ChannelDepth.F32, Str.Get("Images.Colour.Depth.F32")),
    ];

    public static ChannelDepthChoice Of(ChannelDepth depth) => All.First(c => c.Depth == depth);
}
