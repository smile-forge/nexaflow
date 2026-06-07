using System.Diagnostics;
using Nexaflow.Elevation.Contracts;

namespace Nexaflow.PrivilegeBridge.Operations;

/// <summary>Imports a <c>.reg</c> file via <c>reg.exe import</c> while elevated, so writes into protected
/// hives succeed.</summary>
internal sealed class RegImportOperation : IElevatedOperation
{
    public string Id => ElevatedOps.RegImport;

    public ElevatedOperationResult Execute(IReadOnlyDictionary<string, string> args)
    {
        var file = args.GetValueOrDefault(ElevatedArgs.RegFile);
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
            return ElevatedOperationResult.Fail(Id, ElevatedErrorKind.OperationFailed,
                $"Import file not found: '{file}'.");

        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo("reg.exe", $"import \"{file}\"")
                {
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    RedirectStandardError  = true,
                    RedirectStandardOutput = true,
                },
            };
            proc.Start();
            var stderr = proc.StandardError.ReadToEnd();
            proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();

            return proc.ExitCode == 0
                ? ElevatedOperationResult.Ok(Id, $"Imported '{Path.GetFileName(file)}'.")
                : ElevatedOperationResult.Fail(Id, ElevatedErrorKind.OperationFailed,
                    string.IsNullOrWhiteSpace(stderr) ? $"reg import failed (exit {proc.ExitCode})." : stderr.Trim());
        }
        catch (Exception ex)
        {
            return ElevatedOperationResult.Fail(Id, ElevatedErrorKind.OperationFailed, ex.Message);
        }
    }
}
