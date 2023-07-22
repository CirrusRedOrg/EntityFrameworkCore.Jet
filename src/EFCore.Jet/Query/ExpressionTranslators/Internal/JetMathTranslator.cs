// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EntityFrameworkCore.Jet.Utilities;
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
    public class JetMathTranslator : IMethodCallTranslator
    {
        private readonly JetSqlExpressionFactory _sqlExpressionFactory;

        private static readonly Dictionary<MethodInfo, string> _supportedMethodTranslationsDirect = new Dictionary<MethodInfo, string>
        {
            {typeof(Math).GetRuntimeMethod(nameof(Math.Abs), new[] {typeof(decimal)})!, "ABS"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Abs), new[] {typeof(double)})!, "ABS"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Abs), new[] {typeof(float)})!, "ABS"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Abs), new[] { typeof(int) }) !, "ABS"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Abs), new[] { typeof(long) }) !, "ABS"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Abs), new[] { typeof(sbyte) }) !, "ABS"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Abs), new[] { typeof(short) }) !, "ABS"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Pow), new[] { typeof(double), typeof(double) }) !, "POW"}, // This is handled by JetQuerySqlGenerator
            {typeof(Math).GetRuntimeMethod(nameof(Math.Exp), new[] { typeof(double) }) !, "EXP"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Log), new[] {typeof(double)})!, "LOG"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Sqrt), new[] {typeof(double)})!, "SQR"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Atan), new[] {typeof(double)})!, "ATN"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Cos), new[] {typeof(double)})!, "COS"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Sin), new[] {typeof(double)})!, "SIN"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Tan), new[] {typeof(double)})!, "TAN"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Sign), new[] {typeof(decimal)})!, "SGN"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Sign), new[] {typeof(double)})!, "SGN"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Sign), new[] {typeof(float)})!, "SGN"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Sign), new[] {typeof(int)})!, "SGN"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Sign), new[] {typeof(long)})!, "SGN"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Sign), new[] {typeof(sbyte)})!, "SGN"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Sign), new[] {typeof(short)})!, "SGN"},
            { typeof(MathF).GetRuntimeMethod(nameof(MathF.Abs), new[] { typeof(float) })!, "ABS" },
            { typeof(MathF).GetRuntimeMethod(nameof(MathF.Pow), new[] { typeof(float), typeof(float) })!, "POW" },
            { typeof(MathF).GetRuntimeMethod(nameof(MathF.Exp), new[] { typeof(float) })!, "EXP" },
            { typeof(MathF).GetRuntimeMethod(nameof(MathF.Log), new[] { typeof(float) })!, "LOG" },
            { typeof(MathF).GetRuntimeMethod(nameof(MathF.Sqrt), new[] { typeof(float) })!, "SQR" },
            { typeof(MathF).GetRuntimeMethod(nameof(MathF.Atan), new[] { typeof(float) })!, "ATN" },
            { typeof(MathF).GetRuntimeMethod(nameof(MathF.Cos), new[] { typeof(float) })!, "COS" },
            { typeof(MathF).GetRuntimeMethod(nameof(MathF.Sin), new[] { typeof(float) })!, "SIN" },
            { typeof(MathF).GetRuntimeMethod(nameof(MathF.Tan), new[] { typeof(float) })!, "TAN" },
            { typeof(MathF).GetRuntimeMethod(nameof(MathF.Sign), new[] { typeof(float) })!, "SGN" }
        };

        private static readonly MethodInfo[] _supportedMethodTranslationsIndirect = {
            typeof(Math).GetRuntimeMethod(nameof(Math.Acos), new[] {typeof(double)})!,
            typeof(Math).GetRuntimeMethod(nameof(Math.Asin), new[] {typeof(double)})!,
            typeof(Math).GetRuntimeMethod(nameof(Math.Atan2), new[] {typeof(double), typeof(double)})!,
            typeof(Math).GetRuntimeMethod(nameof(Math.Floor), new[] {typeof(decimal)})!,
            typeof(Math).GetRuntimeMethod(nameof(Math.Floor), new[] {typeof(double)})!,
            typeof(Math).GetRuntimeMethod(nameof(Math.Ceiling), new[] {typeof(decimal)})!,
            typeof(Math).GetRuntimeMethod(nameof(Math.Ceiling), new[] {typeof(double)})!,
            typeof(Math).GetRuntimeMethod(nameof(Math.Log10), new[] {typeof(double)})!,
            typeof(Math).GetRuntimeMethod(nameof(Math.Log), new[] {typeof(double), typeof(double)})!,
            typeof(MathF).GetRuntimeMethod(nameof(MathF.Ceiling), new[] { typeof(float) })!,
            typeof(MathF).GetRuntimeMethod(nameof(MathF.Floor), new[] { typeof(float) })!,
            typeof(MathF).GetRuntimeMethod(nameof(MathF.Log10), new[] { typeof(float) }) !,
            typeof(MathF).GetRuntimeMethod(nameof(MathF.Log), new[] { typeof(float), typeof(float) }) !,
            typeof(MathF).GetRuntimeMethod(nameof(MathF.Acos), new[] { typeof(float) }) !,
            typeof(MathF).GetRuntimeMethod(nameof(MathF.Asin), new[] { typeof(float) }) !,
            typeof(MathF).GetRuntimeMethod(nameof(MathF.Atan2), new[] { typeof(float), typeof(float) })!,
        };

        private static readonly IEnumerable<MethodInfo> _truncateMethodInfos = new[]
        {
            typeof(Math).GetRuntimeMethod(nameof(Math.Truncate), new[] {typeof(decimal)})!,
            typeof(Math).GetRuntimeMethod(nameof(Math.Truncate), new[] {typeof(double)})!,
            typeof(MathF).GetRuntimeMethod(nameof(MathF.Truncate), new[] { typeof(float) })!
        };

        private static readonly IEnumerable<MethodInfo> _roundMethodInfos = new[]
        {
            typeof(Math).GetRuntimeMethod(nameof(Math.Round), new[] {typeof(decimal)})!,
            typeof(Math).GetRuntimeMethod(nameof(Math.Round), new[] {typeof(double)})!,
            typeof(Math).GetRuntimeMethod(nameof(Math.Round), new[] {typeof(decimal), typeof(int)})!,
            typeof(Math).GetRuntimeMethod(nameof(Math.Round), new[] {typeof(double), typeof(int)})!,
            typeof(MathF).GetRuntimeMethod(nameof(MathF.Round), new[] { typeof(float) })!,
            typeof(MathF).GetRuntimeMethod(nameof(MathF.Round), new[] { typeof(float), typeof(int) })!
        };

        public JetMathTranslator(ISqlExpressionFactory sqlExpressionFactory)
            => _sqlExpressionFactory = (JetSqlExpressionFactory)sqlExpressionFactory;

        public SqlExpression? Translate(SqlExpression? instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments, IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            Check.NotNull(method, nameof(method));

            if (_supportedMethodTranslationsDirect.TryGetValue(method, out var sqlFunctionName))
            {
                var typeMapping = arguments.Count == 1
                    ? ExpressionExtensions.InferTypeMapping(arguments[0])
                    : ExpressionExtensions.InferTypeMapping(arguments[0], arguments[1]);

                var newArguments = new SqlExpression[arguments.Count];
                newArguments[0] = _sqlExpressionFactory.ApplyTypeMapping(arguments[0], typeMapping);

                if (arguments.Count == 2)
                {
                    newArguments[1] = _sqlExpressionFactory.ApplyTypeMapping(arguments[1], typeMapping);
                }

                return _sqlExpressionFactory.Function(
                    sqlFunctionName,
                    newArguments,
                    nullable: true,
                    argumentsPropagateNullability: newArguments.Select(_ => true).ToArray(),
                    method.ReturnType,
                    sqlFunctionName == "SIGN" ? null : typeMapping);
            }

            if (_supportedMethodTranslationsIndirect.Contains(method))
            {
                return method.Name switch
                {
                    // Arccos(X) = Atn(-X / Sqr(-X * X + 1)) + 2 * Atn(1)
                    nameof(Math.Acos) => _sqlExpressionFactory.Add(
                        _sqlExpressionFactory.Constant(Math.Atan(1) * 2),
                        _sqlExpressionFactory.Function(
                            "ATN",
                            new[]
                            {
                                _sqlExpressionFactory.Divide(
                                    _sqlExpressionFactory.Negate(arguments[0]),
                                    Translate(
                                        null,
                                        typeof(Math).GetMethod(nameof(Math.Sqrt))!,
                                        new[]
                                        {
                                            _sqlExpressionFactory.Add(
                                                _sqlExpressionFactory.Negate(
                                                    _sqlExpressionFactory.Multiply(
                                                        arguments[0],
                                                        arguments[0]
                                                    )
                                                ),
                                                _sqlExpressionFactory.Constant(1d)
                                            )
                                        },
                                        logger
                                    )!
                                )
                            },
                            true,
                            new[] { true },
                            method.ReturnType)),

                    // Arcsin(X) = Atn(X / Sqr(-X * X + 1))
                    nameof(Math.Asin) => _sqlExpressionFactory.Function(
                        "ATN",
                        new[]
                        {
                            _sqlExpressionFactory.Divide(
                                arguments[0],
                                Translate(
                                    null,
                                    typeof(Math).GetMethod(nameof(Math.Sqrt)) !,
                                    new[]
                                    {
                                        _sqlExpressionFactory.Add(
                                            _sqlExpressionFactory.Negate(
                                                _sqlExpressionFactory.Multiply(
                                                    arguments[0],
                                                    arguments[0]
                                                )
                                            ),
                                            _sqlExpressionFactory.Constant(1d)
                                        )
                                    },
                                    logger
                                )!
                            )
                        },
                        true,
                        new[] { true },
                        method.ReturnType),

                    // Logn(x) = Log(x) / Log(n)
                    nameof(Math.Log10) => _sqlExpressionFactory.Divide(
                        _sqlExpressionFactory.Function("LOG", new[] { arguments[0] }, true, new[] { true }, method.ReturnType),
                        _sqlExpressionFactory.Constant(Math.Log(10))
                    ),

                    // Math.Log(x, n) //Logn(x) = Log(x) / Log(n)
                    nameof(Math.Log) => _sqlExpressionFactory.Divide(
                        _sqlExpressionFactory.Function("LOG", new[] { arguments[0] }, true, new[] { true }, method.ReturnType),
                        _sqlExpressionFactory.Function("LOG", new[] { arguments[1] }, true, new[] { true }, method.ReturnType)
                    ),

                    nameof(Math.Floor) => CreateFix(arguments, method.ReturnType),
                    nameof(Math.Ceiling) => CreateCeiling(arguments, method.ReturnType),

                    nameof(Math.Atan2) => _sqlExpressionFactory.Function(
                        "ATN",
                        new[]
                        {
                            _sqlExpressionFactory.Divide(
                                arguments[0],
                                arguments[1]
                            )
                        },
                        true,
                        new[] { true },
                        method.ReturnType),


                    _ => null,
                };
            }

            if (_truncateMethodInfos.Contains(method))
            {
                var argument = arguments[0];
                // C# has Round over decimal/double/float only so our argument will be one of those types (compiler puts convert node)
                // In database result will be same type except for float which returns double which we need to cast back to float.
                var resultType = argument.Type;
                if (resultType == typeof(float))
                {
                    resultType = typeof(double);
                }
                var result = (SqlExpression)_sqlExpressionFactory.Function(
                    "INT",
                    new[] { argument },
                    nullable: true,
                    argumentsPropagateNullability: new[] { true },
                    resultType);

                if (argument.Type == typeof(float))
                {
                    result = _sqlExpressionFactory.Convert(result, typeof(float));
                }

                return _sqlExpressionFactory.ApplyTypeMapping(result, argument.TypeMapping);
            }

            if (_roundMethodInfos.Contains(method))
            {
                var argument = arguments[0];
                var digits = arguments.Count == 2 ? arguments[1] : _sqlExpressionFactory.Constant(0);
                // C# has Round over decimal/double/float only so our argument will be one of those types (compiler puts convert node)
                // In database result will be same type except for float which returns double which we need to cast back to float.
                var resultType = argument.Type;
                if (resultType == typeof(float))
                {
                    resultType = typeof(double);
                }

                var result = (SqlExpression)_sqlExpressionFactory.Function(
                    "ROUND",
                    new[] { argument, digits },
                    nullable: false,
                    argumentsPropagateNullability: new[] { true, false },
                    resultType);

                if (argument.Type == typeof(float))
                {
                    result = _sqlExpressionFactory.Convert(result, typeof(float));
                }

                return _sqlExpressionFactory.ApplyTypeMapping(result, argument.TypeMapping);
            }

            return null;
        }

        private SqlExpression CreateCeiling(IReadOnlyList<SqlExpression> arguments, Type methodReturnType)
        {
            SqlFunctionExpression fixExpression = (SqlFunctionExpression)CreateFix(arguments, methodReturnType);
            var addoneexp = _sqlExpressionFactory.Add(fixExpression, _sqlExpressionFactory.Constant(1));
            return _sqlExpressionFactory.Case(
                new[]
                {
                    new CaseWhenClause(
                        _sqlExpressionFactory.Equal(
                            fixExpression,
                            arguments[0]),
                        fixExpression)
                },
                addoneexp);
        }

        private SqlExpression CreateFix(IReadOnlyList<SqlExpression> arguments, Type methodReturnType)
        {
            var typeMapping = arguments.Count == 1
                ? ExpressionExtensions.InferTypeMapping(arguments[0])
                : ExpressionExtensions.InferTypeMapping(arguments[0], arguments[1]);

            var newArguments = new SqlExpression[arguments.Count];
            newArguments[0] = _sqlExpressionFactory.ApplyTypeMapping(arguments[0], typeMapping);

            if (arguments.Count == 2)
            {
                newArguments[1] = _sqlExpressionFactory.ApplyTypeMapping(arguments[1], typeMapping);
            }

            return _sqlExpressionFactory.Function(
                "FIX",
                newArguments,
                nullable: true,
                argumentsPropagateNullability: newArguments.Select(_ => true).ToArray(),
                methodReturnType,
                typeMapping);
        }
    }
}