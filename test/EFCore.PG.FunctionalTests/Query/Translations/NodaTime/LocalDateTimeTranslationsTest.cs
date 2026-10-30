using Microsoft.EntityFrameworkCore.TestModels.NodaTime;
using NodaTime;

namespace Microsoft.EntityFrameworkCore.Query.Translations.NodaTime;

public class LocalDateTimeTranslationsTest : QueryTestBase<NodaTimeQueryNpgsqlFixture>
{
    public LocalDateTimeTranslationsTest(NodaTimeQueryNpgsqlFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    [ConditionalTheory]
    [MemberData(nameof(IsAsyncData))]
    public async Task Subtract_LocalDateTime(bool async)
    {
        await AssertQuery(
            async,
            ss => ss.Set<NodaTimeTypes>().Where(t => t.LocalDateTime + Period.FromDays(1) - t.LocalDateTime == Period.FromDays(1)));

        AssertSql(
            """
SELECT n."Id", n."DateInterval", n."Duration", n."Instant", n."InstantRange", n."Interval", n."LocalDate", n."LocalDate2", n."LocalDateRange", n."LocalDateTime", n."LocalTime", n."Long", n."OffsetTime", n."Period", n."TimeZoneId", n."ZonedDateTime"
FROM "NodaTimeTypes" AS n
WHERE age(n."LocalDateTime" + INTERVAL 'P1D', n."LocalDateTime") = INTERVAL 'P1D'
""");
    }

    [ConditionalTheory]
    [MemberData(nameof(IsAsyncData))]
    public async Task Year(bool async)
    {
        await AssertQuery(
            async,
            ss => ss.Set<NodaTimeTypes>().Where(t => t.LocalDateTime.Year == 2018));

        AssertSql(
            """
SELECT n."Id", n."DateInterval", n."Duration", n."Instant", n."InstantRange", n."Interval", n."LocalDate", n."LocalDate2", n."LocalDateRange", n."LocalDateTime", n."LocalTime", n."Long", n."OffsetTime", n."Period", n."TimeZoneId", n."ZonedDateTime"
FROM "NodaTimeTypes" AS n
WHERE trunc(date_part('year', n."LocalDateTime") - 0.5)::int + 1 = 2018
""");
    }

    [ConditionalTheory]
    [InlineData(false, -100)]
    [InlineData(true, -100)]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    public async Task Year_astronomical(bool async, int year)
    {
        await using var context = CreateContext();
        await context.Database.CreateExecutionStrategy().ExecuteAsync(
            async () =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync();

                var entity = await context.NodaTimeTypes.SingleAsync();
                entity.LocalDateTime = new LocalDateTime(year, 12, 31, 12, 0);
                await context.SaveChangesAsync();
                Fixture.TestSqlLoggerFactory.Clear();

                var query = context.NodaTimeTypes.Select(t => t.LocalDateTime.Year);
                Assert.Equal(year, async ? await query.SingleAsync() : query.Single());

                AssertSql(
                    """
SELECT trunc(date_part('year', n."LocalDateTime") - 0.5)::int + 1
FROM "NodaTimeTypes" AS n
LIMIT 2
""");
            });
    }

    [ConditionalTheory]
    [MemberData(nameof(IsAsyncData))]
    public async Task Month(bool async)
    {
        await AssertQuery(
            async,
            ss => ss.Set<NodaTimeTypes>().Where(t => t.LocalDateTime.Month == 4));

        AssertSql(
            """
SELECT n."Id", n."DateInterval", n."Duration", n."Instant", n."InstantRange", n."Interval", n."LocalDate", n."LocalDate2", n."LocalDateRange", n."LocalDateTime", n."LocalTime", n."Long", n."OffsetTime", n."Period", n."TimeZoneId", n."ZonedDateTime"
FROM "NodaTimeTypes" AS n
WHERE date_part('month', n."LocalDateTime")::int = 4
""");
    }

    [ConditionalTheory]
    [MemberData(nameof(IsAsyncData))]
    public async Task DayOfYear(bool async)
    {
        await AssertQuery(
            async,
            ss => ss.Set<NodaTimeTypes>().Where(t => t.LocalDateTime.DayOfYear == 110));

        AssertSql(
            """
SELECT n."Id", n."DateInterval", n."Duration", n."Instant", n."InstantRange", n."Interval", n."LocalDate", n."LocalDate2", n."LocalDateRange", n."LocalDateTime", n."LocalTime", n."Long", n."OffsetTime", n."Period", n."TimeZoneId", n."ZonedDateTime"
FROM "NodaTimeTypes" AS n
WHERE date_part('doy', n."LocalDateTime")::int = 110
""");
    }

    [ConditionalTheory]
    [MemberData(nameof(IsAsyncData))]
    public async Task Day(bool async)
    {
        await AssertQuery(
            async,
            ss => ss.Set<NodaTimeTypes>().Where(t => t.LocalDateTime.Day == 20));

        AssertSql(
            """
SELECT n."Id", n."DateInterval", n."Duration", n."Instant", n."InstantRange", n."Interval", n."LocalDate", n."LocalDate2", n."LocalDateRange", n."LocalDateTime", n."LocalTime", n."Long", n."OffsetTime", n."Period", n."TimeZoneId", n."ZonedDateTime"
FROM "NodaTimeTypes" AS n
WHERE date_part('day', n."LocalDateTime")::int = 20
""");
    }

    [ConditionalTheory]
    [MemberData(nameof(IsAsyncData))]
    public async Task Hour(bool async)
    {
        await AssertQuery(
            async,
            ss => ss.Set<NodaTimeTypes>().Where(t => t.LocalDateTime.Hour == 10));

        AssertSql(
            """
SELECT n."Id", n."DateInterval", n."Duration", n."Instant", n."InstantRange", n."Interval", n."LocalDate", n."LocalDate2", n."LocalDateRange", n."LocalDateTime", n."LocalTime", n."Long", n."OffsetTime", n."Period", n."TimeZoneId", n."ZonedDateTime"
FROM "NodaTimeTypes" AS n
WHERE date_part('hour', n."LocalDateTime")::int = 10
""");
    }

    [ConditionalTheory]
    [MemberData(nameof(IsAsyncData))]
    public async Task Minute(bool async)
    {
        await AssertQuery(
            async,
            ss => ss.Set<NodaTimeTypes>().Where(t => t.LocalDateTime.Minute == 31));

        AssertSql(
            """
SELECT n."Id", n."DateInterval", n."Duration", n."Instant", n."InstantRange", n."Interval", n."LocalDate", n."LocalDate2", n."LocalDateRange", n."LocalDateTime", n."LocalTime", n."Long", n."OffsetTime", n."Period", n."TimeZoneId", n."ZonedDateTime"
FROM "NodaTimeTypes" AS n
WHERE date_part('minute', n."LocalDateTime")::int = 31
""");
    }

    [ConditionalTheory]
    [MemberData(nameof(IsAsyncData))]
    public async Task Second(bool async)
    {
        await AssertQuery(
            async,
            ss => ss.Set<NodaTimeTypes>().Where(t => t.LocalDateTime.Second == 33));

        AssertSql(
            """
SELECT n."Id", n."DateInterval", n."Duration", n."Instant", n."InstantRange", n."Interval", n."LocalDate", n."LocalDate2", n."LocalDateRange", n."LocalDateTime", n."LocalTime", n."Long", n."OffsetTime", n."Period", n."TimeZoneId", n."ZonedDateTime"
FROM "NodaTimeTypes" AS n
WHERE floor(date_part('second', n."LocalDateTime"))::int = 33
""");
    }

    [ConditionalTheory]
    [MemberData(nameof(IsAsyncData))]
    public async Task Date(bool async)
    {
        await AssertQuery(
            async,
            ss => ss.Set<NodaTimeTypes>().Where(t => t.LocalDateTime.Date == new LocalDate(2018, 4, 20)));

        AssertSql(
            """
SELECT n."Id", n."DateInterval", n."Duration", n."Instant", n."InstantRange", n."Interval", n."LocalDate", n."LocalDate2", n."LocalDateRange", n."LocalDateTime", n."LocalTime", n."Long", n."OffsetTime", n."Period", n."TimeZoneId", n."ZonedDateTime"
FROM "NodaTimeTypes" AS n
WHERE n."LocalDateTime"::date = DATE '2018-04-20'
""");
    }

    [ConditionalFact] // #3831
    public async Task DateTrunc()
    {
        await using var ctx = CreateContext();

        _ = await ctx.Set<NodaTimeTypes>().Select(t => EF.Functions.DateTrunc("day", t.LocalDateTime)).ToListAsync();

        AssertSql(
            """
SELECT date_trunc('day', n."LocalDateTime")
FROM "NodaTimeTypes" AS n
""");
    }

    [ConditionalFact] // #3831
    public Task DateTrunc_with_timezone_is_not_translated()
    {
        using var ctx = CreateContext();

        return AssertTranslationFailed(
            () => ctx.Set<NodaTimeTypes>()
                .Where(t => EF.Functions.DateTrunc("day", t.LocalDateTime, "Europe/Berlin") == default)
                .ToListAsync());
    }

    [ConditionalTheory]
    [MemberData(nameof(IsAsyncData))]
    public async Task Time(bool async)
    {
        await AssertQuery(
            async,
            ss => ss.Set<NodaTimeTypes>().Where(t => t.LocalDateTime.TimeOfDay == new LocalTime(10, 31, 33, 666)));

        AssertSql(
            """
SELECT n."Id", n."DateInterval", n."Duration", n."Instant", n."InstantRange", n."Interval", n."LocalDate", n."LocalDate2", n."LocalDateRange", n."LocalDateTime", n."LocalTime", n."Long", n."OffsetTime", n."Period", n."TimeZoneId", n."ZonedDateTime"
FROM "NodaTimeTypes" AS n
WHERE n."LocalDateTime"::time = TIME '10:31:33.666'
""");
    }

    [ConditionalTheory]
    [MemberData(nameof(IsAsyncData))]
    public async Task DayOfWeek(bool async)
    {
        await AssertQuery(
            async,
            ss => ss.Set<NodaTimeTypes>().Where(t => t.LocalDateTime.DayOfWeek == IsoDayOfWeek.Friday));

        AssertSql(
            """
SELECT n."Id", n."DateInterval", n."Duration", n."Instant", n."InstantRange", n."Interval", n."LocalDate", n."LocalDate2", n."LocalDateRange", n."LocalDateTime", n."LocalTime", n."Long", n."OffsetTime", n."Period", n."TimeZoneId", n."ZonedDateTime"
FROM "NodaTimeTypes" AS n
WHERE CASE floor(date_part('dow', n."LocalDateTime"))::int
    WHEN 0 THEN 7
    ELSE floor(date_part('dow', n."LocalDateTime"))::int
END = 5
""");
    }

    [ConditionalTheory]
    [MemberData(nameof(IsAsyncData))]
    public async Task InZoneLeniently_ToInstant(bool async)
    {
        await AssertQuery(
            async,
            ss => ss.Set<NodaTimeTypes>().Where(
                t => t.LocalDateTime.InZoneLeniently(DateTimeZoneProviders.Tzdb["Europe/Berlin"]).ToInstant()
                    == new ZonedDateTime(new LocalDateTime(2018, 4, 20, 8, 31, 33, 666), DateTimeZone.Utc, Offset.Zero).ToInstant()));

        AssertSql(
            """
@ToInstant='2018-04-20T08:31:33Z' (DbType = DateTime)

SELECT n."Id", n."DateInterval", n."Duration", n."Instant", n."InstantRange", n."Interval", n."LocalDate", n."LocalDate2", n."LocalDateRange", n."LocalDateTime", n."LocalTime", n."Long", n."OffsetTime", n."Period", n."TimeZoneId", n."ZonedDateTime"
FROM "NodaTimeTypes" AS n
WHERE n."LocalDateTime" AT TIME ZONE 'Europe/Berlin' = @ToInstant
""");
    }

    [ConditionalTheory]
    [MemberData(nameof(IsAsyncData))]
    public async Task InZoneLeniently_ToInstant_with_column_time_zone(bool async)
    {
        await AssertQuery(
            async,
            ss => ss.Set<NodaTimeTypes>().Where(
                t => t.LocalDateTime.InZoneLeniently(DateTimeZoneProviders.Tzdb[t.TimeZoneId]).ToInstant()
                    == new ZonedDateTime(
                        new LocalDateTime(2018, 4, 20, 8, 31, 33, 666), DateTimeZone.Utc, Offset.Zero).ToInstant()));

        AssertSql(
            """
@ToInstant='2018-04-20T08:31:33Z' (DbType = DateTime)

SELECT n."Id", n."DateInterval", n."Duration", n."Instant", n."InstantRange", n."Interval", n."LocalDate", n."LocalDate2", n."LocalDateRange", n."LocalDateTime", n."LocalTime", n."Long", n."OffsetTime", n."Period", n."TimeZoneId", n."ZonedDateTime"
FROM "NodaTimeTypes" AS n
WHERE n."LocalDateTime" AT TIME ZONE n."TimeZoneId" = @ToInstant
""");
    }

    [ConditionalFact]
    public async Task Distance()
    {
        await using var context = CreateContext();
        var closest = await context.NodaTimeTypes
            .OrderBy(t => EF.Functions.Distance(t.LocalDateTime, new LocalDateTime(2018, 4, 1, 0, 0, 0))).FirstAsync();

        Assert.Equal(1, closest.Id);

        AssertSql(
            """
SELECT n."Id", n."DateInterval", n."Duration", n."Instant", n."InstantRange", n."Interval", n."LocalDate", n."LocalDate2", n."LocalDateRange", n."LocalDateTime", n."LocalTime", n."Long", n."OffsetTime", n."Period", n."TimeZoneId", n."ZonedDateTime"
FROM "NodaTimeTypes" AS n
ORDER BY n."LocalDateTime" <-> TIMESTAMP '2018-04-01T00:00:00' NULLS FIRST
LIMIT 1
""");
    }

    [ConditionalTheory]
    [MemberData(nameof(IsAsyncData))]
    public async Task ToDateTimeUnspecified(bool async)
    {
        await AssertQuery(
            async,
            ss => ss.Set<NodaTimeTypes>()
                .Where(t => t.LocalDateTime.ToDateTimeUnspecified() == new DateTime(2018, 4, 20, 10, 31, 33, 666)));

        AssertSql(
            """
SELECT n."Id", n."DateInterval", n."Duration", n."Instant", n."InstantRange", n."Interval", n."LocalDate", n."LocalDate2", n."LocalDateRange", n."LocalDateTime", n."LocalTime", n."Long", n."OffsetTime", n."Period", n."TimeZoneId", n."ZonedDateTime"
FROM "NodaTimeTypes" AS n
WHERE n."LocalDateTime" = TIMESTAMP '2018-04-20T10:31:33.666'
""");
    }

    [ConditionalTheory]
    [MemberData(nameof(IsAsyncData))]
    public async Task Plus_Period(bool async)
    {
        await AssertQuery(
            async,
            ss => ss.Set<NodaTimeTypes>().Select(t => t.LocalDateTime.Plus(Period.FromDays(1))));

        AssertSql(
            """
SELECT n."LocalDateTime" + INTERVAL 'P1D'
FROM "NodaTimeTypes" AS n
""");
    }

    [ConditionalTheory]
    [MemberData(nameof(IsAsyncData))]
    public async Task Minus_Period(bool async)
    {
        await AssertQuery(
            async,
            ss => ss.Set<NodaTimeTypes>().Select(t => t.LocalDateTime.Minus(Period.FromMonths(1))));

        AssertSql(
            """
SELECT n."LocalDateTime" - INTERVAL 'P1M'
FROM "NodaTimeTypes" AS n
""");
    }

    [ConditionalTheory]
    [MemberData(nameof(IsAsyncData))]
    public async Task Add_static(bool async)
    {
        await AssertQuery(
            async,
            ss => ss.Set<NodaTimeTypes>().Where(t => LocalDateTime.Add(t.LocalDateTime, Period.FromDays(1)) > t.LocalDateTime));

        AssertSql(
            """
SELECT n."Id", n."DateInterval", n."Duration", n."Instant", n."InstantRange", n."Interval", n."LocalDate", n."LocalDate2", n."LocalDateRange", n."LocalDateTime", n."LocalTime", n."Long", n."OffsetTime", n."Period", n."TimeZoneId", n."ZonedDateTime"
FROM "NodaTimeTypes" AS n
WHERE n."LocalDateTime" + INTERVAL 'P1D' > n."LocalDateTime"
""");
    }

    [ConditionalTheory]
    [MemberData(nameof(IsAsyncData))]
    public async Task Subtract_static_Period(bool async)
    {
        await AssertQuery(
            async,
            ss => ss.Set<NodaTimeTypes>().Where(t => LocalDateTime.Subtract(t.LocalDateTime, Period.FromDays(1)) < t.LocalDateTime));

        AssertSql(
            """
SELECT n."Id", n."DateInterval", n."Duration", n."Instant", n."InstantRange", n."Interval", n."LocalDate", n."LocalDate2", n."LocalDateRange", n."LocalDateTime", n."LocalTime", n."Long", n."OffsetTime", n."Period", n."TimeZoneId", n."ZonedDateTime"
FROM "NodaTimeTypes" AS n
WHERE n."LocalDateTime" - INTERVAL 'P1D' < n."LocalDateTime"
""");
    }

    [ConditionalTheory]
    [MemberData(nameof(IsAsyncData))]
    public async Task PlusYears(bool async)
    {
        await AssertQuery(
            async,
            ss => ss.Set<NodaTimeTypes>().Select(t => t.LocalDateTime.PlusYears(2)));

        AssertSql(
            """
SELECT n."LocalDateTime" + make_interval(years => 2)
FROM "NodaTimeTypes" AS n
""");
    }

    [ConditionalTheory]
    [MemberData(nameof(IsAsyncData))]
    public async Task PlusMonths(bool async)
    {
        await AssertQuery(
            async,
            ss => ss.Set<NodaTimeTypes>().Select(t => t.LocalDateTime.PlusMonths(t.Id)));

        AssertSql(
            """
SELECT n."LocalDateTime" + make_interval(months => n."Id")
FROM "NodaTimeTypes" AS n
""");
    }

    [ConditionalTheory]
    [MemberData(nameof(IsAsyncData))]
    public async Task PlusWeeks(bool async)
    {
        await AssertQuery(
            async,
            ss => ss.Set<NodaTimeTypes>().Select(t => t.LocalDateTime.PlusWeeks(3)));

        AssertSql(
            """
SELECT n."LocalDateTime" + make_interval(weeks => 3)
FROM "NodaTimeTypes" AS n
""");
    }

    [ConditionalTheory]
    [MemberData(nameof(IsAsyncData))]
    public async Task PlusDays(bool async)
    {
        await AssertQuery(
            async,
            ss => ss.Set<NodaTimeTypes>().Select(t => t.LocalDateTime.PlusDays(-30)));

        AssertSql(
            """
SELECT n."LocalDateTime" + make_interval(days => -30)
FROM "NodaTimeTypes" AS n
""");
    }

    [ConditionalTheory]
    [MemberData(nameof(IsAsyncData))]
    public async Task PlusHours(bool async)
    {
        await AssertQuery(
            async,
            ss => ss.Set<NodaTimeTypes>().Select(t => t.LocalDateTime.PlusHours(20)));

        AssertSql(
            """
SELECT n."LocalDateTime" + make_interval(hours => 20::int)
FROM "NodaTimeTypes" AS n
""");
    }

    [ConditionalTheory]
    [MemberData(nameof(IsAsyncData))]
    public async Task PlusMinutes(bool async)
    {
        await AssertQuery(
            async,
            ss => ss.Set<NodaTimeTypes>().Select(t => t.LocalDateTime.PlusMinutes(t.Id)));

        AssertSql(
            """
SELECT n."LocalDateTime" + make_interval(mins => n."Id")
FROM "NodaTimeTypes" AS n
""");
    }

    [ConditionalTheory]
    [MemberData(nameof(IsAsyncData))]
    public async Task PlusSeconds(bool async)
    {
        await AssertQuery(
            async,
            ss => ss.Set<NodaTimeTypes>().Select(t => t.LocalDateTime.PlusSeconds(t.Long)));

        AssertSql(
            """
SELECT n."LocalDateTime" + make_interval(secs => n."Long"::double precision)
FROM "NodaTimeTypes" AS n
""");
    }

    [ConditionalTheory]
    [MemberData(nameof(IsAsyncData))]
    public async Task PlusMonths_month_end(bool async)
    {
        await AssertQuery(
            async,
            ss => ss.Set<NodaTimeTypes>().Select(t => t.LocalDateTime.PlusDays(-20).PlusMonths(1)));

        AssertSql(
            """
SELECT n."LocalDateTime" + make_interval(days => -20) + make_interval(months => 1)
FROM "NodaTimeTypes" AS n
""");
    }

    [ConditionalTheory]
    [MemberData(nameof(IsAsyncData))]
    public async Task PlusHours_negative_across_days(bool async)
    {
        await AssertQuery(
            async,
            ss => ss.Set<NodaTimeTypes>().Select(t => t.LocalDateTime.PlusHours(-30)));

        AssertSql(
            """
SELECT n."LocalDateTime" + make_interval(hours => -30::int)
FROM "NodaTimeTypes" AS n
""");
    }

    [ConditionalTheory]
    [MemberData(nameof(IsAsyncData))]
    public async Task PlusMilliseconds(bool async)
    {
        await AssertQuery(
            async,
            ss => ss.Set<NodaTimeTypes>().Select(t => t.LocalDateTime.PlusMilliseconds(t.Long)));

        AssertSql(
            """
SELECT n."LocalDateTime" + n."Long" * INTERVAL 'PT0.001S'
FROM "NodaTimeTypes" AS n
""");
    }

    private NodaTimeContext CreateContext()
        => Fixture.CreateContext();

    private void AssertSql(params string[] expected)
        => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);
}
