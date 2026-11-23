using System.IO;

namespace Nexaflow.Tests.UIJourneys.Infrastructure;

/// <summary>
/// A folder of Nexaflow's own models a journey runs on (<c>NEXAFLOW_TEST_AI_DIR</c>), checked before it starts for what the
/// journey cannot do without. A journey's app downloads nothing, so a part missing from the folder would show only as a
/// picture that never arrives; absent, the journey is inconclusive, naming the part — it says nothing about the app.
/// </summary>
public static class OwnedModelFolder
{
    /// <summary>The picture safety check as the app keeps it: OwenElliott's image safety classifier, M.</summary>
    private const string PictureCheck = @"tools\image-safety-classifier-m\image-safety-classifier-m.onnx";

    /// <summary>
    /// Inconclusive unless <paramref name="aiDir"/> holds the picture safety check, which every picture made passes before it is
    /// saved or shown while Enhanced AI Protection Mode is on — as it is in a journey's app. Held as the app keeps it: the file,
    /// and the <c>.verified</c> mark the store writes once its hash is checked, without which the store fetches it again.
    /// </summary>
    public static void RequirePictureCheck(string aiDir, string variable)
    {
        var classifier = Path.Combine(aiDir, PictureCheck);
        if (!File.Exists(classifier) || !File.Exists(classifier + ".verified"))
            Assert.Inconclusive($"{variable} holds no picture safety check ({PictureCheck}): every picture made passes it, and a journey's "
                              + "app downloads nothing. The app fetches it into its own store on the first picture made there.");
    }

    /// <summary>
    /// Inconclusive unless <paramref name="aiDir"/> holds every file the app's catalogue lists for <paramref name="modelId"/>, each with
    /// its <c>.verified</c> mark. A model kept from an older build lacks the files a newer one lists, and the app then offers none of
    /// what the model does — a journey about that model would find controls missing and wait out every one of them. The catalogue is
    /// the one the checkout ships, read as a file: the folder is as complete as the build that is driven needs it to be. The files of
    /// the LoRAs named in <paramref name="loras"/> are required with the model's own.
    /// </summary>
    public static void RequireModel(string aiDir, string modelId, string variable, params string[] loras)
    {
        var catalogue = Path.Combine(Nexaflow.Tests.Fixtures.TestSampleData.RepoRoot, "src", "Nexaflow.Providers", "Nexaflow.Providers.AiHost", "owned-models.json");
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(catalogue));
        var model = document.RootElement.GetProperty("models").EnumerateArray()
                            .FirstOrDefault(m => m.GetProperty("id").GetString() == modelId);
        if (model.ValueKind != System.Text.Json.JsonValueKind.Object)
            throw new InvalidOperationException($"The catalogue lists no model '{modelId}'.");

        // The model's own files, and those of each LoRA the journey draws with: a LoRA is listed beside the model, not among its files.
        var wanted = model.GetProperty("files").EnumerateArray().Concat(
            model.TryGetProperty("loras", out var all)
                ? all.EnumerateArray().Where(lora => loras.Contains(lora.GetProperty("id").GetString())).SelectMany(lora => lora.GetProperty("files").EnumerateArray())
                : []);
        var missing = wanted
                           .Select(file => file.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar))
                           .Where(path => !File.Exists(Path.Combine(aiDir, path)) || !File.Exists(Path.Combine(aiDir, path) + ".verified"))
                           .ToList();
        if (missing.Count > 0)
            Assert.Inconclusive($"{variable} holds {modelId} as an older build kept it: {missing.Count} file(s) this build's catalogue lists are not there or "
                              + $"not verified (first: {missing[0]}). A journey's app downloads nothing; the app fetches them into its own store when a picture is made with it.");
    }
}
