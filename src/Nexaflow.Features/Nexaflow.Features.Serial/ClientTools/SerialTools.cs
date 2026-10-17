using System.Globalization;
using System.IO.Ports;
using System.Text;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Serial.ViewModels;

namespace Nexaflow.Features.Serial.ClientTools;

/// <summary>
/// The agent's hands on the terminal. Reading needs no approval; opening the port and sending both reach the device
/// and are approved by the user each time.
/// </summary>
public static class SerialTools
{
    public static IReadOnlyList<IClientTool> For(SerialViewModel page) =>
    [
        new DelegateClientTool("serial_ports",
            "Lists the machine's COM ports and the device behind each.",
            [],
            ToolSafety.SafeOperation,
            (_, _) =>
            {
                page.RefreshPortsCommand.Execute(null);
                var text = string.Join("\n", page.Ports.Select(p => $"- {p.DisplayName}{(p.InstanceId is { } id ? $" ({id})" : "")}"));
                return Task.FromResult(ToolResult.Ok($"{page.Ports.Count} port(s)", text.Length == 0 ? "No COM ports." : text));
            },
            parallelizable: true),

        new DelegateClientTool("serial_read",
            "The end of the transcript: what came in (←) and went out (→), with times. Can wait a moment for more to arrive first.",
            [new ClientToolParameter("lines", "How many lines, 1–500. Default 50.", Required: false, Type: "integer"),
             new ClientToolParameter("wait_ms", "Wait this long for new data first, 0–10000. Default 0.", Required: false, Type: "integer"),
             new ClientToolParameter("hex", "Include each line's bytes in hex.", Required: false, Type: "boolean")],
            ToolSafety.SafeOperation,
            async (args, ct) =>
            {
                int wait = Math.Clamp(ToolArgs.Int(args, "wait_ms", 0), 0, 10_000);
                if (wait > 0) { await Task.Delay(wait, ct); page.TakeIn(); }
                int lines = Math.Clamp(ToolArgs.Int(args, "lines", 50), 1, 500);
                return ToolResult.Ok($"{Math.Min(lines, page.Transcript.Count)} line(s) from {page.Port}", page.TranscriptText(lines, ToolArgs.Bool(args, "hex")));
            }),

        new DelegateClientTool("serial_connect",
            "Opens the port. Settings left out keep what the page shows. Opening can reset a microcontroller (DTR), so the user approves it.",
            [new ClientToolParameter("port", "e.g. 'COM5'. Defaults to the page's port.", Required: false),
             new ClientToolParameter("baud", "Baud rate, e.g. 115200.", Required: false, Type: "integer"),
             new ClientToolParameter("data_bits", "5–8.", Required: false, Type: "integer"),
             new ClientToolParameter("parity", "None, Odd, Even, Mark or Space.", Required: false),
             new ClientToolParameter("stop_bits", "1, 1.5 or 2.", Required: false)],
            ToolSafety.RequiresApproval,
            async (args, _) =>
            {
                if (ToolArgs.Str(args, "port") is { } port) page.Retarget(port);
                if (ToolArgs.IntOrNull(args, "baud") is { } baud) page.BaudRate = baud;
                if (ToolArgs.IntOrNull(args, "data_bits") is { } bits) page.DataBits = Math.Clamp(bits, 5, 8);
                if (ToolArgs.Str(args, "parity") is { } parity && Enum.TryParse<Parity>(parity, true, out var p)) page.Parity = p;
                if (ToolArgs.Str(args, "stop_bits") is { } stop) page.StopBits = stop switch { "2" => StopBits.Two, "1.5" => StopBits.OnePointFive, _ => StopBits.One };
                var said = await page.ConnectCoreAsync();
                page.Status = said;
                return page.IsConnected ? ToolResult.Ok($"Connected to {page.Port}", said) : ToolResult.Error(said);
            }),

        new DelegateClientTool("serial_send",
            "Sends to the device: text with a line ending, or bytes as hex. Can wait for the reply and return it.",
            [new ClientToolParameter("text", "What to send. With hex=true, bytes as hex such as '02 10 FF'."),
             new ClientToolParameter("hex", "Send 'text' as hex bytes rather than characters.", Required: false, Type: "boolean"),
             new ClientToolParameter("line_ending", "After text: none, cr, lf or crlf. Default: the page's choice.", Required: false),
             new ClientToolParameter("wait_ms", "Wait this long for a reply and return it, 0–10000. Default 500.", Required: false, Type: "integer")],
            ToolSafety.RequiresApproval,
            async (args, ct) =>
            {
                if (!page.IsConnected) return ToolResult.Error($"{page.Port} is not connected — serial_connect first.");
                var ending = ToolArgs.Str(args, "line_ending")?.ToLowerInvariant() switch
                {
                    "none" => SerialLineEnding.None, "cr" => SerialLineEnding.CR, "lf" => SerialLineEnding.LF, "crlf" => SerialLineEnding.CRLF,
                    _ => page.LineEnding,
                };
                if (!SerialViewModel.TryEncode(ToolArgs.Str(args, "text") ?? "", ToolArgs.Bool(args, "hex"), ending, out var bytes, out var error))
                    return ToolResult.Error(error!);
                int before = page.Transcript.Count;
                var (sent, message) = await page.SendCoreAsync(bytes, ct);
                page.Status = message;
                if (!sent) return ToolResult.Error(message);
                int wait = Math.Clamp(ToolArgs.Int(args, "wait_ms", 500), 0, 10_000);
                if (wait > 0) { await Task.Delay(wait, ct); page.TakeIn(); }
                var reply = string.Join("\n", page.Transcript.Skip(before).Where(l => l.Direction == SerialDirection.Received).Select(l => $"{l.Time} ← {l.Text}"));
                return ToolResult.Ok(message, reply.Length == 0 ? $"{message}. Nothing came back in {wait} ms." : $"{message}. Reply:\n{reply}");
            }),

        new DelegateClientTool("serial_disconnect",
            "Closes the port.",
            [],
            ToolSafety.SafeOperation,
            (_, _) =>
            {
                page.DisconnectCommand.Execute(null);
                return Task.FromResult(ToolResult.Ok($"Closed {page.Port}", page.Status));
            }),
    ];
}
