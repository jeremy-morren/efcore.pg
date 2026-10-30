using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.TestModels.NodaTime;
using NodaTime;

namespace Microsoft.EntityFrameworkCore.Query.Translations.NodaTime;

/// <summary>
///     Checks that NodaTime expressions evaluated by PostgreSQL produce exactly the same results as NodaTime itself.
/// </summary>
/// <remarks>
///     <para>
///         Each case is a lambda, its arguments and its expected (correct) result. The lambda is evaluated in .NET
///         (via <see cref="LambdaExpression.Compile()" />), which must return the expected result (so that each case's expectation
///         is checked against NodaTime/.NET itself), and twice in the database: once with each lambda parameter replaced by
///         <see cref="EF.Parameter{T}" />, and once by <see cref="EF.Constant{T}" /> (so that literal generation is covered too), which
///         must also return the expected result. This prevents EF from evaluating the expression on the client, and
///         <see cref="AssertEvaluatedInDatabase" /> verifies that it didn't. The result is projected, so it must also be read back as the
///         right type; in addition, <see cref="NodaTimeParityFixture" /> verifies that every SQL expression in the query has a type mapping
///         matching its NodaTime type.
///     </para>
///     <para>
///         To reproduce a discrepancy, add a single <c>yield return Case(...)</c> line to the relevant group below.
///         Known discrepancies are marked with <see cref="NodaTimeParityTestExtensions.Skip" />.
///     </para>
/// </remarks>
[SuppressMessage("Usage", "xUnit1046:Avoid using TheoryDataRow arguments that are not serializable")]
public class NodaTimeParityTest : IClassFixture<NodaTimeParityFixture>
{
    public NodaTimeParityTest(NodaTimeParityFixture fixture, ITestOutputHelper testOutputHelper)
    {
        Fixture = fixture;
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    private NodaTimeParityFixture Fixture { get; }

    private static readonly LocalDate Date = new(2018, 4, 20);
    private static readonly LocalDate MonthEnd = new(2018, 1, 31);
    private static readonly LocalDate March31 = new(2018, 3, 31);
    private static readonly LocalDate LeapDay = new(2016, 2, 29);
    private static readonly LocalDate FirstCeDay = new(1, 1, 1);

    private static readonly LocalDateTime DateAndTime = new(2018, 4, 20, 10, 31, 33, 666);
    private static readonly LocalDateTime MonthEndDateTime = new(2018, 1, 31, 23, 30);
    private static readonly LocalDateTime LeapDayDateTime = new(2016, 2, 29, 12, 0);

    private static readonly LocalTime Time = new(10, 31, 33, 666);
    private static readonly LocalTime LateTime = new(23, 30);
    private static readonly LocalTime EarlyTime = new(0, 15);

    // Europe/Berlin (the fixture's session time zone) switched to daylight saving time at 2018-03-25T01:00Z
    // Durations of whole days must not be affected by this.
    private static readonly Instant Instant = Instant.FromUtc(2018, 4, 20, 10, 31, 33) + Duration.FromMilliseconds(666);
    private static readonly Instant BeforeDst = Instant.FromUtc(2018, 3, 24, 12, 0);
    private static readonly ZonedDateTime Zoned = Instant.InUtc();
    private static readonly ZonedDateTime ZonedBeforeDst = BeforeDst.InUtc();

    private static Period PeriodOf(
        int years = 0,
        int months = 0,
        int days = 0,
        long hours = 0,
        long minutes = 0,
        long seconds = 0,
        long milliseconds = 0)
        => new PeriodBuilder
        {
            Years = years,
            Months = months,
            Days = days,
            Hours = hours,
            Minutes = minutes,
            Seconds = seconds,
            Milliseconds = milliseconds
        }.Build();

    private static Instant InstantAt(int year, int month, int day, int hour, int minute, int second = 0, int millisecond = 0)
        => Instant.FromUtc(year, month, day, hour, minute, second) + Duration.FromMilliseconds(millisecond);

    private static ZonedDateTime ZonedAt(int year, int month, int day, int hour, int minute, int second = 0, int millisecond = 0)
        => new(InstantAt(year, month, day, hour, minute, second, millisecond), DateTimeZone.Utc);

    #region LocalDate

    public static IEnumerable<TheoryDataRow<LambdaExpression, object?[], object?>> LocalDateCases()
    {
        // LocalDate +/- Period
        yield return Case((LocalDate d, Period p) => d.Plus(p), Date, Period.FromMonths(1), new LocalDate(2018, 5, 20));
        yield return Case((LocalDate d, Period p) => d.Plus(p), MonthEnd, Period.FromMonths(1), new LocalDate(2018, 2, 28));
        yield return Case((LocalDate d, Period p) => d.Plus(p), LeapDay, Period.FromYears(1), new LocalDate(2017, 2, 28));
        yield return Case((LocalDate d, Period p) => d.Plus(p), Date, Period.FromDays(-400), new LocalDate(2017, 3, 16));
        yield return Case((LocalDate d, Period p) => d.Plus(p), Date, Period.FromWeeks(3), new LocalDate(2018, 5, 11));
        yield return Case(
            (LocalDate d, Period p) => d.Plus(p), MonthEnd, Period.FromMonths(1) + Period.FromDays(1),
            new LocalDate(2018, 3, 1));
        yield return Case((LocalDate d, Period p) => d.Minus(p), Date, Period.FromMonths(1), new LocalDate(2018, 3, 20));
        yield return Case((LocalDate d, Period p) => d.Minus(p), March31, Period.FromMonths(1), new LocalDate(2018, 2, 28));
        yield return Case((LocalDate d, Period p) => d.Minus(p), FirstCeDay, Period.FromYears(1), new LocalDate(0, 1, 1));
        yield return Case((LocalDate d, Period p) => LocalDate.Add(d, p), MonthEnd, Period.FromMonths(13), new LocalDate(2019, 2, 28));
        yield return Case((LocalDate d, Period p) => LocalDate.Subtract(d, p), LeapDay, Period.FromYears(4), new LocalDate(2012, 2, 29));
        yield return Case((LocalDate d, Period p) => d + p, MonthEnd, Period.FromMonths(1), new LocalDate(2018, 2, 28));
        yield return Case((LocalDate d, Period p) => d + p, LeapDay, Period.FromYears(1), new LocalDate(2017, 2, 28));
        yield return Case((LocalDate d, Period p) => d - p, March31, Period.FromMonths(1), new LocalDate(2018, 2, 28));
        yield return Case((LocalDate d, Period p) => (d + p).Day, MonthEnd, Period.FromMonths(1), 28);
        yield return Case((LocalDate d, Period p) => (d + p).DayOfYear, MonthEnd, Period.FromMonths(1), 59);

        // PlusDays/Weeks/Months/Years
        yield return Case((LocalDate d, int n) => d.PlusDays(n), Date, 1, new LocalDate(2018, 4, 21));
        yield return Case((LocalDate d, int n) => d.PlusDays(n), Date, -1000, new LocalDate(2015, 7, 25));
        yield return Case((LocalDate d, int n) => d.PlusDays(n), new LocalDate(2016, 2, 28), 1, new LocalDate(2016, 2, 29));
        yield return Case((LocalDate d, int n) => d.PlusWeeks(n), Date, 3, new LocalDate(2018, 5, 11));
        yield return Case((LocalDate d, int n) => d.PlusWeeks(n), Date, -60, new LocalDate(2017, 2, 24));
        yield return Case((LocalDate d, int n) => d.PlusMonths(n), Date, 0, new LocalDate(2018, 4, 20));
        yield return Case((LocalDate d, int n) => d.PlusMonths(n), MonthEnd, 1, new LocalDate(2018, 2, 28));
        yield return Case((LocalDate d, int n) => d.PlusMonths(n), MonthEnd, -2, new LocalDate(2017, 11, 30));
        yield return Case((LocalDate d, int n) => d.PlusMonths(n), MonthEnd, 13, new LocalDate(2019, 2, 28));
        yield return Case((LocalDate d, int n) => d.PlusMonths(n), March31, -1, new LocalDate(2018, 2, 28));
        yield return Case((LocalDate d, int n) => d.PlusYears(n), LeapDay, 1, new LocalDate(2017, 2, 28));
        yield return Case((LocalDate d, int n) => d.PlusYears(n), LeapDay, 4, new LocalDate(2020, 2, 29));
        yield return Case((LocalDate d, int n) => d.PlusYears(n), LeapDay, -100, new LocalDate(1916, 2, 29));
        yield return Case((LocalDate d, int n) => d.PlusYears(n), FirstCeDay, -1, new LocalDate(0, 1, 1));
        yield return Case((LocalDate d, int n) => d.PlusYears(n), FirstCeDay, -10, new LocalDate(-9, 1, 1));
        yield return Case((LocalDate d, int n) => d.PlusMonths(n).PlusDays(n), MonthEnd, 1, new LocalDate(2018, 3, 1));
        yield return Case((LocalDate d, int n) => d.PlusMonths(n).Day, MonthEnd, 1, 28);
        yield return Case((LocalDate d, int n) => d.PlusYears(n).Year, FirstCeDay, -1, 0);
        yield return Case((LocalDate d, int n) => d.PlusWeeks(n).DayOfWeek, Date, 2, IsoDayOfWeek.Friday);

        // LocalDate - LocalDate, with dates for which age() matches NodaTime (see NpgsqlNodaTimeMethodCallTranslator)
        yield return Case((LocalDate a, LocalDate b) => a - b, Date, new LocalDate(2017, 3, 19), PeriodOf(years: 1, months: 1, days: 1));
        yield return Case((LocalDate a, LocalDate b) => a - b, new LocalDate(2017, 3, 19), Date, PeriodOf(years: -1, months: -1, days: -1));
        yield return Case((LocalDate a, LocalDate b) => a - b, Date, Date, Period.Zero);
        yield return Case((LocalDate a, LocalDate b) => a - b, new LocalDate(2018, 3, 1), MonthEnd, PeriodOf(months: 1, days: 1));
        yield return Case(
            (LocalDate a, LocalDate b) => a.Minus(b), Date, new LocalDate(2017, 3, 19), PeriodOf(years: 1, months: 1, days: 1));
        yield return Case(
            (LocalDate a, LocalDate b) => LocalDate.Subtract(a, b), new LocalDate(2018, 3, 1), MonthEnd, PeriodOf(months: 1, days: 1));
        yield return Case((LocalDate a, LocalDate b) => (a - b).Months, Date, new LocalDate(2017, 3, 19), 1);
        yield return Case((LocalDate a, LocalDate b) => (a - b).Days, new LocalDate(2018, 3, 1), MonthEnd, 1);

        // LocalDate + LocalTime, At/AtMidnight
        yield return Case((LocalDate d, LocalTime t) => d + t, LeapDay, Time, new LocalDateTime(2016, 2, 29, 10, 31, 33, 666));
        yield return Case((LocalDate d, LocalTime t) => (d + t).Hour, LeapDay, Time, 10);
        yield return Case(
            (LocalDate d, LocalTime t) => (d + t).PlusHours(14), LeapDay, Time,
            new LocalDateTime(2016, 3, 1, 0, 31, 33, 666));
        yield return Case((LocalDate d, LocalTime t) => d.At(t), LeapDay, Time, new LocalDateTime(2016, 2, 29, 10, 31, 33, 666));
        yield return Case(
            (LocalDate d, LocalTime t) => d.PlusYears(1).At(t), LeapDay, Time,
            new LocalDateTime(2017, 2, 28, 10, 31, 33, 666));
        yield return Case((LocalDate d, int n) => d.PlusMonths(n).AtMidnight(), MonthEnd, 1, new LocalDateTime(2018, 2, 28, 0, 0));

        // Members, comparisons and conversions
        yield return Case((LocalDate d) => d.Year, Date, 2018);
        yield return Case((LocalDate d) => d.Month, Date, 4);
        yield return Case((LocalDate d) => d.Day, Date, 20);
        yield return Case((LocalDate d) => d.DayOfYear, Date, 110);
        yield return Case((LocalDate d) => d.DayOfWeek, Date, IsoDayOfWeek.Friday);
        yield return Case((LocalDate d) => d.DayOfWeek, new LocalDate(2018, 4, 22), IsoDayOfWeek.Sunday);
        yield return Case((LocalDate d) => d.Year, new LocalDate(-5, 1, 1), -5);
        yield return Case((LocalDate d) => d.AtMidnight(), LeapDay, new LocalDateTime(2016, 2, 29, 0, 0));
        yield return Case((LocalDate d) => d.ToDateOnly(), Date, new DateOnly(2018, 4, 20));
        yield return Case((LocalDate a, LocalDate b) => a < b, Date, LeapDay, false);
        yield return Case((LocalDate a, LocalDate b) => a == b, Date, Date, true);
        yield return Case((LocalDate a, LocalDate b) => a > b, Date, LeapDay, true);
    }

    [ConditionalTheory]
    [MemberData(nameof(LocalDateCases))]
    public Task LocalDate_matches_NodaTime(LambdaExpression expression, object?[] arguments, object? expected)
        => AssertCorrect(expression, arguments, expected);

    #endregion LocalDate

    #region LocalDateTime

    public static IEnumerable<TheoryDataRow<LambdaExpression, object?[], object?>> LocalDateTimeCases()
    {
        // LocalDateTime +/- Period
        yield return Case(
            (LocalDateTime d, Period p) => d.Plus(p), DateAndTime, Period.FromMonths(1),
            new LocalDateTime(2018, 5, 20, 10, 31, 33, 666));
        yield return Case(
            (LocalDateTime d, Period p) => d.Plus(p), MonthEndDateTime, Period.FromMonths(1),
            new LocalDateTime(2018, 2, 28, 23, 30));
        yield return Case(
            (LocalDateTime d, Period p) => d.Plus(p), MonthEndDateTime, Period.FromMonths(1) + Period.FromHours(1),
            new LocalDateTime(2018, 3, 1, 0, 30));
        yield return Case(
            (LocalDateTime d, Period p) => d.Plus(p), MonthEndDateTime, Period.FromMonths(1) + Period.FromHours(-24),
            new LocalDateTime(2018, 2, 27, 23, 30));
        yield return Case(
            (LocalDateTime d, Period p) => d.Plus(p), LeapDayDateTime, Period.FromYears(1) + Period.FromDays(1),
            new LocalDateTime(2017, 3, 1, 12, 0));
        yield return Case(
            (LocalDateTime d, Period p) => d.Plus(p), DateAndTime, Period.FromHours(1000) + Period.FromMinutes(-5),
            new LocalDateTime(2018, 6, 1, 2, 26, 33, 666));
        yield return Case(
            (LocalDateTime d, Period p) => d.Plus(p), DateAndTime, Period.FromMilliseconds(334),
            new LocalDateTime(2018, 4, 20, 10, 31, 34));
        yield return Case(
            (LocalDateTime d, Period p) => d.Minus(p), DateAndTime, Period.FromMonths(1),
            new LocalDateTime(2018, 3, 20, 10, 31, 33, 666));
        yield return Case(
            (LocalDateTime d, Period p) => d.Minus(p), new LocalDateTime(2018, 3, 31, 0, 0), Period.FromMonths(1),
            new LocalDateTime(2018, 2, 28, 0, 0));
        yield return Case(
            (LocalDateTime d, Period p) => d.Minus(p), MonthEndDateTime, Period.FromDays(1) + Period.FromSeconds(30),
            new LocalDateTime(2018, 1, 30, 23, 29, 30));
        yield return Case(
            (LocalDateTime d, Period p) => LocalDateTime.Add(d, p), MonthEndDateTime, Period.FromWeeks(5),
            new LocalDateTime(2018, 3, 7, 23, 30));
        yield return Case(
            (LocalDateTime d, Period p) => LocalDateTime.Subtract(d, p), LeapDayDateTime, Period.FromYears(4),
            new LocalDateTime(2012, 2, 29, 12, 0));
        yield return Case(
            (LocalDateTime d, Period p) => d + p, MonthEndDateTime, Period.FromMonths(1),
            new LocalDateTime(2018, 2, 28, 23, 30));
        yield return Case(
            (LocalDateTime d, Period p) => d + p, MonthEndDateTime, Period.FromMonths(1) + Period.FromHours(1),
            new LocalDateTime(2018, 3, 1, 0, 30));
        yield return Case(
            (LocalDateTime d, Period p) => d - p, MonthEndDateTime, Period.FromMonths(1),
            new LocalDateTime(2017, 12, 31, 23, 30));
        yield return Case((LocalDateTime d, Period p) => (d + p).Date, MonthEndDateTime, Period.FromHours(1), new LocalDate(2018, 2, 1));
        yield return Case((LocalDateTime d, Period p) => (d + p).TimeOfDay, MonthEndDateTime, Period.FromHours(1), new LocalTime(0, 30));

        // PlusYears/Months/Weeks/Days/Hours/Minutes/Seconds
        yield return Case((LocalDateTime d, int n) => d.PlusYears(n), LeapDayDateTime, 1, new LocalDateTime(2017, 2, 28, 12, 0));
        yield return Case((LocalDateTime d, int n) => d.PlusYears(n), LeapDayDateTime, -4, new LocalDateTime(2012, 2, 29, 12, 0));
        yield return Case((LocalDateTime d, int n) => d.PlusMonths(n), MonthEndDateTime, 1, new LocalDateTime(2018, 2, 28, 23, 30));
        yield return Case((LocalDateTime d, int n) => d.PlusMonths(n), MonthEndDateTime, -11, new LocalDateTime(2017, 2, 28, 23, 30));
        yield return Case((LocalDateTime d, int n) => d.PlusWeeks(n), DateAndTime, 3, new LocalDateTime(2018, 5, 11, 10, 31, 33, 666));
        yield return Case((LocalDateTime d, int n) => d.PlusWeeks(n), DateAndTime, -3, new LocalDateTime(2018, 3, 30, 10, 31, 33, 666));
        yield return Case((LocalDateTime d, int n) => d.PlusDays(n), MonthEndDateTime, 1, new LocalDateTime(2018, 2, 1, 23, 30));
        yield return Case((LocalDateTime d, int n) => d.PlusDays(n), DateAndTime, -1000, new LocalDateTime(2015, 7, 25, 10, 31, 33, 666));
        yield return Case((LocalDateTime d, long n) => d.PlusHours(n), MonthEndDateTime, 1, new LocalDateTime(2018, 2, 1, 0, 30));
        yield return Case((LocalDateTime d, long n) => d.PlusHours(n), DateAndTime, -30, new LocalDateTime(2018, 4, 19, 4, 31, 33, 666));
        yield return Case(
            (LocalDateTime d, long n) => d.PlusHours(n), DateAndTime, 100_000,
            new LocalDateTime(2029, 9, 16, 2, 31, 33, 666));
        yield return Case((LocalDateTime d, long n) => d.PlusMinutes(n), MonthEndDateTime, 30, new LocalDateTime(2018, 2, 1, 0, 0));
        yield return Case(
            (LocalDateTime d, long n) => d.PlusMinutes(n), DateAndTime, -1_000_000,
            new LocalDateTime(2016, 5, 25, 23, 51, 33, 666));
        yield return Case((LocalDateTime d, long n) => d.PlusSeconds(n), MonthEndDateTime, 1800, new LocalDateTime(2018, 2, 1, 0, 0));
        yield return Case(
            (LocalDateTime d, long n) => d.PlusSeconds(n), DateAndTime, -100_000_000,
            new LocalDateTime(2015, 2, 18, 0, 44, 53, 666));
        yield return Case((LocalDateTime d, long n) => d.PlusMilliseconds(n), DateAndTime, 334, new LocalDateTime(2018, 4, 20, 10, 31, 34));
        yield return Case(
            (LocalDateTime d, long n) => d.PlusMilliseconds(n), MonthEndDateTime, 1_800_001,
            new LocalDateTime(2018, 2, 1, 0, 0, 0, 1));
        yield return Case(
            (LocalDateTime d, long n) => d.PlusMilliseconds(n), DateAndTime, -86_400_001,
            new LocalDateTime(2018, 4, 19, 10, 31, 33, 665));
        yield return Case(
            (LocalDateTime d, long n) => d.PlusMilliseconds(n), DateAndTime, 9_000_000_000_001,
            new LocalDateTime(2303, 7, 3, 2, 31, 33, 667));
        yield return Case(
            (LocalDateTime d, int n) => d.PlusMonths(n).PlusDays(n), MonthEndDateTime, 1,
            new LocalDateTime(2018, 3, 1, 23, 30));
        yield return Case(
            (LocalDateTime d, int n) => d.PlusDays(n).PlusMonths(n), MonthEndDateTime, 1,
            new LocalDateTime(2018, 3, 1, 23, 30));
        yield return Case((LocalDateTime d, long n) => d.PlusMinutes(n).Date, MonthEndDateTime, 30, new LocalDate(2018, 2, 1));
        yield return Case((LocalDateTime d, long n) => d.PlusMinutes(n).Minute, MonthEndDateTime, 45, 15);

        // LocalDateTime - LocalDateTime, less than a month apart, as timestamp subtraction never returns months or years
        // (see NpgsqlNodaTimeMethodCallTranslator)
        yield return Case((LocalDateTime a, LocalDateTime b) => a - b, DateAndTime, DateAndTime.PlusHours(-3), Period.FromHours(3));
        yield return Case((LocalDateTime a, LocalDateTime b) => a - b, DateAndTime, DateAndTime.PlusDays(-1), Period.FromDays(1));
        yield return Case((LocalDateTime a, LocalDateTime b) => a - b, DateAndTime.PlusDays(-1), DateAndTime, Period.FromDays(-1));
        yield return Case(
            (LocalDateTime a, LocalDateTime b) => a.Minus(b), DateAndTime, DateAndTime.PlusHours(-30), PeriodOf(days: 1, hours: 6));
        yield return Case(
            (LocalDateTime a, LocalDateTime b) => LocalDateTime.Subtract(a, b), DateAndTime, DateAndTime.PlusHours(-3),
            Period.FromHours(3));

        // Members, comparisons and conversions
        yield return Case((LocalDateTime d) => d.Year, DateAndTime, 2018);
        yield return Case((LocalDateTime d) => d.Month, DateAndTime, 4);
        yield return Case((LocalDateTime d) => d.Day, DateAndTime, 20);
        yield return Case((LocalDateTime d) => d.Hour, DateAndTime, 10);
        yield return Case((LocalDateTime d) => d.Minute, DateAndTime, 31);
        yield return Case((LocalDateTime d) => d.Second, DateAndTime, 33);
        yield return Case((LocalDateTime d) => d.Millisecond, DateAndTime, 666);
        yield return Case((LocalDateTime d) => d.DayOfYear, DateAndTime, 110);
        yield return Case((LocalDateTime d) => d.DayOfWeek, DateAndTime, IsoDayOfWeek.Friday);
        yield return Case((LocalDateTime d) => d.DayOfWeek, new LocalDateTime(2018, 4, 22, 0, 0), IsoDayOfWeek.Sunday);
        yield return Case((LocalDateTime d) => d.Date, DateAndTime, Date);
        yield return Case((LocalDateTime d) => d.TimeOfDay, DateAndTime, Time);
        yield return Case((LocalDateTime d) => d.Year, new LocalDateTime(-5, 1, 1, 0, 0), -5);
        yield return Case(
            (LocalDateTime d) => d.ToDateTimeUnspecified(), DateAndTime,
            new DateTime(2018, 4, 20, 10, 31, 33, 666, DateTimeKind.Unspecified));
        yield return Case((LocalDateTime d) => d.InUtc().ToInstant(), DateAndTime, Instant);
        yield return Case(
            (LocalDateTime d) => d.InZoneLeniently(DateTimeZoneProviders.Tzdb["Europe/Berlin"]).ToInstant(), DateAndTime,
            InstantAt(2018, 4, 20, 8, 31, 33, 666));
        yield return Case((LocalDateTime a, LocalDateTime b) => a < b, DateAndTime, MonthEndDateTime, false);
        yield return Case((LocalDateTime a, LocalDateTime b) => a == b, DateAndTime, DateAndTime, true);
        yield return Case((LocalDateTime d) => d.Date.PlusDays(1).AtMidnight(), DateAndTime, new LocalDateTime(2018, 4, 21, 0, 0));
    }

    [ConditionalTheory]
    [MemberData(nameof(LocalDateTimeCases))]
    public Task LocalDateTime_matches_NodaTime(LambdaExpression expression, object?[] arguments, object? expected)
        => AssertCorrect(expression, arguments, expected);

    #endregion LocalDateTime

    #region LocalTime

    public static IEnumerable<TheoryDataRow<LambdaExpression, object?[], object?>> LocalTimeCases()
    {
        // LocalTime +/- Period
        yield return Case((LocalTime t, Period p) => t.Plus(p), Time, Period.FromHours(1), new LocalTime(11, 31, 33, 666));
        yield return Case((LocalTime t, Period p) => t.Plus(p), LateTime, Period.FromHours(1), new LocalTime(0, 30));
        yield return Case(
            (LocalTime t, Period p) => t.Plus(p), LateTime, Period.FromHours(49) + Period.FromMinutes(31),
            new LocalTime(1, 1));
        yield return Case((LocalTime t, Period p) => t.Plus(p), Time, Period.FromMilliseconds(334), new LocalTime(10, 31, 34));
        yield return Case((LocalTime t, Period p) => t.Minus(p), EarlyTime, Period.FromMinutes(30), new LocalTime(23, 45));
        yield return Case((LocalTime t, Period p) => t.Minus(p), EarlyTime, Period.FromHours(25), new LocalTime(23, 15));
        yield return Case((LocalTime t, Period p) => LocalTime.Add(t, p), LateTime, Period.FromSeconds(3600), new LocalTime(0, 30));
        yield return Case((LocalTime t, Period p) => LocalTime.Subtract(t, p), EarlyTime, Period.FromSeconds(3600), new LocalTime(23, 15));
        yield return Case((LocalTime t, Period p) => t + p, LateTime, Period.FromHours(1), new LocalTime(0, 30));
        yield return Case((LocalTime t, Period p) => t - p, EarlyTime, Period.FromHours(1), new LocalTime(23, 15));

        // PlusHours/Minutes/Seconds
        yield return Case((LocalTime t, long n) => t.PlusHours(n), Time, 20, new LocalTime(6, 31, 33, 666));
        yield return Case((LocalTime t, long n) => t.PlusHours(n), Time, -11, new LocalTime(23, 31, 33, 666));
        yield return Case((LocalTime t, long n) => t.PlusHours(n), Time, 1_000_001, new LocalTime(3, 31, 33, 666));
        yield return Case((LocalTime t, long n) => t.PlusMinutes(n), LateTime, 45, new LocalTime(0, 15));
        yield return Case((LocalTime t, long n) => t.PlusMinutes(n), EarlyTime, -700, new LocalTime(12, 35));
        yield return Case((LocalTime t, long n) => t.PlusSeconds(n), LateTime, 200_000, new LocalTime(7, 3, 20));
        yield return Case((LocalTime t, long n) => t.PlusSeconds(n), EarlyTime, -901, new LocalTime(23, 59, 59));
        yield return Case((LocalTime t, long n) => t.PlusMilliseconds(n), Time, 334, new LocalTime(10, 31, 34));
        yield return Case((LocalTime t, long n) => t.PlusMilliseconds(n), LateTime, 1_800_001, new LocalTime(0, 0, 0, 1));
        yield return Case((LocalTime t, long n) => t.PlusMilliseconds(n), EarlyTime, -900_001, new LocalTime(23, 59, 59, 999));
        yield return Case((LocalTime t, long n) => t.PlusMinutes(n).Hour, LateTime, 45, 0);

        // LocalTime - LocalTime
        yield return Case((LocalTime a, LocalTime b) => a.Minus(b), LateTime, EarlyTime, PeriodOf(hours: 23, minutes: 15));
        yield return Case((LocalTime a, LocalTime b) => a.Minus(b), EarlyTime, LateTime, PeriodOf(hours: -23, minutes: -15));
        yield return Case(
            (LocalTime a, LocalTime b) => a.Minus(b), Time, EarlyTime,
            PeriodOf(hours: 10, minutes: 16, seconds: 33, milliseconds: 666));
        yield return Case(
            (LocalTime a, LocalTime b) => LocalTime.Subtract(a, b), LateTime, Time,
            PeriodOf(hours: 12, minutes: 58, seconds: 26, milliseconds: 334));
        yield return Case(
            (LocalTime a, LocalTime b) => a - b, LateTime, Time,
            PeriodOf(hours: 12, minutes: 58, seconds: 26, milliseconds: 334));
        yield return Case((LocalTime a, LocalTime b) => (a - b).Minutes, LateTime, Time, 58);

        // Members, comparisons and conversions
        yield return Case((LocalTime t) => t.Hour, Time, 10);
        yield return Case((LocalTime t) => t.Minute, Time, 31);
        yield return Case((LocalTime t) => t.Second, Time, 33);
        yield return Case((LocalTime t) => t.Millisecond, Time, 666);
        yield return Case((LocalTime t) => t.ToTimeOnly(), Time, new TimeOnly(10, 31, 33, 666));
        yield return Case((LocalTime a, LocalTime b) => a < b, EarlyTime, LateTime, true);
        yield return Case((LocalTime a, LocalTime b) => a == b, Time, Time, true);
    }

    [ConditionalTheory]
    [MemberData(nameof(LocalTimeCases))]
    public Task LocalTime_matches_NodaTime(LambdaExpression expression, object?[] arguments, object? expected)
        => AssertCorrect(expression, arguments, expected);

    #endregion LocalTime

    #region Period

    public static IEnumerable<TheoryDataRow<LambdaExpression, object?[], object?>> PeriodCases()
    {
        // Periods which Npgsql reads back unchanged, i.e. which it doesn't normalize (see NpgsqlNodaTimeMethodCallTranslator)
        yield return Case((Period a, Period b) => a + b, Period.FromDays(1), Period.FromHours(2), PeriodOf(days: 1, hours: 2));
        yield return Case((Period a, Period b) => a + b, Period.FromYears(1), Period.FromMonths(1), PeriodOf(years: 1, months: 1));
        yield return Case((Period a, Period b) => Period.Add(a, b), Period.FromDays(1), Period.FromHours(2), PeriodOf(days: 1, hours: 2));
        yield return Case((Period a, Period b) => Period.Subtract(a, b), Period.FromHours(3), Period.FromHours(1), Period.FromHours(2));
        yield return Case((Period a, Period b) => (a + b).Days, Period.FromDays(1), Period.FromDays(2), 3);
        yield return Case((Period a, Period b) => (a - b).Hours, Period.FromHours(3), Period.FromHours(5), -2);
        yield return Case((long n) => Period.FromHours(n), 5, PeriodOf(hours: 5));
        yield return Case((Period a) => +a, PeriodOf(days: 1, hours: 2), PeriodOf(days: 1, hours: 2));

        // Members, factories and negation
        yield return Case(
            (Period p) => p.Years, PeriodOf(years: 2, months: 3, days: 4, hours: 5, minutes: 6, seconds: 7, milliseconds: 8),
            2);
        yield return Case(
            (Period p) => p.Months, PeriodOf(years: 2, months: 3, days: 4, hours: 5, minutes: 6, seconds: 7, milliseconds: 8),
            3);
        yield return Case(
            (Period p) => p.Days, PeriodOf(years: 2, months: 3, days: 4, hours: 5, minutes: 6, seconds: 7, milliseconds: 8),
            4);
        yield return Case(
            (Period p) => p.Hours, PeriodOf(years: 2, months: 3, days: 4, hours: 5, minutes: 6, seconds: 7, milliseconds: 8),
            5L);
        yield return Case(
            (Period p) => p.Minutes, PeriodOf(years: 2, months: 3, days: 4, hours: 5, minutes: 6, seconds: 7, milliseconds: 8),
            6L);
        yield return Case(
            (Period p) => p.Seconds, PeriodOf(years: 2, months: 3, days: 4, hours: 5, minutes: 6, seconds: 7, milliseconds: 8),
            7L);
        yield return Case(
            (Period p) => p.Milliseconds, PeriodOf(years: 2, months: 3, days: 4, hours: 5, minutes: 6, seconds: 7, milliseconds: 8),
            8L);
        yield return Case((Period p) => p.Years, PeriodOf(years: -2, months: -3), -2);
        yield return Case((Period p) => p.Seconds, PeriodOf(seconds: -7, milliseconds: -8), -7L);
        yield return Case((Period p) => p.Milliseconds, PeriodOf(seconds: -7, milliseconds: -8), -8L);
        yield return Case((int n) => Period.FromYears(n), 2, PeriodOf(years: 2));
        yield return Case((int n) => Period.FromDays(n), 40, PeriodOf(days: 40));
        yield return Case((long n) => Period.FromMinutes(n), 30L, PeriodOf(minutes: 30));
        yield return Case((long n) => Period.FromSeconds(n), 30L, PeriodOf(seconds: 30));
        yield return Case((Period p) => -p, PeriodOf(days: 1, hours: 2), PeriodOf(days: -1, hours: -2));
    }

    [ConditionalTheory]
    [MemberData(nameof(PeriodCases))]
    public Task Period_matches_NodaTime(LambdaExpression expression, object?[] arguments, object? expected)
        => AssertCorrect(expression, arguments, expected);

    #endregion Period

    #region Instant

    public static IEnumerable<TheoryDataRow<LambdaExpression, object?[], object?>> InstantCases()
    {
        // Instant +/- Duration, across a daylight saving time transition in the session time zone
        yield return Case((Instant i, Duration d) => i + d, BeforeDst, Duration.FromDays(1), InstantAt(2018, 3, 25, 12, 0));
        yield return Case((Instant i, Duration d) => i + d, BeforeDst, Duration.FromHours(36), InstantAt(2018, 3, 26, 0, 0));
        yield return Case((Instant i, Duration d) => i + d, BeforeDst, Duration.FromHours(5), InstantAt(2018, 3, 24, 17, 0));
        yield return Case((Instant i, Duration d) => i - d, Instant, Duration.FromDays(30), InstantAt(2018, 3, 21, 10, 31, 33, 666));
        yield return Case((Instant i, Duration d) => i.Plus(d), Instant, Duration.FromDays(1), InstantAt(2018, 4, 21, 10, 31, 33, 666));
        yield return Case((Instant i, Duration d) => i.Plus(d), BeforeDst, Duration.FromDays(1), InstantAt(2018, 3, 25, 12, 0));
        yield return Case((Instant i, Duration d) => i.Plus(d), BeforeDst, Duration.FromHours(36), InstantAt(2018, 3, 26, 0, 0));
        yield return Case((Instant i, Duration d) => i.Plus(d), BeforeDst, Duration.FromHours(5), InstantAt(2018, 3, 24, 17, 0));
        yield return Case(
            (Instant i, Duration d) => i.Minus(d), Instant, Duration.FromMilliseconds(666),
            InstantAt(2018, 4, 20, 10, 31, 33));
        yield return Case((Instant i, Duration d) => i.Minus(d), Instant, Duration.FromDays(30), InstantAt(2018, 3, 21, 10, 31, 33, 666));
        yield return Case((Instant i, Duration d) => Instant.Add(i, d), BeforeDst, Duration.FromDays(2), InstantAt(2018, 3, 26, 12, 0));
        yield return Case(
            (Instant i, Duration d) => Instant.Subtract(i, d), Instant, Duration.FromDays(30),
            InstantAt(2018, 3, 21, 10, 31, 33, 666));

        // Instant - Instant
        yield return Case(
            (Instant a, Instant b) => a.Minus(b), Instant, BeforeDst,
            Duration.FromTimeSpan(new TimeSpan(26, 22, 31, 33, 666)));
        yield return Case(
            (Instant a, Instant b) => Instant.Subtract(a, b), BeforeDst, Instant,
            -Duration.FromTimeSpan(new TimeSpan(26, 22, 31, 33, 666)));
        yield return Case((Instant a, Instant b) => a - b, Instant, BeforeDst, Duration.FromTimeSpan(new TimeSpan(26, 22, 31, 33, 666)));
        yield return Case((Instant a, Instant b) => (a - b).Days, Instant, BeforeDst, 26);

        // Negative durations (with a days component)
        yield return Case((Instant i, Duration d) => i + d, BeforeDst, Duration.FromHours(-26), InstantAt(2018, 3, 23, 10, 0));
        yield return Case((Instant i, Duration d) => i - d, BeforeDst, Duration.FromHours(-36), InstantAt(2018, 3, 26, 0, 0));
        yield return Case((Instant i, Duration d) => i.Minus(d), BeforeDst, Duration.FromHours(-36), InstantAt(2018, 3, 26, 0, 0));

        // Conversions to ZonedDateTime, composed with arithmetic
        yield return Case((Instant i) => i.InUtc(), BeforeDst, ZonedAt(2018, 3, 24, 12, 0));
        yield return Case(
            (Instant i) => i.InZone(DateTimeZoneProviders.Tzdb["Europe/Berlin"]).ToInstant(), BeforeDst,
            InstantAt(2018, 3, 24, 12, 0));
        yield return Case(
            (Instant i, Instant j) => i.InZone(DateTimeZoneProviders.Tzdb["Europe/Berlin"]).ToInstant() == j, Instant, Instant,
            true);
        yield return Case((Instant i, Duration d) => i.InUtc().Plus(d), BeforeDst, Duration.FromDays(1), ZonedAt(2018, 3, 25, 12, 0));
        yield return Case(
            (Instant i, Duration d) => i.InUtc().Plus(d).ToInstant(), BeforeDst, Duration.FromDays(1),
            InstantAt(2018, 3, 25, 12, 0));
        yield return Case((Instant i, int n) => i.InUtc().PlusHours(n), BeforeDst, 24, ZonedAt(2018, 3, 25, 12, 0));

        // Arithmetic on InZone(), across a daylight saving time transition in that time zone: it's exact, and members use that zone
        yield return Case(
            (Instant i) => i.InZone(DateTimeZoneProviders.Tzdb["Europe/Berlin"]).PlusHours(24).ToInstant(), BeforeDst,
            InstantAt(2018, 3, 25, 12, 0));
        yield return Case(
            (Instant i, Duration d) => i.InZone(DateTimeZoneProviders.Tzdb["Europe/Berlin"]).Plus(d).ToInstant(), BeforeDst,
            Duration.FromDays(1), InstantAt(2018, 3, 25, 12, 0));
        yield return Case(
            (Instant i, Duration d) => i.InZone(DateTimeZoneProviders.Tzdb["Europe/Berlin"]).Minus(d).ToInstant(), BeforeDst,
            Duration.FromHours(36), InstantAt(2018, 3, 23, 0, 0));
        yield return Case((Instant i) => i.InZone(DateTimeZoneProviders.Tzdb["Europe/Berlin"]).PlusHours(24).Hour, BeforeDst, 14);
        yield return Case(
            (Instant i) => i.InZone(DateTimeZoneProviders.Tzdb["Europe/Berlin"]).PlusHours(24).LocalDateTime, BeforeDst,
            new LocalDateTime(2018, 3, 25, 14, 0));
        yield return Case(
            (Instant i) => ZonedDateTime.Subtract(
                i.InZone(DateTimeZoneProviders.Tzdb["Europe/Berlin"]).PlusHours(24), i.InZone(DateTimeZoneProviders.Tzdb["Europe/Berlin"])),
            BeforeDst, Duration.FromHours(24));
        yield return Case(
            (Instant i) => i.InZone(DateTimeZoneProviders.Tzdb["Europe/Berlin"]).PlusHours(1).Minus(i.InUtc()), BeforeDst,
            Duration.FromHours(1));
        yield return Case(
            (LocalDateTime d) => d.InZoneLeniently(DateTimeZoneProviders.Tzdb["Europe/Berlin"]).PlusHours(1).ToInstant(),
            new LocalDateTime(2018, 3, 25, 1, 30), InstantAt(2018, 3, 25, 1, 30));

        // Comparisons, members (in UTC and in a time zone), Unix times and conversions
        yield return Case((Instant a, Instant b) => a < b, BeforeDst, Instant, true);
        yield return Case((Instant a, Instant b) => a == b, BeforeDst, BeforeDst, true);
        yield return Case((Instant a, Instant b) => a >= b, BeforeDst, Instant, false);
        yield return Case((Instant i) => i.InUtc().Year, Instant, 2018);
        yield return Case((Instant i) => i.InUtc().Month, Instant, 4);
        yield return Case((Instant i) => i.InUtc().Day, Instant, 20);
        yield return Case((Instant i) => i.InUtc().Hour, Instant, 10);
        yield return Case((Instant i) => i.InUtc().Minute, Instant, 31);
        yield return Case((Instant i) => i.InUtc().Second, Instant, 33);
        yield return Case((Instant i) => i.InUtc().Millisecond, Instant, 666);
        yield return Case((Instant i) => i.InUtc().DayOfYear, Instant, 110);
        yield return Case((Instant i) => i.InUtc().DayOfWeek, Instant, IsoDayOfWeek.Friday);
        yield return Case((Instant i) => i.InUtc().Date, Instant, Date);
        yield return Case((Instant i) => i.InUtc().TimeOfDay, Instant, Time);
        yield return Case((Instant i) => i.InUtc().LocalDateTime, Instant, DateAndTime);
        yield return Case((Instant i) => i.InZone(DateTimeZoneProviders.Tzdb["Europe/Berlin"]).Hour, BeforeDst, 13);
        yield return Case(
            (Instant i) => i.InZone(DateTimeZoneProviders.Tzdb["Europe/Berlin"]).Date, InstantAt(2018, 3, 24, 23, 30),
            new LocalDate(2018, 3, 25));
        yield return Case((Instant i) => i.InZone(DateTimeZoneProviders.Tzdb["Europe/Berlin"]).TimeOfDay, BeforeDst, new LocalTime(13, 0));
        yield return Case(
            (Instant i) => i.InZone(DateTimeZoneProviders.Tzdb["Europe/Berlin"]).DayOfWeek, InstantAt(2018, 3, 24, 23, 30),
            IsoDayOfWeek.Sunday);
        yield return Case(
            (Instant i) => i.InZone(DateTimeZoneProviders.Tzdb["America/New_York"]).LocalDateTime, Instant,
            new LocalDateTime(2018, 4, 20, 6, 31, 33, 666));
        yield return Case((Instant i) => i.ToDateTimeUtc(), Instant, new DateTime(2018, 4, 20, 10, 31, 33, 666, DateTimeKind.Utc));
        yield return Case((Instant i) => i.ToUnixTimeSeconds(), Instant, 1524220293L);
        yield return Case((Instant i) => i.ToUnixTimeMilliseconds(), Instant, 1524220293666L);
        yield return Case((long n) => Instant.FromUnixTimeSeconds(n), 1524220293L, InstantAt(2018, 4, 20, 10, 31, 33));
        yield return Case((long n) => Instant.FromUnixTimeMilliseconds(n), 1524220293666L, Instant);
        yield return Case((Instant a, Instant b) => Instant.Max(a, b), BeforeDst, Instant, Instant);
        yield return Case((Instant a, Instant b) => Instant.Min(a, b), BeforeDst, Instant, BeforeDst);
    }

    [ConditionalTheory]
    [MemberData(nameof(InstantCases))]
    public Task Instant_matches_NodaTime(LambdaExpression expression, object?[] arguments, object? expected)
        => AssertCorrect(expression, arguments, expected);

    #endregion Instant

    #region ZonedDateTime

    public static IEnumerable<TheoryDataRow<LambdaExpression, object?[], object?>> OffsetDateTimeCases()
    {
        // OffsetDateTime +/- Duration is exact, like Instant's, across a daylight saving time transition in the session time zone.
        // OffsetDateTime is mapped to timestamptz, so it's read back with a zero offset.
        var beforeDst = BeforeDst.WithOffset(Offset.Zero);
        var offset = Instant.WithOffset(Offset.Zero);

        yield return Case((OffsetDateTime o, Duration d) => o + d, beforeDst, Duration.FromDays(1), OffsetAt(2018, 3, 25, 12, 0));
        yield return Case((OffsetDateTime o, Duration d) => o - d, beforeDst, Duration.FromHours(36), OffsetAt(2018, 3, 23, 0, 0));
        yield return Case(
            (OffsetDateTime a, OffsetDateTime b) => a - b, offset, beforeDst, Duration.FromTimeSpan(new TimeSpan(26, 22, 31, 33, 666)));
        yield return Case((OffsetDateTime o, Duration d) => o.Plus(d), beforeDst, Duration.FromDays(1), OffsetAt(2018, 3, 25, 12, 0));
        yield return Case((OffsetDateTime o, Duration d) => o.Minus(d), beforeDst, Duration.FromHours(36), OffsetAt(2018, 3, 23, 0, 0));
        yield return Case(
            (OffsetDateTime a, OffsetDateTime b) => a.Minus(b), offset, beforeDst,
            Duration.FromTimeSpan(new TimeSpan(26, 22, 31, 33, 666)));
        yield return Case(
            (OffsetDateTime o, Duration d) => OffsetDateTime.Add(o, d), beforeDst, Duration.FromDays(2), OffsetAt(2018, 3, 26, 12, 0));
        yield return Case(
            (OffsetDateTime o, Duration d) => OffsetDateTime.Subtract(o, d), beforeDst, Duration.FromDays(1),
            OffsetAt(2018, 3, 23, 12, 0));
        yield return Case(
            (OffsetDateTime a, OffsetDateTime b) => OffsetDateTime.Subtract(a, b), beforeDst, offset,
            -Duration.FromTimeSpan(new TimeSpan(26, 22, 31, 33, 666)));
        yield return Case((OffsetDateTime o, int n) => o.PlusHours(n), beforeDst, 24, OffsetAt(2018, 3, 25, 12, 0));
        yield return Case((OffsetDateTime o, int n) => o.PlusMinutes(n), beforeDst, 24 * 60, OffsetAt(2018, 3, 25, 12, 0));
        yield return Case((OffsetDateTime o, long n) => o.PlusSeconds(n), beforeDst, 86_400, OffsetAt(2018, 3, 25, 12, 0));
        yield return Case((OffsetDateTime o, long n) => o.PlusMilliseconds(n), beforeDst, -1500, OffsetAt(2018, 3, 24, 11, 59, 58, 500));

        static OffsetDateTime OffsetAt(int year, int month, int day, int hour, int minute, int second = 0, int millisecond = 0)
            => InstantAt(year, month, day, hour, minute, second, millisecond).WithOffset(Offset.Zero);

        // Members and conversions
        yield return Case((OffsetDateTime o) => o.Year, Instant.WithOffset(Offset.Zero), 2018);
        yield return Case((OffsetDateTime o) => o.Hour, Instant.WithOffset(Offset.Zero), 10);
        yield return Case((OffsetDateTime o) => o.Date, Instant.WithOffset(Offset.Zero), Date);
        yield return Case((OffsetDateTime o) => o.TimeOfDay, Instant.WithOffset(Offset.Zero), Time);
        yield return Case((OffsetDateTime o) => o.LocalDateTime, Instant.WithOffset(Offset.Zero), DateAndTime);
        yield return Case((OffsetDateTime o) => o.ToInstant(), Instant.WithOffset(Offset.Zero), Instant);
        yield return Case((OffsetDateTime o) => o.DayOfWeek, Instant.WithOffset(Offset.Zero), IsoDayOfWeek.Friday);
    }

    [ConditionalTheory]
    [MemberData(nameof(OffsetDateTimeCases))]
    public Task OffsetDateTime_matches_NodaTime(LambdaExpression expression, object?[] arguments, object? expected)
        => AssertCorrect(expression, arguments, expected);

    public static IEnumerable<TheoryDataRow<LambdaExpression, object?[], object?>> ZonedDateTimeCases()
    {
        // ZonedDateTime +/- Duration, across a daylight saving time transition in the session time zone
        yield return Case((ZonedDateTime z, Duration d) => z + d, ZonedBeforeDst, Duration.FromDays(1), ZonedAt(2018, 3, 25, 12, 0));
        yield return Case((ZonedDateTime z, Duration d) => z + d, ZonedBeforeDst, Duration.FromHours(36), ZonedAt(2018, 3, 26, 0, 0));
        yield return Case((ZonedDateTime z, Duration d) => z - d, Zoned, Duration.FromDays(30), ZonedAt(2018, 3, 21, 10, 31, 33, 666));
        yield return Case((ZonedDateTime z, Duration d) => z.Plus(d), ZonedBeforeDst, Duration.FromDays(1), ZonedAt(2018, 3, 25, 12, 0));
        yield return Case((ZonedDateTime z, Duration d) => z.Plus(d), ZonedBeforeDst, Duration.FromHours(36), ZonedAt(2018, 3, 26, 0, 0));
        yield return Case((ZonedDateTime z, Duration d) => z.Minus(d), Zoned, Duration.FromDays(30), ZonedAt(2018, 3, 21, 10, 31, 33, 666));
        yield return Case(
            (ZonedDateTime z, Duration d) => ZonedDateTime.Add(z, d), ZonedBeforeDst, Duration.FromDays(2),
            ZonedAt(2018, 3, 26, 12, 0));
        yield return Case(
            (ZonedDateTime z, Duration d) => ZonedDateTime.Subtract(z, d), Zoned, Duration.FromDays(30),
            ZonedAt(2018, 3, 21, 10, 31, 33, 666));

        // PlusHours/Minutes/Seconds
        yield return Case((ZonedDateTime z, int n) => z.PlusHours(n), ZonedBeforeDst, 24, ZonedAt(2018, 3, 25, 12, 0));
        yield return Case((ZonedDateTime z, int n) => z.PlusHours(n), Zoned, -1000, ZonedAt(2018, 3, 9, 18, 31, 33, 666));
        yield return Case((ZonedDateTime z, int n) => z.PlusMinutes(n), ZonedBeforeDst, 24 * 60, ZonedAt(2018, 3, 25, 12, 0));
        yield return Case((ZonedDateTime z, int n) => z.PlusMinutes(n), Zoned, -100_000, ZonedAt(2018, 2, 9, 23, 51, 33, 666));
        yield return Case((ZonedDateTime z, long n) => z.PlusSeconds(n), ZonedBeforeDst, 86_400, ZonedAt(2018, 3, 25, 12, 0));
        yield return Case((ZonedDateTime z, long n) => z.PlusSeconds(n), Zoned, -10_000_000, ZonedAt(2017, 12, 25, 16, 44, 53, 666));
        yield return Case((ZonedDateTime z, long n) => z.PlusMilliseconds(n), ZonedBeforeDst, 86_400_000, ZonedAt(2018, 3, 25, 12, 0));
        yield return Case((ZonedDateTime z, long n) => z.PlusMilliseconds(n), Zoned, -666, ZonedAt(2018, 4, 20, 10, 31, 33));

        // ZonedDateTime - ZonedDateTime
        yield return Case(
            (ZonedDateTime a, ZonedDateTime b) => a.Minus(b), Zoned, ZonedBeforeDst,
            Duration.FromTimeSpan(new TimeSpan(26, 22, 31, 33, 666)));
        yield return Case(
            (ZonedDateTime a, ZonedDateTime b) => ZonedDateTime.Subtract(a, b), ZonedBeforeDst, Zoned,
            -Duration.FromTimeSpan(new TimeSpan(26, 22, 31, 33, 666)));
        yield return Case(
            (ZonedDateTime a, ZonedDateTime b) => a - b, Zoned, ZonedBeforeDst,
            Duration.FromTimeSpan(new TimeSpan(26, 22, 31, 33, 666)));

        // Conversions to Instant, composed with arithmetic
        yield return Case((ZonedDateTime z) => z.ToInstant(), ZonedBeforeDst, InstantAt(2018, 3, 24, 12, 0));
        yield return Case(
            (ZonedDateTime z, Duration d) => z.Plus(d).ToInstant(), ZonedBeforeDst, Duration.FromDays(1),
            InstantAt(2018, 3, 25, 12, 0));
        yield return Case(
            (ZonedDateTime z, Duration d) => z.ToInstant().Plus(d), ZonedBeforeDst, Duration.FromDays(1),
            InstantAt(2018, 3, 25, 12, 0));
        yield return Case(
            (ZonedDateTime z, ZonedDateTime y) => z.ToInstant() - y.ToInstant(), Zoned, ZonedBeforeDst,
            Duration.FromTimeSpan(new TimeSpan(26, 22, 31, 33, 666)));

        // Members and comparisons
        yield return Case((ZonedDateTime z) => z.Year, Zoned, 2018);
        yield return Case((ZonedDateTime z) => z.Month, Zoned, 4);
        yield return Case((ZonedDateTime z) => z.Day, Zoned, 20);
        yield return Case((ZonedDateTime z) => z.Hour, Zoned, 10);
        yield return Case((ZonedDateTime z) => z.Minute, Zoned, 31);
        yield return Case((ZonedDateTime z) => z.Second, Zoned, 33);
        yield return Case((ZonedDateTime z) => z.Millisecond, Zoned, 666);
        yield return Case((ZonedDateTime z) => z.DayOfYear, Zoned, 110);
        yield return Case((ZonedDateTime z) => z.DayOfWeek, Zoned, IsoDayOfWeek.Friday);
        yield return Case((ZonedDateTime z) => z.Date, Zoned, Date);
        yield return Case((ZonedDateTime z) => z.TimeOfDay, Zoned, Time);
        yield return Case((ZonedDateTime z) => z.LocalDateTime, Zoned, DateAndTime);
        yield return Case((ZonedDateTime a, ZonedDateTime b) => a.ToInstant() < b.ToInstant(), ZonedBeforeDst, Zoned, true);
    }

    [ConditionalTheory]
    [MemberData(nameof(ZonedDateTimeCases))]
    public Task ZonedDateTime_matches_NodaTime(LambdaExpression expression, object?[] arguments, object? expected)
        => AssertCorrect(expression, arguments, expected);

    #endregion ZonedDateTime

    #region TimeSpan and Duration

    // TimeSpan and NodaTime's Duration are both an exact amount of time mapped to PostgreSQL interval, with the same arithmetic and the
    // same component/total members (with the same semantics), so the exact same cases are generated for both.
    // Each value with its expected total and components, which are truncated towards zero and all have the value's sign
    private static readonly (TimeSpan Value, IntervalParts Expected)[] IntervalValues =
    [
        (TimeSpan.Zero, new(0, 0, 0, 0, 0, 0)),
        (new(1, 2, 3, 4, 500), new(93_784_500_000, 1, 2, 3, 4, 500)),
        (-new TimeSpan(1, 2, 3, 4, 500), new(-93_784_500_000, -1, -2, -3, -4, -500)),
        (TimeSpan.FromHours(36), new(129_600_000_000, 1, 12, 0, 0, 0)),
        (TimeSpan.FromHours(-26), new(-93_600_000_000, -1, -2, 0, 0, 0)),
        (TimeSpan.FromSeconds(-90), new(-90_000_000, 0, 0, -1, -30, 0)),
        (TimeSpan.FromMilliseconds(-1500), new(-1_500_000, 0, 0, 0, -1, -500)),
        (TimeSpan.FromMilliseconds(-1500.5), new(-1_500_500, 0, 0, 0, -1, -500)),
        (TimeSpan.FromMicroseconds(500), new(500, 0, 0, 0, 0, 0)),
        (TimeSpan.FromMicroseconds(-999_999), new(-999_999, 0, 0, 0, 0, -999)),
        (new(1000, 23, 59, 59, 999, 999), new(86_486_399_999_999, 1000, 23, 59, 59, 999))
    ];

    // Pairs of values which are added and subtracted in SQL, with the expected results.
    // PostgreSQL adds and subtracts an interval's day and time fields separately and Npgsql writes a
    // TimeSpan/Duration with the same sign in both fields and less than a day in the time field.
    // So the result's fields can have opposite signs, or a time field of a day or more,
    // in which case the components must be read from justify_hours() (JustifyRequired).
    private static readonly IntervalPair[] IntervalPairs =
    [
        // Fields with the same sign, and a time field of less than a day
        new(TimeSpan.FromHours(1), TimeSpan.FromMinutes(30),
            Sum: new(5_400_000_000, 0, 1, 30, 0, 0), SumJustifyRequired: false,
            Difference: new(1_800_000_000, 0, 0, 30, 0, 0), DifferenceJustifyRequired: false),
        new(TimeSpan.FromDays(-1), TimeSpan.FromHours(-1),
            Sum: new(-90_000_000_000, -1, -1, 0, 0, 0), SumJustifyRequired: false,
            // -1 day +01:00:00
            Difference: new(-82_800_000_000, 0, -23, 0, 0, 0), DifferenceJustifyRequired: true),

        // Fields with opposite signs
        new(TimeSpan.FromDays(1), TimeSpan.FromHours(-1),
            // 1 day -01:00:00
            Sum: new(82_800_000_000, 0, 23, 0, 0, 0), SumJustifyRequired: true,
            Difference: new(90_000_000_000, 1, 1, 0, 0, 0), DifferenceJustifyRequired: false),
        new(TimeSpan.FromDays(-1), TimeSpan.FromMilliseconds(2500.5),
            // -1 day +00:00:02.5005
            Sum: new(-86_397_499_500, 0, -23, -59, -57, -499), SumJustifyRequired: true,
            Difference: new(-86_402_500_500, -1, 0, 0, -2, -500), DifferenceJustifyRequired: false),
        new(TimeSpan.FromDays(1), TimeSpan.FromMilliseconds(1500),
            Sum: new(86_401_500_000, 1, 0, 0, 1, 500), SumJustifyRequired: false,
            // 1 day -00:00:01.5
            Difference: new(86_398_500_000, 0, 23, 59, 58, 500), DifferenceJustifyRequired: true),
        new(TimeSpan.FromDays(1), TimeSpan.FromHours(25),
            // 2 days 01:00:00
            Sum: new(176_400_000_000, 2, 1, 0, 0, 0), SumJustifyRequired: false,
            // -01:00:00 (0 days)
            Difference: new(-3_600_000_000, 0, -1, 0, 0, 0), DifferenceJustifyRequired: false),

        // A time field of a day or more
        new(TimeSpan.FromHours(20), TimeSpan.FromHours(20),
            // 40:00:00
            Sum: new(144_000_000_000, 1, 16, 0, 0, 0), SumJustifyRequired: true,
            Difference: new(0, 0, 0, 0, 0, 0), DifferenceJustifyRequired: false),
        new(TimeSpan.FromHours(-20), TimeSpan.FromHours(20),
            Sum: new(0, 0, 0, 0, 0, 0), SumJustifyRequired: false,
            // -40:00:00
            Difference: new(-144_000_000_000, -1, -16, 0, 0, 0), DifferenceJustifyRequired: true)
    ];

    private static readonly string[] IntervalMembers =
    [
        nameof(TimeSpan.Days), nameof(TimeSpan.Hours), nameof(TimeSpan.Minutes), nameof(TimeSpan.Seconds),
        nameof(TimeSpan.Milliseconds), nameof(TimeSpan.TotalDays), nameof(TimeSpan.TotalHours), nameof(TimeSpan.TotalMinutes),
        nameof(TimeSpan.TotalSeconds), nameof(TimeSpan.TotalMilliseconds)
    ];

    // TimeSpan and NodaTime's Duration are both an exact amount of time mapped to PostgreSQL interval, with the same arithmetic
    // and the same component/total members (with the same semantics), so the exact same cases are generated for both.
    public static IEnumerable<TheoryDataRow<LambdaExpression, object?[], object?>> IntervalCases(Type type)
    {
        var a = Expression.Parameter(type, "a");
        var b = Expression.Parameter(type, "b");

        foreach (var (value, expected) in IntervalValues)
        {
            yield return Row(Expression.Lambda(Expression.Negate(a), a), [Convert(value)], Convert(expected.Negate().ToTimeSpan()));

            foreach (var member in IntervalMembers)
            {
                yield return Row(Expression.Lambda(Expression.Property(a, member), a), [Convert(value)], expected.Member(member));
            }
        }

        foreach (var pair in IntervalPairs)
        {
            object?[] arguments = [Convert(pair.Left), Convert(pair.Right)];

            yield return Row(Expression.Lambda(Expression.Add(a, b), a, b), arguments, Convert(pair.Sum.ToTimeSpan()));
            yield return Row(Expression.Lambda(Expression.Subtract(a, b), a, b), arguments, Convert(pair.Difference.ToTimeSpan()));
            yield return Row(Expression.Lambda(Expression.Equal(a, b), a, b), [Convert(pair.Left), Convert(pair.Left)], true);
            yield return Row(Expression.Lambda(Expression.Equal(a, b), a, b), arguments, pair.Left == pair.Right);

            foreach (var member in IntervalMembers)
            {
                yield return Row(
                    Expression.Lambda(Expression.Property(Expression.Add(a, b), member), a, b), arguments, pair.Sum.Member(member));
                yield return Row(
                    Expression.Lambda(Expression.Property(Expression.Subtract(a, b), member), a, b), arguments,
                    pair.Difference.Member(member));
            }
        }

        yield break;

        object Convert(TimeSpan value)
            => type == typeof(Duration) ? Duration.FromTimeSpan(value) : value;
    }

    [ConditionalTheory]
    [MemberData(nameof(IntervalCases), typeof(TimeSpan))]
    public Task TimeSpan_matches_NET(LambdaExpression expression, object?[] arguments, object? expected)
        => AssertCorrect(expression, arguments, expected);

    [ConditionalTheory]
    [MemberData(nameof(IntervalCases), typeof(Duration))]
    public Task Duration_matches_NodaTime(LambdaExpression expression, object?[] arguments, object? expected)
        => AssertCorrect(expression, arguments, expected);

    public static IEnumerable<TheoryDataRow<TimeSpan, TimeSpan, bool, IntervalParts, bool>> IntervalPairCases()
        => IntervalPairs.SelectMany(p => new TheoryDataRow<TimeSpan, TimeSpan, bool, IntervalParts, bool>[]
        {
            new(p.Left, p.Right, false, p.Sum, p.SumJustifyRequired) { TestDisplayName = $"{p.Left} + {p.Right}" },
            new(p.Left, p.Right, true, p.Difference, p.DifferenceJustifyRequired) { TestDisplayName = $"{p.Left} - {p.Right}" }
        });

    /// <summary>
    ///     Checks that the components of an interval computed in SQL can only be read from justify_hours() when its fields have opposite
    ///     signs or its time field is a day or more, i.e. that the justify_hours() in the translation is required,
    ///     and that the pairs above cover both cases.
    /// </summary>
    [ConditionalTheory]
    [MemberData(nameof(IntervalPairCases))]
    public async Task Interval_components_require_justify_hours(
        TimeSpan left,
        TimeSpan right,
        bool subtract,
        IntervalParts expected,
        bool justifyRequired)
    {
        await using var context = Fixture.CreateContext();

        var expectedFields = new IntervalFields
        {
            Days = expected.Days,
            Hours = expected.Hours,
            Minutes = expected.Minutes,
            Seconds = expected.Seconds,
            Milliseconds = expected.Milliseconds
        };

        Assert.Equal(expectedFields, await ReadComponents("justify_hours(v)"));

        var withoutJustifyHours = await ReadComponents("v");
        Assert.True(
            justifyRequired != (withoutJustifyHours == expectedFields),
            justifyRequired
                ? $"Expected the components without justify_hours() to be wrong, but they were {withoutJustifyHours}"
                : $"Expected {expectedFields} without justify_hours(), but got {withoutJustifyHours}");

        return;

        Task<IntervalFields> ReadComponents(string interval)
        {
            // Only fixed SQL fragments are interpolated; the values are parameters
            var sql = $"""
                       SELECT
                           date_part('day', {interval})::int AS "Days",
                           date_part('hour', {interval})::int AS "Hours",
                           date_part('minute', {interval})::int AS "Minutes",
                           trunc(date_part('second', {interval}))::int AS "Seconds",
                           trunc(date_part('millisecond', {interval}))::int % 1000 AS "Milliseconds"
                       FROM (SELECT @left {(subtract ? "-" : "+")} @right AS v) AS i
                       """;

            return context.Database
                .SqlQueryRaw<IntervalFields>(sql, new NpgsqlParameter("left", left), new NpgsqlParameter("right", right))
                .SingleAsync();
        }
    }

    public sealed record IntervalParts(long Microseconds, int Days, int Hours, int Minutes, int Seconds, int Milliseconds)
    {
        public TimeSpan ToTimeSpan()
            => TimeSpan.FromTicks(Microseconds * TimeSpan.TicksPerMicrosecond);

        public IntervalParts Negate()
            => new(-Microseconds, -Days, -Hours, -Minutes, -Seconds, -Milliseconds);

        // The components are int and the totals double (without the object cast, a switch expression would make them all double)
        public object Member(string name)
            => name switch
            {
                nameof(TimeSpan.Days) => (object)Days,
                nameof(TimeSpan.Hours) => Hours,
                nameof(TimeSpan.Minutes) => Minutes,
                nameof(TimeSpan.Seconds) => Seconds,
                nameof(TimeSpan.Milliseconds) => Milliseconds,
                nameof(TimeSpan.TotalDays) => Microseconds / 86_400_000_000.0,
                nameof(TimeSpan.TotalHours) => Microseconds / 3_600_000_000.0,
                nameof(TimeSpan.TotalMinutes) => Microseconds / 60_000_000.0,
                nameof(TimeSpan.TotalSeconds) => Microseconds / 1_000_000.0,
                nameof(TimeSpan.TotalMilliseconds) => Microseconds / 1_000.0,
                _ => throw new ArgumentOutOfRangeException(nameof(name), name, null)
            };
    }

    private sealed record IntervalPair(
        TimeSpan Left,
        TimeSpan Right,
        IntervalParts Sum,
        bool SumJustifyRequired,
        IntervalParts Difference,
        bool DifferenceJustifyRequired);

    private sealed record IntervalFields
    {
        public int Days { get; init; }
        public int Hours { get; init; }
        public int Minutes { get; init; }
        public int Seconds { get; init; }
        public int Milliseconds { get; init; }
    }

    #endregion TimeSpan and Duration

    #region Duration methods

    // Duration's fluent arithmetic methods, which are equivalent to its operators (covered by IntervalCases)
    public static IEnumerable<TheoryDataRow<LambdaExpression, object?[], object?>> DurationMethodCases()
    {
        // Duration * and / a number (PostgreSQL interval * double precision etc.), Duration / Duration, and unary +
        yield return Case((Duration d, double n) => d * n, Duration.FromHours(3), 2.5, Duration.FromMinutes(450));
        yield return Case((Duration d, long n) => d * n, Duration.FromHours(-26), 2, Duration.FromHours(-52));
        yield return Case((Duration d, double n) => n * d, Duration.FromHours(3), 2.5, Duration.FromMinutes(450));
        yield return Case((Duration d, long n) => n * d, Duration.FromHours(3), 3, Duration.FromHours(9));
        yield return Case((Duration d, double n) => d / n, Duration.FromHours(3), 2, Duration.FromMinutes(90));
        yield return Case((Duration d, long n) => d / n, Duration.FromHours(-26), 4, Duration.FromMinutes(-390));
        yield return Case((Duration a, Duration b) => a / b, Duration.FromHours(3), Duration.FromMinutes(45), 4.0);
        yield return Case((Duration a, Duration b) => a / b, Duration.FromHours(-26), Duration.FromHours(4), -6.5);
        yield return Case((Duration d) => +d, Duration.FromHours(-26), Duration.FromHours(-26));
        yield return Case((Duration d, long n) => Duration.Multiply(d, n), Duration.FromHours(3), 3, Duration.FromHours(9));
        yield return Case((Duration d, long n) => Duration.Multiply(n, d), Duration.FromHours(3), 3, Duration.FromHours(9));
        yield return Case((Duration d, double n) => Duration.Multiply(d, n), Duration.FromHours(3), 2.5, Duration.FromMinutes(450));
        yield return Case((Duration d, double n) => Duration.Multiply(n, d), Duration.FromHours(3), 2.5, Duration.FromMinutes(450));
        yield return Case((Duration d, long n) => Duration.Divide(d, n), Duration.FromHours(3), 4, Duration.FromMinutes(45));
        yield return Case((Duration d, double n) => Duration.Divide(d, n), Duration.FromHours(3), 2, Duration.FromMinutes(90));
        yield return Case((Duration a, Duration b) => Duration.Divide(a, b), Duration.FromHours(3), Duration.FromMinutes(45), 4.0);
        yield return Case((Duration d, double n) => (d * n).Hours, Duration.FromHours(3), 2.5, 7);

        yield return Case((Duration a, Duration b) => a.Plus(b), Duration.FromDays(1), Duration.FromHours(2), Duration.FromHours(26));
        yield return Case((Duration a, Duration b) => a.Plus(b), Duration.FromHours(23), Duration.FromHours(2), Duration.FromHours(25));
        yield return Case((Duration a, Duration b) => a.Minus(b), Duration.FromDays(1), Duration.FromHours(25), Duration.FromHours(-1));
        yield return Case(
            (Duration a, Duration b) => Duration.Add(a, b), Duration.FromDays(1), Duration.FromMilliseconds(1),
            Duration.FromMilliseconds(86_400_001));
        yield return Case(
            (Duration a, Duration b) => Duration.Subtract(a, b), Duration.FromDays(1), Duration.FromMilliseconds(1),
            Duration.FromMilliseconds(86_399_999));
        yield return Case((Duration d) => Duration.Negate(d), Duration.FromDays(1) + Duration.FromHours(1), Duration.FromHours(-25));
        yield return Case((Duration d) => Duration.Negate(d), Duration.FromHours(-26), Duration.FromHours(26));
        yield return Case((Duration a, Duration b) => a.Minus(b).Hours, Duration.FromDays(1), Duration.FromHours(25), -1);
        yield return Case(
            (Duration a, Duration b) => a.Plus(b).Milliseconds, Duration.FromDays(-1), Duration.FromMilliseconds(2500.5),
            -499);

        // Factories, comparisons, ticks and conversions to and from TimeSpan
        yield return Case((double n) => Duration.FromDays(n), 1.5, Duration.FromHours(36));
        yield return Case((int n) => Duration.FromDays(n), 2, Duration.FromDays(2));
        yield return Case((double n) => Duration.FromHours(n), 1.5, Duration.FromMinutes(90));
        yield return Case((long n) => Duration.FromMinutes(n), 90L, Duration.FromMinutes(90));
        yield return Case((long n) => Duration.FromSeconds(n), 90L, Duration.FromSeconds(90));
        yield return Case((long n) => Duration.FromMilliseconds(n), 1500L, Duration.FromMilliseconds(1500));
        yield return Case((Duration a, Duration b) => a < b, Duration.FromHours(1), Duration.FromHours(2), true);
        yield return Case((Duration a, Duration b) => a == b, Duration.FromDays(1), Duration.FromHours(24), true);
        yield return Case((Duration d) => d.BclCompatibleTicks, Duration.FromMilliseconds(1500), 15000000L);
        yield return Case((Duration d) => d.ToTimeSpan(), Duration.FromHours(-26), TimeSpan.FromHours(-26));
        yield return Case((TimeSpan t) => Duration.FromTimeSpan(t), TimeSpan.FromHours(-26), Duration.FromHours(-26));
    }

    [ConditionalTheory]
    [MemberData(nameof(DurationMethodCases))]
    public Task Duration_methods_match_NodaTime(LambdaExpression expression, object?[] arguments, object? expected)
        => AssertCorrect(expression, arguments, expected);

    #endregion Duration methods

    #region DateInterval

    public static IEnumerable<TheoryDataRow<LambdaExpression, object?[], object?>> DateIntervalCases()
    {
        var february = Dates2018(2, 1, 2, 28);
        var acrossLeapDay = new DateInterval(new LocalDate(2016, 2, 28), new LocalDate(2016, 3, 1));
        var singleDay = new DateInterval(Date, Date);

        yield return Case((DateInterval i) => i.Start, february, new LocalDate(2018, 2, 1));
        yield return Case((DateInterval i) => i.End, february, new LocalDate(2018, 2, 28));
        yield return Case((DateInterval i) => i.End, singleDay, new LocalDate(2018, 4, 20));
        yield return Case((DateInterval i) => i.Length, february, 28);
        yield return Case((DateInterval i) => i.Length, acrossLeapDay, 3);
        yield return Case((DateInterval i) => i.Length, singleDay, 1);
        yield return Case((DateInterval i) => i.End.PlusMonths(1), february, new LocalDate(2018, 3, 28));
        yield return Case((DateInterval i, LocalDate d) => i.Contains(d), february, new LocalDate(2018, 2, 28), true);
        yield return Case((DateInterval i, LocalDate d) => i.Contains(d), february, new LocalDate(2018, 3, 1), false);
        yield return Case((DateInterval i, LocalDate d) => i.Contains(d), february, new LocalDate(2018, 1, 31), false);
        yield return Case((DateInterval i, LocalDate d) => i.Contains(d.PlusMonths(1)), february, MonthEnd, true);
        yield return Case((DateInterval a, DateInterval b) => a.Contains(b), february, Dates2018(2, 10, 2, 28), true);
        yield return Case((DateInterval a, DateInterval b) => a.Contains(b), february, Dates2018(2, 10, 3, 1), false);
        yield return Case(
            (DateInterval a, DateInterval b) => a.Intersection(b), february, Dates2018(2, 28, 3, 10),
            Dates2018(2, 28, 2, 28));
        yield return Case((DateInterval a, DateInterval b) => a.Union(b), february, Dates2018(3, 1, 3, 10), Dates2018(2, 1, 3, 10));
        yield return Case((DateInterval a, DateInterval b) => a.Union(b), february, Dates2018(1, 20, 2, 10), Dates2018(1, 20, 2, 28));

        // Intersection/Union return null for intervals which don't overlap (or for Union, aren't contiguous either)
        var march = Dates2018(3, 1, 3, 31);
        var april = Dates2018(4, 1, 4, 30);
        yield return Case((DateInterval a, DateInterval b) => a.Intersection(b), february, april, null);
        yield return Case((DateInterval a, DateInterval b) => a.Intersection(b) == null, february, april, true);
        yield return Case((DateInterval a, DateInterval b) => a.Intersection(b) != null, february, march, false);
        yield return Case((DateInterval a, DateInterval b) => a.Union(b), february, april, null);
        yield return Case((DateInterval a, DateInterval b) => a.Union(b) == null, february, april, true);
        yield return Case((DateInterval a, DateInterval b) => a.Union(b), february, march, Dates2018(2, 1, 3, 31));
        yield return Case((DateInterval a, DateInterval b) => a.Union(b) == null, february, march, false);

        static DateInterval Dates2018(int startMonth, int startDay, int endMonth, int endDay)
            => new(new LocalDate(2018, startMonth, startDay), new LocalDate(2018, endMonth, endDay));
    }

    [ConditionalTheory]
    [MemberData(nameof(DateIntervalCases))]
    public Task DateInterval_matches_NodaTime(LambdaExpression expression, object?[] arguments, object? expected)
        => AssertCorrect(expression, arguments, expected);

    #endregion DateInterval

    #region Interval

    public static IEnumerable<TheoryDataRow<LambdaExpression, object?[], object?>> IntervalCases()
    {
        var acrossDst = new Interval(BeforeDst, BeforeDst + Duration.FromDays(2));
        var unboundedStart = new Interval(null, Instant);

        yield return Case((Interval i) => i.Start, acrossDst, InstantAt(2018, 3, 24, 12, 0));
        yield return Case((Interval i) => i.End, acrossDst, InstantAt(2018, 3, 26, 12, 0));
        yield return Case((Interval i) => i.Duration, acrossDst, Duration.FromDays(2));
        yield return Case((Interval i) => i.Duration.TotalHours, acrossDst, 48.0);
        yield return Case((Interval i) => i.HasStart, acrossDst, true);
        yield return Case((Interval i) => i.HasStart, unboundedStart, false);
        yield return Case((Interval i) => i.HasEnd, unboundedStart, true);
        yield return Case((Interval i) => i.Start.Plus(Duration.FromDays(1)), acrossDst, InstantAt(2018, 3, 25, 12, 0));
        yield return Case((Interval i, Instant x) => i.Contains(x), acrossDst, BeforeDst, true);
        yield return Case((Interval i, Instant x) => i.Contains(x), acrossDst, BeforeDst + Duration.FromDays(2), false);
        yield return Case((Interval i, Instant x) => i.Contains(x), acrossDst, BeforeDst + Duration.FromHours(47), true);
        yield return Case((Interval i, Instant x) => i.Contains(x.Plus(Duration.FromDays(2))), acrossDst, BeforeDst, false);
        yield return Case((Interval i, Instant x) => i.Contains(x), unboundedStart, Instant.FromUtc(1, 1, 1, 0, 0), true);
    }

    [ConditionalTheory]
    [MemberData(nameof(IntervalCases))]
    public Task Interval_matches_NodaTime(LambdaExpression expression, object?[] arguments, object? expected)
        => AssertCorrect(expression, arguments, expected);

    #endregion Interval

    #region BCL date/time types

    // The same discrepancies apply to the BCL date/time types; they're covered here since this fixture's session time zone has daylight
    // saving time. DateTime/DateTimeOffset values are mapped to timestamptz, so they're UTC.
    public static IEnumerable<TheoryDataRow<LambdaExpression, object?[], object?>> BclCases()
    {
        var utcBeforeDst = new DateTime(2018, 3, 24, 12, 0, 0, DateTimeKind.Utc);
        var offsetBeforeDst = new DateTimeOffset(2018, 3, 24, 12, 0, 0, TimeSpan.Zero);
        var monthEndBeforeDst = new DateTimeOffset(2018, 2, 28, 23, 30, 0, TimeSpan.Zero);

        // Arithmetic on timestamptz
        yield return Case((DateTime d, double n) => d.AddDays(n), utcBeforeDst, 1, Utc(2018, 3, 25, 12));
        yield return Case(
            (d, n) => d.AddMonths(n), new DateTime(2018, 2, 24, 12, 0, 0, DateTimeKind.Utc), 1, Utc(2018, 3, 24, 12));
        yield return Case((DateTime d, double n) => d.AddHours(n), utcBeforeDst, 24, Utc(2018, 3, 25, 12));
        yield return Case((d, t) => d + t, utcBeforeDst, TimeSpan.FromDays(1), Utc(2018, 3, 25, 12));
        yield return Case(
            (d, t) => d - t, new DateTime(2018, 3, 26, 12, 0, 0, DateTimeKind.Utc), TimeSpan.FromHours(36),
            Utc(2018, 3, 25, 0));
        yield return Case((d, t) => d + t, utcBeforeDst, TimeSpan.FromHours(-26), Utc(2018, 3, 23, 10));
        yield return Case(
            (DateTimeOffset d, double n) => d.AddDays(n), offsetBeforeDst, 1, new DateTimeOffset(2018, 3, 25, 12, 0, 0, TimeSpan.Zero));
        yield return Case(
            (d, n) => d.AddMonths(n), monthEndBeforeDst, 1, new DateTimeOffset(2018, 3, 28, 23, 30, 0, TimeSpan.Zero));
        yield return Case(
            (d, t) => d + t, offsetBeforeDst, TimeSpan.FromDays(1),
            new DateTimeOffset(2018, 3, 25, 12, 0, 0, TimeSpan.Zero));

        // DateTime/DateTimeOffset.Add/Subtract are equivalent to the operators
        yield return Case((DateTime d, TimeSpan t) => d.Add(t), utcBeforeDst, TimeSpan.FromDays(1), Utc(2018, 3, 25, 12));
        yield return Case(
            (DateTime d, TimeSpan t) => d.Subtract(t), new DateTime(2018, 3, 26, 12, 0, 0, DateTimeKind.Utc), TimeSpan.FromHours(36),
            Utc(2018, 3, 25, 0));
        yield return Case((DateTime a, DateTime b) => a.Subtract(b), Utc(2018, 3, 25, 12), utcBeforeDst, TimeSpan.FromDays(1));
        yield return Case(
            (DateTimeOffset d, TimeSpan t) => d.Add(t), offsetBeforeDst, TimeSpan.FromDays(1),
            new DateTimeOffset(2018, 3, 25, 12, 0, 0, TimeSpan.Zero));
        yield return Case(
            (DateTimeOffset d, TimeSpan t) => d.Subtract(t), offsetBeforeDst, TimeSpan.FromHours(36),
            new DateTimeOffset(2018, 3, 23, 0, 0, 0, TimeSpan.Zero));
        yield return Case(
            (DateTimeOffset a, DateTimeOffset b) => a.Subtract(b), new DateTimeOffset(2018, 3, 25, 12, 0, 0, TimeSpan.Zero),
            offsetBeforeDst, TimeSpan.FromDays(1));

        // TimeSpan * and / a number (PostgreSQL interval * double precision etc.), TimeSpan / TimeSpan, unary +, and the methods
        yield return Case((TimeSpan t, double n) => t * n, TimeSpan.FromHours(3), 2.5, TimeSpan.FromMinutes(450));
        yield return Case((TimeSpan t, double n) => t * n, TimeSpan.FromHours(-26), 2, TimeSpan.FromHours(-52));
        yield return Case((TimeSpan t, double n) => n * t, TimeSpan.FromHours(3), 2.5, TimeSpan.FromMinutes(450));
        yield return Case((TimeSpan t, double n) => t / n, TimeSpan.FromHours(-26), 4, TimeSpan.FromMinutes(-390));
        yield return Case((TimeSpan a, TimeSpan b) => a / b, TimeSpan.FromHours(3), TimeSpan.FromMinutes(45), 4.0);
        yield return Case((TimeSpan a, TimeSpan b) => a / b, TimeSpan.FromHours(-26), TimeSpan.FromHours(4), -6.5);
        yield return Case((TimeSpan t) => +t, TimeSpan.FromHours(-26), TimeSpan.FromHours(-26));
        yield return Case((TimeSpan a, TimeSpan b) => a.Add(b), TimeSpan.FromDays(1), TimeSpan.FromHours(-25), TimeSpan.FromHours(-1));
        yield return Case((TimeSpan a, TimeSpan b) => a.Subtract(b), TimeSpan.FromDays(1), TimeSpan.FromHours(25), TimeSpan.FromHours(-1));
        yield return Case((TimeSpan t) => t.Negate(), TimeSpan.FromHours(26), TimeSpan.FromHours(-26));
        yield return Case((TimeSpan t, double n) => t.Multiply(n), TimeSpan.FromHours(3), 2.5, TimeSpan.FromMinutes(450));
        yield return Case((TimeSpan t, double n) => t.Divide(n), TimeSpan.FromHours(3), 2, TimeSpan.FromMinutes(90));
        yield return Case((TimeSpan a, TimeSpan b) => a.Divide(b), TimeSpan.FromHours(3), TimeSpan.FromMinutes(45), 4.0);
        yield return Case((TimeSpan t) => t.Duration(), TimeSpan.FromHours(-26), TimeSpan.FromHours(26));
        yield return Case((TimeSpan t) => t.Duration(), TimeSpan.FromHours(26), TimeSpan.FromHours(26));
        yield return Case(
            (TimeSpan a, TimeSpan b) => (a - b).Duration(), TimeSpan.FromDays(1), TimeSpan.FromHours(25), TimeSpan.FromHours(1));

        // DateOnly: FromDateTime of a timestamptz takes the date in UTC, not in the session time zone (in which this one is
        // already 2018-03-25)
        var date = new DateOnly(2018, 3, 24);
        yield return Case((DateTime d) => DateOnly.FromDateTime(d), utcBeforeDst, date);
        yield return Case(
            (DateTime d) => DateOnly.FromDateTime(d), new DateTime(2018, 3, 24, 23, 30, 0, DateTimeKind.Utc), date);
        yield return Case((DateTime d) => DateOnly.FromDateTime(d).AddDays(1), utcBeforeDst, new DateOnly(2018, 3, 25));
        yield return Case((DateTime d) => DateOnly.FromDateTime(d).Day, utcBeforeDst, 24);
        yield return Case(
            (DateOnly d, TimeOnly t) => d.ToDateTime(t), date, new TimeOnly(10, 31, 33), new DateTime(2018, 3, 24, 10, 31, 33));
        yield return Case((DateOnly d, int n) => d.AddDays(n), new DateOnly(2018, 2, 28), 1, new DateOnly(2018, 3, 1));
        yield return Case((DateOnly d, int n) => d.AddDays(n), date, -1000, new DateOnly(2015, 6, 28));
        yield return Case((DateOnly d, int n) => d.AddMonths(n), new DateOnly(2018, 1, 31), 1, new DateOnly(2018, 2, 28));
        yield return Case((DateOnly d, int n) => d.AddYears(n), new DateOnly(2016, 2, 29), 1, new DateOnly(2017, 2, 28));
        yield return Case((DateOnly d) => d.DayNumber, date, 736776);
        yield return Case((DateOnly a, DateOnly b) => a.DayNumber - b.DayNumber, date, new DateOnly(2017, 3, 19), 370);
        yield return Case((int n) => DateOnly.FromDayNumber(n), 736776, date);
        yield return Case((DateOnly d) => d.Year, date, 2018);
        yield return Case((DateOnly d) => d.Month, date, 3);
        yield return Case((DateOnly d) => d.Day, date, 24);
        yield return Case((DateOnly d) => d.DayOfYear, date, 83);
        yield return Case((DateOnly d) => d.DayOfWeek, date, DayOfWeek.Saturday);
        yield return Case(
            (DateTime d) => TimeOnly.FromDateTime(d), new DateTime(2018, 3, 24, 23, 30, 0, DateTimeKind.Utc), new TimeOnly(23, 30));
        yield return Case((DateTime d) => TimeOnly.FromDateTime(d).Hour, utcBeforeDst, 12);

        var lateOffset = new DateTimeOffset(2018, 4, 20, 23, 31, 33, 666, TimeSpan.Zero);
        var lateTime = new TimeOnly(23, 31, 33, 666);

        // DateTime arithmetic and comparisons (members aren't translated for a parameter, whose Kind isn't known)
        yield return Case(
            (DateTime d, int n) => d.AddMonths(n), new DateTime(2018, 1, 31, 23, 30, 0, DateTimeKind.Utc), 1,
            new DateTime(2018, 2, 28, 23, 30, 0, DateTimeKind.Utc));
        yield return Case((DateTime d, int n) => d.AddYears(n), Utc(2016, 2, 29, 12), 1, Utc(2017, 2, 28, 12));
        yield return Case((DateTime d, double n) => d.AddMinutes(n), utcBeforeDst, 1440, Utc(2018, 3, 25, 12));
        yield return Case((DateTime d, double n) => d.AddSeconds(n), utcBeforeDst, 86400, Utc(2018, 3, 25, 12));
        yield return Case(
            (DateTime d, double n) => d.AddMilliseconds(n), utcBeforeDst, -1500,
            new DateTime(2018, 3, 24, 11, 59, 58, 500, DateTimeKind.Utc));
        yield return Case((DateTime a, DateTime b) => a < b, utcBeforeDst, Utc(2018, 3, 25, 12), true);

        // DateTimeOffset members, Unix times and arithmetic
        yield return Case((DateTimeOffset d) => d.Year, lateOffset, 2018);
        yield return Case((DateTimeOffset d) => d.Day, lateOffset, 20);
        yield return Case((DateTimeOffset d) => d.Hour, lateOffset, 23);
        yield return Case((DateTimeOffset d) => d.Millisecond, lateOffset, 666);
        yield return Case(
            (DateTimeOffset d) => d.DayOfWeek, new DateTimeOffset(2018, 4, 21, 23, 31, 33, 666, TimeSpan.Zero),
            DayOfWeek.Saturday);
        yield return Case((DateTimeOffset d) => d.Date, lateOffset, new DateTime(2018, 4, 20));
        yield return Case((DateTimeOffset d) => d.DateTime, lateOffset, new DateTime(2018, 4, 20, 23, 31, 33, 666));
        yield return Case((DateTimeOffset d) => d.UtcDateTime, lateOffset, new DateTime(2018, 4, 20, 23, 31, 33, 666, DateTimeKind.Utc));
        yield return Case((DateTimeOffset d) => d.TimeOfDay, lateOffset, TimeSpan.FromMilliseconds(84693666));
        yield return Case((DateTimeOffset d) => d.ToUnixTimeSeconds(), lateOffset, 1524267093L);
        yield return Case((DateTimeOffset d) => d.ToUnixTimeMilliseconds(), lateOffset, 1524267093666L);
        yield return Case(
            (long n) => DateTimeOffset.FromUnixTimeSeconds(n), 1524267093L,
            new DateTimeOffset(2018, 4, 20, 23, 31, 33, TimeSpan.Zero));
        yield return Case((long n) => DateTimeOffset.FromUnixTimeMilliseconds(n), 1524267093666L, lateOffset);
        yield return Case(
            (DateTimeOffset d, int n) => d.AddMonths(n), new DateTimeOffset(2018, 1, 31, 23, 30, 0, TimeSpan.Zero), 1,
            monthEndBeforeDst);
        yield return Case(
            (DateTimeOffset d, double n) => d.AddHours(n), offsetBeforeDst, 24,
            new DateTimeOffset(2018, 3, 25, 12, 0, 0, TimeSpan.Zero));

        // TimeOnly members, arithmetic (wrapping around midnight), IsBetween and comparisons
        yield return Case((TimeOnly t) => t.Hour, lateTime, 23);
        yield return Case((TimeOnly t) => t.Minute, lateTime, 31);
        yield return Case((TimeOnly t) => t.Second, lateTime, 33);
        yield return Case((TimeOnly t) => t.Millisecond, lateTime, 666);
        yield return Case((TimeOnly t, double n) => t.AddHours(n), new TimeOnly(23, 31), 2, new TimeOnly(1, 31));
        yield return Case((TimeOnly t, double n) => t.AddMinutes(n), new TimeOnly(23, 31), 30, new TimeOnly(0, 1));
        yield return Case((TimeOnly t, TimeSpan s) => t.Add(s), new TimeOnly(23, 31), TimeSpan.FromHours(-25), new TimeOnly(22, 31));
        yield return Case((TimeOnly t) => t.ToTimeSpan(), lateTime, TimeSpan.FromMilliseconds(84693666));
        yield return Case((TimeSpan s) => TimeOnly.FromTimeSpan(s), TimeSpan.FromHours(23.5), new TimeOnly(23, 30));
        yield return Case(
            (TimeOnly t, TimeOnly s) => t.IsBetween(s, new TimeOnly(23, 59)), new TimeOnly(23, 31), new TimeOnly(22, 0),
            true);
        yield return Case((TimeOnly t, TimeOnly s) => t.IsBetween(s, new TimeOnly(1, 0)), new TimeOnly(0, 31), new TimeOnly(22, 0), true);
        yield return Case((TimeOnly a, TimeOnly b) => a < b, new TimeOnly(8, 0), new TimeOnly(9, 0), true);
        yield return Case((TimeOnly a, TimeOnly b) => a - b, new TimeOnly(8, 0), new TimeOnly(9, 30), TimeSpan.FromMinutes(1350));

        // DateOnly comparisons and arithmetic
        yield return Case((DateOnly a, DateOnly b) => a < b, date, new DateOnly(2018, 3, 25), true);
        yield return Case((DateOnly d, int n) => d.AddMonths(n).Day, new DateOnly(2018, 1, 31), 1, 28);

        // TimeSpan factories, comparisons and ticks
        yield return Case((double n) => TimeSpan.FromDays(n), 1.5, TimeSpan.FromHours(36));
        yield return Case((double n) => TimeSpan.FromHours(n), -26.0, TimeSpan.FromHours(-26));
        yield return Case((double n) => TimeSpan.FromMinutes(n), 90.0, TimeSpan.FromMinutes(90));
        yield return Case((double n) => TimeSpan.FromSeconds(n), 90.5, TimeSpan.FromMilliseconds(90500));
        yield return Case((double n) => TimeSpan.FromMilliseconds(n), 1500.0, TimeSpan.FromMilliseconds(1500));
        yield return Case((TimeSpan a, TimeSpan b) => a < b, TimeSpan.FromHours(1), TimeSpan.FromHours(2), true);
        yield return Case((TimeSpan a, TimeSpan b) => a == b, TimeSpan.FromDays(1), TimeSpan.FromHours(24), true);
        yield return Case((TimeSpan t) => t.Ticks, TimeSpan.FromMilliseconds(1500), 15000000L);

        // TimeSpan arithmetic and members are covered by IntervalCases, along with Duration's
        yield break;

        static DateTime Utc(int year, int month, int day, int hour)
            => new(year, month, day, hour, 0, 0, DateTimeKind.Utc);
    }

    [ConditionalTheory]
    [MemberData(nameof(BclCases))]
    public Task Bcl_matches_NET(LambdaExpression expression, object?[] arguments, object? expected)
        => AssertCorrect(expression, arguments, expected);

    #endregion BCL date/time types

    #region Support

    private static TheoryDataRow<LambdaExpression, object?[], object?> Case<T, TResult>(
        Expression<Func<T, TResult>> expression,
        T argument,
        TResult expected)
        => Row(expression, [argument], expected);

    private static TheoryDataRow<LambdaExpression, object?[], object?> Case<T1, T2, TResult>(
        Expression<Func<T1, T2, TResult>> expression,
        T1 argument1,
        T2 argument2,
        TResult expected)
        => Row(expression, [argument1, argument2], expected);

    private static TheoryDataRow<LambdaExpression, object?[], object?> Row(
        LambdaExpression expression,
        object?[] arguments,
        object? expected)
        => new(expression, arguments, expected) { TestDisplayName = $"{Describe(expression, arguments)} = {expected}" };

    private static string Describe(LambdaExpression expression, object?[] arguments)
        => $"{expression.Body} with " + string.Join(", ", expression.Parameters.Select((p, i) => $"{p.Name}={arguments[i]}"));

    private Task AssertCorrect(LambdaExpression expression, object?[] arguments, object? expected)
        => (Task)AssertCorrectMethod.MakeGenericMethod(expression.ReturnType).Invoke(this, [expression, arguments, expected])!;

    private static readonly MethodInfo AssertCorrectMethod
        = typeof(NodaTimeParityTest).GetMethod(nameof(AssertCorrectCore), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private async Task AssertCorrectCore<TResult>(LambdaExpression expression, object?[] arguments, object? expectedObject)
    {
        if (expectedObject is not null)
        {
            Assert.IsType<TResult>(expectedObject);
        }

        var expected = (TResult)expectedObject!;

        // The case's expected result must be NodaTime's/.NET's own result
        var client = (TResult)expression.Compile().DynamicInvoke(arguments)!;
        Assert.True(
            IsEqual(expression, expected, client),
            $"{Describe(expression, arguments)}: expected {expected}, .NET returned {client}");

        await using var context = Fixture.CreateContext();

        foreach (var argumentMethod in new[] { EfParameterMethod, EfConstantMethod })
        {
            Fixture.TestSqlLoggerFactory.Clear();
            var actual = await QueryDatabase<TResult>(context, ToServerSelector<TResult>(expression, arguments, argumentMethod));

            AssertEvaluatedInDatabase(argumentMethod == EfParameterMethod ? arguments.Length : 0);
            Assert.True(
                IsEqual(expression, expected, actual),
                $"{Describe(expression, arguments)} (as {argumentMethod.Name}): expected {expected}, PostgreSQL returned {actual}");
        }
    }

    private static Task<TResult> QueryDatabase<TResult>(NodaTimeContext context, Expression<Func<NodaTimeTypes, TResult>> selector)
        => context.Set<NodaTimeTypes>().Select(selector).SingleAsync();

    /// <summary>
    ///     EF evaluates an untranslated final projection on the client (e.g. <c>SELECT @p</c>, with the method applied to the parameter's
    ///     value when reading it), which would make a case pass without testing the database.
    ///     <see cref="NodaTimeParityFixture" /> rejects such queries.
    /// </summary>
    [ConditionalFact]
    public async Task Client_evaluation_is_rejected()
    {
        await using var context = Fixture.CreateContext();

        foreach (var argumentMethod in new[] { EfParameterMethod, EfConstantMethod })
        {
            var selector = ToServerSelector<long>((Duration d) => ClientOnly(d), [Duration.FromDays(1)], argumentMethod);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => QueryDatabase(context, selector));
            Assert.Contains("evaluated on the client", exception.Message);
        }
    }

    private static long ClientOnly(Duration duration)
        => duration.BclCompatibleTicks;

    private static bool IsEqual<TResult>(LambdaExpression expression, TResult expected, TResult actual)
        => EqualityComparer<TResult>.Default.Equals(expected, actual) || IsWithinTotalMemberTolerance(expression, expected, actual);

    /// <summary>
    ///     Known discrepancies are skipped, but their expected result must still be NodaTime's/.NET's own result.
    /// </summary>
    [ConditionalFact]
    public void Known_discrepancies_expect_the_NodaTime_result()
    {
        var rows = typeof(NodaTimeParityTest).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.ReturnType == typeof(IEnumerable<TheoryDataRow<LambdaExpression, object?[], object?>>))
            .SelectMany(m => m.GetParameters() is [{ ParameterType: var parameterType }] && parameterType == typeof(Type)
                ? new object?[][] { [typeof(TimeSpan)], [typeof(Duration)] }.Select(a => m.Invoke(null, a))
                : [m.Invoke(null, [])])
            .SelectMany(r => (IEnumerable<TheoryDataRow<LambdaExpression, object?[], object?>>)r!)
            .Where(r => r.Skip is not null);

        foreach (var row in rows)
        {
            var (expression, arguments, expected) = row.Data;
            var client = expression.Compile().DynamicInvoke(arguments);

            Assert.True(
                Equals(expected, client) || IsWithinTotalMemberTolerance(expression, expected, client),
                $"{Describe(expression, arguments)}: expected {expected}, .NET returned {client}");
        }
    }

    /// <summary>
    ///     TimeSpan/Duration's TotalDays...TotalMilliseconds are floating-point, and can differ from the database's result in the last digits.
    ///     The database computes date_part('epoch', x) / n, rounding twice; TimeSpan computes ticks / n and
    ///     NodaTime's Duration computes days * unitsPerDay + nanoOfDay / n, whose error is relative to days * unitsPerDay.
    ///     So these are compared within 1e-14 * (|expected| + unitsPerDay), which still catches any actual mistranslation (e.g. wrong units or sign).
    /// </summary>
    private static bool IsWithinTotalMemberTolerance(LambdaExpression expression, object? expected, object? actual)
    {
        if (expected is not double expectedDouble
            || actual is not double actualDouble
            || expression.Body is not MemberExpression { Member.Name: var memberName })
        {
            return false;
        }

        double? unitsPerDay = memberName switch
        {
            nameof(TimeSpan.TotalDays) => 1,
            nameof(TimeSpan.TotalHours) => 24,
            nameof(TimeSpan.TotalMinutes) => 24 * 60,
            nameof(TimeSpan.TotalSeconds) => 24 * 60 * 60,
            nameof(TimeSpan.TotalMilliseconds) => 24 * 60 * 60 * 1000,
            _ => null
        };

        return unitsPerDay is not null
            && Math.Abs(expectedDouble - actualDouble) <= 1e-14 * (Math.Abs(expectedDouble) + unitsPerDay.Value);
    }

    /// <summary>
    ///     Rewrites <c>(a, b) => a.Plus(b)</c> to <c>t => EF.Parameter(a).Plus(EF.Parameter(b))</c> (or <see cref="EF.Constant{T}" />),
    ///     so that the database evaluates the whole expression.
    /// </summary>
    private static Expression<Func<NodaTimeTypes, TResult>> ToServerSelector<TResult>(
        LambdaExpression expression,
        object?[] arguments,
        MethodInfo argumentMethod)
    {
        var body = expression.Body;
        for (var i = 0; i < expression.Parameters.Count; i++)
        {
            var parameter = expression.Parameters[i];
            var holder = Activator.CreateInstance(typeof(ValueHolder<>).MakeGenericType(parameter.Type), arguments[i])!;
            var value = Expression.Call(
                argumentMethod.MakeGenericMethod(parameter.Type),
                Expression.Field(Expression.Constant(holder), nameof(ValueHolder<>.Value)));

            body = ReplacingExpressionVisitor.Replace(parameter, value, body);
        }

        return Expression.Lambda<Func<NodaTimeTypes, TResult>>(body, Expression.Parameter(typeof(NodaTimeTypes), "t"));
    }

    /// <summary>
    ///     Guards against accidental client-side evaluation: each argument must have been sent as its own parameter, and the projection
    ///     must reference all of them (rather than e.g. a single pre-computed parameter). Note that EF can't evaluate a subtree containing
    ///     <see cref="EF.Parameter{T}" /> on the client, so it would fail rather than silently pre-compute it.
    /// </summary>
    private void AssertEvaluatedInDatabase(int argumentCount)
    {
        var sql = Assert.Single(Fixture.TestSqlLoggerFactory.SqlStatements);

        var parameterNames = Regex.Matches(sql, @"^(@\w+)=", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(argumentCount, parameterNames.Count);

        // The query's FROM starts a line (unlike e.g. that of EXTRACT(epoch FROM ...))
        var projection = Regex.Match(sql, @"^SELECT (.*?)\r?\nFROM ", RegexOptions.Multiline | RegexOptions.Singleline).Groups[1].Value;
        Assert.All(parameterNames, p => Assert.Matches(Regex.Escape(p) + @"\b", projection));
    }

    private static readonly MethodInfo EfParameterMethod = typeof(EF).GetMethod(nameof(EF.Parameter))!;
    private static readonly MethodInfo EfConstantMethod = typeof(EF).GetMethod(nameof(EF.Constant))!;

    private sealed class ValueHolder<T>(T value)
    {
        public readonly T Value = value;
    }

    #endregion Support
}

internal static class NodaTimeParityTestExtensions
{
    /// <summary>
    ///     Marks a case as a known discrepancy between PostgreSQL and NodaTime.
    /// </summary>
    public static TheoryDataRow<LambdaExpression, object?[], object?> Skip(
        this TheoryDataRow<LambdaExpression, object?[], object?> row,
        string reason)
    {
        row.Skip = reason;
        return row;
    }
}
