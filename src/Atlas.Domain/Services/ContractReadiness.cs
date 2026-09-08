using Atlas.Domain.Entities;

namespace Atlas.Domain.Services;

public record ActivationBlocker(string Code, string Message, Guid? ItemId = null);

/// <summary>One assessment used by both the explanation and the activation command.</summary>
public static class ContractReadiness
{
    public static List<ActivationBlocker> Assess(EmploymentContract contract, IEnumerable<OnboardingItem> items,
        bool countryActive = true)
    {
        var blockers = items.Where(i => i.IsRequired && !i.IsCompleted).OrderBy(i => i.Type).ThenBy(i => i.Id)
            .Select(i => new ActivationBlocker("required-onboarding-incomplete", i.Title, i.Id)).ToList();
        if (contract.Status != ContractStatus.Draft)
            blockers.Add(new("not-draft", $"Only draft contracts can be activated; this contract is {contract.Status}."));
        if (!countryActive) blockers.Add(new("country-paused", "Hiring in this country is paused."));
        return blockers;
    }
}
