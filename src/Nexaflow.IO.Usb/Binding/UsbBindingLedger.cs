using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexaflow.IO.Usb.Binding;

/// <summary>
/// Every binding Nexaflow has made on this machine, in <c>%ProgramData%\Nexaflow\Devices\usb-bindings.json</c>.
/// Anyone may read it — listing bindings needs no prompt — but only the elevated bridge writes it, and only into a
/// directory owned by Administrators or SYSTEM: a ledger a standard user could plant would let them steer what the
/// bridge "restores".
/// </summary>
public sealed class UsbBindingLedger
{
    readonly bool _bridgeOwned;
    readonly string _mutexName;

    UsbBindingLedger(string path, bool bridgeOwned)
    {
        FilePath = path;
        _bridgeOwned = bridgeOwned;
        // Bridges run in any session, so theirs is machine-wide; creating a Global\ object takes a privilege only an
        // elevated token holds, so a ledger at another path (tests, tools) serialises within its session instead.
        _mutexName = bridgeOwned
            ? @"Global\Nexaflow.UsbBindingLedger"
            : $@"Local\Nexaflow.UsbBindingLedger.{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())))[..16]}";
    }

    public static string MachinePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Nexaflow", "Devices", "usb-bindings.json");

    public string FilePath { get; }

    /// <summary>The machine ledger, for reading.</summary>
    public static UsbBindingLedger Machine() => new(MachinePath, bridgeOwned: false);

    /// <summary>The machine ledger as the elevated bridge uses it: its directory is created admin-only, and refused if anyone else owns it.</summary>
    public static UsbBindingLedger MachineForBridge()
    {
        EnsureAdministratorsOnlyDirectory(Path.GetDirectoryName(MachinePath)!);
        return new(MachinePath, bridgeOwned: true);
    }

    /// <summary>A ledger at an arbitrary path with no ownership checks — tests and tools.</summary>
    public static UsbBindingLedger AtPath(string path) => new(path, bridgeOwned: false);

    public IReadOnlyList<UsbBindingRecord> Read()
    {
        if (_bridgeOwned) RequireTrustedOwner(Path.GetDirectoryName(FilePath)!);
        try
        {
            using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize(stream, UsbBindingJsonContext.Default.ListUsbBindingRecord) ?? [];
        }
        catch (FileNotFoundException) { return []; }
        catch (DirectoryNotFoundException) { return []; }
    }

    /// <summary>The newest record for an interface that is not cleanly restored.</summary>
    public UsbBindingRecord? Active(string interfaceInstanceId) =>
        Read().LastOrDefault(r => r.IsActive && string.Equals(r.InterfaceInstanceId, interfaceInstanceId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Writes one record (insert or replace by id) atomically, serialised across bridge processes.</summary>
    public void Save(UsbBindingRecord record)
    {
        using var mutex = new Mutex(false, _mutexName);
        bool owned = false;
        try
        {
            try { owned = mutex.WaitOne(TimeSpan.FromSeconds(30)); }
            catch (AbandonedMutexException) { owned = true; } // the previous holder died; the file itself was replaced atomically
            if (!owned) throw new TimeoutException("Another process held the USB binding ledger for 30 seconds.");

            var records = Read().ToList();
            records.RemoveAll(r => r.Id == record.Id);
            records.Add(record);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temp = FilePath + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                JsonSerializer.Serialize(stream, records, UsbBindingJsonContext.Default.ListUsbBindingRecord);
            File.Move(temp, FilePath, overwrite: true);
        }
        finally
        {
            if (owned) mutex.ReleaseMutex();
        }
    }

    static void EnsureAdministratorsOnlyDirectory(string directory)
    {
        var info = new DirectoryInfo(directory);
        if (info.Exists)
        {
            RequireTrustedOwner(directory);
            info.SetAccessControl(AdministratorsOnly());
            return;
        }
        info.Parent?.Create();
        info.Create(AdministratorsOnly());
    }

    static void RequireTrustedOwner(string directory)
    {
        if (!Directory.Exists(directory)) return;
        var owner = new DirectoryInfo(directory).GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        if (owner is null || !(owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) || owner.IsWellKnown(WellKnownSidType.LocalSystemSid)))
            throw new UnauthorizedAccessException($"{directory} is owned by {owner?.Value ?? "nobody"}, not Administrators or SYSTEM; the binding ledger there is not trusted.");
    }

    static DirectorySecurity AdministratorsOnly()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        security.SetOwner(administrators);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(administrators, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
        return security;
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(List<UsbBindingRecord>))]
internal sealed partial class UsbBindingJsonContext : JsonSerializerContext;
