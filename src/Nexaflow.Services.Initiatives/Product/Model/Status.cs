namespace Nexaflow.Services.Initiatives.Product.Model;

/// <summary>
/// The single status vocabulary used everywhere — a node's own state, a node↔concern link, and a
/// snaplink. Serialized snake_case: should / shouldnt / done / faulted (displayed "shouldn't").
/// </summary>
public enum Status
{
    /// <summary>Intended but not yet done — the aspirational backlog. Wants attention.</summary>
    Should,

    /// <summary>Deliberately excluded / out of scope. De-emphasised.</summary>
    Shouldnt,

    /// <summary>Complete.</summary>
    Done,

    /// <summary>Broken / has a bug — a leak that must not vanish.</summary>
    Faulted
}
