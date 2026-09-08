namespace Atlas.Domain.Services;

/// <summary>
/// Pure leave math. Leave consumes working days only: Monday through Friday,
/// inclusive of both endpoints, excluding explicitly supplied holidays.
/// </summary>
public static class LeaveCalculator
{
    /// <summary>Counts the working days (Mon-Fri) in [start, end] inclusive.</summary>
    public static int CountWorkingDays(DateOnly start, DateOnly end) => CountWorkingDays(start, end, new HashSet<DateOnly>());

    public static int CountWorkingDays(DateOnly start, DateOnly end, IReadOnlySet<DateOnly> holidays)
    {
        if (end < start)
        {
            throw new DomainException(
                $"End date {end:yyyy-MM-dd} cannot be before the start date {start:yyyy-MM-dd}.");
        }

        var days = 0;
        for (var number = start.DayNumber; number <= end.DayNumber; number++)
        {
            var day = DateOnly.FromDayNumber(number);
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !holidays.Contains(day))
            {
                days++;
            }
        }

        return days;
    }
}
