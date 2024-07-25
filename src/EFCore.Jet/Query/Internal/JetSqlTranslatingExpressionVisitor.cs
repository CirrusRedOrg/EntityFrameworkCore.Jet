// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using System.Text;
using EntityFrameworkCore.Jet.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Query.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class JetSqlTranslatingExpressionVisitor : RelationalSqlTranslatingExpressionVisitor
{
    private readonly QueryCompilationContext _queryCompilationContext;
    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    private static readonly HashSet<string> DateTimeDataTypes
        = new()
        {
            "time",
            "date",
            "datetime",
            "datetime2",
            "datetimeoffset"
        };

    private static readonly HashSet<Type> DateTimeClrTypes
        = new()
        {
            typeof(TimeOnly),
            typeof(DateOnly),
            typeof(TimeSpan),
            typeof(DateTime),
            typeof(DateTimeOffset)
        };

    private static readonly HashSet<ExpressionType> ArithmeticOperatorTypes
        = new()
        {
            ExpressionType.Add,
            ExpressionType.Subtract,
            ExpressionType.Multiply,
            ExpressionType.Divide,
            ExpressionType.Modulo
        };

    private static readonly MethodInfo StringStartsWithMethodInfo
        = typeof(string).GetRuntimeMethod(nameof(string.StartsWith), new[] { typeof(string) })!;

    private static readonly MethodInfo StringEndsWithMethodInfo
        = typeof(string).GetRuntimeMethod(nameof(string.EndsWith), new[] { typeof(string) })!;

    private static readonly MethodInfo StringContainsMethodInfo
        = typeof(string).GetRuntimeMethod(nameof(string.Contains), new[] { typeof(string) })!;

    private static readonly MethodInfo EscapeLikePatternParameterMethod =
        typeof(JetSqlTranslatingExpressionVisitor).GetTypeInfo().GetDeclaredMethod(nameof(ConstructLikePatternParameter))!;

    private const char LikeEscapeChar = '\\';
    private const string LikeEscapeString = "\\";

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public JetSqlTranslatingExpressionVisitor(
        RelationalSqlTranslatingExpressionVisitorDependencies dependencies,
        QueryCompilationContext queryCompilationContext,
        QueryableMethodTranslatingExpressionVisitor queryableMethodTranslatingExpressionVisitor)
        : base(dependencies, queryCompilationContext, queryableMethodTranslatingExpressionVisitor)
    {
        _queryCompilationContext = queryCompilationContext;
        _sqlExpressionFactory = dependencies.SqlExpressionFactory;
    }

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    protected override Expression VisitBinary(BinaryExpression binaryExpression)
    {
        if (binaryExpression.NodeType == ExpressionType.ArrayIndex
            && binaryExpression.Left.Type == typeof(byte[]))
        {
            return TranslateByteArrayElementAccess(
                binaryExpression.Left,
                binaryExpression.Right,
                binaryExpression.Type);
        }

        var visitedExpression = base.VisitBinary(binaryExpression);

        if (visitedExpression is SqlBinaryExpression sqlBinaryExpression
            && ArithmeticOperatorTypes.Contains(sqlBinaryExpression.OperatorType))
        {
            var inferredProviderType = GetProviderType(sqlBinaryExpression.Left) ?? GetProviderType(sqlBinaryExpression.Right);
            if (inferredProviderType != null)
            {
                if (DateTimeDataTypes.Contains(inferredProviderType))
                {
                    return QueryCompilationContext.NotTranslatedExpression;
                }
            }
            else
            {
                var leftType = sqlBinaryExpression.Left.Type;
                var rightType = sqlBinaryExpression.Right.Type;
                if (DateTimeClrTypes.Contains(leftType)
                    || DateTimeClrTypes.Contains(rightType))
                {
                    return QueryCompilationContext.NotTranslatedExpression;
                }
            }
        }

        return visitedExpression;
    }

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    protected override Expression VisitUnary(UnaryExpression unaryExpression)
    {
        if (unaryExpression.NodeType == ExpressionType.ArrayLength
            && unaryExpression.Operand.Type == typeof(byte[]))
        {
            if (!(base.Visit(unaryExpression.Operand) is SqlExpression sqlExpression))
            {
                return QueryCompilationContext.NotTranslatedExpression;
            }
            throw new InvalidOperationException(JetStrings.ByteArrayLength);
        }

        return base.VisitUnary(unaryExpression);
    }

    protected override Expression VisitExtension(Expression extensionExpression)
    {
        var result =  base.VisitExtension(extensionExpression);
        if (extensionExpression is ShapedQueryExpression shapedQueryExpression)
        {
            var shaperExpression = shapedQueryExpression.ShaperExpression;
            if (shapedQueryExpression.ResultCardinality == ResultCardinality.SingleOrDefault
                && !shaperExpression.Type.IsNullableType() && result is SqlFunctionExpression { Name: "COALESCE"} sqlFunctionExpression)
            {
                if (sqlFunctionExpression.Arguments?[1] is SqlConstantExpression { Value: DateTime { Ticks: 0 } })
                {
                    var newconst = new SqlConstantExpression(new DateTime(100, 1, 1), sqlFunctionExpression.Arguments[1].TypeMapping);
                    return _sqlExpressionFactory.Coalesce(sqlFunctionExpression.Arguments[0],
                        (SqlExpression)Visit(newconst));
                }
            }
        }
        return result;
    }

    protected override Expression VisitMethodCall(MethodCallExpression methodCallExpression)
    {
        var method = methodCallExpression.Method;

        if (method.IsGenericMethod
            && method.GetGenericMethodDefinition() == EnumerableMethods.ElementAt
            && methodCallExpression.Arguments[0].Type == typeof(byte[]))
        {
            return TranslateByteArrayElementAccess(
                methodCallExpression.Arguments[0],
                methodCallExpression.Arguments[1],
                methodCallExpression.Type);
        }

        if (method == StringStartsWithMethodInfo
            && TryTranslateStartsEndsWithContains(
                methodCallExpression.Object!, methodCallExpression.Arguments[0], StartsEndsWithContains.StartsWith, out var translation1))
        {
            return translation1;
        }

        if (method == StringEndsWithMethodInfo
            && TryTranslateStartsEndsWithContains(
                methodCallExpression.Object!, methodCallExpression.Arguments[0], StartsEndsWithContains.EndsWith, out var translation2))
        {
            return translation2;
        }

        if (method == StringContainsMethodInfo
            && TryTranslateStartsEndsWithContains(
                methodCallExpression.Object!, methodCallExpression.Arguments[0], StartsEndsWithContains.Contains, out var translation3))
        {
            return translation3;
        }

        return base.VisitMethodCall(methodCallExpression);

        bool TryTranslateStartsEndsWithContains(
            Expression instance,
            Expression pattern,
            StartsEndsWithContains methodType,
            [NotNullWhen(true)] out SqlExpression? translation)
        {
            if (Visit(instance) is not SqlExpression translatedInstance
                || Visit(pattern) is not SqlExpression translatedPattern)
            {
                translation = null;
                return false;
            }

            var stringTypeMapping = ExpressionExtensions.InferTypeMapping(translatedInstance, translatedPattern);

            translatedInstance = _sqlExpressionFactory.ApplyTypeMapping(translatedInstance, stringTypeMapping);
            translatedPattern = _sqlExpressionFactory.ApplyTypeMapping(translatedPattern, stringTypeMapping);

            switch (translatedPattern)
            {
                case SqlConstantExpression patternConstant:
                    {
                        // The pattern is constant. Aside from null and empty string, we escape all special characters (%, _, \) and send a
                        // simple LIKE
                        translation = patternConstant.Value switch
                        {
                            null => _sqlExpressionFactory.Like(translatedInstance, _sqlExpressionFactory.Constant(null,typeof(string), stringTypeMapping)),

                            // In .NET, all strings start with/end with/contain the empty string, but SQL LIKE return false for empty patterns.
                            // Return % which always matches instead.
                            // Note that we don't just return a true constant, since null strings shouldn't match even an empty string
                            // (but SqlNullabilityProcess will convert this to a true constant if the instance is non-nullable)
                            "" => _sqlExpressionFactory.Like(translatedInstance, _sqlExpressionFactory.Constant("%")),

                            string s => s.Any(IsLikeWildChar)
                                ? _sqlExpressionFactory.Like(
                                    translatedInstance,
                                    _sqlExpressionFactory.Constant(
                                        methodType switch
                                        {
                                            StartsEndsWithContains.StartsWith => EscapeLikePattern(s) + '%',
                                            StartsEndsWithContains.EndsWith => '%' + EscapeLikePattern(s),
                                            StartsEndsWithContains.Contains => $"%{EscapeLikePattern(s)}%",

                                            _ => throw new ArgumentOutOfRangeException(nameof(methodType), methodType, null)
                                        }))
                                : _sqlExpressionFactory.Like(
                                    translatedInstance,
                                    _sqlExpressionFactory.Constant(
                                        methodType switch
                                        {
                                            StartsEndsWithContains.StartsWith => s + '%',
                                            StartsEndsWithContains.EndsWith => '%' + s,
                                            StartsEndsWithContains.Contains => $"%{s}%",

                                            _ => throw new ArgumentOutOfRangeException(nameof(methodType), methodType, null)
                                        })),

                            _ => throw new UnreachableException()
                        };

                        return true;
                    }

                case SqlParameterExpression patternParameter
                    when patternParameter.Name.StartsWith(QueryCompilationContext.QueryParameterPrefix, StringComparison.Ordinal):
                    {
                        // The pattern is a parameter, register a runtime parameter that will contain the rewritten LIKE pattern, where
                        // all special characters have been escaped.
                        var lambda = Expression.Lambda(
                            Expression.Call(
                                EscapeLikePatternParameterMethod,
                                QueryCompilationContext.QueryContextParameter,
                                Expression.Constant(patternParameter.Name),
                                Expression.Constant(methodType)),
                            QueryCompilationContext.QueryContextParameter);

                        var escapedPatternParameter =
                            _queryCompilationContext.RegisterRuntimeParameter(
                                $"{patternParameter.Name}_{methodType.ToString().ToLower(CultureInfo.InvariantCulture)}", lambda);

                        translation = _sqlExpressionFactory.Like(
                            translatedInstance,
                            new SqlParameterExpression(escapedPatternParameter.Name!, escapedPatternParameter.Type, stringTypeMapping));

                        return true;
                    }

                default:
                    // The pattern is a column or a complex expression; the possible special characters in the pattern cannot be escaped,
                    // preventing us from translating to LIKE.
                    translation = methodType switch
                    {
                        // For StartsWith/EndsWith, use LEFT or RIGHT instead to extract substring and compare:
                        // WHERE instance IS NOT NULL AND pattern IS NOT NULL AND LEFT(instance, LEN(pattern)) = pattern
                        // This is less efficient than LIKE (i.e. StartsWith does an index scan instead of seek), but we have no choice.
                        // Note that we compensate for the case where both the instance and the pattern are null (null.StartsWith(null)); a
                        // simple equality would yield true in that case, but we want false. We technically
                        StartsEndsWithContains.StartsWith or StartsEndsWithContains.EndsWith
                            => _sqlExpressionFactory.AndAlso(
                                _sqlExpressionFactory.IsNotNull(translatedInstance),
                                _sqlExpressionFactory.AndAlso(
                                    _sqlExpressionFactory.IsNotNull(translatedPattern),
                                    _sqlExpressionFactory.Equal(
                                        _sqlExpressionFactory.Function(
                                            methodType is StartsEndsWithContains.StartsWith ? "LEFT" : "RIGHT",
                                            new[]
                                            {
                                                translatedInstance,
                                                _sqlExpressionFactory.Coalesce(
                                                    _sqlExpressionFactory.Function(
                                                        "LEN",
                                                        new[] { translatedPattern },
                                                        nullable: true,
                                                        argumentsPropagateNullability: new[] { false },
                                                        typeof(int)),
                                                    _sqlExpressionFactory.Constant(0)
                                                    )
                                            },
                                            nullable: true,
                                            argumentsPropagateNullability: new[] { true, false },
                                            typeof(string),
                                            stringTypeMapping),
                                        translatedPattern))),

                        // For Contains, just use INSTR and check if the result is greater than 0.
                        // Add a check to return null when the pattern is an empty string (and the string isn't null)
                        StartsEndsWithContains.Contains
                            => _sqlExpressionFactory.AndAlso(
                                _sqlExpressionFactory.IsNotNull(translatedInstance),
                                _sqlExpressionFactory.AndAlso(
                                    _sqlExpressionFactory.IsNotNull(translatedPattern),
                                    _sqlExpressionFactory.OrElse(
                                        _sqlExpressionFactory.GreaterThan(
                                            _sqlExpressionFactory.Function(
                                                "INSTR",
                                                new[] { _sqlExpressionFactory.Constant(1), translatedInstance, translatedPattern, _sqlExpressionFactory.Constant(1) },
                                                nullable: true,
                                                argumentsPropagateNullability: new[] { false, true, true, false },
                                                typeof(int)),
                                            _sqlExpressionFactory.Constant(0)),
                                        _sqlExpressionFactory.Like(
                                            translatedPattern,
                                            _sqlExpressionFactory.Constant(string.Empty, stringTypeMapping))))),

                        _ => throw new UnreachableException()
                    };

                    return true;
            }
        }
    }

    private static string? ConstructLikePatternParameter(
        QueryContext queryContext,
        string baseParameterName,
        StartsEndsWithContains methodType)
        => queryContext.ParameterValues[baseParameterName] switch
        {
            null => null,

            // In .NET, all strings start/end with the empty string, but SQL LIKE return false for empty patterns.
            // Return % which always matches instead.
            "" => "%",

            string s => methodType switch
            {
                StartsEndsWithContains.StartsWith => EscapeLikePattern(s) + '%',
                StartsEndsWithContains.EndsWith => '%' + EscapeLikePattern(s),
                StartsEndsWithContains.Contains => $"%{EscapeLikePattern(s)}%",
                _ => throw new ArgumentOutOfRangeException(nameof(methodType), methodType, null)
            },

            _ => throw new UnreachableException()
        };

    private enum StartsEndsWithContains
    {
        StartsWith,
        EndsWith,
        Contains
    }

    //Extra resources
    // https://support.microsoft.com/en-us/office/like-operator-b2f7ef03-9085-4ffb-9829-eef18358e931
    // https://support.microsoft.com/en-us/office/access-wildcard-character-reference-af00c501-7972-40ee-8889-e18abaad12d1
    // https://support.microsoft.com/en-us/office/use-wildcards-in-queries-and-parameters-in-access-ec057a45-78b1-4d16-8c20-242cde582e0b
    //These are the characters to escape in LIKE pattern
    private static bool IsLikeWildChar(char c)
        => c == '%' || c == '_' || c == '[' || c == '^' || c == '?' || c == '#' || c == '*';

    private static string EscapeLikePattern(string pattern)
    {
        var builder = new StringBuilder();
        foreach (var c in pattern)
        {
            if (IsLikeWildChar(c))
            {
                builder.Append('[');
                builder.Append(c);
                builder.Append(']');
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private Expression TranslateByteArrayElementAccess(Expression array, Expression index, Type resultType)
    {
        var visitedArray = Visit(array);
        var visitedIndex = Visit(index);

        return visitedArray is SqlExpression sqlArray
            && visitedIndex is SqlExpression sqlIndex
                ? Dependencies.SqlExpressionFactory.Function(
                    "ASCB",
                    new[] { Dependencies.SqlExpressionFactory.Function(
                        "MIDB",
                        new[] { 
                            sqlArray,
                            Dependencies.SqlExpressionFactory.Add(
                                Dependencies.SqlExpressionFactory.ApplyDefaultTypeMapping(sqlIndex),
                                Dependencies.SqlExpressionFactory.Constant(1)),
                            Dependencies.SqlExpressionFactory.Constant(1) },
                        nullable: true,
                        argumentsPropagateNullability: new[] { true, true, true },
                        typeof(byte[])) },
                    nullable: true,
                    argumentsPropagateNullability: new[] { true },
                    typeof(int))

                : QueryCompilationContext.NotTranslatedExpression;
    }

    private static string? GetProviderType(SqlExpression expression)
        => expression.TypeMapping?.StoreType;
}
