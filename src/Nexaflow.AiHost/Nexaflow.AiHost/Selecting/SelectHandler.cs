using System.Threading.Channels;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;
using Nexaflow.AiHost.Generation;

namespace Nexaflow.AiHost.Selecting;

/// <summary>
/// Picks things out of a picture (<see cref="SelectInPicture"/>): the picture read once as the request arrives, then
/// each click or phrase written to it answered from what was read, until its input ends. A part is taken from the
/// registry for each answer and given back after it, so a page open on a picture holds the GPU only while it is asking;
/// what was read stays in the host's memory for as long as the request does.
/// </summary>
internal sealed class SelectHandler(ModelRegistry models, GpuScheduler gpu) : HostStreamHandler<SelectInPicture>
{
    protected override async Task HandleAsync(SelectInPicture request, Func<HostReply, Task> reply, ChannelReader<RequestInput> input, CancellationToken ct)
    {
        Validate(request);
        var rgba = await File.ReadAllBytesAsync(request.Pixels, ct);
        if (rgba.Length != (long)request.Width * request.Height * 4)
            throw new HostRequestException(HostErrorKind.Invalid, $"{request.Pixels} holds {rgba.Length} bytes, not {request.Width}x{request.Height} RGBA.");

        Task Stage(string stage) => reply(new Progress(request.RequestId, stage, 0, 0));
        var image = PictureSelecting.Prepare(rgba, request.Width, request.Height);
        PictureFeatures features;
        using (var room = models.BorrowRoom(ids => reply(new MadeWay(request.RequestId, ids))))
        {
            var spec = request.Parts[SelectingParts.ImageEncoder];
            using var lease = await models.AcquireAsync(spec, resident: false, Stage, ct, room);
            using (await gpu.NextAsync(request.Priority, spec.Device, ct))
            {
                ct.ThrowIfCancellationRequested();
                features = lease.As<PictureEncoder>().Encode(image);
            }
        }
        await reply(new PictureEncoded(request.RequestId));

        await foreach (var item in HostStreamInput.ReadAsync(input, () => Stage("selecting"), ct))
        {
            switch (item)
            {
                case SelectByPoints points:
                    await reply(await SelectAsync(request, features, points, reply, ct));
                    break;
                case SelectByPhrase phrase:
                    await reply(await FindAsync(request, features, phrase, reply, ct));
                    break;
            }
        }
    }

    private async Task<PointsSelected> SelectAsync(SelectInPicture request, PictureFeatures features, SelectByPoints ask,
                                                   Func<HostReply, Task> reply, CancellationToken ct)
    {
        Validate(ask.Mask);
        if (ask.Points.Count == 0) throw new HostRequestException(HostErrorKind.Invalid, "A selection needs a point.");
        var (points, labels) = PictureSelecting.Prompt(ask.Points, request.Width, request.Height);

        float[] masks, ious;
        float objectScore;
        using (var room = models.BorrowRoom(ids => reply(new MadeWay(request.RequestId, ids))))
        {
            var spec = request.Parts[SelectingParts.ClickDecoder];
            using var lease = await models.AcquireAsync(spec, resident: false, s => reply(new Progress(request.RequestId, s, 0, 0)), ct, room);
            using (await gpu.NextAsync(request.Priority, spec.Device, ct))
                (masks, ious, objectScore) = lease.As<ClickDecoder>().Decode(features, points, labels);
        }

        var mask = new byte[ask.Mask.Width * ask.Mask.Height];
        SelectionBox? bounds = null;
        if (PictureSelecting.Chosen(masks, ious, objectScore, ask.Points.Count) is { } low)
        {
            PictureSelecting.Threshold(PictureSelecting.ClickMaskAt(low, ask.Mask.Width, ask.Mask.Height), mask);
            bounds = PictureSelecting.Bounds(mask, ask.Mask.Width, ask.Mask.Height, request.Width, request.Height);
        }
        await File.WriteAllBytesAsync(ask.Mask.Into, mask, ct);
        return new PointsSelected(request.RequestId, ask.Turn, bounds);
    }

    private async Task<PhraseFound> FindAsync(SelectInPicture request, PictureFeatures features, SelectByPhrase ask,
                                              Func<HostReply, Task> reply, CancellationToken ct)
    {
        Validate(ask.Masks);
        string phrase = ask.Phrase.Trim();
        if (phrase.Length == 0)
        {
            if (ask.Examples.Count == 0) throw new HostRequestException(HostErrorKind.Invalid, "A search needs a phrase or an example.");
            phrase = PictureSelecting.ExamplesOnly;
        }

        float[] logits, boxes, low;
        float presence;
        Func<string, Task> stage = s => reply(new Progress(request.RequestId, s, 0, 0));
        using (var room = models.BorrowRoom(ids => reply(new MadeWay(request.RequestId, ids))))
        {
            var textSpec = request.Parts[SelectingParts.TextEncoder];
            float[] text;
            bool[] padding;
            using (var lease = await models.AcquireAsync(textSpec, resident: false, stage, ct, room))
            using (await gpu.NextAsync(request.Priority, textSpec.Device, ct))
                (text, padding) = lease.As<TextEncoder>().Encode(phrase);

            float[] exampleTokens = [];
            if (ask.Examples.Count > 0)
            {
                var (exampleBoxes, exampleLabels) = PictureSelecting.Examples(ask.Examples, request.Width, request.Height);
                var exemplarSpec = request.Parts[SelectingParts.ExemplarEncoder];
                using var lease = await models.AcquireAsync(exemplarSpec, resident: false, stage, ct, room);
                using (await gpu.NextAsync(request.Priority, exemplarSpec.Device, ct))
                    exampleTokens = lease.As<ExemplarEncoder>().Encode(features, exampleBoxes, exampleLabels);
            }

            var finderSpec = request.Parts[SelectingParts.Finder];
            using (var lease = await models.AcquireAsync(finderSpec, resident: false, stage, ct, room))
            using (await gpu.NextAsync(request.Priority, finderSpec.Device, ct))
                (logits, presence, boxes, low) = lease.As<Finder>().Find(features, text, padding, exampleTokens);
        }

        var kept = PictureSelecting.Kept(logits, presence, boxes, request.Width, request.Height);
        int size = ask.Masks.Width * ask.Masks.Height;
        const int plane = ClickDecoder.Side * ClickDecoder.Side;
        var masks = new byte[(long)size * kept.Count];
        Parallel.For(0, kept.Count, i =>
        {
            var logitsAt = PictureSelecting.FoundMaskAt(low.AsSpan(kept[i].Query * plane, plane).ToArray(), ask.Masks.Width, ask.Masks.Height);
            PictureSelecting.Threshold(logitsAt, masks.AsSpan(i * size, size));
        });
        await File.WriteAllBytesAsync(ask.Masks.Into, masks, ct);
        return new PhraseFound(request.RequestId, ask.Turn, [.. kept.Select(k => k.Found)]);
    }

    private static void Validate(SelectInPicture request)
    {
        foreach (var role in SelectingParts.All)
            if (!request.Parts.ContainsKey(role)) throw new HostRequestException(HostErrorKind.Invalid, $"A selection needs its {role}.");
        if (request.Width <= 0 || request.Height <= 0 || (long)request.Width * request.Height > SelectInPicture.MostPixels)
            throw new HostRequestException(HostErrorKind.Invalid, $"A {request.Width}x{request.Height} picture is not one a selection reads.");
    }

    private static void Validate(MaskTarget target)
    {
        if (target.Width is <= 0 or > SelectInPicture.MostMaskSide || target.Height is <= 0 or > SelectInPicture.MostMaskSide)
            throw new HostRequestException(HostErrorKind.Invalid, $"A mask is not written at {target.Width}x{target.Height}.");
        if (string.IsNullOrWhiteSpace(target.Into)) throw new HostRequestException(HostErrorKind.Invalid, "A mask needs a file to be written to.");
    }
}
