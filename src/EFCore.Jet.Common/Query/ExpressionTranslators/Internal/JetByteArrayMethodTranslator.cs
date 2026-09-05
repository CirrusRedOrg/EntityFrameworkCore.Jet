// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.Jet.Query.ExpressionTranslators.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
/// <remarks>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </remarks>
public class JetByteArrayMethodTranslator(ISqlExpressionFactory sqlExpressionFactory) : IMethodCallTranslator
{
    private readonly ISqlExpressionFactory _sqlExpressionFactory = sqlExpressionFactory;

    private MethodInfo ByteArrayLength = typeof(JetDbFunctionsExtensions).GetRuntimeMethod(
        nameof(JetDbFunctionsExtensions.ByteArrayLength),
        [typeof(DbFunctions), typeof(byte[])])!;

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public virtual SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (method == ByteArrayLength)
        {
            var isBinaryMaxDataType = arguments[1] is SqlParameterExpression;
            SqlExpression dataLengthSqlFunction = _sqlExpressionFactory.Function(
                "LENB",
                [arguments[1]],
                nullable: true,
                argumentsPropagateNullability: [true],
                isBinaryMaxDataType ? typeof(long) : typeof(int));

            var rightval = _sqlExpressionFactory.Function(
                "ASCB",
                [
                    _sqlExpressionFactory.Function(
                        "RIGHTB",
                        [arguments[1], _sqlExpressionFactory.Constant(1)],
                        nullable: true,
                        argumentsPropagateNullability: [true, true],
                        typeof(byte[]))
                ],
                nullable: true,
                argumentsPropagateNullability: [true],
                typeof(int));

            var minusOne = _sqlExpressionFactory.Subtract(dataLengthSqlFunction, _sqlExpressionFactory.Constant(1));
            var whenClause = new CaseWhenClause(_sqlExpressionFactory.Equal(rightval, _sqlExpressionFactory.Constant(0)), minusOne);

            dataLengthSqlFunction = _sqlExpressionFactory.Case([whenClause], dataLengthSqlFunction);

            return isBinaryMaxDataType
                ? _sqlExpressionFactory.Convert(dataLengthSqlFunction, typeof(int))
                : dataLengthSqlFunction;
        }

        if (method.IsGenericMethod
            && method.DeclaringType == typeof(Enumerable))
        {
            switch (method.Name)
            {
                case nameof(Enumerable.Contains) when arguments is [var source, var item] && source.Type == typeof(byte[]):
                {
                    var sourceTypeMapping = source.TypeMapping;

                    var value = item is SqlConstantExpression constantValue
                        ? _sqlExpressionFactory.Constant(new[] { (byte)constantValue.Value! }, sourceTypeMapping)
                        : _sqlExpressionFactory.Function(
                            "CHR",
                            [item],
                            nullable: true,
                            argumentsPropagateNullability: [true],
                            typeof(string));

                    return _sqlExpressionFactory.GreaterThan(
                        _sqlExpressionFactory.Function(
                            "INSTR",
                            [
                                _sqlExpressionFactory.Constant(1),
                                _sqlExpressionFactory.Function(
                                    "STRCONV",
                                    [source, _sqlExpressionFactory.Constant(64)],
                                    nullable: true,
                                    argumentsPropagateNullability: [true, false],
                                    typeof(string)),
                                value,
                                _sqlExpressionFactory.Constant(0)
                            ],
                            nullable: true,
                            argumentsPropagateNullability: [false, true, true, false],
                            typeof(int)),
                        _sqlExpressionFactory.Constant(0));
                }

                // First without a predicate
                case nameof(Enumerable.First) when arguments is [var source] && source.Type == typeof(byte[]):
                    return _sqlExpressionFactory.Function(
                        "ASCB",
                        [
                            _sqlExpressionFactory.Function(
                                "MIDB",
                                [source, _sqlExpressionFactory.Constant(1), _sqlExpressionFactory.Constant(1)],
                                nullable: true,
                                argumentsPropagateNullability: [true, true, true],
                                typeof(byte[]))
                        ],
                        nullable: true,
                        argumentsPropagateNullability: [true],
                        typeof(int));

                // Any without a predicate. LENB answers "are there any bytes at all" exactly, and unlike
                // ByteArrayLength it needs no caveat: LENB reports the UTF-16 byte count and so rounds an odd
                // length UP to even, which is why an EXACT length is unobtainable — but that rounding can
                // never move a value across zero. An empty array is 0 and every non-empty array is at least 2,
                // so the trailing-0x00 ambiguity behind ByteArrayLength's warning cannot arise for a > 0 test.
                case nameof(Enumerable.Any) when arguments is [var source] && source.Type == typeof(byte[]):
                    return _sqlExpressionFactory.GreaterThan(
                        _sqlExpressionFactory.Function(
                            "LENB",
                            [source],
                            nullable: true,
                            argumentsPropagateNullability: [true],
                            typeof(int)),
                        _sqlExpressionFactory.Constant(0));
            }
        }

        return null;
    }

    private static string? GetProviderType(SqlExpression expression)
        => expression.TypeMapping?.StoreType;
}
