namespace Nexaflow.Features.Common;

/// <summary>Where a tool's models stand on this machine.</summary>
public enum ToolModelsState
{
    /// <summary>This machine cannot run the tool: no build for it, or no room on its GPU and no CPU build.</summary>
    Unavailable,

    /// <summary>The tool runs here once its models are fetched — <see cref="IToolModels.DownloadBytes"/> of them.</summary>
    NotDownloaded,

    /// <summary>Its models are being fetched (<see cref="IToolModels.DownloadPercent"/>).</summary>
    Downloading,

    /// <summary>Everything it needs is here.</summary>
    Ready,
}

/// <summary>A picture to restore, by file: interleaved 8-bit RGBA, row by row, straight alpha, nothing else.</summary>
/// <param name="Pixels">The file the picture is in.</param>
/// <param name="Scale">1, 2 or 4: how many times its size it comes back — at 1, cleaned and kept at its size.</param>
/// <param name="Faces">Whether the faces found in it are remade.</param>
/// <param name="Into">The file the restored picture is written to, in the same form; replaced if it is there.</param>
public sealed record PictureRestoreRequest(string Pixels, int Width, int Height, int Scale, bool Faces, string Into);

/// <summary>A restored picture, in the request's file: its size, and how many faces were remade in it.</summary>
public sealed record PictureRestoration(int Width, int Height, int FacesRestored);

/// <summary>
/// The models a tool runs on this machine — a tool rather than an AI service: a person uses it from a page, an assistant
/// from its tools, and nothing in the cloud stands in for it. App-wide: every workspace shares the one set, fetched once,
/// when a person asks.
/// </summary>
public interface IToolModels
{
    ToolModelsState State { get; }

    /// <summary>What fetching the models still costs, in bytes; 0 once they are here.</summary>
    long DownloadBytes { get; }

    /// <summary>How far a download has got, 0 to 100, while <see cref="State"/> is <see cref="ToolModelsState.Downloading"/>.</summary>
    int DownloadPercent { get; }

    /// <summary>Raised, on any thread, when <see cref="State"/>, <see cref="DownloadBytes"/> or <see cref="DownloadPercent"/> changes.</summary>
    event EventHandler? Changed;

    /// <summary>Starts fetching the models, shown with the app's other work in the background; nothing if they are here
    /// or already coming.</summary>
    void Download();
}

/// <summary>
/// Pictures restored by neural networks Nexaflow runs on this machine: made twice or four times their size, or kept at
/// their size and cleaned of JPEG blocks, noise and softness, the faces in them found and remade.
/// </summary>
public interface IPictureTools : IToolModels
{
    /// <summary>Whether a <paramref name="width"/> × <paramref name="height"/> picture is restored at <paramref name="scale"/>:
    /// a scale whose result would be too large to hold is not.</summary>
    bool Restores(int width, int height, int scale);

    /// <summary>Names the models: a picture restored by others is not the same restoration, so whatever keeps one keys it by this.</summary>
    string ModelsVersion { get; }

    /// <summary>Restores a picture, reporting how far it has got from 0 to 1.</summary>
    /// <exception cref="InvalidOperationException">The tools are not ready here, the picture is too large for the scale,
    /// or it could not be restored.</exception>
    Task<PictureRestoration> RestoreAsync(PictureRestoreRequest request, IProgress<double>? progress, CancellationToken ct);
}
