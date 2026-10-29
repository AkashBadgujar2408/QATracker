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

    /// <summary>
    /// Working hours from <paramref name="start"/> to <paramref name="end"/> (fix time, retest turnaround, closure time):
    /// the clock time that falls on working days (IST), with a full working day counting <see cref="HoursPerDay"/> hours –
    /// the inverse of <see cref="AddWorkingHours"/> for whole days. Example: Sat 18:00 → Mon 18:00 IST = 8 h (Sunday
    /// skipped). Zero when <paramref name="end"/> is not after <paramref name="start"/>.
    /// </summary>
    public static double WorkingHoursBetween(DateTimeOffset start, DateTimeOffset end, IReadOnlySet<DateOnly> holidays)
    {
        if (end <= start)
        {
            return 0;
        }

        var from = start.ToOffset(Ist);
        var to = end.ToOffset(Ist);
        var day = DateOnly.FromDateTime(from.DateTime);
        var lastDay = DateOnly.FromDateTime(to.DateTime);
        var clock = TimeSpan.Zero;
        for (; day <= lastDay; day = day.AddDays(1))
        {
            if (!IsWorkingDay(day, holidays))
            {
                continue;
            }

            var dayStart = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), Ist);
            var segmentStart = from > dayStart ? from : dayStart;
            var dayEnd = dayStart.AddDays(1);
            var segmentEnd = to < dayEnd ? to : dayEnd;
            clock += segmentEnd - segmentStart;
        }

        return clock.TotalHours * HoursPerDay / 24;
    }

    /// <summary><see cref="WorkingHoursBetween"/> in working days (8 hours each).</summary>
    public static double WorkingDaysBetween(DateTimeOffset start, DateTimeOffset end, IReadOnlySet<DateOnly> holidays) =>
        WorkingHoursBetween(start, end, holidays) / HoursPerDay;

    /// <summary>The IST calendar date of an instant (dashboards group by IST day).</summary>
    public static DateOnly IstDate(DateTimeOffset instant) => DateOnly.FromDateTime(instant.ToOffset(Ist).DateTime);

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
