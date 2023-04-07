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
    public class JetObjectToStringTranslator : IMethodCallTranslator
    {
        private readonly JetSqlExpressionFactory _sqlExpressionFactory;

        private const int DefaultLength = 100;

        private static readonly Dictionary<Type, string> _typeMapping
            = new()
            {
                { typeof(sbyte), "varchar(4)" },
                { typeof(byte), "varchar(3)" },
                { typeof(short), "varchar(6)" },
                { typeof(ushort), "varchar(5)" },
                { typeof(int), "varchar(11)" },
                { typeof(uint), "varchar(10)" },
                { typeof(long), "varchar(20)" },
                { typeof(ulong), "varchar(20)" },
                { typeof(float), $"varchar({DefaultLength})" },
                { typeof(double), $"varchar({DefaultLength})" },
                { typeof(decimal), $"varchar({DefaultLength})" },
                { typeof(char), "varchar(1)" },
                { typeof(DateTime), $"varchar({DefaultLength})" },
                { typeof(DateTimeOffset), $"varchar({DefaultLength})" },
                { typeof(TimeSpan), $"varchar({DefaultLength})" },
                { typeof(Guid), "varchar(36)" },
                { typeof(byte[]), $"varchar({DefaultLength})" },
            };

        public JetObjectToStringTranslator(SqlExpressionFactory sqlExpressionFactory)
            => _sqlExpressionFactory = (JetSqlExpressionFactory)sqlExpressionFactory;

        public virtual SqlExpression Translate(
            SqlExpression instance,
            MethodInfo method,
            IReadOnlyList<SqlExpression> arguments,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            Check.NotNull(method, nameof(method));
            Check.NotNull(arguments, nameof(arguments));
            Check.NotNull(logger, nameof(logger));

            if (instance == null || method.Name != nameof(ToString) || arguments.Count != 0)
            {
                return null;
            }

            if (instance.Type == typeof(bool))
            {
                if (instance is ColumnExpression columnExpression && columnExpression.IsNullable)
                {
                    return _sqlExpressionFactory.Case(
                        new[]
                        {
                            new CaseWhenClause(
                                _sqlExpressionFactory.Equal(instance, _sqlExpressionFactory.Constant(false)),
                                _sqlExpressionFactory.Constant(false.ToString())),
                            new CaseWhenClause(
                                _sqlExpressionFactory.Equal(instance, _sqlExpressionFactory.Constant(true)),
                                _sqlExpressionFactory.Constant(true.ToString()))
                        },
                        _sqlExpressionFactory.Constant(null));
                }

                return _sqlExpressionFactory.Case(
                    new[]
                    {
                        new CaseWhenClause(
                            _sqlExpressionFactory.Equal(instance, _sqlExpressionFactory.Constant(false)),
                            _sqlExpressionFactory.Constant(false.ToString()))
                    },
                    _sqlExpressionFactory.Constant(true.ToString()));
            }

            return _typeMapping.TryGetValue(instance.Type, out var storeType)
                ? _sqlExpressionFactory.Convert(instance, typeof(string))
                : null;
        }
    }
}