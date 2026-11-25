namespace Nexaflow.Features.Common;

/// <summary>
/// Implemented by a config's own Options page (a view marked as drawing the config) that needs the
/// shell — typically for the themed file/folder pickers. The Options / Configure host calls
/// <see cref="AttachShell"/> once, right after constructing the page (which is handed only its config), so the
/// control never reaches for <c>Application.Current</c> or a <c>Microsoft.Win32</c> dialog.
/// </summary>
public interface IShellAware
{
    void AttachShell(IShellServices shell);
}
