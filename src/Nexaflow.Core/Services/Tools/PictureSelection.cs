using System.IO;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Provisioning;
using Nexaflow.Features.Common;
using Nexaflow.Providers.AiHost;
using Nexaflow.Providers.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Services.Tools;

/// <summary>
/// Picture selection as features and the assistant use it (<see cref="IPictureSelection"/>): SAM 3.1 in the AI host
/// (<see cref="OwnedPictureSelection"/>). A feature hands over pixels and gets masks back.
/// </summary>
public sealed class PictureSelection : LocalToolModels, IPictureSelection
{
    private readonly OwnedPictureSelection _selection;

    public PictureSelection(OwnedPictureSelection selection, IModelAssetStore store, IBackgroundActivityManager? activities) : base(selection, store, activities)
        => _selection = selection;

    public override string Name => Str.Get("Shell.Tools.Selection.Name");

    public override string About => Str.Get("Shell.Tools.Selection.About");

    public async Task<IPictureSelectionSession> OpenAsync(ReadOnlyMemory<byte> rgba, int width, int height, CancellationToken ct)
    {
        if (width <= 0 || height <= 0 || (long)width * height > SelectInPicture.MostPixels || rgba.Length != (long)width * height * 4)
            throw new InvalidOperationException($"A {width}x{height} picture of {rgba.Length} bytes is not one a selection reads.");
        return new Session(await _selection.OpenAsync(rgba, width, height, ct), width, height);
    }

    private sealed class Session(OwnedSelectionSession session, int width, int height) : IPictureSelectionSession
    {
        public int Width => width;

        public int Height => height;

        public async Task<PictureMask> SelectAsync(IReadOnlyList<PicturePoint> points, PictureBox? box, int maskWidth, int maskHeight, CancellationToken ct)
        {
            var asked = new List<SelectionPoint>(points.Count + 2);
            if (box is not null)   // the makers put a box's corners before the clicks
            {
                asked.Add(new SelectionPoint((float)box.Left, (float)box.Top, SelectionPointKind.BoxTopLeft));
                asked.Add(new SelectionPoint((float)box.Right, (float)box.Bottom, SelectionPointKind.BoxBottomRight));
            }
            asked.AddRange(points.Select(p => new SelectionPoint((float)p.X, (float)p.Y, p.Include ? SelectionPointKind.Include : SelectionPointKind.Exclude)));
            if (asked.Count == 0) throw new InvalidOperationException("A selection needs a click or a box.");

            var selected = await session.SelectAsync(asked, new MaskTarget(maskWidth, maskHeight), ct);
            using (selected.Mask)
                return new PictureMask(selected.Mask.ToArray(), maskWidth, maskHeight, Box(selected.Bounds));
        }

        public async Task<IReadOnlyList<PictureFind>> FindAsync(string phrase, IReadOnlyList<PictureExample> examples, int maskWidth, int maskHeight, CancellationToken ct)
        {
            var found = await session.FindAsync(phrase, [.. examples.Select(e => new SelectionExample(
                new SelectionBox((float)e.Box.Left, (float)e.Box.Top, (float)e.Box.Right, (float)e.Box.Bottom), e.Like))],
                new MaskTarget(maskWidth, maskHeight), ct);
            using var masks = found.Masks;
            int size = maskWidth * maskHeight;
            return [.. found.Found.Select((f, i) =>
            {
                var box = Box(f.Box)!;
                return new PictureFind(f.Score, box, new PictureMask(masks.Memory.Slice(i * size, size).ToArray(), maskWidth, maskHeight, box));
            })];
        }

        private static PictureBox? Box(SelectionBox? box) => box is null ? null : new PictureBox(box.Left, box.Top, box.Right, box.Bottom);

        public ValueTask DisposeAsync() => session.DisposeAsync();
    }
}
