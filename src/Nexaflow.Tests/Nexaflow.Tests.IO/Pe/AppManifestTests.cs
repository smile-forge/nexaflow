using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nexaflow.IO.Pe;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Pe;

/// <summary>
/// The manifest decoder. The three settings that actually change how Windows runs a binary — the
/// UAC level, the supported-OS list and the DPI mode — each live in a different namespace, which is
/// why they are asserted individually rather than through one round-trip.
/// </summary>
[TestClass]
[CoversNode("functionality-2")]
public sealed class AppManifestTests
{
    private const string FullManifest = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0">
          <assemblyIdentity type="win32" name="Contoso.Tool" version="2.3.0.0"
                            processorArchitecture="amd64" publicKeyToken="0123456789abcdef"/>
          <description>A test manifest</description>
          <trustInfo xmlns="urn:schemas-microsoft-com:asm.v3">
            <security>
              <requestedPrivileges>
                <requestedExecutionLevel level="requireAdministrator" uiAccess="false"/>
              </requestedPrivileges>
            </security>
          </trustInfo>
          <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
            <application>
              <supportedOS Id="{35138b9a-5d96-4fbd-8e2d-a2440225f93a}"/>
              <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}"/>
            </application>
          </compatibility>
          <application xmlns="urn:schemas-microsoft-com:asm.v3">
            <windowsSettings>
              <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">permonitorv2</dpiAwareness>
              <longPathAware xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">true</longPathAware>
              <activeCodePage xmlns="http://schemas.microsoft.com/SMI/2019/WindowsSettings">UTF-8</activeCodePage>
              <heapType xmlns="http://schemas.microsoft.com/SMI/2020/WindowsSettings">SegmentHeap</heapType>
            </windowsSettings>
          </application>
          <dependency>
            <dependentAssembly>
              <assemblyIdentity type="win32" name="Microsoft.Windows.Common-Controls"
                                version="6.0.0.0" processorArchitecture="amd64"
                                publicKeyToken="6595b64144ccf1df" language="*"/>
            </dependentAssembly>
          </dependency>
        </assembly>
        """;

    [TestMethod, TestCategory("Unit")]
    public void Identity_is_decoded()
    {
        var manifest = AppManifest.Parse(FullManifest);

        Assert.IsFalse(manifest.IsEmpty);
        Assert.AreEqual("Contoso.Tool", manifest.AssemblyName);
        Assert.AreEqual("2.3.0.0", manifest.AssemblyVersion);
        Assert.AreEqual("amd64", manifest.ProcessorArchitecture);
        Assert.AreEqual("A test manifest", manifest.Description);
    }

    [TestMethod, TestCategory("Unit")]
    public void The_requested_execution_level_is_decoded()
    {
        var manifest = AppManifest.Parse(FullManifest);

        Assert.AreEqual(PeExecutionLevel.RequireAdministrator, manifest.ExecutionLevel);
        Assert.IsTrue(manifest.RequiresElevation);
        Assert.IsFalse(manifest.UiAccess);
    }

    [TestMethod, TestCategory("Unit")]
    public void An_absent_trust_block_is_unspecified_rather_than_asInvoker()
    {
        // The difference matters: no block means installer detection and virtualisation apply,
        // which is not the same as explicitly asking to run as the invoking user.
        var manifest = AppManifest.Parse("""
            <assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0"/>
            """);

        Assert.AreEqual(PeExecutionLevel.Unspecified, manifest.ExecutionLevel);
        Assert.IsFalse(manifest.RequiresElevation);
    }

    [TestMethod, TestCategory("Unit")]
    public void Supported_os_guids_map_to_names()
    {
        var manifest = AppManifest.Parse(FullManifest);

        Assert.AreEqual(2, manifest.SupportedOs.Count);
        CollectionAssert.AreEquivalent(
            new[] { "Windows 7", "Windows 10 / 11" },
            manifest.SupportedOs.Select(o => o.Name).ToArray());
        Assert.IsFalse(manifest.RunsUnderCompatibilityShims, "Windows 10/11 is declared.");
    }

    [TestMethod, TestCategory("Unit")]
    public void Declaring_no_windows_10_support_means_shims_apply()
    {
        var manifest = AppManifest.Parse("""
            <assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0">
              <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1"><application>
                <supportedOS Id="{1f676c76-80e1-4239-95bb-83d0f6d0da78}"/>
              </application></compatibility>
            </assembly>
            """);

        Assert.AreEqual("Windows 8.1", manifest.SupportedOs.Single().Name);
        Assert.IsTrue(manifest.RunsUnderCompatibilityShims);
    }

    [TestMethod, TestCategory("Unit")]
    public void Windows_settings_are_decoded_and_the_unmodelled_ones_kept()
    {
        var manifest = AppManifest.Parse(FullManifest);

        Assert.AreEqual(PeDpiAwareness.PerMonitorV2, manifest.DpiAwareness);
        Assert.IsTrue(manifest.LongPathAware);
        Assert.AreEqual("UTF-8", manifest.ActiveCodePage);

        // Nothing is silently dropped, so a setting introduced after this decoder still shows.
        Assert.IsTrue(manifest.WindowsSettings.ContainsKey("heapType"));
        Assert.AreEqual("SegmentHeap", manifest.WindowsSettings["heapType"]);
    }

    [TestMethod, TestCategory("Unit")]
    public void The_modern_dpi_element_wins_over_the_legacy_boolean()
    {
        var manifest = AppManifest.Parse("""
            <assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0">
              <application xmlns="urn:schemas-microsoft-com:asm.v3"><windowsSettings>
                <dpiAware>true</dpiAware>
                <dpiAwareness>permonitorv2,permonitor</dpiAwareness>
              </windowsSettings></application>
            </assembly>
            """);

        Assert.AreEqual(PeDpiAwareness.PerMonitorV2, manifest.DpiAwareness);
    }

    [TestMethod, TestCategory("Unit")]
    public void Dependent_assemblies_are_listed()
    {
        var manifest = AppManifest.Parse(FullManifest);

        var dependency = manifest.Dependencies.Single();
        Assert.AreEqual("Microsoft.Windows.Common-Controls", dependency.Name);
        Assert.AreEqual("6.0.0.0", dependency.Version);
        Assert.AreEqual("6595b64144ccf1df", dependency.PublicKeyToken);
    }

    [TestMethod, TestCategory("Unit")]
    public void Malformed_xml_keeps_the_source_and_reports_why()
    {
        // Being able to look at a broken manifest is exactly when you most want to.
        var manifest = AppManifest.Parse("<assembly><unclosed>");

        Assert.IsFalse(manifest.IsEmpty);
        Assert.IsNotNull(manifest.ParseError);
        Assert.AreEqual("<assembly><unclosed>", manifest.RawXml);
    }

    [TestMethod, TestCategory("Unit")]
    public void Empty_input_yields_the_empty_manifest()
    {
        Assert.IsTrue(AppManifest.Parse(null).IsEmpty);
        Assert.IsTrue(AppManifest.Parse("   ").IsEmpty);
    }

    [TestMethod, TestCategory("Unit")]
    public void The_embedded_manifest_of_a_real_binary_decodes()
    {
        using var image = PeReader.Read(PeFixtures.Notepad);

        var manifest = image.Manifest;
        Assert.IsFalse(manifest.IsEmpty, "notepad.exe embeds an RT_MANIFEST.");
        Assert.IsFalse(manifest.IsExternal);
        Assert.AreNotEqual(PeDpiAwareness.Unspecified, manifest.DpiAwareness);
        Assert.IsNull(manifest.ParseError);
    }
}
