using IceCrow.Tracking;

namespace IceCrow.ProfileSync.Factories;

/// <summary>
/// One-way mapping from tracking evidence certainty onto the profile
/// contract. Each value maps to its equal and an unrecognised value degrades
/// to Unknown, so a record can never be more certain than the tracker was.
/// Record factories share this instead of mapping ad hoc.
/// </summary>
public static class EvidenceCertaintyMapping
{
    public static Certainty ToCertainty(EvidenceCertainty certainty) => certainty switch
    {
        EvidenceCertainty.Exact => Certainty.Exact,
        EvidenceCertainty.Partial => Certainty.Partial,
        EvidenceCertainty.Inferred => Certainty.Inferred,
        _ => Certainty.Unknown,
    };
}
