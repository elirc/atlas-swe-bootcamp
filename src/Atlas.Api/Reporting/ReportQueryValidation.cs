namespace Atlas.Api.Reporting;

/// <summary>Validates public query inputs before arithmetic or database work.</summary>
internal static class ReportQueryValidation
{
    public static Dictionary<string, string[]> PeriodErrors(
        int? fromYear, int? fromMonth, int? toYear, int? toMonth)
    {
        var errors = new Dictionary<string, string[]>();
        ValidateSide("from", fromYear, fromMonth, errors);
        ValidateSide("to", toYear, toMonth, errors);
        if (errors.Count == 0 && fromYear is not null && toYear is not null
            && fromYear * 100 + (fromMonth ?? 1) > toYear * 100 + (toMonth ?? 12))
        {
            errors["toYear"] = ["The end period must be on or after the start period."];
        }
        return errors;
    }

    private static void ValidateSide(string prefix, int? year, int? month, Dictionary<string, string[]> errors)
    {
        if (year is < 1 or > 9999)
            errors[prefix + "Year"] = ["Year must be between 1 and 9999."];
        if (month is < 1 or > 12)
            errors[prefix + "Month"] = ["Month must be between 1 and 12."];
        else if (month is not null && year is null)
            errors[prefix + "Year"] = ["Year is required when month is supplied."];
    }

    // DateOnly.AddDays can throw even for a positive int. Bound by the chosen date.
    public static bool TryCutoff(DateOnly asOf, int window, out DateOnly cutoff)
    {
        cutoff = asOf;
        if (window < 0 || window > DateOnly.MaxValue.DayNumber - asOf.DayNumber)
            return false;
        cutoff = asOf.AddDays(window);
        return true;
    }

    public static IResult InvalidWindow() => Results.ValidationProblem(new Dictionary<string, string[]>
    {
        ["withinDays"] = ["withinDays must be non-negative and must not extend beyond 9999-12-31."],
    });
}
