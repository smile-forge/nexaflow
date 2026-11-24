namespace Nexaflow.AiHost.Catalog;

/// <summary>How a language model's context holds the keys and values of each token it has read.</summary>
public enum ContextCache
{
    /// <summary>As 16-bit floats, as the model works them out.</summary>
    F16,

    /// <summary>
    /// As 8-bit values, a block of 32 sharing one scale: 34 bytes where <see cref="F16"/> takes 64, so near twice the context fits
    /// in the same memory, with no difference a reply shows.
    /// </summary>
    Q8,
}
