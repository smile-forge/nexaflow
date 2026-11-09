using System.Collections.Frozen;
using System.Globalization;

namespace Nexaflow.AiHost.Client;

/// <summary>
/// A host's exit code as a person reads it. A process a native fault ends exits with the fault's NTSTATUS — written in
/// hex, and for the common ones with its name and what it usually means; a code the host chose itself is written as it is.
/// </summary>
public static class AiHostExitCode
{
    private static readonly FrozenDictionary<uint, (string Name, string Meaning)> Known = new Dictionary<uint, (string, string)>
    {
        [0xC0000005] = ("STATUS_ACCESS_VIOLATION", "access violation"),
        [0xC0000409] = ("STATUS_STACK_BUFFER_OVERRUN", "fast-fail: abort() or a failed runtime check"),
        [0xC0000374] = ("STATUS_HEAP_CORRUPTION", "heap corruption"),
        [0xC00000FD] = ("STATUS_STACK_OVERFLOW", "stack overflow"),
        [0xC000001D] = ("STATUS_ILLEGAL_INSTRUCTION", "an instruction this processor does not have"),
        [0xC0000017] = ("STATUS_NO_MEMORY", "out of memory"),
        [0xC0000135] = ("STATUS_DLL_NOT_FOUND", "a DLL was not found"),
        [0xC0000139] = ("STATUS_ENTRYPOINT_NOT_FOUND", "a DLL lacks a function it was expected to have"),
        [0xC0000142] = ("STATUS_DLL_INIT_FAILED", "a DLL failed to initialise"),
        [0xC000013A] = ("STATUS_CONTROL_C_EXIT", "ended by Ctrl+C"),
        [0xE0434352] = ("CLR exception", "an unhandled .NET exception"),
    }.ToFrozenDictionary();

    /// <summary><paramref name="code"/> in words: <c>0xC0000409 (STATUS_STACK_BUFFER_OVERRUN: fast-fail: …)</c> for a known
    /// fault, <c>0xC0000123</c> for another status, <c>1</c> for a code the host chose.</summary>
    public static string Describe(int code)
    {
        uint status = unchecked((uint)code);
        if (Known.TryGetValue(status, out var known)) return $"0x{status:X8} ({known.Name}: {known.Meaning})";
        return code is >= 0 and < 0x10000 ? code.ToString(CultureInfo.InvariantCulture) : $"0x{status:X8}";
    }
}
