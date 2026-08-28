namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// Runs a body on a fresh STA thread and rethrows whatever it threw. WPF elements can only be constructed
/// and driven from an STA thread, so every UI-category test that builds a control goes through here.
/// <para>Lives in Tests.Fixtures because three suites now need it — Visuals renders off-screen, Core
/// builds theme dictionaries, and neither can reference the other. One copy, like <see cref="AsyncPumpCore"/>.</para>
/// </summary>
public static class UiThread
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
        // Rethrow with the STA thread's own stack intact — a bare `throw caught` resets it to this
        // line, which leaves a failure inside the body with nothing to point at.
        if (caught is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(caught).Throw();
    }
}
