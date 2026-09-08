namespace Atlas.Api;

/// <summary>Small HTTP input rules shared by list routes. These do not grant access.</summary>
internal static class ApiInput
{
    public static bool IsAsciiCode(string value, int length) =>
        value.Length == length && value.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z');

    public static IResult? CodeFilter(string? value, int length, string field, out string? normalized)
    {
        normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
        return normalized is null || IsAsciiCode(value!.Trim(), length)
            ? null : Error(field, $"Use {length} ASCII letters.");
    }

    public static IResult? Prefix(string? value, out string? normalized)
    {
        normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
        return normalized?.Length > 200 ? Error("namePrefix", "Name prefix must be at most 200 characters.") : null;
    }

    public static bool DefinedEnum<T>(string? value, out T parsed) where T : struct, Enum =>
        Enum.TryParse(value?.Trim(), ignoreCase: true, out parsed) && Enum.IsDefined(parsed) && !value!.Contains(',');

    public static IResult Error(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    public static IResult? DateRange(DateOnly from, DateOnly to, int maximumDays)
    {
        if (from.Year is < 2000 or > 2100) return Error("from", "Year must be between 2000 and 2100.");
        if (to.Year is < 2000 or > 2100) return Error("to", "Year must be between 2000 and 2100.");
        return to < from || to.DayNumber - from.DayNumber + 1 > maximumDays
            ? Error("to", $"Choose an ordered range of at most {maximumDays} days.") : null;
    }

    public static IResult? PeriodRange(string? fromPeriod, string? toPeriod, out DateOnly from, out DateOnly to)
    {
        from = to = default;
        if (!TryPeriod(fromPeriod, out from)) return Error("fromPeriod", "Use YYYY-MM with year 2000–2100.");
        if (!TryPeriod(toPeriod, out var end)) return Error("toPeriod", "Use YYYY-MM with year 2000–2100.");
        var count = (end.Year - from.Year) * 12 + end.Month - from.Month + 1;
        if (count is < 1 or > 12) return Error("toPeriod", "Choose an ordered range of at most 12 months.");
        to = end.AddMonths(1).AddDays(-1);
        return null;
    }

    private static bool TryPeriod(string? value, out DateOnly date)
    {
        date = default;
        if (value is null || value.Length != 7 || value[4] != '-'
            || !int.TryParse(value.AsSpan(0, 4), out var year) || year is < 2000 or > 2100
            || !int.TryParse(value.AsSpan(5, 2), out var month) || month is < 1 or > 12
            || value.Where((_, i) => i != 4).Any(c => c is < '0' or > '9')) return false;
        date = new DateOnly(year, month, 1);
        return true;
    }
}
