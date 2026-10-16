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

    /// <summary>Raised, on any thread, when <see cref="State"/>, <see cref="DownloadBytes"/>, <see cref="DownloadPercent"/> or <see cref="DownloadFailure"/> changes.</summary>
    event EventHandler? Changed;

    /// <summary>Starts fetching the models, shown with the app's other work in the background; nothing if they are here
    /// or already coming.</summary>
    void Download();

    /// <summary>Names the models: what one set made is not what another makes, so whatever keeps a result keys it by this.</summary>
    string ModelsVersion { get; }

    /// <summary>Why the last download stopped short of everything, while <see cref="State"/> is
    /// <see cref="ToolModelsState.NotDownloaded"/> — offered again with it; null when none has.</summary>
    string? DownloadFailure { get; }
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

    /// <summary>Restores a picture, reporting how far it has got from 0 to 1.</summary>
    /// <exception cref="InvalidOperationException">The tools are not ready here, the picture is too large for the scale,
    /// or it could not be restored.</exception>
    Task<PictureRestoration> RestoreAsync(PictureRestoreRequest request, IProgress<double>? progress, CancellationToken ct);
}

/// <summary>A click on a picture, in its pixels: on the thing meant (<see cref="Include"/>) or off it.</summary>
public sealed record PicturePoint(double X, double Y, bool Include);

/// <summary>A box on a picture, in its pixels.</summary>
public sealed record PictureBox(double Left, double Top, double Right, double Bottom);

/// <summary>An example box for a phrase: something <see cref="Like"/> what is meant, or something it does not mean.</summary>
public sealed record PictureExample(PictureBox Box, bool Like);

/// <summary>A mask: <see cref="Width"/> × <see cref="Height"/> bytes, row by row, 255 over what is picked and 0
/// elsewhere. <see cref="Bounds"/> is the box round it in the picture's pixels; null where nothing is picked.</summary>
public sealed record PictureMask(byte[] Pixels, int Width, int Height, PictureBox? Bounds);

/// <summary>Something a phrase found: how surely, from 0.5 to 1, the box round it in the picture's pixels, and its mask.</summary>
public sealed record PictureFind(double Score, PictureBox Box, PictureMask Mask);

/// <summary>
/// Things picked out of pictures by a neural network Nexaflow runs on this machine: by clicks on a thing and off it, a
/// box drawn round it, or everything a phrase names ("wheel", "child in a red vest"), helped by example boxes.
/// </summary>
public interface IPictureSelection : IToolModels
{
    /// <summary>
    /// Opens a selection on a picture — interleaved 8-bit RGBA, row by row — and returns once it is read, after which
    /// each click, box and phrase is answered from that reading. Hold it for as long as you select from that picture, and
    /// dispose it when the page closes or moves to another picture.
    /// </summary>
    /// <exception cref="InvalidOperationException">The tool is not ready here, or the picture could not be read.</exception>
    Task<IPictureSelectionSession> OpenAsync(ReadOnlyMemory<byte> rgba, int width, int height, CancellationToken ct);
}

/// <summary>A selection on one picture (<see cref="IPictureSelection.OpenAsync"/>).</summary>
public interface IPictureSelectionSession : IAsyncDisposable
{
    /// <summary>The picture's size, in pixels.</summary>
    int Width { get; }

    int Height { get; }

    /// <summary>The one thing clicks on it and off it, or a box round it, or both, mean — its mask at
    /// <paramref name="maskWidth"/> × <paramref name="maskHeight"/>: the picture's own size for a cut-out, smaller for
    /// showing it.</summary>
    /// <exception cref="InvalidOperationException">The selection has ended, or it could not be made.</exception>
    Task<PictureMask> SelectAsync(IReadOnlyList<PicturePoint> points, PictureBox? box, int maskWidth, int maskHeight, CancellationToken ct);

    /// <summary>Everything a phrase names, helped by <paramref name="examples"/> — with no phrase, everything like the
    /// examples — surest first, each with its mask at <paramref name="maskWidth"/> × <paramref name="maskHeight"/>.</summary>
    /// <exception cref="InvalidOperationException">The selection has ended, or it could not be made.</exception>
    Task<IReadOnlyList<PictureFind>> FindAsync(string phrase, IReadOnlyList<PictureExample> examples, int maskWidth, int maskHeight, CancellationToken ct);
}

/// <summary>What a block of a page is, as the page reader marks it.</summary>
public enum PageBlockKind
{
    /// <summary>A heading, its level unsaid.</summary>
    Title,

    /// <summary>Prose, its maths in LaTeX between <c>\(</c> and <c>\)</c>.</summary>
    Text,

    /// <summary>A formula set apart: LaTeX between <c>\[</c> and <c>\]</c>.</summary>
    Equation,

    /// <summary>A table, as HTML.</summary>
    Table,

    /// <summary>A figure — a picture, a chart, a diagram — whose place on the page is all that is read of it.</summary>
    Figure,

    /// <summary>A figure's or a table's caption.</summary>
    Caption,

    /// <summary>A note at the foot of the page.</summary>
    Footnote,

    /// <summary>What runs along the top or the foot of every page — a running head, a page number — rather than in its text.</summary>
    Margin,
}

/// <summary>One block of a page: what it is, where it stands in fractions of the page's width and height, and what it
/// says (<see cref="PageBlockKind"/>); nothing for a figure.</summary>
public sealed record PageBlock(PageBlockKind Kind, double Left, double Top, double Right, double Bottom, string Content);

/// <summary>Where a picture holds writing — each region of text, in its pixels — and whether it is a page of writing
/// rather than a few words on a sign or a label: what <see cref="IPageReading.ReadPageAsync"/> reads.</summary>
public sealed record TextOnPicture(IReadOnlyList<PictureBox> Regions, bool IsPage);

/// <summary>
/// Pages of writing read by neural networks Nexaflow runs on this machine: whether, and where, a picture holds writing, in
/// tens of milliseconds on the CPU; and a page — prose, columns, tables, formulas, figures — read into its blocks in
/// reading order, its columns read one after another, its maths as LaTeX and its tables as HTML.
/// </summary>
public interface IPageReading : IToolModels
{
    /// <summary>Where a picture — interleaved 8-bit RGBA, row by row — holds writing, and whether it is a page.</summary>
    /// <exception cref="InvalidOperationException">The tool is not ready here, or the picture could not be read.</exception>
    Task<TextOnPicture> FindTextAsync(ReadOnlyMemory<byte> rgba, int width, int height, CancellationToken ct);

    /// <summary>Reads the page in <paramref name="picture"/> — a PNG or JPEG file, at the resolution it was scanned or
    /// photographed at — into its blocks in reading order, each reported to <paramref name="read"/> as it is read.</summary>
    /// <exception cref="InvalidOperationException">The tool is not ready here, or the page could not be read.</exception>
    Task<IReadOnlyList<PageBlock>> ReadPageAsync(string picture, IProgress<PageBlock>? read, CancellationToken ct);
}
