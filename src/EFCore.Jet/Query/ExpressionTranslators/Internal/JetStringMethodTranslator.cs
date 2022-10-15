// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

        private static readonly MethodInfo _indexOfMethodInfo
            = typeof(string).GetRequiredRuntimeMethod(nameof(string.IndexOf), typeof(string));

        private static readonly MethodInfo _replaceMethodInfo
            = typeof(string).GetRequiredRuntimeMethod(nameof(string.Replace), typeof(string), typeof(string));

        private static readonly MethodInfo _toLowerMethodInfo
            = typeof(string).GetRequiredRuntimeMethod(nameof(string.ToLower), Array.Empty<Type>());

        private static readonly MethodInfo _toUpperMethodInfo
            = typeof(string).GetRequiredRuntimeMethod(nameof(string.ToUpper), Array.Empty<Type>());

        private static readonly MethodInfo _substringMethodInfoWithOneArg
            = typeof(string).GetRequiredRuntimeMethod(nameof(string.Substring), typeof(int));

        private static readonly MethodInfo _substringMethodInfoWithTwoArgs
            = typeof(string).GetRequiredRuntimeMethod(nameof(string.Substring), typeof(int), typeof(int));

        private static readonly MethodInfo _isNullOrEmptyMethodInfo
            = typeof(string).GetRequiredRuntimeMethod(nameof(string.IsNullOrEmpty), typeof(string));

        private static readonly MethodInfo _isNullOrWhiteSpaceMethodInfo
            = typeof(string).GetRequiredRuntimeMethod(nameof(string.IsNullOrWhiteSpace), typeof(string));

        // Method defined in netcoreapp2.0 only
        private static readonly MethodInfo _trimStartMethodInfoWithoutArgs
            = typeof(string).GetRequiredRuntimeMethod(nameof(string.TrimStart), Array.Empty<Type>());

        private static readonly MethodInfo _trimEndMethodInfoWithoutArgs
            = typeof(string).GetRequiredRuntimeMethod(nameof(string.TrimEnd), Array.Empty<Type>());

        private static readonly MethodInfo _trimMethodInfoWithoutArgs
            = typeof(string).GetRequiredRuntimeMethod(nameof(string.Trim), Array.Empty<Type>());

        // Method defined in netstandard2.0
        private static readonly MethodInfo _trimStartMethodInfoWithCharArrayArg
            = typeof(string).GetRequiredRuntimeMethod(nameof(string.TrimStart), typeof(char[]));

        private static readonly MethodInfo _trimEndMethodInfoWithCharArrayArg
            = typeof(string).GetRequiredRuntimeMethod(nameof(string.TrimEnd), typeof(char[]));

        private static readonly MethodInfo _trimMethodInfoWithCharArrayArg
            = typeof(string).GetRequiredRuntimeMethod(nameof(string.Trim), typeof(char[]));

        private static readonly MethodInfo _startsWithMethodInfo
            = typeof(string).GetRequiredRuntimeMethod(nameof(string.StartsWith), typeof(string));

        private static readonly MethodInfo _containsMethodInfo
            = typeof(string).GetRequiredRuntimeMethod(nameof(string.Contains), typeof(string));

        private static readonly MethodInfo _endsWithMethodInfo
            = typeof(string).GetRequiredRuntimeMethod(nameof(string.EndsWith), typeof(string));

        private static readonly MethodInfo _firstOrDefaultMethodInfoWithoutArgs
            = typeof(Enumerable).GetRuntimeMethods().Single(
                m => m.Name == nameof(Enumerable.FirstOrDefault)
                    && m.GetParameters().Length == 1).MakeGenericMethod(typeof(char));

        private static readonly MethodInfo _lastOrDefaultMethodInfoWithoutArgs
            = typeof(Enumerable).GetRuntimeMethods().Single(
                m => m.Name == nameof(Enumerable.LastOrDefault)
                    && m.GetParameters().Length == 1).MakeGenericMethod(typeof(char));


        public JetStringMethodTranslator(ISqlExpressionFactory sqlExpressionFactory)
            => _sqlExpressionFactory = (JetSqlExpressionFactory)sqlExpressionFactory;

        public SqlExpression Translate(SqlExpression instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments, IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            if (_indexOfMethodInfo.Equals(method))
            {
                var argument = arguments[0];
                var stringTypeMapping = ExpressionExtensions.InferTypeMapping(instance, argument)!;
                argument = _sqlExpressionFactory.ApplyTypeMapping(argument, stringTypeMapping);

                SqlExpression charIndexExpression;
                var storeType = stringTypeMapping.StoreType;
                if (string.Equals(storeType, "nvarchar(max)", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(storeType, "varchar(max)", StringComparison.OrdinalIgnoreCase))
                {
                    charIndexExpression = _sqlExpressionFactory.Function(
                        "INSTR",
                        new[] { _sqlExpressionFactory.ApplyTypeMapping(instance, stringTypeMapping), argument },
                        nullable: true,
                        argumentsPropagateNullability: new[] { true, true },
                        typeof(long));

                    charIndexExpression = _sqlExpressionFactory.Convert(charIndexExpression, typeof(int));
                }
                else
                {
                    charIndexExpression = _sqlExpressionFactory.Function(
                        "INSTR",
                        new[] { _sqlExpressionFactory.ApplyTypeMapping(instance, stringTypeMapping), argument },
                        nullable: true,
                        argumentsPropagateNullability: new[] { true, true },
                        method.ReturnType);
                }

                charIndexExpression = _sqlExpressionFactory.Subtract(charIndexExpression, _sqlExpressionFactory.Constant(1));
                return charIndexExpression;
            }

            if (Equals(method, _containsMethodInfo))
            {
                var patternExpression = arguments[0];
                var patternConstantExpression = patternExpression as SqlConstantExpression;

                // CHECK: Index usage. It is likely needed to switch to the SQL Server approach of pre-searching
                //        with LIKE first to narrow down the results and use INSTR only for whatever remains.
                var charIndexExpression = _sqlExpressionFactory.GreaterThan(
                    _sqlExpressionFactory.Function(
                        "INSTR",
                        new[]
                        {
                            _sqlExpressionFactory.Constant(1),
                            instance,
                            patternExpression,
                            _sqlExpressionFactory.Constant(0)
                        },
                        false,
                        new[] {false},
                        typeof(int)),
                    _sqlExpressionFactory.Constant(0));

                return patternConstantExpression != null
                    ? (string) patternConstantExpression.Value == string.Empty
                        ? (SqlExpression) _sqlExpressionFactory.Constant(true)
                        : charIndexExpression
                    : _sqlExpressionFactory.OrElse(
                        charIndexExpression,
                        _sqlExpressionFactory.Equal(patternExpression, _sqlExpressionFactory.Constant(string.Empty)));
            }

            if (Equals(method, _startsWithMethodInfo))
            {
                return _sqlExpressionFactory.Like(
                    // ReSharper disable once AssignNullToNotNullAttribute
                    instance,
                    _sqlExpressionFactory.Add(arguments[0], _sqlExpressionFactory.Constant("%"))
                );
            }

            if (Equals(method, _endsWithMethodInfo))
            {
                return _sqlExpressionFactory.Like(
                    instance,
                    _sqlExpressionFactory.Add(_sqlExpressionFactory.Constant("%"), arguments[0]));
            }

            // Jet TRIM does not take arguments.
            // _trimWithNoParam is only available since .NET Core 2.0 (or .NET Standard 2.1).
            if (Equals(method, _trimMethodInfoWithoutArgs) ||
                Equals(method, _trimMethodInfoWithCharArrayArg) && ((arguments[0] as SqlConstantExpression)?.Value == null ||
                 ((arguments[0] as SqlConstantExpression)?.Value as Array)?.Length == 0))
            {
                return _sqlExpressionFactory.Function("TRIM", new[] {instance}, false, new[] {false}, method.ReturnType, instance.TypeMapping);
            }

            // Jet LTRIM does not take arguments
            // _trimStartWithNoParam is only available since .NET Core 2.0 (or .NET Standard 2.1).
            if (Equals(method, _trimStartMethodInfoWithoutArgs) ||
                Equals(method, _trimStartMethodInfoWithCharArrayArg) && ((arguments[0] as SqlConstantExpression)?.Value == null ||
                 ((arguments[0] as SqlConstantExpression)?.Value as Array)?.Length == 0))
            {
                return _sqlExpressionFactory.Function("LTRIM", new[] {instance}, false, new[] {false}, method.ReturnType, instance.TypeMapping);
            }

            // Jet RTRIM does not take arguments
            // _trimEndWithNoParam is only available since .NET Core 2.0 (or .NET Standard 2.1).
            if (Equals(method, _trimEndMethodInfoWithoutArgs) ||
                Equals(method, _trimEndMethodInfoWithCharArrayArg) && ((arguments[0] as SqlConstantExpression)?.Value == null ||
                 ((arguments[0] as SqlConstantExpression)?.Value as Array)?.Length == 0))
            {
                return _sqlExpressionFactory.Function("RTRIM", new[] {instance}, false, new[] {false}, method.ReturnType, instance.TypeMapping);
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

            if (_substringMethodInfoWithOneArg.Equals(method) ||
                _substringMethodInfoWithTwoArgs.Equals(method))
            {
                var parameters = new List<SqlExpression>(
                    new[]
                    {
                        instance,
                        // Accommodate for JET assumption of 1-based string indexes
                        arguments[0] is SqlConstantExpression constantExpression
                            ? (SqlExpression) _sqlExpressionFactory.Constant((int) constantExpression.Value + 1)
                            : _sqlExpressionFactory.Add(
                                arguments[0],
                                _sqlExpressionFactory.Constant(1))
                    });
                if (arguments.Count >= 2)
                {
                    parameters.Add(arguments[1]);
                }

                return _sqlExpressionFactory.Function(
                    "MID",
                    parameters,
                    false, new[] {false},
                    method.ReturnType,instance.TypeMapping);
            }

            if (_replaceMethodInfo.Equals(method))
            {
                var firstArgument = arguments[0];
                var secondArgument = arguments[1];
                var stringTypeMapping = ExpressionExtensions.InferTypeMapping(instance, firstArgument, secondArgument);

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

            if (Equals(method, _isNullOrWhiteSpaceMethodInfo))
            {
                return _sqlExpressionFactory.OrElse(
                    _sqlExpressionFactory.IsNull(arguments[0]),
                    _sqlExpressionFactory.Equal(
                        _sqlExpressionFactory.Function(
                            "TRIM",
                            new[] {arguments[0]},
                            false, new[] {false},
                            typeof(string)),
                        _sqlExpressionFactory.Constant(string.Empty)));
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
    }
}