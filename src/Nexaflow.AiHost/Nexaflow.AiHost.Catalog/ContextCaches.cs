namespace Nexaflow.AiHost.Catalog;

/// <summary>What each way of holding a context costs.</summary>
public static class ContextCaches
{
    /// <summary>What a token held as <paramref name="cache"/> takes, where held as <see cref="ContextCache.F16"/> it takes <paramref name="asF16"/>.</summary>
    public static long BytesPerToken(this ContextCache cache, long asF16) => cache == ContextCache.Q8 ? asF16 * 34 / 64 : asF16;
}
