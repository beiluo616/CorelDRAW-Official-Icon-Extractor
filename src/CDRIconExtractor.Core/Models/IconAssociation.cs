namespace CDRIconExtractor.Core.Models;

public enum AssociationConfidence
{
    Exact,
    Strong,
    Heuristic,
    Unmapped
}

public sealed record IconAssociation(
    DrawUiCommand Command,
    IconAsset? Asset,
    AssociationConfidence Confidence,
    string Reason,
    string? IconGuid = null,
    string? ResourceIdHint = null);
