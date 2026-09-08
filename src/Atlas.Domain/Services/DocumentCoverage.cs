using Atlas.Domain.Entities;

namespace Atlas.Domain.Services;

public record DocumentCoverageBlocker(string Code, Guid? DocumentId);
public record DocumentRequirementAssessment(string Type, int MinimumRemainingDays, bool ExpiryRequired,
    List<Guid> QualifyingDocumentIds, List<DocumentCoverageBlocker> Blockers)
{
    public bool Covered => QualifyingDocumentIds.Count > 0;
}

public static class DocumentCoverage
{
    public static DocumentRequirementAssessment Assess(CountryDocumentRequirement requirement, IEnumerable<ComplianceDocument> documents, DateOnly asOf)
    {
        var candidates = documents.Where(x => x.Type == requirement.Type).OrderBy(x => x.Id).ToList();
        var qualified = new List<Guid>(); var blockers = new List<DocumentCoverageBlocker>();
        if (candidates.Count == 0) blockers.Add(new("missing-type", null));
        foreach (var candidate in candidates)
        {
            var code = candidate.IssuedDate > asOf ? "not-yet-issued"
                : candidate.ExpiryDate is null && requirement.ExpiryRequired ? "expiry-required"
                : candidate.ExpiryDate < asOf ? "expired"
                : candidate.ExpiryDate is { } expiry && expiry.DayNumber - asOf.DayNumber < requirement.MinimumRemainingDays ? "insufficient-remaining-days" : null;
            if (code is null) qualified.Add(candidate.Id); else blockers.Add(new(code, candidate.Id));
        }
        // One qualifying record satisfies a type; other records do not veto it.
        return new(requirement.Type.ToString(), requirement.MinimumRemainingDays, requirement.ExpiryRequired, qualified,
            qualified.Count > 0 ? [] : blockers.OrderBy(x => x.Code, StringComparer.Ordinal).ThenBy(x => x.DocumentId).ToList());
    }
}
