using Atlas.Domain.Entities;

namespace Atlas.Domain.Services;

public record SelectedFee(decimal Rate, Guid? ScheduleId);
public record SelectedTerms(int Days, Guid? TermsId);
public static class BillingConfiguration
{
    public static SelectedFee Fee(Client client, IEnumerable<ClientFeeSchedule> schedules, int year, int month)
    {
        var entry = schedules.Where(x => x.ClientId == client.Id && x.Year * 100 + x.Month <= year * 100 + month)
            .OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).FirstOrDefault();
        return new(entry?.Rate ?? client.ManagementFeeRate, entry?.Id);
    }
    public static SelectedTerms Terms(Guid clientId, IEnumerable<ClientBillingTerms> terms, DateOnly issueDay)
    {
        var entry = terms.Where(x => x.ClientId == clientId && x.EffectiveDate <= issueDay).OrderByDescending(x => x.EffectiveDate).FirstOrDefault();
        return new(entry?.TermDays ?? 30, entry?.Id);
    }
}
