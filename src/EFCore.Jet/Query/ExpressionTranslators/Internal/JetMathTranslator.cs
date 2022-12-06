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
            {typeof(Math).GetRequiredRuntimeMethod(nameof(Math.Abs), new[] {typeof(decimal)}), "ABS"},
            {typeof(Math).GetRequiredRuntimeMethod(nameof(Math.Abs), new[] {typeof(double)}), "ABS"},
            {typeof(Math).GetRequiredRuntimeMethod(nameof(Math.Abs), new[] {typeof(float)}), "ABS"},
            {typeof(Math).GetRequiredRuntimeMethod(nameof(Math.Abs), new[] {typeof(int)}), "ABS"},
            {typeof(Math).GetRequiredRuntimeMethod(nameof(Math.Abs), new[] {typeof(long)}), "ABS"},
            {typeof(Math).GetRequiredRuntimeMethod(nameof(Math.Abs), new[] {typeof(sbyte)}), "ABS"},
            {typeof(Math).GetRequiredRuntimeMethod(nameof(Math.Abs), new[] {typeof(short)}), "ABS"},
            {typeof(Math).GetRequiredRuntimeMethod(nameof(Math.Pow), new[] {typeof(double), typeof(double)}), "POW"}, // This is handled by JetQuerySqlGenerator
            {typeof(Math).GetRequiredRuntimeMethod(nameof(Math.Exp), new[] {typeof(double)}), "EXP"},
            {typeof(Math).GetRequiredRuntimeMethod(nameof(Math.Log), new[] {typeof(double)}), "LOG"},
            {typeof(Math).GetRequiredRuntimeMethod(nameof(Math.Sqrt), new[] {typeof(double)}), "SQR"},
            {typeof(Math).GetRequiredRuntimeMethod(nameof(Math.Atan), new[] {typeof(double)}), "ATN"},
            {typeof(Math).GetRequiredRuntimeMethod(nameof(Math.Cos), new[] {typeof(double)}), "COS"},
            {typeof(Math).GetRequiredRuntimeMethod(nameof(Math.Sin), new[] {typeof(double)}), "SIN"},
            {typeof(Math).GetRequiredRuntimeMethod(nameof(Math.Tan), new[] {typeof(double)}), "TAN"},
            {typeof(Math).GetRequiredRuntimeMethod(nameof(Math.Sign), new[] {typeof(decimal)}), "SGN"},
            {typeof(Math).GetRequiredRuntimeMethod(nameof(Math.Sign), new[] {typeof(double)}), "SGN"},
            {typeof(Math).GetRequiredRuntimeMethod(nameof(Math.Sign), new[] {typeof(float)}), "SGN"},
            {typeof(Math).GetRequiredRuntimeMethod(nameof(Math.Sign), new[] {typeof(int)}), "SGN"},
            {typeof(Math).GetRequiredRuntimeMethod(nameof(Math.Sign), new[] {typeof(long)}), "SGN"},
            {typeof(Math).GetRequiredRuntimeMethod(nameof(Math.Sign), new[] {typeof(sbyte)}), "SGN"},
            {typeof(Math).GetRequiredRuntimeMethod(nameof(Math.Sign), new[] {typeof(short)}), "SGN"},
            { typeof(MathF).GetRequiredRuntimeMethod(nameof(MathF.Abs), typeof(float)), "ABS" },
            { typeof(MathF).GetRequiredRuntimeMethod(nameof(MathF.Pow), typeof(float), typeof(float)), "POW" },
            { typeof(MathF).GetRequiredRuntimeMethod(nameof(MathF.Exp), typeof(float)), "EXP" },
            { typeof(MathF).GetRequiredRuntimeMethod(nameof(MathF.Log), typeof(float)), "LOG" },
            { typeof(MathF).GetRequiredRuntimeMethod(nameof(MathF.Sqrt), typeof(float)), "SQR" },
            { typeof(MathF).GetRequiredRuntimeMethod(nameof(MathF.Atan), typeof(float)), "ATN" },
            { typeof(MathF).GetRequiredRuntimeMethod(nameof(MathF.Cos), typeof(float)), "COS" },
            { typeof(MathF).GetRequiredRuntimeMethod(nameof(MathF.Sin), typeof(float)), "SIN" },
            { typeof(MathF).GetRequiredRuntimeMethod(nameof(MathF.Tan), typeof(float)), "TAN" },
            { typeof(MathF).GetRequiredRuntimeMethod(nameof(MathF.Sign), typeof(float)), "SGN" }
        };

        private static readonly MethodInfo[] _supportedMethodTranslationsIndirect = {
            typeof(Math).GetRequiredRuntimeMethod(nameof(Math.Acos), new[] {typeof(double)}),
            typeof(Math).GetRequiredRuntimeMethod(nameof(Math.Asin), new[] {typeof(double)}),
            typeof(Math).GetRequiredRuntimeMethod(nameof(Math.Atan2), new[] {typeof(double), typeof(double)}),
            typeof(Math).GetRequiredRuntimeMethod(nameof(Math.Floor), new[] {typeof(decimal)}),
            typeof(Math).GetRequiredRuntimeMethod(nameof(Math.Floor), new[] {typeof(double)}),
            typeof(Math).GetRequiredRuntimeMethod(nameof(Math.Ceiling), new[] {typeof(decimal)}),
            typeof(Math).GetRequiredRuntimeMethod(nameof(Math.Ceiling), new[] {typeof(double)}),
            typeof(Math).GetRequiredRuntimeMethod(nameof(Math.Log10), new[] {typeof(double)}),
            typeof(Math).GetRequiredRuntimeMethod(nameof(Math.Log), new[] {typeof(double), typeof(double)}),
            typeof(MathF).GetRequiredRuntimeMethod(nameof(MathF.Ceiling), typeof(float)),
            typeof(MathF).GetRequiredRuntimeMethod(nameof(MathF.Floor), typeof(float)),
            typeof(MathF).GetRequiredRuntimeMethod(nameof(MathF.Log10), typeof(float)),
            typeof(MathF).GetRequiredRuntimeMethod(nameof(MathF.Log), typeof(float), typeof(float)),
            typeof(MathF).GetRequiredRuntimeMethod(nameof(MathF.Acos), typeof(float)),
            typeof(MathF).GetRequiredRuntimeMethod(nameof(MathF.Asin), typeof(float)),
            typeof(MathF).GetRequiredRuntimeMethod(nameof(MathF.Atan2), typeof(float), typeof(float)),
        };

        private static readonly IEnumerable<MethodInfo> _truncateMethodInfos = new[]
        {
            typeof(Math).GetRequiredRuntimeMethod(nameof(Math.Truncate), new[] {typeof(decimal)}),
            typeof(Math).GetRequiredRuntimeMethod(nameof(Math.Truncate), new[] {typeof(double)}),
            typeof(MathF).GetRequiredRuntimeMethod(nameof(MathF.Truncate), typeof(float))
        };

        private static readonly IEnumerable<MethodInfo> _roundMethodInfos = new[]
        {
            typeof(Math).GetRequiredRuntimeMethod(nameof(Math.Round), new[] {typeof(decimal)}),
            typeof(Math).GetRequiredRuntimeMethod(nameof(Math.Round), new[] {typeof(double)}),
            typeof(Math).GetRequiredRuntimeMethod(nameof(Math.Round), new[] {typeof(decimal), typeof(int)}),
            typeof(Math).GetRequiredRuntimeMethod(nameof(Math.Round), new[] {typeof(double), typeof(int)}),
            typeof(MathF).GetRequiredRuntimeMethod(nameof(MathF.Round), typeof(float)),
            typeof(MathF).GetRequiredRuntimeMethod(nameof(MathF.Round), typeof(float), typeof(int))
        };

        public JetMathTranslator(ISqlExpressionFactory sqlExpressionFactory)
            => _sqlExpressionFactory = (JetSqlExpressionFactory)sqlExpressionFactory;

        public SqlExpression Translate(SqlExpression instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments, IDiagnosticsLogger<DbLoggerCategory.Query> logger)
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
                    argumentsPropagateNullability: newArguments.Select(a => true).ToArray(),
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
                                        typeof(Math).GetMethod(nameof(Math.Sqrt)),
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
                                    )
                                )
                            },
                            true,
                            new[] {true},
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
                                    typeof(Math).GetMethod(nameof(Math.Sqrt)),
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
                                )
                            )
                        },
                        true,
                        new[] {true},
                        method.ReturnType),

                    // Logn(x) = Log(x) / Log(n)
                    nameof(Math.Log10) => _sqlExpressionFactory.Divide(
                        _sqlExpressionFactory.Function("LOG", new[] {arguments[0]}, true, new[] {true}, method.ReturnType),
                        _sqlExpressionFactory.Constant(Math.Log(10))
                    ),

                    // Math.Log(x, n) //Logn(x) = Log(x) / Log(n)
                    nameof(Math.Log) => _sqlExpressionFactory.Divide(
                        _sqlExpressionFactory.Function("LOG", new[] {arguments[0]}, true, new[] {true}, method.ReturnType),
                        _sqlExpressionFactory.Function("LOG", new[] {arguments[1]}, true, new[] {true}, method.ReturnType)
                    ),

                    _ => null,
                };
            }

            if (_truncateMethodInfos.Contains(method))
            {
                var argument = arguments[0];
                var result =  (SqlExpression)_sqlExpressionFactory.Function(
                    "INT",
                    new[] {argument},
                    true,
                    new[] {true},
                    typeof(double));
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
                // Result of ROUND for float/double is always double in server side
                var result = (SqlExpression)_sqlExpressionFactory.Function(
                    "ROUND",
                    new[] { argument, digits },
                    nullable: true,
                    argumentsPropagateNullability: new[] { true, true },
                    typeof(double));

                if (argument.Type == typeof(float))
                {
                    result = _sqlExpressionFactory.Convert(result, typeof(float));
                }

                return _sqlExpressionFactory.ApplyTypeMapping(result, argument.TypeMapping);
            }

            return null;
        }
    }
}