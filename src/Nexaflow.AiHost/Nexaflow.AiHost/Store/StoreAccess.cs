using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Nexaflow.AiHost.Store;

/// <summary>
/// Who may do what in the machine's model store and in the host's own folder. Where the AI host service is installed, they are the
/// service's: it alone writes, SYSTEM and Administrators keep full control, and every user reads — so a host an app starts for
/// itself, while the service is stopped or speaks another protocol, still runs the models there. Where it is not, every user has
/// full control of the store, so whichever user's app starts a host, that host can fetch, adopt and remove. Either way the folder's
/// rules are its own, inherited by everything beneath it and by nothing above: a file moved in from a user's folder, or made by a
/// user while the store was theirs, keeps no rule of its own.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class StoreAccess
{
    private const InheritanceFlags Everything = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

    private static readonly SecurityIdentifier System         = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier Users          = new(WellKnownSidType.BuiltinUsersSid, null);

    /// <summary>Gives <paramref name="folder"/> to the service <paramref name="service"/>, every user reading.</summary>
    /// <param name="takeOwnership">Make Administrators the owner of it and of everything in it — as the installer does, running as
    /// SYSTEM — so no user who made a file there keeps the right to change its rules.</param>
    public static void ForService(string folder, SecurityIdentifier service, bool takeOwnership)
        => Apply(folder, [Rule(System, FileSystemRights.FullControl), Rule(Administrators, FileSystemRights.FullControl),
                          Rule(service, FileSystemRights.FullControl), Rule(Users, FileSystemRights.ReadAndExecute)], takeOwnership);

    /// <summary>Gives <paramref name="folder"/> to every user, in full control.</summary>
    /// <param name="takeOwnership">As for <see cref="ForService"/>.</param>
    public static void ForUsers(string folder, bool takeOwnership)
        => Apply(folder, [Rule(System, FileSystemRights.FullControl), Rule(Administrators, FileSystemRights.FullControl),
                          Rule(Users, FileSystemRights.FullControl)], takeOwnership);

    private static FileSystemAccessRule Rule(SecurityIdentifier who, FileSystemRights rights)
        => new(who, rights, Everything, PropagationFlags.None, AccessControlType.Allow);

    private static void Apply(string path, IReadOnlyList<FileSystemAccessRule> rules, bool takeOwnership)
    {
        var folder = Directory.CreateDirectory(path);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var rule in rules) security.AddAccessRule(rule);
        if (takeOwnership) security.SetOwner(Administrators);
        folder.SetAccessControl(security);
        foreach (var entry in folder.EnumerateFileSystemInfos("*", SearchOption.AllDirectories)) Inherit(entry, takeOwnership);
    }

    /// <summary>Leaves <paramref name="entry"/> the rules it inherits, and none of its own.</summary>
    private static void Inherit(FileSystemInfo entry, bool takeOwnership)
    {
        switch (entry)
        {
            case DirectoryInfo directory:
                var folder = new DirectorySecurity();
                folder.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);
                if (takeOwnership) folder.SetOwner(Administrators);
                directory.SetAccessControl(folder);
                break;
            case FileInfo file:
                var security = new FileSecurity();
                security.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);
                if (takeOwnership) security.SetOwner(Administrators);
                file.SetAccessControl(security);
                break;
        }
    }
}
