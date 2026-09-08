using Atlas.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure;

public static class ComplianceQueries
{
    public static IQueryable<ComplianceDocument> CurrentDocumentsAt(this AtlasDbContext db, DateOnly asOf)
    {
        var cutoff = new DateTimeOffset(asOf.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        return db.ComplianceDocuments.AsNoTracking().Where(document => document.CreatedAtUtc <= cutoff
            && !db.ComplianceDocuments.Any(next => next.SupersedesDocumentId == document.Id && next.CreatedAtUtc <= cutoff));
    }
}
