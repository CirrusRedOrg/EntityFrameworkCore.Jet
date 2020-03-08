// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EntityFrameworkCore.Jet.Utilities;
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
            {typeof(Math).GetRuntimeMethod(nameof(Math.Abs), new[] {typeof(decimal)}), "Abs"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Abs), new[] {typeof(double)}), "Abs"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Abs), new[] {typeof(float)}), "Abs"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Abs), new[] {typeof(int)}), "Abs"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Abs), new[] {typeof(long)}), "Abs"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Abs), new[] {typeof(sbyte)}), "Abs"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Abs), new[] {typeof(short)}), "Abs"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Pow), new[] {typeof(double), typeof(double)}), "Pow"}, // This is handled by JetQuerySqlGenerator
            {typeof(Math).GetRuntimeMethod(nameof(Math.Exp), new[] {typeof(double)}), "Exp"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Log), new[] {typeof(double)}), "Log"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Sqrt), new[] {typeof(double)}), "Sqr"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Atan), new[] {typeof(double)}), "Atn"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Cos), new[] {typeof(double)}), "Cos"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Sin), new[] {typeof(double)}), "Sin"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Tan), new[] {typeof(double)}), "Tan"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Sign), new[] {typeof(decimal)}), "Sgn"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Sign), new[] {typeof(double)}), "Sgn"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Sign), new[] {typeof(float)}), "Sgn"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Sign), new[] {typeof(int)}), "Sgn"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Sign), new[] {typeof(long)}), "Sgn"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Sign), new[] {typeof(sbyte)}), "Sgn"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Sign), new[] {typeof(short)}), "Sgn"}
        };

        private static readonly MethodInfo[] _supportedMethodTranslationsIndirect = new MethodInfo[]
        {
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

        public SqlExpression Translate(SqlExpression instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments)
        {
            Check.NotNull(method, nameof(method));

            if (_supportedMethodTranslationsDirect.TryGetValue(method, out var sqlFunctionName))
            {
                return _sqlExpressionFactory.Function(
                    sqlFunctionName,
                    arguments,
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
                            "Atn",
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
                                        }
                                    )
                                )
                            },
                            method.ReturnType)),

                    // Arcsin(X) = Atn(X / Sqr(-X * X + 1))
                    nameof(Math.Asin) => _sqlExpressionFactory.Function(
                        "Atn",
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
                                    }
                                )
                            )
                        },
                        method.ReturnType),

                    // Logn(x) = Log(x) / Log(n)
                    nameof(Math.Log10) => _sqlExpressionFactory.Divide(
                        _sqlExpressionFactory.Function("Log", new[] {arguments[0]}, method.ReturnType),
                        _sqlExpressionFactory.Constant(Math.Log(10))
                    ),

                    // Math.Log(x, n) //Logn(x) = Log(x) / Log(n)
                    nameof(Math.Log) => _sqlExpressionFactory.Divide(
                        _sqlExpressionFactory.Function("Log", new[] {arguments[0]}, method.ReturnType),
                        _sqlExpressionFactory.Function("Log", new[] {arguments[1]}, method.ReturnType)
                    ),

                    _ => null,
                };
            }

            if (_truncateMethodInfos.Contains(method))
            {
                return _sqlExpressionFactory.Function(
                    "Int",
                    new[] {arguments[0]},
                    method.ReturnType);
            }

            if (_roundMethodInfos.Contains(method))
            {
                return _sqlExpressionFactory.Function(
                    "ROUND",
                    arguments.Count == 1
                        ? new[] {arguments[0], _sqlExpressionFactory.Constant(0)}
                        : new[] {arguments[0], arguments[1]},
                    method.ReturnType);
            }

            return null;
        }
    }
}