namespace Nexaflow.Features.SystemInfo.Services;

/// <summary>
/// The in-process write behind a User-scope edit — the one change the Environment Variables page makes
/// without the elevation bridge. Machine-scope writes never come here; they go through
/// <see cref="Nexaflow.Features.Common.IShellServices.RunElevatedAsync"/>.
/// </summary>
public interface IUserEnvVarWriter
{
    /// <summary>Sets <paramref name="name"/> in the user's persistent environment; a null <paramref name="value"/> deletes it.</summary>
    void Set(string name, string? value);
}

/// <summary>
/// Writes HKCU\Environment through <see cref="Environment.SetEnvironmentVariable(string, string?, EnvironmentVariableTarget)"/>,
/// which also broadcasts WM_SETTINGCHANGE itself, so running programs hear of the change with no manual notify.
/// The broadcast blocks until every top-level window has answered it.
/// </summary>
public sealed class UserEnvVarWriter : IUserEnvVarWriter
{
    public void Set(string name, string? value) =>
        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.User);
}
