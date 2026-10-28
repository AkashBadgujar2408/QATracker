using QaTracker.Domain.Rules;

namespace QaTracker.Domain.Tests.Rules;

public sealed class WorkingCalendarTests
{
    private static readonly HashSet<DateOnly> NoHolidays = [];

    private static DateTimeOffset Ist(int month, int day, int hour, int minute = 0) =>
        new(2026, month, day, hour, minute, 0, WorkingCalendar.Ist);

    [Fact]
    public void WholeDays_KeepTheTimeOfDay()
    {
        // Tue 27/Oct 18:00 + 2 days → Thu 29/Oct 18:00
        WorkingCalendar.AddWorkingHours(Ist(10, 27, 18), 16, NoHolidays).ShouldBe(Ist(10, 29, 18));
    }

    [Fact]
    public void Sundays_AreSkipped()
    {
        // Sat 31/Oct + 7 working days → Mon 09/Nov (Sundays 01/Nov and 08/Nov skipped)
        WorkingCalendar.AddWorkingHours(Ist(10, 31, 18), 56, NoHolidays).ShouldBe(Ist(11, 9, 18));
    }

    [Fact]
    public void Holidays_AreSkipped()
    {
        var holidays = new HashSet<DateOnly> { new(2026, 10, 28) };
        // Tue 27/Oct + 1 day, Wed is a holiday → Thu 29/Oct
        WorkingCalendar.AddWorkingHours(Ist(10, 27, 10), 8, holidays).ShouldBe(Ist(10, 29, 10));
    }

    [Fact]
    public void PartialDays_AddClockHours()
    {
        WorkingCalendar.AddWorkingHours(Ist(10, 27, 10), 12, NoHolidays).ShouldBe(Ist(10, 28, 14));
    }

    [Fact]
    public void ADueTimeOnASunday_MovesToMonday()
    {
        // Sat 31/Oct 20:00 + 5 h → Sun 01:00 → Mon 02/Nov 01:00
        WorkingCalendar.AddWorkingHours(Ist(10, 31, 20), 5, NoHolidays).ShouldBe(Ist(11, 2, 1));
    }

    [Fact]
    public void Days_AreCountedInIst_AndTheResultIsUtc()
    {
        // 19:00 UTC Mon 26/Oct is 00:30 IST Tue 27/Oct.
        var start = new DateTimeOffset(2026, 10, 26, 19, 0, 0, TimeSpan.Zero);
        var due = WorkingCalendar.AddWorkingHours(start, 8, NoHolidays);

        due.Offset.ShouldBe(TimeSpan.Zero);
        due.ShouldBe(Ist(10, 28, 0, 30));
    }

    [Fact]
    public void NegativeHours_AreRejected() =>
        Should.Throw<ArgumentOutOfRangeException>(() => WorkingCalendar.AddWorkingHours(Ist(10, 27, 10), -1, NoHolidays));
}
