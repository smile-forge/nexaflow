using System.IO;
using System.Text;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;

namespace Nexaflow.Core.Services.Agent;

/// <summary>
/// One agent run as the model saw it — every message it was sent and everything it wrote back, whole — written to a
/// file of its own in the folder <see cref="Variable"/> names. For reading what the model was actually given when a
/// run goes wrong; nothing is written unless the variable is set.
/// </summary>
internal sealed class RunTranscript
{
    public const string Variable = "NEXAFLOW_AI_TRANSCRIPTS";

    private readonly string _path;
    private int _written;
    private string? _offered;
    private string? _instructions;

    private RunTranscript(string path) => _path = path;

    /// <summary>A transcript for a run begun by <paramref name="input"/>, or null when transcripts are off.</summary>
    public static RunTranscript? Begin(string input)
    {
        if (Environment.GetEnvironmentVariable(Variable) is not { Length: > 0 } folder) return null;
        Directory.CreateDirectory(folder);
        var name = string.Concat(input.Where(char.IsLetterOrDigit).Take(40));
        return new RunTranscript(Path.Combine(folder, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{name}.txt"));
    }

    /// <summary>A step of the run: the messages sent that were not in an earlier step, the tools offered and the
    /// instructions when they were chosen again, and what the model wrote.</summary>
    public void Step(IReadOnlyList<LlmMessage> sent, IReadOnlyList<IClientTool> offered, string written)
    {
        var text = new StringBuilder();
        var names = string.Join(", ", offered.Select(t => t.Name));
        if (names != _offered)
        {
            text.Append(">>> tools offered\n").Append(names).Append("\n\n");
            _offered = names;
        }
        if (_written > 0 && sent.Count > 0 && sent[0].Text != _instructions)
            text.Append(">>> ").Append(sent[0].Role).Append(", written again\n").Append(sent[0].Text).Append("\n\n");
        if (sent.Count > 0) _instructions = sent[0].Text;

        for (; _written < sent.Count; _written++)
            text.Append(">>> ").Append(sent[_written].Role).Append('\n').Append(sent[_written].Text).Append("\n\n");
        text.Append("<<< written\n").Append(written).Append("\n\n");
        Append(text.ToString());
    }

    /// <summary>A step's misread calls, written again in a context of their own: what took the step's place.</summary>
    public void Repaired(string written) => Append($"<<< written again, in place of the above\n{written}\n\n");

    private void Append(string text)
    {
        try { File.AppendAllText(_path, text); }
        catch (IOException) { /* a transcript is a diagnostic: the run goes on without it */ }
    }
}
