using System.IO;
using System.Runtime.InteropServices;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing.Kept;
using Nexaflow.IO.Common;

namespace Nexaflow.Features.Images.Editing.SceneDepth;

/// <summary>
/// The scene depth of photos, kept on disk so each photo's is estimated once: a plane for each photo and set of models,
/// found again by the photo's content whenever an edit needs it — after a restart too — so moving a focus point or a
/// haze's start never runs the model again. Past <see cref="SceneDepthMaps(string, long)"/>'s budget the least recently used
/// go first.
/// </summary>
[LocalDiskOnly("scene depth kept in the app's cache, and handed to scene depth through the temp folder; the photo itself is read through the runtime's file system")]
public sealed class SceneDepthMaps(string folder, long budgetBytes)
{
    /// <summary>How much disk the kept planes may take together before the oldest are let go — a plane is under a megabyte.</summary>
    public const long DefaultBudget = 256L << 20;

    public static string DefaultFolder => KeptFiles.CacheFolder("scene-depth");

    private readonly KeptFiles _kept = new(folder, budgetBytes, ".depth");

    public SceneDepthMaps() : this(DefaultFolder, DefaultBudget) { }

    public string Folder => _kept.Folder;

    /// <summary>Where <paramref name="photo"/>'s scene depth by the models <paramref name="modelsVersion"/> names is kept.</summary>
    public string PathOf(string photo, string modelsVersion) => _kept.PathOf(photo, Made(modelsVersion));

    /// <summary>The kept scene depth of <paramref name="photo"/>, if it is here — marked as just used.</summary>
    public string? Find(string photo, string modelsVersion) => _kept.Find(photo, Made(modelsVersion));

    private static string Made(string modelsVersion) => $"scene-depth|{modelsVersion}";

    /// <summary>
    /// Estimates <paramref name="photo"/>'s scene depth — unless it is kept already — and keeps it: the photo at full size,
    /// upright, handed to scene depth by file, and what it gives spread into nearness (<see cref="SceneDepthPlane"/>).
    /// </summary>
    /// <exception cref="InvalidOperationException">Scene depth is not here, or could not do it.</exception>
    public async Task<string> MakeAsync(ImageEditingServices services, string photo, ISceneDepth depth, CancellationToken ct)
    {
        var path = PathOf(photo, depth.ModelsVersion);
        if (File.Exists(path))
        {
            KeptFiles.Touch(path);
            return path;
        }
        var picture = await Task.Run(() => services.ReadUprightAsync(photo, ct), ct);
        var plane = await EstimateAsync(depth, picture, ct);
        Directory.CreateDirectory(Folder);
        var written = path + ".tmp";
        await Task.Run(() =>
        {
            using var file = File.Create(written);
            plane.Write(file);
        }, ct);
        _kept.Keep(written, path);
        return path;
    }

    /// <summary>The kept scene depth of <paramref name="photo"/>, estimated first where it is not kept yet.</summary>
    /// <exception cref="InvalidOperationException">Scene depth is not here, or could not do it.</exception>
    public async Task<SceneDepthPlane> PlaneAsync(ImageEditingServices services, string photo, ISceneDepth depth, CancellationToken ct)
    {
        var path = await MakeAsync(services, photo, depth, ct);
        return await Task.Run(() => Read(path), ct);
    }

    /// <summary>What scene depth makes of <paramref name="picture"/>, handed over by file.</summary>
    internal static async Task<SceneDepthPlane> EstimateAsync(ISceneDepth depth, UprightPixels picture, CancellationToken ct)
    {
        var work = Directory.CreateTempSubdirectory("nexaflow-scene-depth-").FullName;
        try
        {
            string pixels = Path.Combine(work, "photo.rgba"), into = Path.Combine(work, "disparity.f32");
            await File.WriteAllBytesAsync(pixels, picture.Rgba, ct);
            var estimate = await depth.EstimateAsync(new SceneDepthRequest(pixels, picture.Width, picture.Height, into), ct);
            var bytes = await File.ReadAllBytesAsync(into, ct);
            if (bytes.Length != estimate.Width * estimate.Height * sizeof(float))
                throw new InvalidOperationException($"Scene depth wrote {bytes.Length} bytes, not a {estimate.Width}x{estimate.Height} disparity.");
            return SceneDepthPlane.FromDisparity(MemoryMarshal.Cast<byte, float>(bytes), estimate.Width, estimate.Height);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch (IOException) { /* the temp folder's own clean-up has it */ }
        }
    }

    /// <summary>A kept plane, read back.</summary>
    /// <exception cref="InvalidDataException">The file is not a kept plane.</exception>
    public static SceneDepthPlane Read(string path) => SceneDepthPlane.Read(File.ReadAllBytes(path));
}
