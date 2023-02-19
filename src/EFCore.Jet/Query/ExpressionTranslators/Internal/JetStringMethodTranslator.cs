// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.Jet.Query.ExpressionTranslators.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetStringMethodTranslator : IMethodCallTranslator
    {
        private readonly JetSqlExpressionFactory _sqlExpressionFactory;

        private static readonly MethodInfo IndexOfMethodInfo
            = typeof(string).GetRuntimeMethod(nameof(string.IndexOf), new[] { typeof(string) })!;

        private static readonly MethodInfo IndexOfMethodInfoWithStartingPosition
            = typeof(string).GetRuntimeMethod(nameof(string.IndexOf), new[] { typeof(string), typeof(int) })!;

        private static readonly MethodInfo _replaceMethodInfo
            = typeof(string).GetRuntimeMethod(nameof(string.Replace), new[] { typeof(string), typeof(string) })!;

        private static readonly MethodInfo _toLowerMethodInfo
            = typeof(string).GetRuntimeMethod(nameof(string.ToLower), Type.EmptyTypes)!;

        private static readonly MethodInfo _toUpperMethodInfo
            = typeof(string).GetRuntimeMethod(nameof(string.ToUpper), Type.EmptyTypes)!;

        private static readonly MethodInfo _substringMethodInfoWithOneArg
            = typeof(string).GetRuntimeMethod(nameof(string.Substring), new[] { typeof(int) })!;

        private static readonly MethodInfo _substringMethodInfoWithTwoArgs
            = typeof(string).GetRuntimeMethod(nameof(string.Substring), new[] { typeof(int), typeof(int) })!;

        private static readonly MethodInfo _isNullOrEmptyMethodInfo
            = typeof(string).GetRuntimeMethod(nameof(string.IsNullOrEmpty), new[] { typeof(string) })!;

        private static readonly MethodInfo _isNullOrWhiteSpaceMethodInfo
            = typeof(string).GetRuntimeMethod(nameof(string.IsNullOrWhiteSpace), new[] { typeof(string) })!;

        // Method defined in netcoreapp2.0 only
        private static readonly MethodInfo _trimStartMethodInfoWithoutArgs
            = typeof(string).GetRuntimeMethod(nameof(string.TrimStart), Type.EmptyTypes)!;

        private static readonly MethodInfo _trimEndMethodInfoWithoutArgs
            = typeof(string).GetRuntimeMethod(nameof(string.TrimEnd), Type.EmptyTypes)!;

        private static readonly MethodInfo _trimMethodInfoWithoutArgs
            = typeof(string).GetRuntimeMethod(nameof(string.Trim), Type.EmptyTypes)!;

        // Method defined in netstandard2.0
        private static readonly MethodInfo _trimStartMethodInfoWithCharArrayArg
            = typeof(string).GetRuntimeMethod(nameof(string.TrimStart), new[] { typeof(char[]) })!;

        private static readonly MethodInfo _trimEndMethodInfoWithCharArrayArg
            = typeof(string).GetRuntimeMethod(nameof(string.TrimEnd), new[] { typeof(char[]) })!;

        private static readonly MethodInfo _trimMethodInfoWithCharArrayArg
            = typeof(string).GetRuntimeMethod(nameof(string.Trim), new[] { typeof(char[]) })!;

        private static readonly MethodInfo _startsWithMethodInfo
            = typeof(string).GetRuntimeMethod(nameof(string.StartsWith), new[] { typeof(string) })!;

        private static readonly MethodInfo _containsMethodInfo
            = typeof(string).GetRuntimeMethod(nameof(string.Contains), new[] { typeof(string) })!;

        private static readonly MethodInfo _endsWithMethodInfo
            = typeof(string).GetRuntimeMethod(nameof(string.EndsWith), new[] { typeof(string) })!;

        private static readonly MethodInfo _firstOrDefaultMethodInfoWithoutArgs
            = typeof(Enumerable).GetRuntimeMethods().Single(
                m => m.Name == nameof(Enumerable.FirstOrDefault)
                     && m.GetParameters().Length == 1).MakeGenericMethod(typeof(char));

        private static readonly MethodInfo _lastOrDefaultMethodInfoWithoutArgs
            = typeof(Enumerable).GetRuntimeMethods().Single(
                m => m.Name == nameof(Enumerable.LastOrDefault)
                     && m.GetParameters().Length == 1).MakeGenericMethod(typeof(char));

        private const char LikeEscapeChar = '\\';
        private const string LikeEscapeString = "\\";

        public JetStringMethodTranslator(ISqlExpressionFactory sqlExpressionFactory)
            => _sqlExpressionFactory = (JetSqlExpressionFactory)sqlExpressionFactory;

        public SqlExpression? Translate(SqlExpression? instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments, IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            if (instance != null)
            {
                if (IndexOfMethodInfo.Equals(method))
                {
                    return TranslateIndexOf(instance, method, arguments[0], null);
                }

                if (IndexOfMethodInfoWithStartingPosition.Equals(method))
                {
                    return TranslateIndexOf(instance, method, arguments[0], arguments[1]);
                }

                if (_containsMethodInfo.Equals(method))
                {
                    var pattern = arguments[0];
                    var stringTypeMapping = ExpressionExtensions.InferTypeMapping(instance, pattern);
                    instance = _sqlExpressionFactory.ApplyTypeMapping(instance, stringTypeMapping);
                    pattern = _sqlExpressionFactory.ApplyTypeMapping(pattern, stringTypeMapping);

                    if (pattern is SqlConstantExpression constantPattern)
                    {
                        if (!(constantPattern.Value is string patternValue))
                        {
                            return _sqlExpressionFactory.Like(
                                instance,
                                _sqlExpressionFactory.Constant(null, stringTypeMapping));
                        }

                        if (patternValue.Length == 0)
                        {
                            return _sqlExpressionFactory.Constant(true);
                        }

                        return patternValue.Any(IsLikeWildChar)
                            ? _sqlExpressionFactory.Like(
                                instance,
                                _sqlExpressionFactory.Constant($"%{EscapeLikePattern(patternValue)}%"),
                                _sqlExpressionFactory.Constant(LikeEscapeString))
                            : _sqlExpressionFactory.Like(instance, _sqlExpressionFactory.Constant($"%{patternValue}%"));
                    }

                    return _sqlExpressionFactory.OrElse(
                        _sqlExpressionFactory.Like(
                            pattern,
                            _sqlExpressionFactory.Constant(string.Empty, stringTypeMapping)),
                        _sqlExpressionFactory.GreaterThan(
                            _sqlExpressionFactory.Function(
                                "INSTR",
                                new[] { _sqlExpressionFactory.Constant(1), instance, pattern, _sqlExpressionFactory.Constant(1) },
                                nullable: true,
                                argumentsPropagateNullability: new[] { true,true, true, true },
                                typeof(int)),
                            _sqlExpressionFactory.Constant(0)));
                }

                if (_startsWithMethodInfo.Equals(method))
                {
                    return TranslateStartsEndsWith(instance, arguments[0], true);
                }

                if (_endsWithMethodInfo.Equals(method))
                {
                    return TranslateStartsEndsWith(instance, arguments[0], false);
                }

                // Jet TRIM does not take arguments.
                // _trimWithNoParam is only available since .NET Core 2.0 (or .NET Standard 2.1).
                if (Equals(method, _trimMethodInfoWithoutArgs) ||
                    Equals(method, _trimMethodInfoWithCharArrayArg) &&
                    ((arguments[0] as SqlConstantExpression)?.Value as Array)?.Length == 0)
                {
                    return _sqlExpressionFactory.Function("TRIM", new[] { instance }, true, new[] { true },
                        instance.Type, instance.TypeMapping);
                }

                // Jet LTRIM does not take arguments
                // _trimStartWithNoParam is only available since .NET Core 2.0 (or .NET Standard 2.1).
                if (Equals(method, _trimStartMethodInfoWithoutArgs) ||
                    Equals(method, _trimStartMethodInfoWithCharArrayArg) &&
                    ((arguments[0] as SqlConstantExpression)?.Value as Array)?.Length == 0)
                {
                    return _sqlExpressionFactory.Function("LTRIM", new[] { instance }, true, new[] { true },
                        instance.Type, instance.TypeMapping);
                }

                // Jet RTRIM does not take arguments
                // _trimEndWithNoParam is only available since .NET Core 2.0 (or .NET Standard 2.1).
                if (Equals(method, _trimEndMethodInfoWithoutArgs) ||
                    Equals(method, _trimEndMethodInfoWithCharArrayArg) &&
                    ((arguments[0] as SqlConstantExpression)?.Value as Array)?.Length == 0)
                {
                    return _sqlExpressionFactory.Function("RTRIM", new[] { instance }, true, new[] { true }, 
                        instance.Type, instance.TypeMapping);
                }

                if (_toLowerMethodInfo.Equals(method)
                    || _toUpperMethodInfo.Equals(method))
                {
                    return _sqlExpressionFactory.Function(
                        _toLowerMethodInfo.Equals(method) ? "LCASE" : "UCASE",
                        new[] { instance },
                        nullable: true,
                        argumentsPropagateNullability: new[] { true },
                        method.ReturnType,
                        instance.TypeMapping);
                }

                if (_substringMethodInfoWithOneArg.Equals(method))
                {
                    return _sqlExpressionFactory.Function(
                        "MID",
                        new[]
                        {
                            instance,
                            _sqlExpressionFactory.Add(
                                arguments[0],
                                _sqlExpressionFactory.Constant(1)),
                            _sqlExpressionFactory.Function(
                                "LEN",
                                new[] { instance },
                                nullable: true,
                                argumentsPropagateNullability: new[] { true },
                                typeof(int))
                        },
                        nullable: true,
                        argumentsPropagateNullability: new[] { true, true, true },
                        method.ReturnType,
                        instance.TypeMapping);
                }

                if (_substringMethodInfoWithTwoArgs.Equals(method))
                {
                    return _sqlExpressionFactory.Function(
                        "MID",
                        new[]
                        {
                            instance,
                            _sqlExpressionFactory.Add(
                                arguments[0],
                                _sqlExpressionFactory.Constant(1)),
                            arguments[1]
                        },
                        nullable: true,
                        argumentsPropagateNullability: new[] { true, true, true },
                        method.ReturnType,
                        instance.TypeMapping);
                }

                if (_replaceMethodInfo.Equals(method))
                {
                    var firstArgument = arguments[0];
                    var secondArgument = arguments[1];
                    var stringTypeMapping =
                        ExpressionExtensions.InferTypeMapping(instance, firstArgument, secondArgument);

                    instance = _sqlExpressionFactory.ApplyTypeMapping(instance, stringTypeMapping);
                    firstArgument = _sqlExpressionFactory.ApplyTypeMapping(firstArgument, stringTypeMapping);
                    secondArgument = _sqlExpressionFactory.ApplyTypeMapping(secondArgument, stringTypeMapping);

                    return _sqlExpressionFactory.Function(
                        "REPLACE",
                        new[] { instance, firstArgument, secondArgument },
                        nullable: true,
                        argumentsPropagateNullability: new[] { true, true, true },
                        method.ReturnType,
                        stringTypeMapping);
                }
            }

            if (_isNullOrEmptyMethodInfo.Equals(method))
            {
                var argument = arguments[0];

                return _sqlExpressionFactory.OrElse(
                    _sqlExpressionFactory.IsNull(argument),
                    _sqlExpressionFactory.Like(
                        argument,
                        _sqlExpressionFactory.Constant(string.Empty)));
            }

            if (_isNullOrWhiteSpaceMethodInfo.Equals(method))
            {
                var argument = arguments[0];

                return _sqlExpressionFactory.OrElse(
                    _sqlExpressionFactory.IsNull(argument),
                    _sqlExpressionFactory.Equal(
                        argument,
                        _sqlExpressionFactory.Constant(string.Empty, argument.TypeMapping)));
            }

            if (_firstOrDefaultMethodInfoWithoutArgs.Equals(method))
            {
                var argument = arguments[0];
                return _sqlExpressionFactory.Function(
                    "MID",
                    new[] { argument, _sqlExpressionFactory.Constant(1), _sqlExpressionFactory.Constant(1) },
                    nullable: true,
                    argumentsPropagateNullability: new[] { true, true, true },
                    method.ReturnType);
            }

            if (_lastOrDefaultMethodInfoWithoutArgs.Equals(method))
            {
                var argument = arguments[0];
                return _sqlExpressionFactory.Function(
                    "MID",
                    new[]
                    {
                            argument,
                            _sqlExpressionFactory.Function(
                                "LEN",
                                new[] { argument },
                                nullable: true,
                                argumentsPropagateNullability: new[] { true },
                                typeof(int)),
                            _sqlExpressionFactory.Constant(1)
                    },
                    nullable: true,
                    argumentsPropagateNullability: new[] { true, true, true },
                    method.ReturnType);
            }

            return null;
        }

        private SqlExpression TranslateStartsEndsWith(SqlExpression instance, SqlExpression pattern, bool startsWith)
        {
            var stringTypeMapping = ExpressionExtensions.InferTypeMapping(instance, pattern);

            instance = _sqlExpressionFactory.ApplyTypeMapping(instance, stringTypeMapping);
            pattern = _sqlExpressionFactory.ApplyTypeMapping(pattern, stringTypeMapping);

            if (pattern is SqlConstantExpression constantExpression)
            {
                // The pattern is constant. Aside from null or empty, we escape all special characters (%, _, \)
                // in C# and send a simple LIKE
                if (!(constantExpression.Value is string patternValue))
                {
                    return _sqlExpressionFactory.Like(
                        instance,
                        _sqlExpressionFactory.Constant(null, stringTypeMapping));
                }

                return patternValue.Any(IsLikeWildChar)
                    ? _sqlExpressionFactory.Like(
                        instance,
                        _sqlExpressionFactory.Constant(
                            startsWith
                                ? EscapeLikePattern(patternValue) + '%'
                                : '%' + EscapeLikePattern(patternValue)),
                        _sqlExpressionFactory.Constant(LikeEscapeString))
                    : _sqlExpressionFactory.Like(
                        instance,
                        _sqlExpressionFactory.Constant(startsWith ? patternValue + '%' : '%' + patternValue));
            }

            // The pattern is non-constant, we use LEFT or RIGHT to extract substring and compare.
            if (startsWith)
            {
                return _sqlExpressionFactory.Equal(
                    _sqlExpressionFactory.Function(
                        "LEFT",
                        new[]
                        {
                        instance,
                        _sqlExpressionFactory.Function(
                            "LEN",
                            new[] { pattern },
                            nullable: true,
                            argumentsPropagateNullability: new[] { true },
                            typeof(int))
                        },
                        nullable: true,
                        argumentsPropagateNullability: new[] { true, true },
                        typeof(string),
                        stringTypeMapping),
                    pattern);
            }

            return _sqlExpressionFactory.Equal(
                _sqlExpressionFactory.Function(
                    "RIGHT",
                    new[]
                    {
                    instance,
                    _sqlExpressionFactory.Function(
                        "LEN",
                        new[] { pattern },
                        nullable: true,
                        argumentsPropagateNullability: new[] { true },
                        typeof(int))
                    },
                    nullable: true,
                    argumentsPropagateNullability: new[] { true, true },
                    typeof(string),
                    stringTypeMapping),
                pattern);
        }

        private SqlExpression TranslateIndexOf(
        SqlExpression instance,
        MethodInfo method,
        SqlExpression searchExpression,
        SqlExpression? startIndex)
        {
            var stringTypeMapping = ExpressionExtensions.InferTypeMapping(instance, searchExpression)!;
            searchExpression = _sqlExpressionFactory.ApplyTypeMapping(searchExpression, stringTypeMapping);
            instance = _sqlExpressionFactory.ApplyTypeMapping(instance, stringTypeMapping);

            var charIndexArguments = new List<SqlExpression> { instance, searchExpression};

            if (startIndex is not null)
            {
                charIndexArguments.Insert(0,
                    startIndex is SqlConstantExpression { Value: int constantStartIndex }
                        ? _sqlExpressionFactory.Constant(constantStartIndex + 1, typeof(int))
                        : _sqlExpressionFactory.Add(startIndex, _sqlExpressionFactory.Constant(1)));
            }
            charIndexArguments.Add(_sqlExpressionFactory.Constant(1));

            var argumentsPropagateNullability = Enumerable.Repeat(true, charIndexArguments.Count);

            SqlExpression charIndexExpression;
            var storeType = stringTypeMapping.StoreType;
            if (string.Equals(storeType, "nvarchar(max)", StringComparison.OrdinalIgnoreCase)
                || string.Equals(storeType, "varchar(max)", StringComparison.OrdinalIgnoreCase))
            {
                charIndexExpression = _sqlExpressionFactory.Function(
                    "InStr",
                    charIndexArguments,
                    nullable: true,
                    argumentsPropagateNullability,
                    typeof(long));

                charIndexExpression = _sqlExpressionFactory.Convert(charIndexExpression, typeof(int));
            }
            else
            {
                charIndexExpression = _sqlExpressionFactory.Function(
                    "InStr",
                    charIndexArguments,
                    nullable: true,
                    argumentsPropagateNullability,
                    method.ReturnType);
            }

            charIndexExpression = _sqlExpressionFactory.Subtract(charIndexExpression, _sqlExpressionFactory.Constant(1));

            // If the pattern is an empty string, we need to special case to always return 0 (since CHARINDEX return 0, which we'd subtract to
            // -1). Handle separately for constant and non-constant patterns.
            if (searchExpression is SqlConstantExpression { Value: string constantSearchPattern })
            {
                return constantSearchPattern == string.Empty
                    ? _sqlExpressionFactory.Constant(0, typeof(int))
                    : charIndexExpression;
            }

            return _sqlExpressionFactory.Case(
                new[]
                {
                new CaseWhenClause(
                    _sqlExpressionFactory.Equal(
                        searchExpression,
                        _sqlExpressionFactory.Constant(string.Empty, stringTypeMapping)),
                    _sqlExpressionFactory.Constant(0))
                },
                charIndexExpression);
        }

        private static bool IsLikeWildChar(char c)
            => c == '%' || c == '_' || c == '[';

        private static string EscapeLikePattern(string pattern)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < pattern.Length; i++)
            {
                var c = pattern[i];
                if (IsLikeWildChar(c)
                    || c == LikeEscapeChar)
                {
                    builder.Append(LikeEscapeChar);
                }

                builder.Append(c);
            }

            return builder.ToString();
        }

        private SqlExpression TranslateStartsEndsWith(SqlExpression instance, SqlExpression pattern, bool startsWith)
        {
            var stringTypeMapping = ExpressionExtensions.InferTypeMapping(instance, pattern);

            instance = _sqlExpressionFactory.ApplyTypeMapping(instance, stringTypeMapping);
            pattern = _sqlExpressionFactory.ApplyTypeMapping(pattern, stringTypeMapping);

            if (pattern is SqlConstantExpression constantExpression)
            {
                // The pattern is constant. Aside from null or empty, we escape all special characters (%, _, \)
                // in C# and send a simple LIKE
                if (!(constantExpression.Value is string patternValue))
                {
                    return _sqlExpressionFactory.Like(
                        instance,
                        _sqlExpressionFactory.Constant(null, stringTypeMapping));
                }

                return patternValue.Any(IsLikeWildChar)
                    ? _sqlExpressionFactory.Like(
                        instance,
                        _sqlExpressionFactory.Constant(
                            startsWith
                                ? EscapeLikePattern(patternValue) + '%'
                                : '%' + EscapeLikePattern(patternValue)),
                        _sqlExpressionFactory.Constant(LikeEscapeString))
                    : _sqlExpressionFactory.Like(
                        instance,
                        _sqlExpressionFactory.Constant(startsWith ? patternValue + '%' : '%' + patternValue));
            }

            // The pattern is non-constant, we use LEFT or RIGHT to extract substring and compare.
            if (startsWith)
            {
                return _sqlExpressionFactory.Equal(
                    _sqlExpressionFactory.Function(
                        "LEFT",
                        new[]
                        {
                            instance,
                            _sqlExpressionFactory.Function(
                                "LEN",
                                new[] { pattern },
                                nullable: true,
                                argumentsPropagateNullability: new[] { true },
                                typeof(int))
                        },
                        nullable: true,
                        argumentsPropagateNullability: new[] { true, true },
                        typeof(string),
                        stringTypeMapping),
                    pattern);
            }

            return _sqlExpressionFactory.Equal(
                _sqlExpressionFactory.Function(
                    "RIGHT",
                    new[]
                    {
                        instance,
                        _sqlExpressionFactory.Function(
                            "LEN",
                            new[] { pattern },
                            nullable: true,
                            argumentsPropagateNullability: new[] { true },
                            typeof(int))
                    },
                    nullable: true,
                    argumentsPropagateNullability: new[] { true, true },
                    typeof(string),
                    stringTypeMapping),
                pattern);
        }

        // See https://docs.microsoft.com/en-us/sql/t-sql/language-elements/like-transact-sql
        private bool IsLikeWildChar(char c)
            => c == '%' || c == '_' || c == '[';

        private string EscapeLikePattern(string pattern)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < pattern.Length; i++)
            {
                var c = pattern[i];
                if (IsLikeWildChar(c)
                    || c == LikeEscapeChar)
                {
                    builder.Append(LikeEscapeChar);
                }

                builder.Append(c);
            }

            return builder.ToString();
        }
    }
}