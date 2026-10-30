using Npgsql.EntityFrameworkCore.PostgreSQL.Query.Expressions;
using Npgsql.EntityFrameworkCore.PostgreSQL.Query.Expressions.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal.Mapping;
using static Npgsql.EntityFrameworkCore.PostgreSQL.Utilities.Statics;

namespace Npgsql.EntityFrameworkCore.PostgreSQL.Query.ExpressionTranslators.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class NpgsqlDateTimeMethodTranslator(
    IRelationalTypeMappingSource typeMappingSource,
    NpgsqlSqlExpressionFactory sqlExpressionFactory)
    : IMethodCallTranslator
{
    private readonly IRelationalTypeMappingSource _typeMappingSource = typeMappingSource;
    private readonly NpgsqlSqlExpressionFactory _sqlExpressionFactory = sqlExpressionFactory;
    private readonly RelationalTypeMapping _timestampMapping = typeMappingSource.FindMapping(typeof(DateTime), "timestamp without time zone")!;
    private readonly RelationalTypeMapping _timestampTzMapping = typeMappingSource.FindMapping(typeof(DateTime), "timestamp with time zone")!;
    private readonly RelationalTypeMapping _intervalMapping = typeMappingSource.FindMapping(typeof(TimeSpan), "interval")!;
    private readonly RelationalTypeMapping _textMapping = typeMappingSource.FindMapping("text")!;

    /// <inheritdoc />
    public virtual SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        => TranslateDbFunctions(method, arguments)
           ?? TranslateDateTime(instance, method, arguments)
           ?? TranslateDateOnly(instance, method, arguments)
           ?? TranslateTimeOnly(instance, method, arguments)
           ?? TranslateTimeZoneInfo(method, arguments)
           ?? TranslateArithmetic(instance, method, arguments)
           ?? TranslateDatePart(instance, method, arguments);

    // These methods are equivalent to the corresponding operators, which NpgsqlSqlExpressionFactory translates (e.g. timestamptz arithmetic
    // is performed in UTC).
    private SqlExpression? TranslateArithmetic(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments)
    {
        if (instance is null)
        {
            return method.Name switch
            {
                // TimeSpan.FromDays(n) etc.: n * INTERVAL '1 day' (PostgreSQL interval * double precision)
                nameof(TimeSpan.FromDays) when FromValue(typeof(TimeSpan)) is { } n => Multiple(n, TimeSpan.FromDays(1)),
                nameof(TimeSpan.FromHours) when FromValue(typeof(TimeSpan)) is { } n => Multiple(n, TimeSpan.FromHours(1)),
                nameof(TimeSpan.FromMinutes) when FromValue(typeof(TimeSpan)) is { } n => Multiple(n, TimeSpan.FromMinutes(1)),
                nameof(TimeSpan.FromSeconds) when FromValue(typeof(TimeSpan)) is { } n => Multiple(n, TimeSpan.FromSeconds(1)),
                nameof(TimeSpan.FromMilliseconds) when FromValue(typeof(TimeSpan)) is { } n => Multiple(n, TimeSpan.FromMilliseconds(1)),

                nameof(DateTimeOffset.FromUnixTimeSeconds) when method.DeclaringType == typeof(DateTimeOffset)
                    => ToTimestamp(arguments[0]),
                nameof(DateTimeOffset.FromUnixTimeMilliseconds) when method.DeclaringType == typeof(DateTimeOffset)
                    => ToTimestamp(
                        _sqlExpressionFactory.Divide(
                            _sqlExpressionFactory.Convert(arguments[0], typeof(double)), _sqlExpressionFactory.Constant(1000d))),
                _ => null
            };

            // A single number argument (.NET's TimeSpan.FromMilliseconds(long) also has an optional microseconds argument)
            SqlExpression? FromValue(Type declaringType)
                => method.DeclaringType == declaringType
                    && (arguments.Count == 1 || arguments is [_, SqlConstantExpression { Value: 0L }])
                    && arguments[0].Type is var type && (type == typeof(double) || type == typeof(long) || type == typeof(int))
                        ? arguments[0]
                        : null;

            SqlExpression Multiple(SqlExpression n, TimeSpan unit)
                => _sqlExpressionFactory.Multiply(n, _sqlExpressionFactory.Constant(unit));

            SqlExpression ToTimestamp(SqlExpression seconds)
                => _sqlExpressionFactory.Function(
                    "to_timestamp",
                    [seconds],
                    nullable: true,
                    argumentsPropagateNullability: TrueArrays[1],
                    typeof(DateTimeOffset),
                    _typeMappingSource.FindMapping(typeof(DateTimeOffset)));
        }

        if (method.DeclaringType == typeof(DateTimeOffset) && arguments is [])
        {
            // The number of seconds/milliseconds since 1970-01-01, rounded down like .NET; EXTRACT(epoch ...) is exact, unlike date_part()
            switch (method.Name)
            {
                case nameof(DateTimeOffset.ToUnixTimeSeconds):
                    return Floor(_sqlExpressionFactory.Extract("epoch", instance));
                case nameof(DateTimeOffset.ToUnixTimeMilliseconds):
                    return Floor(_sqlExpressionFactory.Multiply(_sqlExpressionFactory.Extract("epoch", instance), _sqlExpressionFactory.Constant(1000m)));
            }

            SqlExpression Floor(SqlExpression value)
                => _sqlExpressionFactory.Convert(
                    _sqlExpressionFactory.Function(
                        "floor", [value], nullable: true, argumentsPropagateNullability: TrueArrays[1], typeof(decimal)),
                    typeof(long));
        }

        if (method.DeclaringType == typeof(DateTime) || method.DeclaringType == typeof(DateTimeOffset))
        {
            return method.Name switch
            {
                nameof(DateTime.Add) when arguments is [var timeSpan] => _sqlExpressionFactory.Add(instance, timeSpan),
                nameof(DateTime.Subtract) when arguments is [var other] => _sqlExpressionFactory.Subtract(instance, other),
                _ => null
            };
        }

        if (method.DeclaringType == typeof(TimeSpan))
        {
            return method.Name switch
            {
                nameof(TimeSpan.Add) when arguments is [var other] => _sqlExpressionFactory.Add(instance, other),
                nameof(TimeSpan.Subtract) when arguments is [var other] => _sqlExpressionFactory.Subtract(instance, other),
                nameof(TimeSpan.Negate) when arguments is [] => _sqlExpressionFactory.Negate(instance),
                nameof(TimeSpan.Multiply) when arguments is [var factor] => _sqlExpressionFactory.Multiply(instance, factor),
                nameof(TimeSpan.Divide) when arguments is [var divisor] => _sqlExpressionFactory.Divide(instance, divisor),

                // The absolute value (there's no abs() for interval)
                nameof(TimeSpan.Duration) when arguments is [] && _sqlExpressionFactory.ApplyDefaultTypeMapping(instance) is var timeSpan
                    => _sqlExpressionFactory.Function(
                        "GREATEST",
                        [timeSpan, _sqlExpressionFactory.Negate(timeSpan)],
                        nullable: true,
                        argumentsPropagateNullability: TrueArrays[2],
                        typeof(TimeSpan),
                        timeSpan.TypeMapping),
                _ => null
            };
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

        if (type != typeof(DateTime) && type != typeof(DateTimeOffset) && type != typeof(TimeSpan))
        {
            return null;
        }

        var fieldArgument = _sqlExpressionFactory.ApplyDefaultTypeMapping(field);
        var valueArgument = _sqlExpressionFactory.ApplyDefaultTypeMapping(value);
        var hasTimeZone = timeZone is not SqlConstantExpression { Value: null };

        if (hasTimeZone && valueArgument.TypeMapping is not NpgsqlTimestampTzTypeMapping)
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

    private SqlExpression? TranslateDatePart(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments)
    {
        if (instance is null
            || (method.DeclaringType != typeof(DateTime) && method.DeclaringType != typeof(DateTimeOffset) && method.DeclaringType != typeof(TimeOnly)))
        {
            return null;
        }

        var datePart = method.Name switch
        {
            nameof(DateTime.AddYears) => "years",
            nameof(DateTime.AddMonths) => "months",
            nameof(DateTime.AddDays) => "days",
            nameof(DateTime.AddHours) => "hours",
            nameof(DateTime.AddMinutes) => "mins",
            nameof(DateTime.AddSeconds) => "secs",
            nameof(DateTime.AddMilliseconds) => "milliseconds",
            nameof(DateTime.AddMicroseconds) => "microseconds",
            _ => null
        };

        if (datePart is null || CreateIntervalExpression(arguments[0], datePart) is not { } interval)
        {
            return null;
        }

        // With the legacy timestamp behavior, a DateTimeOffset has the local offset, and AddMonths/AddYears add calendar months in that
        // offset, like PostgreSQL's timestamptz + interval in the session time zone; so that arithmetic is left as-is (the SQL binary
        // expression is created with its type mapping, so NpgsqlSqlExpressionFactory doesn't perform it in UTC).
        // AddDays etc. add an exact amount of time, so they're performed in UTC like other DateTimeOffset arithmetic.
        if (NpgsqlTypeMappingSource.LegacyTimestampBehavior
            && instance.Type == typeof(DateTimeOffset)
            && datePart is "years" or "months")
        {
            var dateTimeOffset = _sqlExpressionFactory.ApplyDefaultTypeMapping(instance);
            return new SqlBinaryExpression(
                ExpressionType.Add, dateTimeOffset, interval, typeof(DateTimeOffset), dateTimeOffset.TypeMapping);
        }

        // PostgreSQL timestamptz + interval adds days and months in the session time zone,
        // whereas DateTime/DateTimeOffset values mapped to timestamptz are UTC.
        // The arithmetic is performed on a UTC timestamp instead by NpgsqlSqlExpressionFactory,
        // once the instance's type mapping is known (e.g. a parameter's is inferred from the rest of the query).
        return _sqlExpressionFactory.Add(instance, interval, instance.TypeMapping);
    }

    private SqlExpression? TranslateDateTime(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments)
    {
        if (instance is null)
        {
            if (method.DeclaringType == typeof(DateTime) && method.Name == nameof(DateTime.SpecifyKind))
            {
                if (arguments[1] is not SqlConstantExpression { Value: DateTimeKind kind })
                {
                    throw new InvalidOperationException("Translating SpecifyKind is only supported with a constant Kind argument");
                }

                var typeMapping = arguments[0].TypeMapping;

                if (typeMapping is not NpgsqlTimestampTypeMapping and not NpgsqlTimestampTzTypeMapping)
                {
                    throw new InvalidOperationException("Translating SpecifyKind is only supported on timestamp/timestamptz columns");
                }

                if (kind == DateTimeKind.Utc)
                {
                    return typeMapping is NpgsqlTimestampTypeMapping
                        ? _sqlExpressionFactory.AtUtc(arguments[0])
                        : arguments[0];
                }

                if (kind is DateTimeKind.Unspecified or DateTimeKind.Local)
                {
                    return typeMapping is NpgsqlTimestampTzTypeMapping
                        ? _sqlExpressionFactory.AtUtc(arguments[0])
                        : arguments[0];
                }
            }

            if (method.DeclaringType == typeof(NpgsqlDbFunctionsExtensions)
                && method.Name == nameof(NpgsqlDbFunctionsExtensions.Distance)
                && arguments is [_, var dateTime1, var dateTime2]
                && dateTime1.Type == typeof(DateTime))
            {
                return _sqlExpressionFactory.MakePostgresBinary(PgExpressionType.Distance, dateTime1, dateTime2);
            }
        }
        else
        {
            if (method.DeclaringType == typeof(DateTime) && method.Name == nameof(DateTime.ToUniversalTime))
            {
                return _sqlExpressionFactory.Convert(instance, method.ReturnType, _timestampTzMapping);
            }

            if (method.DeclaringType == typeof(DateTime) && method.Name == nameof(DateTime.ToLocalTime))
            {
                return _sqlExpressionFactory.Convert(instance, method.ReturnType, _timestampMapping);
            }
        }

        return null;
    }

    private SqlExpression? TranslateDateOnly(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments)
    {
        if (instance is null)
        {
            if (method.DeclaringType == typeof(DateOnly) && method.Name == nameof(DateOnly.FromDateTime))
            {
                // Note: converting timestamptz to date performs a timezone conversion, which is not what .NET DateOnly.FromDateTime does.
                // So if our operand is a timestamptz, we first change the type to timestamp with AT TIME ZONE 'UTC' (returns the same value
                // but as a timestamptz).
                // If our operand is already timestamp, no need to do anything. We throw for anything else to avoid accidentally applying
                // AT TIME ZONE to a non-timestamptz, which would do a timezone conversion
                // An argument whose type mapping isn't known yet (e.g. a parameter) gets the default one.
                var argument = arguments[0].TypeMapping is null
                    ? _sqlExpressionFactory.ApplyDefaultTypeMapping(arguments[0])
                    : arguments[0];
                var dateTime = argument.TypeMapping switch
                {
                    NpgsqlTimestampTypeMapping => argument,
                    NpgsqlTimestampTzTypeMapping => _sqlExpressionFactory.AtUtc(argument),
                    _ => throw new NotSupportedException("Can only apply DateOnly.FromDateTime on a timestamp or timestamptz column")
                };

                return _sqlExpressionFactory.Convert(dateTime, typeof(DateOnly), _typeMappingSource.FindMapping(typeof(DateOnly)));
            }

            if (method.DeclaringType == typeof(NpgsqlDbFunctionsExtensions)
                && method.Name == nameof(NpgsqlDbFunctionsExtensions.Distance)
                && arguments is [_, var dateOnly1, var dateOnly2]
                && dateOnly1.Type == typeof(DateOnly))
            {
                return _sqlExpressionFactory.MakePostgresBinary(PgExpressionType.Distance, dateOnly1, dateOnly2);
            }

            if (method.DeclaringType == typeof(DateOnly) && method.Name == nameof(DateOnly.FromDayNumber))
            {
                // We use fragment rather than a DateOnly constant, since 0001-01-01 gets rendered as -infinity by default.
                // TODO: Set the right type/type mapping after https://github.com/dotnet/efcore/pull/34995 is merged
                return new SqlBinaryExpression(
                    ExpressionType.Add,
                    _sqlExpressionFactory.Fragment("DATE '0001-01-01'"),
                    _sqlExpressionFactory.ApplyDefaultTypeMapping(arguments[0]),
                    typeof(DateOnly),
                    _typeMappingSource.FindMapping(typeof(DateOnly)));
            }
        }
        else
        {
            if (method.DeclaringType == typeof(DateOnly) && method.Name == nameof(DateOnly.ToDateTime))
            {
                return new SqlBinaryExpression(
                    ExpressionType.Add,
                    _sqlExpressionFactory.ApplyDefaultTypeMapping(instance),
                    _sqlExpressionFactory.ApplyDefaultTypeMapping(arguments[0]),
                    typeof(DateTime),
                    _timestampMapping);
            }

            // In PG, date + int = date (int interpreted as days)
            if (method.DeclaringType == typeof(DateOnly) && method.Name == nameof(DateOnly.AddDays))
            {
                return _sqlExpressionFactory.Add(instance, arguments[0]);
            }

            // For months and years, date + interval yields a timestamp (since interval could have a time component), so we need to cast
            // the results back to date.
            // DateOnly is always mapped to date: apply that mapping to an unmapped instance (e.g. a parameter), as it would otherwise be
            // inferred from the interval.
            if (method.DeclaringType == typeof(DateOnly)
                && method.Name is nameof(DateOnly.AddMonths) or nameof(DateOnly.AddYears)
                && CreateIntervalExpression(arguments[0], method.Name == nameof(DateOnly.AddMonths) ? "months" : "years") is { } interval)
            {
                var date = _sqlExpressionFactory.ApplyDefaultTypeMapping(instance);
                return _sqlExpressionFactory.Convert(
                    _sqlExpressionFactory.Add(date, interval, _timestampMapping), typeof(DateOnly), date.TypeMapping);
            }
        }

        return null;
    }

    private SqlExpression? TranslateTimeOnly(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments)
    {
        if (method.DeclaringType == typeof(TimeOnly) && method.Name == nameof(TimeOnly.FromDateTime))
        {
            // Note: converting timestamptz to time performs a timezone conversion, which is not what .NET TimeOnly.FromDateTime does.
            // So if our operand is a timestamptz, we first change the type to timestamp with AT TIME ZONE 'UTC' (returns the same value
            // but as a timestamptz).
            // If our operand is already timestamp, no need to do anything. We throw for anything else to avoid accidentally applying
            // AT TIME ZONE to a non-timestamptz, which would do a timezone conversion
            // An argument whose type mapping isn't known yet (e.g. a parameter) gets the default one.
            var argument = arguments[0].TypeMapping is null
                ? _sqlExpressionFactory.ApplyDefaultTypeMapping(arguments[0])
                : arguments[0];
            var dateTime = argument.TypeMapping switch
            {
                NpgsqlTimestampTypeMapping => argument,
                NpgsqlTimestampTzTypeMapping => _sqlExpressionFactory.AtUtc(argument),
                _ => throw new NotSupportedException("Can only apply TimeOnly.FromDateTime on a timestamp or timestamptz column")
            };

            return _sqlExpressionFactory.Convert(
                dateTime,
                typeof(TimeOnly),
                _typeMappingSource.FindMapping(typeof(TimeOnly)));
        }

        if (method.DeclaringType == typeof(TimeOnly) && method.Name == nameof(TimeOnly.FromTimeSpan))
        {
            return _sqlExpressionFactory.Convert(arguments[0], typeof(TimeOnly), _typeMappingSource.FindMapping(typeof(TimeOnly)));
        }

        if (instance is not null)
        {
            if (method.DeclaringType == typeof(TimeOnly) && method.Name == nameof(TimeOnly.ToTimeSpan))
            {
                return _sqlExpressionFactory.Convert(instance, typeof(TimeSpan), _typeMappingSource.FindMapping(typeof(TimeSpan)));
            }

            // Like .NET, wrap around midnight when the start is after the end (e.g. 00:30 is between 22:00 and 01:00)
            if (method.DeclaringType == typeof(TimeOnly) && method.Name == nameof(TimeOnly.IsBetween) && arguments is [var start, var end])
            {
                var within = _sqlExpressionFactory.And(
                    _sqlExpressionFactory.GreaterThanOrEqual(instance, start),
                    _sqlExpressionFactory.LessThan(instance, end));
                var wrapped = _sqlExpressionFactory.OrElse(
                    _sqlExpressionFactory.GreaterThanOrEqual(instance, start),
                    _sqlExpressionFactory.LessThan(instance, end));

                return (start, end) switch
                {
                    (SqlConstantExpression { Value: TimeOnly startValue }, SqlConstantExpression { Value: TimeOnly endValue })
                        => startValue <= endValue ? within : wrapped,
                    _ => _sqlExpressionFactory.Case(
                        [new CaseWhenClause(_sqlExpressionFactory.LessThanOrEqual(start, end), within)],
                        wrapped)
                };
            }

            if (method.DeclaringType == typeof(TimeOnly) && method.Name == nameof(TimeOnly.Add)
                && arguments is [var timeSpan]
                && timeSpan.Type == typeof(TimeSpan))
            {
                return _sqlExpressionFactory.Add(instance, timeSpan);
            }
        }

        return null;
    }

    private SqlExpression? TranslateTimeZoneInfo(
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments)
    {
        if (method.DeclaringType != typeof(TimeZoneInfo))
        {
            return null;
        }

        return (method.Name, arguments) switch
        {
            (nameof(TimeZoneInfo.ConvertTimeBySystemTimeZoneId), [var convertDateTime, var timeZoneId])
                when method.ReturnType == typeof(DateTime)
                => convertDateTime.TypeMapping?.StoreType is "timestamp with time zone" or "timestamptz"
                    ? _sqlExpressionFactory.AtTimeZone(convertDateTime, timeZoneId, typeof(DateTime), _timestampMapping)
                    : throw new InvalidOperationException(
                        "TimeZoneInfo.ConvertTimeBySystemTimeZoneId is only supported on columns with type 'timestamp with time zone'"),

            // As with the two-parameter overload, return a timestamp even when the destination is UTC.
            (nameof(TimeZoneInfo.ConvertTimeBySystemTimeZoneId), [var sourceDateTime, var sourceTimeZoneId, var destinationTimeZoneId])
                when sourceDateTime.Type == typeof(DateTime)
                => sourceDateTime.TypeMapping?.StoreTypeNameBase is "timestamp without time zone" or "timestamp"
                    ? _sqlExpressionFactory.AtTimeZone(
                        _sqlExpressionFactory.AtTimeZone(sourceDateTime, sourceTimeZoneId, typeof(DateTime), _timestampTzMapping),
                        destinationTimeZoneId,
                        typeof(DateTime),
                        _timestampMapping)
                    : throw new InvalidOperationException(
                        "TimeZoneInfo.ConvertTimeBySystemTimeZoneId is only supported on columns with type 'timestamp without time zone'"),

            (nameof(TimeZoneInfo.ConvertTimeToUtc), [var utcDateTime])
                when utcDateTime.Type == typeof(DateTime)
                => utcDateTime.TypeMapping?.StoreType is "timestamp without time zone" or "timestamp"
                    ? _sqlExpressionFactory.Convert(utcDateTime, utcDateTime.Type, _timestampTzMapping)
                    : throw new InvalidOperationException(
                        "TimeZoneInfo.ConvertTimeToUtc is only supported on columns with type 'timestamp without time zone'"),

            _ => null
        };
    }

    private SqlExpression? CreateIntervalExpression(SqlExpression intervalNum, string datePart)
    {
        if (datePart is "milliseconds" or "microseconds")
        {
            // Interval text does not support floating-point values formatted in scientific notation.
            return new SqlBinaryExpression(
                ExpressionType.Multiply,
                _sqlExpressionFactory.ApplyDefaultTypeMapping(intervalNum),
                _sqlExpressionFactory.Fragment($"INTERVAL '1 {datePart}'"),
                typeof(TimeSpan),
                _intervalMapping);
        }

        // Note: ideally we'd simply generate a PostgreSQL interval expression, but the .NET mapping of that is TimeSpan,
        // which does not work for months, years, etc. So we generate special fragments instead.
        if (intervalNum is SqlConstantExpression constantExpression)
        {
            // We generate constant intervals as INTERVAL '1 days'
            if (constantExpression.Type == typeof(double)
                && ((double)constantExpression.Value! >= int.MaxValue || (double)constantExpression.Value <= int.MinValue))
            {
                return null;
            }

            // The fragment is typed as an interval, so that e.g. timestamptz + interval is recognized as such (see
            // NpgsqlSqlExpressionFactory), including when the timestamp's type mapping is only inferred later (e.g. a parameter).
            return new SqlFragmentExpression(
                FormattableString.Invariant($"INTERVAL '{constantExpression.Value} {datePart}'"), typeof(TimeSpan), _intervalMapping);
        }

        // For non-constants, we can't parameterize INTERVAL '1 days'. Instead, we use CAST($1 || ' days' AS interval).
        // Note that a make_interval() function also exists, but accepts only int (for all fields except for
        // seconds), so we don't use it.
        // Use a PostgreSQL binary expression so nulls propagate, rather than being coalesced to empty strings as in C# concatenation.
        // See https://github.com/npgsql/efcore.pg/issues/2889.
        return _sqlExpressionFactory.Convert(
            new PgUnknownBinaryExpression(
                _sqlExpressionFactory.Convert(intervalNum, typeof(string), _textMapping),
                _sqlExpressionFactory.Constant(' ' + datePart, _textMapping),
                "||",
                typeof(string),
                _textMapping),
            typeof(TimeSpan),
            _intervalMapping);
    }
}
