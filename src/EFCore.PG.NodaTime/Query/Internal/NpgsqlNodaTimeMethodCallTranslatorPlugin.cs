using Npgsql.EntityFrameworkCore.PostgreSQL.Query;
using Npgsql.EntityFrameworkCore.PostgreSQL.Query.Expressions;
using Npgsql.EntityFrameworkCore.PostgreSQL.Query.Expressions.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal.Mapping;

namespace Npgsql.EntityFrameworkCore.PostgreSQL.NodaTime.Query.Internal;

/// <summary>
///     Provides translation services for <see cref="NodaTime" /> members.
/// </summary>
/// <remarks>
///     See: https://www.postgresql.org/docs/current/static/functions-datetime.html
/// </remarks>
public class NpgsqlNodaTimeMethodCallTranslatorPlugin : IMethodCallTranslatorPlugin
{
    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public NpgsqlNodaTimeMethodCallTranslatorPlugin(
        IRelationalTypeMappingSource typeMappingSource,
        ISqlExpressionFactory sqlExpressionFactory)
    {
        Translators =
        [
            new NpgsqlNodaTimeMethodCallTranslator(typeMappingSource, (NpgsqlSqlExpressionFactory)sqlExpressionFactory)
        ];
    }

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public virtual IEnumerable<IMethodCallTranslator> Translators { get; }
}

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class NpgsqlNodaTimeMethodCallTranslator(
    IRelationalTypeMappingSource typeMappingSource,
    NpgsqlSqlExpressionFactory sqlExpressionFactory)
    : IMethodCallTranslator
{
    private readonly IRelationalTypeMappingSource _typeMappingSource = typeMappingSource;
    private readonly NpgsqlSqlExpressionFactory _sqlExpressionFactory = sqlExpressionFactory;

    private static readonly bool[][] TrueArrays = [[], [true], [true, true], [true, true, true]];

#pragma warning disable EF1001
    /// <inheritdoc />
    public virtual SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        var translated =
            TranslateDbFunctions(method, arguments)
            ?? TranslateArithmetic(instance, method, arguments)
            ?? TranslateConversions(instance, method, arguments)
            ?? TranslateInstant(instance, method, arguments)
            ?? TranslateZonedDateTime(instance, method, arguments)
            ?? TranslateLocalDateTime(instance, method, arguments)
            ?? TranslateLocalDate(instance, method, arguments)
            ?? TranslatePeriod(instance, method, arguments, logger)
            ?? TranslateInterval(instance, method, arguments, logger)
            ?? TranslateDateInterval(instance, method, arguments, logger);

        if (translated is not null)
        {
            return translated;
        }

        if (method.DeclaringType == typeof(IDateTimeZoneProvider) && method.Name == "get_Item" && instance is PendingDateTimeZoneProviderExpression)
        {
            // We're translating an expression such as 'DateTimeZoneProviders.Tzdb["Europe/Berlin"]'.
            // Note that the .NET type of that expression is DateTimeZone, but we just return the string ID for the time zone.
            return arguments[0];
        }

        return null;
    }

    private SqlExpression? TranslateDbFunctions(
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments)
    {
        if (method.DeclaringType != typeof(NpgsqlDbFunctionsExtensions))
        {
            return null;
        }

        return method.Name switch
        {
            nameof(NpgsqlDbFunctionsExtensions.DateTrunc) when arguments is [_, var field, var value, var timeZone]
                => TranslateDateTrunc(method.ReturnType, field, value, timeZone),

            _ => null
        };
    }

    private SqlExpression? TranslateDateTrunc(
        Type returnType,
        SqlExpression field,
        SqlExpression value,
        SqlExpression timeZone)
    {
        var type = Nullable.GetUnderlyingType(returnType) ?? returnType;

        if (type != typeof(LocalDateTime)
            && type != typeof(Instant)
            && type != typeof(ZonedDateTime)
            && type != typeof(Period)
            && type != typeof(Duration))
        {
            return null;
        }

        var fieldArgument = _sqlExpressionFactory.ApplyDefaultTypeMapping(field);
        var valueArgument = _sqlExpressionFactory.ApplyDefaultTypeMapping(value);
        var hasTimeZone = timeZone is not SqlConstantExpression { Value: null };

        if (hasTimeZone && valueArgument.TypeMapping is not (TimestampTzInstantMapping or TimestampTzZonedDateTimeMapping))
        {
            return null;
        }

        var arguments = hasTimeZone
            ? [fieldArgument, valueArgument, _sqlExpressionFactory.ApplyDefaultTypeMapping(timeZone)]
            : new[] { fieldArgument, valueArgument };

        return _sqlExpressionFactory.Function(
            "date_trunc",
            arguments,
            nullable: true,
            argumentsPropagateNullability: TrueArrays[arguments.Length],
            returnType,
            valueArgument.TypeMapping);
    }

    private SqlExpression? TranslateArithmetic(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments)
    {
        var declaringType = method.DeclaringType;

        if (declaringType != typeof(Duration)
            && declaringType != typeof(Period)
            && declaringType != typeof(Instant)
            && declaringType != typeof(ZonedDateTime)
            && declaringType != typeof(OffsetDateTime)
            && declaringType != typeof(LocalDateTime)
            && declaringType != typeof(LocalDate)
            && declaringType != typeof(LocalTime))
        {
            return null;
        }

        // instant.InZone(zone) (and localDateTime.InZoneLeniently(zone)) is translated to a PendingZonedDateTimeExpression, which
        // only exists so that a subsequent member (e.g. Hour) or ToInstant() can use its time zone. ZonedDateTime arithmetic is
        // exact, i.e. on the instant: so it's performed on the instant, and a ZonedDateTime result is pending in the same time zone.
        if (declaringType == typeof(ZonedDateTime)
            && (instance is PendingZonedDateTimeExpression ? instance : arguments.FirstOrDefault(a => a is PendingZonedDateTimeExpression))
                is PendingZonedDateTimeExpression { TimeZoneId: var timeZoneId })
        {
            var result = TranslateArithmetic(
                instance is null ? null : ToInstant(instance),
                method,
                arguments.Select(a => a.Type == typeof(ZonedDateTime) ? ToInstant(a) : a).ToArray());

            return result is not null && method.ReturnType == typeof(ZonedDateTime)
                ? new PendingZonedDateTimeExpression(result, timeZoneId)
                : result;
        }

        // Plus/Minus (instance) and Add/Subtract (static) are equivalent to the + and - operators; the heterogeneous type inference
        // they require (e.g. LocalDate + Period, Instant - Instant => Duration) is handled in NpgsqlSqlExpressionFactory.
        var (left, right) = (instance, arguments) switch
        {
            (not null, [var other]) when method.Name is nameof(Instant.Plus) or nameof(Instant.Minus) => (instance, other),
            (null, [var l, var r]) when method.Name is nameof(Instant.Add) or nameof(Instant.Subtract) => (l, r),
            _ => (null, null)
        };

        if (left is not null && right is not null)
        {
            return method.Name is nameof(Instant.Plus) or nameof(Instant.Add)
                ? _sqlExpressionFactory.Add(left, right)
                : Subtract(left, right);
        }

        if (method.Name == nameof(Duration.Negate) && instance is null && arguments is [var operand])
        {
            return _sqlExpressionFactory.Negate(operand);
        }

        // Duration.Multiply/Divide are equivalent to the * and / operators (see NpgsqlSqlExpressionFactory)
        if (declaringType == typeof(Duration) && instance is null && arguments is [var first, var second])
        {
            return method.Name switch
            {
                nameof(Duration.Multiply) => _sqlExpressionFactory.Multiply(first, second),
                nameof(Duration.Divide) => _sqlExpressionFactory.Divide(first, second),
                _ => null
            };
        }

        if (instance is null || arguments is not [var value])
        {
            return null;
        }

        if (declaringType == typeof(LocalDate))
        {
            return method.Name switch
            {
                // NodaTime adds weeks as exactly 7 days (its weeks field is a fixed-length field of 7 days, see
                // https://github.com/nodatime/nodatime/blob/3.2.2/src/NodaTime/Fields/DatePeriodFields.cs#L13).
                // PostgreSQL date + integer yields a date (see date_pli:
                // https://github.com/postgres/postgres/blob/REL_18_6/src/backend/utils/adt/date.c#L568-L588),
                // whereas date + interval yields a timestamp.
                nameof(LocalDate.PlusDays) => AddDays(value),
                nameof(LocalDate.PlusWeeks) => AddDays(
                    _sqlExpressionFactory.Multiply(_sqlExpressionFactory.ApplyDefaultTypeMapping(value), _sqlExpressionFactory.Constant(7))),
                // Like NodaTime, PostgreSQL keeps the day of month when adding months, unless it would be past the end of the new
                // month, in which case the last day of that month is used (e.g. 2018-01-31 + 1 month = 2018-02-28). See
                // https://www.postgresql.org/docs/current/functions-datetime.html and timestamp_pl_interval:
                // https://github.com/postgres/postgres/blob/REL_18_6/src/backend/utils/adt/timestamp.c#L3111-L3226
                nameof(LocalDate.PlusMonths) => _sqlExpressionFactory.Add(instance, MakeInterval("months", value, typeof(Period))),
                nameof(LocalDate.PlusYears) => _sqlExpressionFactory.Add(instance, MakeInterval("years", value, typeof(Period))),
                _ => null
            };

            SqlExpression AddDays(SqlExpression days)
                => new SqlBinaryExpression(
                    ExpressionType.Add,
                    _sqlExpressionFactory.ApplyDefaultTypeMapping(instance),
                    _sqlExpressionFactory.ApplyDefaultTypeMapping(days),
                    typeof(LocalDate),
                    _typeMappingSource.FindMapping(typeof(LocalDate)));
        }

        // LocalDateTime has all of these; ZonedDateTime, OffsetDateTime and LocalTime only have the time-based ones.
        var intervalPart = method.Name switch
        {
            nameof(LocalDateTime.PlusYears) => "years",
            nameof(LocalDateTime.PlusMonths) => "months",
            nameof(LocalDateTime.PlusWeeks) => "weeks",
            nameof(LocalDateTime.PlusDays) => "days",
            nameof(LocalDateTime.PlusHours) => "hours",
            nameof(LocalDateTime.PlusMinutes) => "mins",
            nameof(LocalDateTime.PlusSeconds) => "secs",
            _ => null
        };

        // ZonedDateTime/OffsetDateTime arithmetic is over Duration, whereas LocalDateTime/LocalTime arithmetic is over Period.
        var intervalType = declaringType == typeof(ZonedDateTime) || declaringType == typeof(OffsetDateTime)
            ? typeof(Duration)
            : typeof(Period);

        if (method.Name == nameof(LocalDateTime.PlusMilliseconds))
        {
            return _sqlExpressionFactory.Add(instance, MakeMillisecondsInterval(value, intervalType));
        }

        // PlusTicks and PlusNanoseconds aren't translated, since PostgreSQL's resolution is one microsecond
        // (https://www.postgresql.org/docs/current/datatype-datetime.html).
        if (intervalPart is null)
        {
            return null;
        }

        // Note that make_interval with only time parts (hours, mins, secs) yields an exact interval, so timestamptz + interval is time
        // zone-independent here.
        return _sqlExpressionFactory.Add(instance, MakeInterval(intervalPart, value, intervalType));
    }

    // The methods are translated like the corresponding operators, so PostgreSQL's results can differ from NodaTime's in the same cases:
    // * LocalDate/LocalDateTime +/- Period: NodaTime adds a period's years and then its months, clamping to the end of the month in
    //   between, whereas PostgreSQL stores the years and months of an interval together as months (e.g. 2016-02-29 + P1Y1M is
    //   2017-03-28 in NodaTime but 2017-03-29 in PostgreSQL). See https://nodatime.org/3.2.x/userguide/arithmetic and
    //   https://www.postgresql.org/docs/current/datatype-datetime.html#DATATYPE-INTERVAL-INPUT
    // * Period +/- Period: Npgsql normalizes the periods it reads (e.g. P3DT-2H is read back as P2DT22H). See PeriodConverter:
    //   https://github.com/npgsql/npgsql/blob/v10.0.3/src/Npgsql.NodaTime/Internal/PeriodConverter.cs#L42
    // * LocalDate - LocalDate: age() subtracts field by field, borrowing the length of the earlier date's month when the days are
    //   negative, whereas NodaTime adds as many whole months as fit to the start date (clamping to the month end) and counts the
    //   remaining days (e.g. 2018-04-30 - 2018-03-31 is 30 days in PostgreSQL but 1 month in NodaTime). See timestamp_age:
    //   https://github.com/postgres/postgres/blob/REL_18_6/src/backend/utils/adt/timestamp.c#L4317-L4454
    //   and Period.DateComponentsBetween: https://github.com/nodatime/nodatime/blob/3.2.2/src/NodaTime/Period.cs#L547
    // * LocalDateTime - LocalDateTime: DESIGN QUESTION: translated with age() like LocalDate - LocalDate (see above), rather than with
    //   timestamp subtraction, which returns days and time but never months or years.
    private SqlExpression Subtract(SqlExpression left, SqlExpression right)
    {
        // Like the LocalDate/LocalDateTime - operator (see NpgsqlSqlTranslatingExpressionVisitor.VisitBinary), use age(), which returns a
        // calendar period rather than date subtraction's integer number of days or timestamp subtraction's days and time; cast dates to
        // timestamp to avoid time zone-dependent arithmetic.
        var leftType = left.Type.UnwrapNullableType();
        if ((leftType == typeof(LocalDate) || leftType == typeof(LocalDateTime)) && right.Type.UnwrapNullableType() == leftType)
        {
            var timestampMapping = _typeMappingSource.FindMapping(typeof(DateTime), "timestamp without time zone");

            return _sqlExpressionFactory.Function(
                "age",
                leftType == typeof(LocalDate)
                    ?
                    [
                        _sqlExpressionFactory.Convert(left, typeof(DateTime), timestampMapping),
                        _sqlExpressionFactory.Convert(right, typeof(DateTime), timestampMapping)
                    ]
                    : [left, right],
                nullable: true,
                argumentsPropagateNullability: TrueArrays[2],
                typeof(Period));
        }

        return _sqlExpressionFactory.Subtract(left, right);
    }

    // Conversions between types which are mapped to the same store type (no PG operation), Unix times, and Duration factories
    private SqlExpression? TranslateConversions(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments)
    {
        var declaringType = method.DeclaringType;

        if (instance is not null && arguments is [])
        {
            return (declaringType?.Name, method.Name) switch
            {
                (nameof(LocalDate), nameof(LocalDate.ToDateOnly))
                    => Retype(instance, typeof(DateOnly), static value => ((LocalDate)value).ToDateOnly()),
                (nameof(LocalTime), nameof(LocalTime.ToTimeOnly))
                    => Retype(instance, typeof(TimeOnly), static value => ((LocalTime)value).ToTimeOnly()),
                (nameof(Duration), nameof(Duration.ToTimeSpan))
                    => Retype(instance, typeof(TimeSpan), static value => ((Duration)value).ToTimeSpan()),
                (nameof(OffsetDateTime), nameof(OffsetDateTime.ToInstant))
                    => Retype(instance, typeof(Instant), static value => ((OffsetDateTime)value).ToInstant()),

                // The number of seconds/milliseconds since 1970-01-01, rounded down like NodaTime; EXTRACT(epoch ...) is exact
                (nameof(Instant), nameof(Instant.ToUnixTimeSeconds)) => Floor(_sqlExpressionFactory.Extract("epoch", instance)),
                (nameof(Instant), nameof(Instant.ToUnixTimeMilliseconds))
                    => Floor(_sqlExpressionFactory.Multiply(_sqlExpressionFactory.Extract("epoch", instance), _sqlExpressionFactory.Constant(1000m))),
                _ => null
            };
        }

        if (instance is not null || declaringType != typeof(Instant) && declaringType != typeof(Duration))
        {
            return null;
        }

        return (declaringType.Name, method.Name, arguments) switch
        {
            (nameof(Instant), nameof(Instant.FromUnixTimeSeconds), [var seconds]) => ToTimestamp(seconds),
            (nameof(Instant), nameof(Instant.FromUnixTimeMilliseconds), [var milliseconds])
                => ToTimestamp(
                    _sqlExpressionFactory.Divide(
                        _sqlExpressionFactory.Convert(milliseconds, typeof(double)), _sqlExpressionFactory.Constant(1000d))),
            (nameof(Instant), nameof(Instant.Max), [var a, var b]) => GreatestOrLeast("GREATEST", a, b),
            (nameof(Instant), nameof(Instant.Min), [var a, var b]) => GreatestOrLeast("LEAST", a, b),

            // Duration.FromDays(n) etc.: n * INTERVAL '1 day' (PostgreSQL interval * double precision)
            (nameof(Duration), nameof(Duration.FromDays), [var n]) => Multiple(n, Duration.FromDays(1)),
            (nameof(Duration), nameof(Duration.FromHours), [var n]) => Multiple(n, Duration.FromHours(1)),
            (nameof(Duration), nameof(Duration.FromMinutes), [var n]) => Multiple(n, Duration.FromMinutes(1)),
            (nameof(Duration), nameof(Duration.FromSeconds), [var n]) => Multiple(n, Duration.FromSeconds(1)),
            (nameof(Duration), nameof(Duration.FromMilliseconds), [var n]) => Multiple(n, Duration.FromMilliseconds(1)),
            (nameof(Duration), nameof(Duration.FromTimeSpan), [var timeSpan])
                => Retype(timeSpan, typeof(Duration), static value => Duration.FromTimeSpan((TimeSpan)value)),
            _ => null
        };

        SqlExpression Floor(SqlExpression value)
            => _sqlExpressionFactory.Convert(
                _sqlExpressionFactory.Function("floor", [value], nullable: true, argumentsPropagateNullability: TrueArrays[1], typeof(decimal)),
                typeof(long));

        SqlExpression ToTimestamp(SqlExpression seconds)
            => _sqlExpressionFactory.Function(
                "to_timestamp",
                [seconds],
                nullable: true,
                argumentsPropagateNullability: TrueArrays[1],
                typeof(Instant),
                _typeMappingSource.FindMapping(typeof(Instant)));

        SqlExpression GreatestOrLeast(string name, SqlExpression a, SqlExpression b)
        {
            var typeMapping = Microsoft.EntityFrameworkCore.Query.ExpressionExtensions.InferTypeMapping(a, b) ?? _typeMappingSource.FindMapping(typeof(Instant));

            return _sqlExpressionFactory.Function(
                name,
                [_sqlExpressionFactory.ApplyTypeMapping(a, typeMapping), _sqlExpressionFactory.ApplyTypeMapping(b, typeMapping)],
                nullable: true,
                argumentsPropagateNullability: TrueArrays[2],
                typeof(Instant),
                typeMapping);
        }

        SqlExpression? Multiple(SqlExpression n, Duration unit)
            => n.Type == typeof(double) || n.Type == typeof(long) || n.Type == typeof(int)
                ? _sqlExpressionFactory.Multiply(n, _sqlExpressionFactory.Constant(unit))
                : null;
    }

    private SqlExpression? TranslateInstant(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments)
        => method.Name switch
        {
            nameof(SystemClock.GetCurrentInstant) when method.DeclaringType == typeof(SystemClock)
                => NpgsqlNodaTimeTypeMappingSourcePlugin.LegacyTimestampBehavior
                    ? _sqlExpressionFactory.AtTimeZone(
                        _sqlExpressionFactory.Function(
                            "NOW",
                            [],
                            nullable: false,
                            argumentsPropagateNullability: [],
                            method.ReturnType),
                        _sqlExpressionFactory.Constant("UTC"),
                        method.ReturnType)
                    : _sqlExpressionFactory.Function(
                        "NOW",
                        [],
                        nullable: false,
                        argumentsPropagateNullability: [],
                        method.ReturnType,
                        _typeMappingSource.FindMapping(typeof(Instant), "timestamp with time zone")),

            // Instant -> ZonedDateTime is a no-op (different types in .NET but both mapped to timestamptz in PG)
            nameof(Instant.InUtc) when method.DeclaringType == typeof(Instant)
                => Retype(instance!, typeof(ZonedDateTime), static value => ((Instant)value).InUtc()),

            // When InZone is called, we have a mismatch: on the .NET NodaTime side, we have a ZonedDateTime; but on the PostgreSQL side,
            // the AT TIME ZONE expression returns a 'timestamp without time zone' (when applied to a 'timestamp with time zone', which is
            // what ZonedDateTime is mapped to).
            nameof(Instant.InZone) when method.DeclaringType == typeof(Instant)
                => new PendingZonedDateTimeExpression(instance!, arguments[0]),

            nameof(Instant.ToDateTimeUtc) when method.DeclaringType == typeof(Instant)
                => Retype(
                    instance!,
                    typeof(DateTime),
                    static value => ((Instant)value).ToDateTimeUtc(),
                    _typeMappingSource.FindMapping(typeof(DateTime), "timestamp with time zone")),

            nameof(NpgsqlNodaTimeDbFunctionsExtensions.Distance)
                when method.DeclaringType == typeof(NpgsqlNodaTimeDbFunctionsExtensions)
                => _sqlExpressionFactory.MakePostgresBinary(PgExpressionType.Distance, arguments[1], arguments[2]),

            _ => null
        };

    /// <summary>
    ///     Gives an expression the .NET type and type mapping of another type which is mapped to the same store type, so that it can be
    ///     further composed and projected as that type. <paramref name="convertValue" /> converts a constant's value to the new type.
    /// </summary>
    private SqlExpression Retype(
        SqlExpression expression,
        Type type,
        Func<object, object> convertValue,
        RelationalTypeMapping? typeMapping = null)
    {
        expression = _sqlExpressionFactory.ApplyDefaultTypeMapping(expression);
        typeMapping ??= _typeMappingSource.FindMapping(type)!;

        // Compare the database types rather than the store type names, which can be aliases (e.g. time and time without time zone)
        if ((expression.TypeMapping as INpgsqlTypeMapping)?.NpgsqlDbType != (typeMapping as INpgsqlTypeMapping)?.NpgsqlDbType)
        {
            return expression;
        }

        // EF removes casts between identical store types, so a Convert would lose the new type mapping (making the expression
        // unreadable as the new type when projected); rebuild the expression with the new type instead.
        return expression switch
        {
            SqlConstantExpression { Value: { } value } => new SqlConstantExpression(convertValue(value), type, typeMapping),
            ColumnExpression c => new ColumnExpression(c.Name, c.TableAlias, c.Column, type, typeMapping, c.IsNullable),
            // A parameter inlined as a constant (e.g. EF.Constant()) keeps its original value (see NpgsqlSqlNullabilityProcessor)
            SqlParameterExpression p => new SqlParameterExpression(p.InvariantName, p.Name, type, p.IsNullable, p.TranslationMode, typeMapping),
            SqlBinaryExpression b => new SqlBinaryExpression(b.OperatorType, b.Left, b.Right, type, typeMapping),
            AtTimeZoneExpression a => new AtTimeZoneExpression(a.Operand, a.TimeZone, type, typeMapping),
            _ => _sqlExpressionFactory.Convert(expression, type, typeMapping)
        };
    }

    private SqlExpression? TranslateZonedDateTime(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments)
        => method.Name switch
        {
            nameof(ZonedDateTime.ToInstant) when method.DeclaringType == typeof(ZonedDateTime) => ToInstant(instance!),

            nameof(NpgsqlNodaTimeDbFunctionsExtensions.Distance)
                when method.DeclaringType == typeof(NpgsqlNodaTimeDbFunctionsExtensions)
                => _sqlExpressionFactory.MakePostgresBinary(PgExpressionType.Distance, arguments[1], arguments[2]),

            _ => null
        };

    private SqlExpression ToInstant(SqlExpression zonedDateTime)
        => zonedDateTime switch
        {
            // instant.InZone(zone).ToInstant() is the instant itself
            PendingZonedDateTimeExpression { Operand.Type: var operandType } instantInZone when operandType == typeof(Instant)
                => instantInZone.Operand,

            // localDateTime.InZoneLeniently(DateTimeZoneProviders.Tzdb["Europe/Berlin"]).ToInstant()
            PendingZonedDateTimeExpression pendingZonedDateTime
                => _sqlExpressionFactory.AtTimeZone(
                    pendingZonedDateTime.Operand,
                    pendingZonedDateTime.TimeZoneId,
                    typeof(Instant),
                    _typeMappingSource.FindMapping(typeof(Instant))),

            // Otherwise, ZonedDateTime -> ToInstant is a no-op (different types in .NET but both mapped to timestamptz in PG)
            _ => Retype(zonedDateTime, typeof(Instant), static value => ((ZonedDateTime)value).ToInstant())
        };

    private SqlExpression? TranslateLocalDateTime(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments)
        => method.Name switch
        {
            nameof(LocalDateTime.InZoneLeniently) when method.DeclaringType == typeof(LocalDateTime)
                => new PendingZonedDateTimeExpression(instance!, arguments[0]),

            nameof(NpgsqlNodaTimeDbFunctionsExtensions.Distance)
                when method.DeclaringType == typeof(NpgsqlNodaTimeDbFunctionsExtensions)
                => _sqlExpressionFactory.MakePostgresBinary(PgExpressionType.Distance, arguments[1], arguments[2]),

            nameof(LocalDateTime.ToDateTimeUnspecified) when method.DeclaringType == typeof(LocalDateTime)
                => Retype(
                    instance!,
                    typeof(DateTime),
                    static value => ((LocalDateTime)value).ToDateTimeUnspecified(),
                    _typeMappingSource.FindMapping(typeof(DateTime), "timestamp without time zone")),

            // A local date and time in UTC is a timestamptz
            nameof(LocalDateTime.InUtc) when method.DeclaringType == typeof(LocalDateTime)
                => _sqlExpressionFactory.AtTimeZone(
                    instance!,
                    _sqlExpressionFactory.Constant("UTC"),
                    typeof(ZonedDateTime),
                    _typeMappingSource.FindMapping(typeof(ZonedDateTime))),

            _ => null
        };

    private SqlExpression? TranslateLocalDate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments)
    {
        if (method.DeclaringType == typeof(NpgsqlNodaTimeDbFunctionsExtensions) && method.Name == nameof(NpgsqlNodaTimeDbFunctionsExtensions.Distance))
        {
            return _sqlExpressionFactory.MakePostgresBinary(PgExpressionType.Distance, arguments[1], arguments[2]);
        }

        if (method.DeclaringType == typeof(LocalDate))
        {
            return method.Name switch
            {
                nameof(LocalDate.At) => new SqlBinaryExpression(
                    ExpressionType.Add,
                    _sqlExpressionFactory.ApplyDefaultTypeMapping(instance!),
                    _sqlExpressionFactory.ApplyDefaultTypeMapping(arguments[0]),
                    typeof(LocalDateTime),
                    _typeMappingSource.FindMapping(typeof(LocalDateTime))),

                nameof(LocalDate.AtMidnight) => new SqlBinaryExpression(
                    ExpressionType.Add,
                    _sqlExpressionFactory.ApplyDefaultTypeMapping(instance!),
                    new SqlConstantExpression(new LocalTime(0, 0, 0), _typeMappingSource.FindMapping(typeof(LocalTime))),
                    typeof(LocalDateTime),
                    _typeMappingSource.FindMapping(typeof(LocalDateTime))),

                _ => null
            };
        }
        return null;
    }

    private SqlExpression? TranslatePeriod(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (method.DeclaringType != typeof(Period))
        {
            return null;
        }

        return method.Name switch
        {
            nameof(Period.FromYears) => MakeInterval("years", arguments[0], typeof(Period)),
            nameof(Period.FromMonths) => MakeInterval("months", arguments[0], typeof(Period)),
            nameof(Period.FromWeeks) => MakeInterval("weeks", arguments[0], typeof(Period)),
            nameof(Period.FromDays) => MakeInterval("days", arguments[0], typeof(Period)),
            nameof(Period.FromHours) => MakeInterval("hours", arguments[0], typeof(Period)),
            nameof(Period.FromMinutes) => MakeInterval("mins", arguments[0], typeof(Period)),
            nameof(Period.FromSeconds) => MakeInterval("secs", arguments[0], typeof(Period)),
            _ => null
        };
    }

    /// <summary>
    ///     Creates an interval of the given number of milliseconds. PostgreSQL's resolution is one microsecond, so milliseconds are
    ///     represented exactly; interval * double precision rounds to the nearest microsecond, so this is exact for intervals of up to
    ///     2^53 microseconds (unlike make_interval(secs => n / 1000.0), where the division can be off by one microsecond). See
    ///     interval_mul: https://github.com/postgres/postgres/blob/REL_18_6/src/backend/utils/adt/timestamp.c#L3629-L3747
    /// </summary>
    private SqlExpression MakeMillisecondsInterval(SqlExpression milliseconds, Type intervalType)
    {
        var intervalMapping = _typeMappingSource.FindMapping(intervalType)!;

        return new SqlBinaryExpression(
            ExpressionType.Multiply,
            _sqlExpressionFactory.ApplyDefaultTypeMapping(milliseconds),
            _sqlExpressionFactory.Constant(
                intervalType == typeof(Duration) ? Duration.FromMilliseconds(1) : Period.FromMilliseconds(1), intervalMapping),
            intervalType,
            intervalMapping);
    }

    private PgFunctionExpression MakeInterval(string intervalPart, SqlExpression value, Type returnType)
    {
        value = _sqlExpressionFactory.ApplyDefaultTypeMapping(value);

        if (intervalPart == "secs")
        {
            value = _sqlExpressionFactory.Convert(value, typeof(double), _typeMappingSource.FindMapping(typeof(double)));
        }
        else if (value.Type == typeof(long))
        {
            // NodaTime methods such as Period.FromHours/Minutes accept a long parameter, but PG make_interval accepts an int.
            // If the parameter happens to be an int cast up to a long, just unwrap it, otherwise downcast from bigint to int
            // (this will throw on the PG side if the bigint is out of int range)
            value = value is SqlUnaryExpression { OperatorType: ExpressionType.Convert } convertExpression
                && convertExpression.TypeMapping!.StoreType == "bigint"
                && convertExpression.Operand.TypeMapping!.StoreType == "integer"
                    ? convertExpression.Operand
                    : _sqlExpressionFactory.Convert(value, typeof(int), _typeMappingSource.FindMapping(typeof(int)));
        }

        return PgFunctionExpression.CreateWithNamedArguments(
            "make_interval",
            [value],
            [intervalPart],
            nullable: true,
            argumentsPropagateNullability: TrueArrays[1],
            builtIn: true,
            returnType,
            typeMapping: null);
    }

    private SqlExpression? TranslateInterval(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (method.DeclaringType == typeof(Interval) && method.Name == nameof(Interval.Contains))
        {
            return _sqlExpressionFactory.Contains(instance!, arguments[0]);
        }

        return null;
    }

    private SqlExpression? TranslateDateInterval(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (method.DeclaringType != typeof(DateInterval) || instance is null || arguments is not [var argument])
        {
            return null;
        }

        // When both sides are DateIntervals without a type mapping (e.g. two parameters), fall back to the default mapping.
        if (argument.Type == typeof(DateInterval))
        {
            var typeMapping = Microsoft.EntityFrameworkCore.Query.ExpressionExtensions.InferTypeMapping(instance, argument)
                ?? _typeMappingSource.FindMapping(typeof(DateInterval));
            instance = _sqlExpressionFactory.ApplyTypeMapping(instance, typeMapping);
            argument = _sqlExpressionFactory.ApplyTypeMapping(argument, typeMapping);
        }

        return method.Name switch
        {
            nameof(DateInterval.Contains) => _sqlExpressionFactory.Contains(instance, argument),

            // NodaTime returns null for intervals which don't overlap, whereas PostgreSQL returns an empty range.
            nameof(DateInterval.Intersection) => _sqlExpressionFactory.Case(
                [
                    new CaseWhenClause(
                        _sqlExpressionFactory.MakePostgresBinary(PgExpressionType.Overlaps, instance, argument),
                        _sqlExpressionFactory.MakePostgresBinary(PgExpressionType.RangeIntersect, instance, argument))
                ],
                elseResult: null),

            // NodaTime returns null for intervals which neither overlap nor are contiguous, whereas PostgreSQL raises an error.
            nameof(DateInterval.Union) => _sqlExpressionFactory.Case(
                [
                    new CaseWhenClause(
                        _sqlExpressionFactory.OrElse(
                            _sqlExpressionFactory.MakePostgresBinary(PgExpressionType.Overlaps, instance, argument),
                            _sqlExpressionFactory.MakePostgresBinary(PgExpressionType.RangeIsAdjacentTo, instance, argument)),
                        _sqlExpressionFactory.MakePostgresBinary(PgExpressionType.RangeUnion, instance, argument))
                ],
                elseResult: null),

            _ => null
        };
    }
#pragma warning restore EF1001
}
