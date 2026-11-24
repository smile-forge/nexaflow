using Nexaflow.AiHost;

namespace Nexaflow.Tests.AiHost;

/// <summary>Clients of the host for a test to ask as: apps run by one account.</summary>
internal static class TestClients
{
    public static readonly ClientIdentity Notes = new("S-1-5-21-1000", "notes");
    public static readonly ClientIdentity Mail = new("S-1-5-21-1000", "mail");
    public static readonly ClientIdentity Paint = new("S-1-5-21-1000", "paint");

    /// <summary>What <paramref name="ask"/> starts, asked as <paramref name="client"/>; the caller's own flow goes on serving
    /// whom it served.</summary>
    public static async Task<T> As<T>(ClientIdentity client, Func<Task<T>> ask)
    {
        client.Enter();
        return await ask();
    }
}
