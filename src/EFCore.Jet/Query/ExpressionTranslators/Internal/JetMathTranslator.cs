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
            {typeof(Math).GetRuntimeMethod(nameof(Math.Abs), new[] {typeof(decimal)}), "ABS"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Abs), new[] {typeof(double)}), "ABS"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Abs), new[] {typeof(float)}), "ABS"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Abs), new[] {typeof(int)}), "ABS"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Abs), new[] {typeof(long)}), "ABS"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Abs), new[] {typeof(sbyte)}), "ABS"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Abs), new[] {typeof(short)}), "ABS"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Pow), new[] {typeof(double), typeof(double)}), "POW"}, // This is handled by JetQuerySqlGenerator
            {typeof(Math).GetRuntimeMethod(nameof(Math.Exp), new[] {typeof(double)}), "EXP"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Log), new[] {typeof(double)}), "LOG"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Sqrt), new[] {typeof(double)}), "SQR"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Atan), new[] {typeof(double)}), "ATN"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Cos), new[] {typeof(double)}), "COS"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Sin), new[] {typeof(double)}), "SIN"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Tan), new[] {typeof(double)}), "TAN"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Sign), new[] {typeof(decimal)}), "SGN"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Sign), new[] {typeof(double)}), "SGN"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Sign), new[] {typeof(float)}), "SGN"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Sign), new[] {typeof(int)}), "SGN"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Sign), new[] {typeof(long)}), "SGN"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Sign), new[] {typeof(sbyte)}), "SGN"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Sign), new[] {typeof(short)}), "SGN"}
        };

        private static readonly MethodInfo[] _supportedMethodTranslationsIndirect = {
            typeof(Math).GetRuntimeMethod(nameof(Math.Acos), new[] {typeof(double)}),
            typeof(Math).GetRuntimeMethod(nameof(Math.Asin), new[] {typeof(double)}),
            typeof(Math).GetRuntimeMethod(nameof(Math.Atan2), new[] {typeof(double), typeof(double)}),
            typeof(Math).GetRuntimeMethod(nameof(Math.Floor), new[] {typeof(decimal)}),
            typeof(Math).GetRuntimeMethod(nameof(Math.Floor), new[] {typeof(double)}),
            typeof(Math).GetRuntimeMethod(nameof(Math.Ceiling), new[] {typeof(decimal)}),
            typeof(Math).GetRuntimeMethod(nameof(Math.Ceiling), new[] {typeof(double)}),
            typeof(Math).GetRuntimeMethod(nameof(Math.Log10), new[] {typeof(double)}),
            typeof(Math).GetRuntimeMethod(nameof(Math.Log), new[] {typeof(double), typeof(double)})
        };

        private static readonly IEnumerable<MethodInfo> _truncateMethodInfos = new[]
        {
            typeof(Math).GetRuntimeMethod(nameof(Math.Truncate), new[] {typeof(decimal)}),
            typeof(Math).GetRuntimeMethod(nameof(Math.Truncate), new[] {typeof(double)})
        };

        private static readonly IEnumerable<MethodInfo> _roundMethodInfos = new[]
        {
            typeof(Math).GetRuntimeMethod(nameof(Math.Round), new[] {typeof(decimal)}),
            typeof(Math).GetRuntimeMethod(nameof(Math.Round), new[] {typeof(double)}),
            typeof(Math).GetRuntimeMethod(nameof(Math.Round), new[] {typeof(decimal), typeof(int)}),
            typeof(Math).GetRuntimeMethod(nameof(Math.Round), new[] {typeof(double), typeof(int)})
        };

        public JetMathTranslator(ISqlExpressionFactory sqlExpressionFactory)
            => _sqlExpressionFactory = (JetSqlExpressionFactory)sqlExpressionFactory;

        public SqlExpression Translate(SqlExpression instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments, IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            Check.NotNull(method, nameof(method));

            if (_supportedMethodTranslationsDirect.TryGetValue(method, out var sqlFunctionName))
            {
                return _sqlExpressionFactory.Function(
                    sqlFunctionName,
                    arguments,
                    false,
                    new[] {false},
                    method.ReturnType);
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
                            false,
                            new[] {false},
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
                        false,
                        new[] {false},
                        method.ReturnType),

                    // Logn(x) = Log(x) / Log(n)
                    nameof(Math.Log10) => _sqlExpressionFactory.Divide(
                        _sqlExpressionFactory.Function("LOG", new[] {arguments[0]}, false, new[] {false}, method.ReturnType),
                        _sqlExpressionFactory.Constant(Math.Log(10))
                    ),

                    // Math.Log(x, n) //Logn(x) = Log(x) / Log(n)
                    nameof(Math.Log) => _sqlExpressionFactory.Divide(
                        _sqlExpressionFactory.Function("LOG", new[] {arguments[0]}, false, new[] {false}, method.ReturnType),
                        _sqlExpressionFactory.Function("LOG", new[] {arguments[1]}, false, new[] {false}, method.ReturnType)
                    ),

                    _ => null,
                };
            }

            if (_truncateMethodInfos.Contains(method))
            {
                return _sqlExpressionFactory.Function(
                    "INT",
                    new[] {arguments[0]},
                    false,
                    new[] {false},
                    method.ReturnType);
            }

            if (_roundMethodInfos.Contains(method))
            {
                return _sqlExpressionFactory.Function(
                    "ROUND",
                    arguments.Count == 1
                        ? new[] {arguments[0], _sqlExpressionFactory.Constant(0)}
                        : new[] {arguments[0], arguments[1]},
                    false,
                    new[] {false},
                    method.ReturnType);
            }

            return null;
        }
    }
}