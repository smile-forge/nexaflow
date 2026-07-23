using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Dicom.ViewModels;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Dicom;

/// <summary>
/// The AI surface and PHI policy: <c>get_context</c> and the client tools describe study/geometry only and
/// never leak patient identifiers, the page declares a raised security risk, and the Hide-patient-info
/// toggle masks the tree.
/// </summary>
[TestClass]
[CoversNode("dicom")]
public class DicomViewModelAiTests
{
    private static string SampleDcm => TestSampleData.Path("dicom", "ct.dcm");

    private static DicomViewModel LoadedVm()
    {
        var vm = new DicomViewModel([SampleDcm], Substitute.For<IShellServices>(),
                                    new Dictionary<string, string> { ["path"] = SampleDcm });
        Assert.IsTrue(SpinWait.SpinUntil(() => !vm.IsLoading, 5000), "container did not load in time");
        return vm;
    }

    [TestMethod]
    [CoversNode("ai-integration")]
    public void GetContext_WithholdsPatientIdentifiers()
    {
        var vm = LoadedVm();
        try
        {
            var ctx = vm.GetContext();
            // The sample's PHI: PatientName "Doe^John", PatientID "TEST001".
            Assert.IsFalse(ctx.Contains("John"), "patient name must not appear in AI context");
            Assert.IsFalse(ctx.Contains("Doe"), "patient name must not appear in AI context");
            Assert.IsFalse(ctx.Contains("TEST001"), "patient ID must not appear in AI context");
            Assert.IsTrue(vm.IsContextReady);
        }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    public void SecurityRisk_IsRaised_ForMedicalData()
    {
        var vm = LoadedVm();
        try { Assert.AreEqual(ContextSecurityRisk.Medium, vm.GetContextSecurityRisk()); }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    [CoversNode("ai-integration")]
    public void ClientTools_ExposeDeidentifiedSurface()
    {
        var vm = LoadedVm();
        try
        {
            var names = vm.GetClientTools().Select(t => t.Name).ToList();
            CollectionAssert.Contains(names, "dicom_list_contents");
            CollectionAssert.Contains(names, "dicom_get_current_image_info");
            CollectionAssert.Contains(names, "dicom_capture_image");
            Assert.IsTrue(vm.GetClientTools().All(t => t.Safety == ToolSafety.SafeOperation),
                "read-only viewer tools auto-run");
        }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    [CoversNode("patient-info-toggle")]
    public void HidePatientInfo_MasksPatientNodeInTree()
    {
        var vm = LoadedVm();
        try
        {
            var patient = vm.Container!.Patients[0];
            StringAssert.Contains(patient.Display, "John");

            vm.HidePatientInfo = true;
            Assert.IsFalse(patient.Display.Contains("John"), "toggling hide masks the patient label");
            Assert.IsFalse(vm.PatientOverlayVisible, "the overlay PHI line is hidden too");
        }
        finally { vm.Dispose(); }
    }
}
