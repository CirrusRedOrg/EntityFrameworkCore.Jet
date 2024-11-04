// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Utilities;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using ExpressionExtensions = Microsoft.EntityFrameworkCore.Query.ExpressionExtensions;

namespace EntityFrameworkCore.Jet.Query.ExpressionTranslators.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetMathTranslator(ISqlExpressionFactory sqlExpressionFactory) : IMethodCallTranslator
    {
        private readonly JetSqlExpressionFactory _sqlExpressionFactory = (JetSqlExpressionFactory)sqlExpressionFactory;

        private static readonly Dictionary<MethodInfo, string> _supportedMethodTranslationsDirect = new()
        {
            {typeof(Math).GetRuntimeMethod(nameof(Math.Abs), [typeof(decimal)])!, "ABS"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Abs), [typeof(double)])!, "ABS"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Abs), [typeof(float)])!, "ABS"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Abs), [typeof(int)]) !, "ABS"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Abs), [typeof(long)]) !, "ABS"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Abs), [typeof(sbyte)]) !, "ABS"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Abs), [typeof(short)]) !, "ABS"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Pow), [typeof(double), typeof(double)]) !, "POW"}, // This is handled by JetQuerySqlGenerator
            {typeof(Math).GetRuntimeMethod(nameof(Math.Exp), [typeof(double)]) !, "EXP"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Log), [typeof(double)])!, "LOG"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Sqrt), [typeof(double)])!, "SQR"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Atan), [typeof(double)])!, "ATN"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Cos), [typeof(double)])!, "COS"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Sin), [typeof(double)])!, "SIN"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Tan), [typeof(double)])!, "TAN"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Sign), [typeof(decimal)])!, "SGN"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Sign), [typeof(double)])!, "SGN"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Sign), [typeof(float)])!, "SGN"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Sign), [typeof(int)])!, "SGN"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Sign), [typeof(long)])!, "SGN"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Sign), [typeof(sbyte)])!, "SGN"},
            {typeof(Math).GetRuntimeMethod(nameof(Math.Sign), [typeof(short)])!, "SGN"},
            { typeof(MathF).GetRuntimeMethod(nameof(MathF.Abs), [typeof(float)])!, "ABS" },
            { typeof(MathF).GetRuntimeMethod(nameof(MathF.Pow), [typeof(float), typeof(float)])!, "POW" },
            { typeof(MathF).GetRuntimeMethod(nameof(MathF.Exp), [typeof(float)])!, "EXP" },
            { typeof(MathF).GetRuntimeMethod(nameof(MathF.Log), [typeof(float)])!, "LOG" },
            { typeof(MathF).GetRuntimeMethod(nameof(MathF.Sqrt), [typeof(float)])!, "SQR" },
            { typeof(MathF).GetRuntimeMethod(nameof(MathF.Atan), [typeof(float)])!, "ATN" },
            { typeof(MathF).GetRuntimeMethod(nameof(MathF.Cos), [typeof(float)])!, "COS" },
            { typeof(MathF).GetRuntimeMethod(nameof(MathF.Sin), [typeof(float)])!, "SIN" },
            { typeof(MathF).GetRuntimeMethod(nameof(MathF.Tan), [typeof(float)])!, "TAN" },
            { typeof(MathF).GetRuntimeMethod(nameof(MathF.Sign), [typeof(float)])!, "SGN" }
        };

        private static readonly MethodInfo[] _supportedMethodTranslationsIndirect =
        [
            typeof(Math).GetRuntimeMethod(nameof(Math.Acos), [typeof(double)])!,
            typeof(Math).GetRuntimeMethod(nameof(Math.Asin), [typeof(double)])!,
            typeof(Math).GetRuntimeMethod(nameof(Math.Atan2), [typeof(double), typeof(double)])!,
            typeof(Math).GetRuntimeMethod(nameof(Math.Floor), [typeof(decimal)])!,
            typeof(Math).GetRuntimeMethod(nameof(Math.Floor), [typeof(double)])!,
            typeof(Math).GetRuntimeMethod(nameof(Math.Ceiling), [typeof(decimal)])!,
            typeof(Math).GetRuntimeMethod(nameof(Math.Ceiling), [typeof(double)])!,
            typeof(Math).GetRuntimeMethod(nameof(Math.Log10), [typeof(double)])!,
            typeof(Math).GetRuntimeMethod(nameof(Math.Log), [typeof(double), typeof(double)])!,
            typeof(MathF).GetRuntimeMethod(nameof(MathF.Ceiling), [typeof(float)])!,
            typeof(MathF).GetRuntimeMethod(nameof(MathF.Floor), [typeof(float)])!,
            typeof(MathF).GetRuntimeMethod(nameof(MathF.Log10), [typeof(float)]) !,
            typeof(MathF).GetRuntimeMethod(nameof(MathF.Log), [typeof(float), typeof(float)]) !,
            typeof(MathF).GetRuntimeMethod(nameof(MathF.Acos), [typeof(float)]) !,
            typeof(MathF).GetRuntimeMethod(nameof(MathF.Asin), [typeof(float)]) !,
            typeof(MathF).GetRuntimeMethod(nameof(MathF.Atan2), [typeof(float), typeof(float)])!,
            typeof(double).GetRuntimeMethod(nameof(double.DegreesToRadians), [typeof(double)])!,
            typeof(double).GetRuntimeMethod(nameof(double.RadiansToDegrees), [typeof(double)])!,
            typeof(float).GetRuntimeMethod(nameof(float.DegreesToRadians), [typeof(float)])!,
            typeof(float).GetRuntimeMethod(nameof(float.RadiansToDegrees), [typeof(float)])!
        ];

        private static readonly IEnumerable<MethodInfo> _truncateMethodInfos =
        [
            typeof(Math).GetRuntimeMethod(nameof(Math.Truncate), [typeof(decimal)])!,
            typeof(Math).GetRuntimeMethod(nameof(Math.Truncate), [typeof(double)])!,
            typeof(MathF).GetRuntimeMethod(nameof(MathF.Truncate), [typeof(float)])!
        ];

        private static readonly IEnumerable<MethodInfo> _roundMethodInfos =
        [
            typeof(Math).GetRuntimeMethod(nameof(Math.Round), [typeof(decimal)])!,
            typeof(Math).GetRuntimeMethod(nameof(Math.Round), [typeof(double)])!,
            typeof(Math).GetRuntimeMethod(nameof(Math.Round), [typeof(decimal), typeof(int)])!,
            typeof(Math).GetRuntimeMethod(nameof(Math.Round), [typeof(double), typeof(int)])!,
            typeof(MathF).GetRuntimeMethod(nameof(MathF.Round), [typeof(float)])!,
            typeof(MathF).GetRuntimeMethod(nameof(MathF.Round), [typeof(float), typeof(int)])!
        ];

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
                            [
                                _sqlExpressionFactory.Divide(
                                    _sqlExpressionFactory.Negate(arguments[0]),
                                    Translate(
                                        null,
                                        typeof(Math).GetMethod(nameof(Math.Sqrt))!,
                                        [
                                            _sqlExpressionFactory.Add(
                                                _sqlExpressionFactory.Negate(
                                                    _sqlExpressionFactory.Multiply(
                                                        arguments[0],
                                                        arguments[0]
                                                    )
                                                ),
                                                _sqlExpressionFactory.Constant(1d)
                                            )
                                        ],
                                        logger
                                    )!
                                )
                            ],
                            true,
                            [true],
                            method.ReturnType)),

                    // Arcsin(X) = Atn(X / Sqr(-X * X + 1))
                    nameof(Math.Asin) => _sqlExpressionFactory.Function(
                        "ATN",
                        [
                            _sqlExpressionFactory.Divide(
                                arguments[0],
                                Translate(
                                    null,
                                    typeof(Math).GetMethod(nameof(Math.Sqrt)) !,
                                    [
                                        _sqlExpressionFactory.Add(
                                            _sqlExpressionFactory.Negate(
                                                _sqlExpressionFactory.Multiply(
                                                    arguments[0],
                                                    arguments[0]
                                                )
                                            ),
                                            _sqlExpressionFactory.Constant(1d)
                                        )
                                    ],
                                    logger
                                )!
                            )
                        ],
                        true,
                        [true],
                        method.ReturnType),

                    // Logn(x) = Log(x) / Log(n)
                    nameof(Math.Log10) => _sqlExpressionFactory.Divide(
                        _sqlExpressionFactory.Function("LOG", [arguments[0]], true, [true], method.ReturnType),
                        _sqlExpressionFactory.Constant(Math.Log(10))
                    ),

                    // Math.Log(x, n) //Logn(x) = Log(x) / Log(n)
                    nameof(Math.Log) => _sqlExpressionFactory.Divide(
                        _sqlExpressionFactory.Function("LOG", [arguments[0]], true, [true], method.ReturnType),
                        _sqlExpressionFactory.Function("LOG", [arguments[1]], true, [true], method.ReturnType)
                    ),

                    nameof(Math.Floor) => CreateFix(arguments, method.ReturnType),
                    nameof(Math.Ceiling) => CreateCeiling(arguments, method.ReturnType),

                    nameof(Math.Atan2) => _sqlExpressionFactory.Function(
                        "ATN",
                        [
                            _sqlExpressionFactory.Divide(
                                arguments[0],
                                arguments[1]
                            )
                        ],
                        true,
                        [true],
                        method.ReturnType),

                    nameof(double.DegreesToRadians) => _sqlExpressionFactory.Multiply(arguments[0], _sqlExpressionFactory.Divide(_sqlExpressionFactory.Constant(Math.PI), _sqlExpressionFactory.Constant(180))),

                    nameof(double.RadiansToDegrees) => _sqlExpressionFactory.Multiply(arguments[0], _sqlExpressionFactory.Divide(_sqlExpressionFactory.Constant(180), _sqlExpressionFactory.Constant(Math.PI))),

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
                    [argument],
                    nullable: true,
                    argumentsPropagateNullability: [true],
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
                    [argument, digits],
                    nullable: false,
                    argumentsPropagateNullability: [true, false],
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
                [
                    new CaseWhenClause(
                        _sqlExpressionFactory.Equal(
                            fixExpression,
                            arguments[0]),
                        fixExpression)
                ],
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