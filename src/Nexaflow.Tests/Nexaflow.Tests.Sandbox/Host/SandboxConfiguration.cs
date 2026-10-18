using System.Xml.Linq;

namespace Nexaflow.Tests.Sandbox.Host;

/// <summary>
/// The sandbox a session runs in, as Windows Sandbox configuration XML. The checkout is mapped writable at
/// <see cref="Checkout"/>, so the agent, the app and the journeys run from the build the host made and write their results
/// back into it, exactly as a run on the host would; the host's .NET is mapped read-only where an app looks for it by
/// default, so nothing has to be installed.
/// </summary>
internal sealed record SandboxConfiguration(string HostCheckout, string HostDotnet, int MemoryMB, bool Networking, bool AudioInput)
{
    /// <summary>Where the checkout appears inside the sandbox, which has only a C: drive.</summary>
    public const string Checkout = @"C:\nexaflow";

    /// <summary>Where an app looks for .NET when nothing says otherwise, so the mapped runtime needs no DOTNET_ROOT.</summary>
    public const string Dotnet = @"C:\Program Files\dotnet";

    /// <summary>A path under the host's checkout, as the sandbox sees it.</summary>
    public string InSandbox(string hostPath) => Path.Combine(Checkout, Path.GetRelativePath(HostCheckout, hostPath));

    public string ToXml(string logonCommand) => new XElement("Configuration",
        new XElement("VGpu", "Enable"),
        new XElement("Networking", Networking ? "Default" : "Disable"),
        new XElement("MemoryInMB", MemoryMB),
        // The journeys copy and paste; redirected, that would be the host's clipboard.
        new XElement("ClipboardRedirection", "Disable"),
        new XElement("AudioInput", AudioInput ? "Enable" : "Disable"),
        new XElement("VideoInput", "Disable"),
        new XElement("PrinterRedirection", "Disable"),
        new XElement("MappedFolders",
            MappedFolder(HostCheckout, Checkout, readOnly: false),
            MappedFolder(HostDotnet, Dotnet, readOnly: true)),
        new XElement("LogonCommand", new XElement("Command", logonCommand)))
        .ToString(SaveOptions.DisableFormatting);

    private static XElement MappedFolder(string host, string sandbox, bool readOnly) => new("MappedFolder",
        new XElement("HostFolder", host),
        new XElement("SandboxFolder", sandbox),
        new XElement("ReadOnly", readOnly ? "true" : "false"));
}
