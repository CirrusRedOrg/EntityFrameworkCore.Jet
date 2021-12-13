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

        // TODO: Translation.
        [NotNull] private static readonly MethodInfo _concat = typeof(string).GetRuntimeMethod(nameof(string.Concat), new[] {typeof(string), typeof(string)});

        [NotNull] private static readonly MethodInfo _contains = typeof(string).GetRuntimeMethod(nameof(string.Contains), new[] {typeof(string)});

        [NotNull] private static readonly MethodInfo _startsWith = typeof(string).GetRuntimeMethod(nameof(string.StartsWith), new[] {typeof(string)});
        [NotNull] private static readonly MethodInfo _endsWith = typeof(string).GetRuntimeMethod(nameof(string.EndsWith), new[] {typeof(string)});

        [NotNull] private static readonly MethodInfo _trimWithNoParam = typeof(string).GetRuntimeMethod(nameof(string.Trim), new Type[0]);
        [NotNull] private static readonly MethodInfo _trimWithChars = typeof(string).GetRuntimeMethod(nameof(string.Trim), new[] {typeof(char[])});
        // [NotNull] private static readonly MethodInfo _trimWithSingleChar = typeof(string).GetRuntimeMethod(nameof(string.Trim), new[] {typeof(char)}); // Jet TRIM does not take arguments

        [NotNull] private static readonly MethodInfo _trimStartWithNoParam = typeof(string).GetRuntimeMethod(nameof(string.TrimStart), new Type[0]);
        [NotNull] private static readonly MethodInfo _trimStartWithChars = typeof(string).GetRuntimeMethod(nameof(string.TrimStart), new[] {typeof(char[])});
        // [NotNull] private static readonly MethodInfo _trimStartWithSingleChar = typeof(string).GetRuntimeMethod(nameof(string.TrimStart), new[] {typeof(char)}); // Jet LTRIM does not take arguments

        [NotNull] private static readonly MethodInfo _trimEndWithNoParam = typeof(string).GetRuntimeMethod(nameof(string.TrimEnd), new Type[0]);
        [NotNull] private static readonly MethodInfo _trimEndWithChars = typeof(string).GetRuntimeMethod(nameof(string.TrimEnd), new[] {typeof(char[])});
        // [NotNull] private static readonly MethodInfo _trimEndWithSingleChar = typeof(string).GetRuntimeMethod(nameof(string.TrimEnd), new[] {typeof(char)}); // Jet LTRIM does not take arguments

        [NotNull] private static readonly MethodInfo _substring = typeof(string).GetTypeInfo()
            .GetDeclaredMethods(nameof(string.Substring))
            .Single(
                m => m.GetParameters()
                    .Length == 1);

        [NotNull] private static readonly MethodInfo _substringWithLength = typeof(string).GetTypeInfo()
            .GetDeclaredMethods(nameof(string.Substring))
            .Single(
                m => m.GetParameters()
                    .Length == 2);

        [NotNull] private static readonly MethodInfo _toLower = typeof(string).GetRuntimeMethod(nameof(string.ToLower), Array.Empty<Type>());
        [NotNull] private static readonly MethodInfo _toUpper = typeof(string).GetRuntimeMethod(nameof(string.ToUpper), Array.Empty<Type>());

        [NotNull] private static readonly MethodInfo _replace = typeof(string).GetRuntimeMethod(nameof(string.Replace), new[] {typeof(string), typeof(string)});

        private static readonly MethodInfo _isNullOrWhiteSpace = typeof(string).GetRuntimeMethod(nameof(string.IsNullOrWhiteSpace), new[] {typeof(string)});

        public JetStringMethodTranslator(ISqlExpressionFactory sqlExpressionFactory)
            => _sqlExpressionFactory = (JetSqlExpressionFactory)sqlExpressionFactory;

        public SqlExpression Translate(SqlExpression instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments, IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            if (Equals(method, _contains))
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

            if (Equals(method, _startsWith))
            {
                return _sqlExpressionFactory.Like(
                    // ReSharper disable once AssignNullToNotNullAttribute
                    instance,
                    _sqlExpressionFactory.Add(arguments[0], _sqlExpressionFactory.Constant("%"))
                );
            }

            if (Equals(method, _endsWith))
            {
                return _sqlExpressionFactory.Like(
                    instance,
                    _sqlExpressionFactory.Add(_sqlExpressionFactory.Constant("%"), arguments[0]));
            }

            // Jet TRIM does not take arguments.
            // _trimWithNoParam is only available since .NET Core 2.0 (or .NET Standard 2.1).
            if (Equals(method, _trimWithNoParam) ||
                Equals(method, _trimWithChars) && ((arguments[0] as SqlConstantExpression)?.Value == null ||
                 ((arguments[0] as SqlConstantExpression)?.Value as Array)?.Length == 0))
            {
                return _sqlExpressionFactory.Function("TRIM", new[] {instance}, false, new[] {false}, method.ReturnType);
            }

            // Jet LTRIM does not take arguments
            // _trimStartWithNoParam is only available since .NET Core 2.0 (or .NET Standard 2.1).
            if (Equals(method, _trimStartWithNoParam) ||
                Equals(method, _trimStartWithChars) && ((arguments[0] as SqlConstantExpression)?.Value == null ||
                 ((arguments[0] as SqlConstantExpression)?.Value as Array)?.Length == 0))
            {
                return _sqlExpressionFactory.Function("LTRIM", new[] {instance}, false, new[] {false}, method.ReturnType);
            }

            // Jet RTRIM does not take arguments
            // _trimEndWithNoParam is only available since .NET Core 2.0 (or .NET Standard 2.1).
            if (Equals(method, _trimEndWithNoParam) ||
                Equals(method, _trimEndWithChars) && ((arguments[0] as SqlConstantExpression)?.Value == null ||
                 ((arguments[0] as SqlConstantExpression)?.Value as Array)?.Length == 0))
            {
                return _sqlExpressionFactory.Function("RTRIM", new[] {instance}, false, new[] {false}, method.ReturnType);
            }

            if (Equals(method, _toLower))
            {
                return _sqlExpressionFactory.Function("LCASE", new[] {instance}, false, new[] {false}, method.ReturnType);
            }

            if (Equals(method, _toUpper))
            {
                return _sqlExpressionFactory.Function("UCASE", new[] {instance}, false, new[] {false}, method.ReturnType);
            }

            if (Equals(_substring, _trimEndWithNoParam) ||
                Equals(_substringWithLength, _trimEndWithChars))
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
                                _sqlExpressionFactory.Constant(1)),
                        arguments[1]
                    });

                // MID can be called with an optional `length` parameter.
                if (arguments.Count >= 2)
                    parameters.Add(arguments[1]);

                return _sqlExpressionFactory.Function(
                    "MID",
                    parameters,
                    false, new[] {false},
                    method.ReturnType);
            }

            if (Equals(method, _replace))
            {
                return _sqlExpressionFactory.Function(
                    "REPLACE",
                    new[] {instance}.Concat(arguments),
                    false, new[] {false},
                    method.ReturnType);
            }

            if (Equals(method, _isNullOrWhiteSpace))
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

            return null;
        }
    }
}