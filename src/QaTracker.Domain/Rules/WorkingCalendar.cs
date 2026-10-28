namespace QaTracker.Domain.Rules;

/// <summary>
/// Working time for SLA due dates (ADR-016): 8 working hours per day; Sundays and holidays are not working days.
/// Days are counted in IST (UTC+05:30, no daylight saving).
/// </summary>
public static class WorkingCalendar
{
    public const int HoursPerDay = 8;

    public static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    public static bool IsWorkingDay(DateOnly date, IReadOnlySet<DateOnly> holidays) =>
        date.DayOfWeek != DayOfWeek.Sunday && !holidays.Contains(date);

    /// <summary>
    /// <paramref name="start"/> plus <paramref name="workingHours"/>: every 8 hours moves to the next working day at the
    /// same time of day; remaining hours are added as clock hours, and the result is moved off a non-working day.
    /// Example: Sat 31/Oct 18:00 IST + 56 h (7 days) → Mon 09/Nov 18:00 IST (two Sundays skipped). Returned in UTC.
    /// </summary>
    public static DateTimeOffset AddWorkingHours(DateTimeOffset start, int workingHours, IReadOnlySet<DateOnly> holidays)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(workingHours);

        var local = start.ToOffset(Ist);
        var date = DateOnly.FromDateTime(local.DateTime);
        for (var days = workingHours / HoursPerDay; days > 0; days--)
        {
            date = NextWorkingDay(date, holidays);
        }

        var due = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), Ist)
            + local.TimeOfDay
            + TimeSpan.FromHours(workingHours % HoursPerDay);

        var dueDate = DateOnly.FromDateTime(due.DateTime);
        if (!IsWorkingDay(dueDate, holidays))
        {
            due = new DateTimeOffset(NextWorkingDay(dueDate, holidays).ToDateTime(TimeOnly.MinValue), Ist) + due.TimeOfDay;
        }

        return due.ToUniversalTime();
    }

    private static DateOnly NextWorkingDay(DateOnly date, IReadOnlySet<DateOnly> holidays)
    {
        do
        {
            date = date.AddDays(1);
        }
        while (!IsWorkingDay(date, holidays));

        return date;
    }
}
