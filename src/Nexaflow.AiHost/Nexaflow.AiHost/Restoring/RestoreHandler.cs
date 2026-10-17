using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Generation;
using Nexaflow.AiHost.Models;
using Nexaflow.IO.Common.Imaging;

namespace Nexaflow.AiHost.Restoring;

/// <summary>
/// Restores a picture as Real-ESRGAN and GFPGAN restore one: the whole picture through the upscaler a tile at a time
/// (<see cref="PictureUpscaling"/>), then — where faces are asked for — each face found by RetinaFace, aligned to its
/// template, remade by GFPGAN and pasted back through the parser's mask (<see cref="FaceAlignment"/>). Each part is held
/// only while it is needed, in the room the picture borrowed, and each call takes the GPU at the request's priority, so a
/// reply or speech gets in between; every tile and face is reported.
/// </summary>
internal sealed class RestoreHandler(ModelRegistry models, GpuScheduler gpu) : HostRequestHandler<RestorePicture>
{
    /// <summary>The longest side a picture is searched for faces at: larger ones are searched at this size, which still
    /// finds every face GFPGAN can remake, without a whole large photo through the detector's first layers.</summary>
    public const int DetectionSide = 2048;

    protected override async Task HandleAsync(RestorePicture request, Func<HostReply, Task> reply, CancellationToken ct)
    {
        Validate(request);
        var rgba = await File.ReadAllBytesAsync(request.Pixels, ct);
        if (rgba.Length != request.Width * request.Height * 4)
            throw new HostRequestException(HostErrorKind.Invalid, $"{request.Pixels} holds {rgba.Length} bytes, not {request.Width}x{request.Height} RGBA.");

        var lane = gpu.Open(request.Priority);
        using var room = models.BorrowRoom(ids => reply(new MadeWay(request.RequestId, ids)));
        Func<string, Task> stage = s => reply(new Progress(request.RequestId, s, 0, 0));

        var upscalerSpec = request.Parts[RestoringParts.UpscalerFor(request.Scale)];
        byte[] restored;
        using (var lease = await models.AcquireAsync(upscalerSpec, resident: false, stage, ct, room))
        {
            var upscaler = lease.As<PictureUpscaler>();
            restored = await PictureUpscaling.UpscaleAsync(rgba, request.Width, request.Height, upscaler.Factor, request.Scale,
                async (tile, width, height, last) =>
                {
                    using (await lane.NextAsync(upscalerSpec.Device, ct))
                    {
                        ct.ThrowIfCancellationRequested();
                        return upscaler.Upscale(tile, width, height, last);
                    }
                },
                (done, total) => reply(new Progress(request.RequestId, RestoringStages.Upscaling, done, total)));
        }

        int faces = request.Faces ? await RepairFacesAsync(request, rgba, restored, lane, stage, room, reply, ct) : 0;
        await File.WriteAllBytesAsync(request.Into, restored, ct);
        await reply(new PictureRestored(request.RequestId, request.Width * request.Scale, request.Height * request.Scale, faces));
    }

    private static void Validate(RestorePicture request)
    {
        if (request.Scale is not (1 or 2 or 4))
            throw new HostRequestException(HostErrorKind.Invalid, $"A picture is restored at 1, 2 or 4 times its size, not {request.Scale}.");
        if (request.Width <= 0 || request.Height <= 0)
            throw new HostRequestException(HostErrorKind.Invalid, "A picture to restore has a size.");
        if (!RestorePicture.Restores(request.Width, request.Height, request.Scale))
            throw new HostRequestException(HostErrorKind.Invalid, $"A {request.Width}x{request.Height} picture is too large to restore at {request.Scale}x.");
        if (string.IsNullOrWhiteSpace(request.Into))
            throw new HostRequestException(HostErrorKind.Invalid, "A restoration needs a file to write the picture to.");
        if (!File.Exists(request.Pixels))
            throw new HostRequestException(HostErrorKind.NotFound, $"The picture to restore is not at {request.Pixels}.");
        foreach (var role in RestoringParts.Of(request.Scale, request.Faces))
            if (!request.Parts.ContainsKey(role))
                throw new HostRequestException(HostErrorKind.Invalid, $"Restoring at {request.Scale}x{(request.Faces ? " with faces" : "")} needs its {role}.");
    }

    /// <summary>Finds the faces in <paramref name="rgba"/>, remakes each and pastes it over <paramref name="restored"/>;
    /// how many were remade.</summary>
    private async Task<int> RepairFacesAsync(RestorePicture request, byte[] rgba, byte[] restored, GpuScheduler.Lane lane, Func<string, Task> stage,
                                             ModelRegistry.Room room, Func<HostReply, Task> reply, CancellationToken ct)
    {
        int width = request.Width, height = request.Height, scale = request.Scale;
        var faces = await FindFacesAsync(request, rgba, lane, stage, room, ct);
        if (faces.Count == 0) return 0;

        var rgb = Rgb(rgba);
        var alignments = faces.Select(FaceAlignment.Alignment).ToList();
        var remade = new List<byte[]>(faces.Count);
        var restorerSpec = request.Parts[RestoringParts.FaceRestorer];
        using (var lease = await models.AcquireAsync(restorerSpec, resident: false, stage, ct, room))
        {
            var restorer = lease.As<FaceRestorer>();
            for (int i = 0; i < faces.Count; i++)
            {
                var crop = PictureResampling.WarpAffine(rgb, width, height, 3, alignments[i], (0, 0, FaceRestorer.Size, FaceRestorer.Size),
                                                        FaceAlignment.CropBorder);
                float[] face;
                using (await lane.NextAsync(restorerSpec.Device, ct))
                {
                    ct.ThrowIfCancellationRequested();
                    face = restorer.Restore(Planar(crop), lastCall: i == faces.Count - 1);
                }
                remade.Add(Interleaved(face));
            }
        }

        var parserSpec = request.Parts[RestoringParts.FaceParser];
        using (var lease = await models.AcquireAsync(parserSpec, resident: false, stage, ct, room))
        {
            var parser = lease.As<FaceParser>();
            for (int i = 0; i < faces.Count; i++)
            {
                float[] parsed;
                using (await lane.NextAsync(parserSpec.Device, ct))
                {
                    ct.ThrowIfCancellationRequested();
                    parsed = parser.Mask(Planar(remade[i]), lastCall: i == faces.Count - 1);
                }
                FaceAlignment.Paste(restored, width * scale, height * scale, remade[i], FaceAlignment.SoftMask(parsed),
                                    FaceAlignment.PasteBack(alignments[i], scale));
                await reply(new Progress(request.RequestId, RestoringStages.Faces, i + 1, faces.Count));
            }
        }
        return faces.Count;
    }

    /// <summary>The faces GFPGAN can remake: found by the detector — on the picture brought within
    /// <see cref="DetectionSide"/> if it is larger — their eyes far enough apart.</summary>
    private async Task<IReadOnlyList<FoundFace>> FindFacesAsync(RestorePicture request, byte[] rgba, GpuScheduler.Lane lane, Func<string, Task> stage,
                                                                 ModelRegistry.Room room, CancellationToken ct)
    {
        int width = request.Width, height = request.Height;
        double shrink = Math.Max(1, (double)Math.Max(width, height) / DetectionSide);
        int dw = (int)(width / shrink), dh = (int)(height / shrink);
        var searched = shrink > 1 ? PictureResampling.ResizeLanczos(rgba, width, height, 4, dw, dh) : rgba;

        var spec = request.Parts[RestoringParts.FaceDetector];
        IReadOnlyList<FoundFace> found;
        using (var lease = await models.AcquireAsync(spec, resident: false, stage, ct, room))
        using (await lane.NextAsync(spec.Device, ct))
        {
            ct.ThrowIfCancellationRequested();
            var (boxes, scores, landmarks) = lease.As<FaceDetector>().Detect(PictureUpscaling.Planar(searched, dw, dh), dw, dh);
            found = RetinaFaceDecoding.Decode(boxes, scores, landmarks, dw, dh);
        }
        return [.. found
            .Select(f => shrink == 1 ? f : f with
            {
                Left = (float)(f.Left * shrink), Top = (float)(f.Top * shrink), Right = (float)(f.Right * shrink), Bottom = (float)(f.Bottom * shrink),
                Points = [.. f.Points.Select(p => (p.X * shrink, p.Y * shrink))],
            })
            .Where(f => Math.Sqrt(Math.Pow(f.Points[0].X - f.Points[1].X, 2) + Math.Pow(f.Points[0].Y - f.Points[1].Y, 2)) >= FaceAlignment.MinimumEyeDistance)];
    }

    private static byte[] Rgb(byte[] rgba)
    {
        var rgb = new byte[rgba.Length / 4 * 3];
        for (int i = 0, j = 0; i < rgba.Length; i += 4, j += 3) (rgb[j], rgb[j + 1], rgb[j + 2]) = (rgba[i], rgba[i + 1], rgba[i + 2]);
        return rgb;
    }

    /// <summary>A 512-pixel interleaved RGB face as planar values from 0 to 1.</summary>
    private static float[] Planar(byte[] rgb)
    {
        int plane = rgb.Length / 3;
        var planar = new float[rgb.Length];
        for (int i = 0; i < plane; i++)
            for (int c = 0; c < 3; c++) planar[c * plane + i] = rgb[i * 3 + c] / 255f;
        return planar;
    }

    /// <summary>A remade face, planar from 0 to 1, as interleaved 8-bit RGB rounded as NumPy rounds.</summary>
    private static byte[] Interleaved(float[] planar)
    {
        int plane = planar.Length / 3;
        var rgb = new byte[planar.Length];
        for (int i = 0; i < plane; i++)
            for (int c = 0; c < 3; c++) rgb[i * 3 + c] = (byte)Math.Clamp(MathF.Round(planar[c * plane + i] * 255f, MidpointRounding.ToEven), 0, 255);
        return rgb;
    }
}
