using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexaflow.Spikes.UsbDriverBinding.Binding;

public enum BindingState { BindIntent, Bound, BindFailed, RestoreIntent, Restored, RestoreFailed }

/// <summary>The spec's BindingRecord plus what Strategy A changes besides the driver: the class and the interface GUID value.</summary>
public sealed record BindingRecord(
    Guid Id, string InterfaceInstanceId, string HardwareId, string? OriginalService, string? OriginalInfName, string? OriginalInfSection,
    string? OriginalClassGuid, string? OriginalMatchingId, string Strategy, string? PublishedInfName, string? CertificateThumbprint,
    string AddedInterfaceGuid, DateTimeOffset BoundAt)
{
    public BindingState State { get; set; }
    public bool ClassChanged { get; set; }
    public List<string> Log { get; init; } = [];
}

/// <summary>Written before every system change and after it, so a crash between the two is recoverable.</summary>
public sealed class BindingLedger(string path)
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Nexaflow", "Devices", "usb-bindings.spike.json");

    public string FilePath => path;

    public List<BindingRecord> Read() =>
        File.Exists(path) ? JsonSerializer.Deserialize<List<BindingRecord>>(File.ReadAllText(path), Options) ?? [] : [];

    public void Save(BindingRecord record)
    {
        var all = Read();
        all.RemoveAll(r => r.Id == record.Id);
        all.Add(record);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(all, Options));
        File.Move(temp, path, overwrite: true);
    }

    public BindingRecord? Active(string instanceId) =>
        Read().LastOrDefault(r => string.Equals(r.InterfaceInstanceId, instanceId, StringComparison.OrdinalIgnoreCase) && r.State is not BindingState.Restored);
}
