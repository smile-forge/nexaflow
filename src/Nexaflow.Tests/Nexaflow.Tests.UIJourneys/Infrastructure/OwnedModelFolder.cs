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
}
