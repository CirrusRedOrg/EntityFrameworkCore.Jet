using System;
using System.Collections.Generic;
using EntityFrameworkCore.Jet.Utilities;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Query.Expressions.Internal
{
    public class IIfSqlFunctionExpression : SqlFunctionExpression
    {
        public IIfSqlFunctionExpression(
            IEnumerable<SqlExpression> arguments,
            RelationalTypeMapping typeMapping = null)
            : base(
                null,
                null,
                "IIf",
                false,
                Check.NotNull(arguments, nameof(arguments)),
                true,
                null,
                typeMapping)
        {
        }

        public IIfSqlFunctionExpression(
            [NotNull] SqlExpression condition,
            [CanBeNull] SqlExpression ifTrue,
            [CanBeNull] SqlExpression ifFalse,
            [CanBeNull] Type returnType = null,
            [CanBeNull] RelationalTypeMapping typeMapping = null)
            : base(
                null,
                null,
                "IIf",
                false,
                new[]
                {
                    condition,
                    ifTrue ?? new SqlConstantExpression(Constant(null), RelationalTypeMapping.NullMapping),
                    ifFalse ?? new SqlConstantExpression(Constant(null), RelationalTypeMapping.NullMapping)
                },
                true,
                returnType,
                typeMapping
            )
        {
        }
    }
}