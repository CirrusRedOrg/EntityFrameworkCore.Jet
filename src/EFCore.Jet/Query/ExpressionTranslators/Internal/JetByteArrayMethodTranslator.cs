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
        if (method is { IsGenericMethod: true, Name: nameof(Enumerable.Contains) }
            && arguments[0].Type == typeof(byte[]))
        {
            var source = arguments[0];
            var sourceTypeMapping = source.TypeMapping;

            var value = arguments[1] is SqlConstantExpression constantValue
                ? _sqlExpressionFactory.Constant(new[] { (byte)constantValue.Value! }, sourceTypeMapping)
                : _sqlExpressionFactory.Function(
                    "CHR",
                    [arguments[1]],
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

        if (method is { IsGenericMethod: true, Name: nameof(Enumerable.First) } && method.GetParameters().Length == 1
            && arguments[0].Type == typeof(byte[]))
        {
            return _sqlExpressionFactory.Function(
                "ASCB",
                [ _sqlExpressionFactory.Function(
                    "MIDB",
                    [arguments[0], _sqlExpressionFactory.Constant(1), _sqlExpressionFactory.Constant(1)],
                    nullable: true,
                    argumentsPropagateNullability: [true, true, true],
                    typeof(byte[])) ],
                nullable: true,
                argumentsPropagateNullability: [true],
                typeof(int));
        }

        return null;
    }

    private static string? GetProviderType(SqlExpression expression)
        => expression.TypeMapping?.StoreType;
}
