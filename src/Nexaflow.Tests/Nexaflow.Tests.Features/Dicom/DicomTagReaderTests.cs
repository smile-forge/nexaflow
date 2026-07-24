using System.Linq;
using Nexaflow.Features.Dicom.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Dicom;

/// <summary>The DICOM tag drawer's data source: flattens a dataset to (tag, name, value) rows, and masks
/// identifying tag values when patient info is hidden.</summary>
[TestClass]
[CoversNode("tag-drawer")]
public class DicomTagReaderTests
{
    private static string SampleDcm => TestSampleData.Path("dicom", "ct.dcm");

    [TestMethod]
    public void Read_ListsTags_WithNameAndValue()
    {
        var tags = DicomTagReader.Read(SampleDcm, hidePatient: false);

        Assert.IsTrue(tags.Count > 5, "a real instance has many tags");
        Assert.IsTrue(tags.Any(t => t.Name == "Modality" && t.Value.Contains("OT")), "non-identifying tags are shown");
        Assert.IsTrue(tags.Any(t => t.Tag == "(0010,0010)" && t.Value.Contains("Doe")), "patient name is shown when not hidden");
    }

    [TestMethod]
    public void Read_HidePatient_MasksIdentifyingValues_KeepsTheRest()
    {
        var tags = DicomTagReader.Read(SampleDcm, hidePatient: true);

        var name = tags.First(t => t.Tag == "(0010,0010)");
        Assert.IsFalse(name.Value.Contains("Doe"), "patient name value must be masked");
        StringAssert.Contains(name.Value, "hidden");
        Assert.IsTrue(tags.Any(t => t.Name == "Modality" && t.Value.Contains("OT")), "non-identifying tags stay visible");
    }
}
