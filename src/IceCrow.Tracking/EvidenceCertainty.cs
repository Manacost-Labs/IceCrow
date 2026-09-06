namespace IceCrow.Tracking;

/// <summary>
/// Tracking-level certainty of a collected fact. The order matters: a fact
/// derived from several inputs may only keep or lower the certainty of its
/// weakest input, and a higher layer (presentation, profile sync) may map
/// these values one-way onto its own vocabulary but never raise them.
/// </summary>
public enum EvidenceCertainty
{
    Unknown = 0,
    Inferred = 1,
    Partial = 2,
    Exact = 3,
}
