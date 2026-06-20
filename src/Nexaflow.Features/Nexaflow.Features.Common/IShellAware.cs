namespace Nexaflow.Features.Common;

/// <summary>
/// Implemented by a custom config-editor control (see <see cref="CustomControlAttribute"/>) that needs the
/// shell — typically for the themed file/folder pickers. The Options / Configure host calls
/// <see cref="AttachShell"/> once, right after constructing the control (which is parameterless), so the
/// control never reaches for <c>Application.Current</c> or a <c>Microsoft.Win32</c> dialog.
/// </summary>
public interface IShellAware
{
    void AttachShell(IShellServices shell);
}
