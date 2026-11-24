using System.Security.AccessControl;
using System.Security.Principal;
using Nexaflow.AiHost.Store;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// Who may do what in the model store: with the service, the service writes and every user reads; without it, every user has full
/// control. Either way the store's rules are its own and everything in it inherits them, keeping none of its own.
/// </summary>
[TestClass]
[CoversNode("ai-host")]
public class StoreAccessTests
{
    private static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);

    /// <summary>A store with a model in it that carries a rule of its own, as one moved in from a user's folder does.</summary>
    private static (DirectoryInfo Store, FileInfo Model) StoreWithAModelOfItsOwn()
    {
        var store = TestTemp.CreateSubdirectory("nexaflow-store-access-");
        var folder = store.CreateSubdirectory("gemma");
        var model = new FileInfo(Path.Combine(folder.FullName, "model.gguf"));
        File.WriteAllBytes(model.FullName, [1, 2, 3]);
        var own = model.GetAccessControl();
        own.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null), FileSystemRights.Read, AccessControlType.Allow));
        model.SetAccessControl(own);
        return (store, model);
    }

    private static IReadOnlyList<FileSystemAccessRule> Explicit(FileSystemSecurity security)
        => [.. security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()];

    private static FileSystemRights RightsOf(FileSystemSecurity security, SecurityIdentifier who)
        => security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
                   .Where(r => r.IdentityReference == who && r.AccessControlType == AccessControlType.Allow)
                   .Aggregate((FileSystemRights)0, (all, r) => all | r.FileSystemRights);

    [TestMethod]
    public void WithoutTheService_EveryUserHasFullControl_OfEverythingInTheStore()
    {
        var (store, model) = StoreWithAModelOfItsOwn();

        StoreAccess.ForUsers(store.FullName, takeOwnership: false);

        var root = store.GetAccessControl();
        Assert.IsTrue(root.AreAccessRulesProtected, "the store's rules are its own, not ProgramData's");
        Assert.AreEqual(FileSystemRights.FullControl, RightsOf(root, Users) & FileSystemRights.FullControl);
        var file = model.GetAccessControl();
        Assert.AreEqual(0, Explicit(file).Count, "a model keeps no rule of its own");
        Assert.AreEqual(FileSystemRights.FullControl, RightsOf(file, Users) & FileSystemRights.FullControl);
    }

    [TestMethod]
    public void WithTheService_TheServiceWrites_AndEveryUserReads()
    {
        var (store, model) = StoreWithAModelOfItsOwn();
        var service = new SecurityIdentifier(WellKnownSidType.NetworkServiceSid, null);

        StoreAccess.ForService(store.FullName, service, takeOwnership: false);

        var file = model.GetAccessControl();
        Assert.AreEqual(0, Explicit(file).Count, "a model keeps no rule of its own");
        Assert.AreEqual(FileSystemRights.FullControl, RightsOf(file, service) & FileSystemRights.FullControl);
        var users = RightsOf(file, Users);
        Assert.AreEqual(FileSystemRights.ReadAndExecute, users & FileSystemRights.ReadAndExecute, "every user reads");
        Assert.AreEqual((FileSystemRights)0, users & (FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.Delete), "and none writes");
    }
}
