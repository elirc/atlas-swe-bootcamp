namespace Atlas.Domain.Services;

/// <summary>A positive change increases outstanding balance; payments and credits are negative.</summary>
public record LedgerMovement(DateTimeOffset AtUtc, decimal Change);

public static class InvoiceLedger
{
    public static decimal Outstanding(decimal originalTotal, IEnumerable<LedgerMovement> movements, DateTimeOffset asOf) =>
        originalTotal + movements.Where(m => m.AtUtc <= asOf).Sum(m => m.Change);

    /// <summary>
    /// A backdated payment can overdraw an earlier balance even when today's balance permits it.
    /// Check every effective instant, grouping ties so ordering cannot invent an intermediate state.
    /// </summary>
    public static void ValidateHistory(decimal originalTotal, IEnumerable<LedgerMovement> movements)
    {
        var balance = originalTotal;
        foreach (var instant in movements.GroupBy(m => m.AtUtc).OrderBy(g => g.Key))
        {
            balance += instant.Sum(m => m.Change);
            if (balance < 0)
                throw new DomainException("The entry exceeds the outstanding invoice balance at its effective date or a later ledger entry.");
        }
    }
}
