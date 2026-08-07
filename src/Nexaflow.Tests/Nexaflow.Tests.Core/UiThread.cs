namespace Nexaflow.Tests.Core;

/// <summary>
/// Runs a body on a fresh STA thread and rethrows whatever it threw. WPF elements can only be constructed
/// and driven from an STA thread, so every UI-category test that builds a control goes through here.
/// <para>Lives in the root test namespace rather than beside any one suite: C# name lookup walks up
/// enclosing namespaces, so <c>Nexaflow.Tests.Core.Visuals.*</c> still sees it unqualified.</para>
/// </summary>
internal static class UiThread
{
    public static void Run(Action action)
    {
        Exception? caught = null;
        var thread = new System.Threading.Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { caught = ex; }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (caught is not null) throw caught;
    }
}
