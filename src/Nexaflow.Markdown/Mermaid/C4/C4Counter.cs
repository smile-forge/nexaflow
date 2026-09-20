namespace Nexaflow.Markdown.Mermaid.C4;

/// <summary>
/// The running number behind C4-PlantUML's numbering: <c>Index()</c> takes the next, <c>LastIndex()</c> repeats the one just
/// given, and <c>SetIndex(n)</c> and <c>increment(n)</c> move it without drawing anything.
/// </summary>
public sealed class C4Counter
{
    private int next = 1;

    /// <summary>The number given out last, or zero before any was.</summary>
    public int Last { get; private set; }

    /// <summary>Takes the next number, moving the count on by <paramref name="offset"/>.</summary>
    public int Next(int offset = 1)
    {
        this.Last = this.next;
        this.next += Math.Max(1, offset);

        return this.Last;
    }

    public void Set(int value) => this.next = value;

    public void Increment(int offset) => this.next += offset;

    /// <summary>What an <c>$index</c> comes to, or null where none is written.</summary>
    public int? Resolve(string? said)
    {
        if (said is not { Length: > 0 }) return null;

        var expression = said.Trim();

        if (expression.StartsWith("LastIndex", StringComparison.OrdinalIgnoreCase))
            return this.Last > 0 ? this.Last : this.Next();

        if (expression.StartsWith("SetIndex", StringComparison.OrdinalIgnoreCase))
        {
            if (Inside(expression) is { } at) this.Set(at);
            return this.Next();
        }

        if (expression.StartsWith("Index", StringComparison.OrdinalIgnoreCase))
            return this.Next(Inside(expression) ?? 1);

        return C4Macro.Number(expression);
    }

    /// <summary>The number between a call's brackets — the <c>2</c> of <c>Index(2)</c>.</summary>
    private static int? Inside(string call)
    {
        var open = call.IndexOf('(');
        var close = call.LastIndexOf(')');

        return open < 0 || close <= open ? null : C4Macro.Number(call[(open + 1)..close].Trim());
    }
}
