using EntityFrameworkCore.Jet.Infrastructure;
using EntityFrameworkCore.Jet.Query.Sql.Internal;
using EntityFrameworkCore.Jet.Utilities;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage.Internal;

namespace EntityFrameworkCore.LibRed.Query.Sql.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class LibRedQuerySqlGenerator : QuerySqlGenerator, IJetExpressionVisitor
    {
        private static readonly Dictionary<string, string> _convertMappings = new()
        {
            { nameof(Boolean), "CBOOL" },
            { nameof(Byte), "CBYTE" },
            { nameof(SByte), "CINT" },
            { nameof(Int16), "CINT" },
            { nameof(Int32), "CLNG" },
            { nameof(Int64), "CLNG" },
            { nameof(Single), "CSNG" },
            { nameof(Double), "CDBL" },
            { nameof(Decimal), "CDEC" },
            { nameof(DateTime), "CDATE" },
            { nameof(TimeOnly), "TIMEVALUE" },
        };

        // VBA functions that raise on a NULL in a numeric argument - a length, start, count or code - instead of
        // propagating it, with the positions that need guarding. Verified against ACE in
        // LibRed.Core.Tests.AceNullArgumentProbeTest; add an entry as others turn up.
        private static readonly Dictionary<string, int[]> _nullHostileArguments = new(StringComparer.OrdinalIgnoreCase)
        {
            { "MID", [1, 2] },
        };

        private readonly ITypeMappingSource _typeMappingSource;
        private readonly ISqlGenerationHelper _sqlGenerationHelper;
        private CoreTypeMapping? _boolTypeMapping;
        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public LibRedQuerySqlGenerator(
            QuerySqlGeneratorDependencies dependencies,
            ITypeMappingSource typeMappingSource)
            : base(dependencies)
        {
            _typeMappingSource = typeMappingSource;
            _sqlGenerationHelper = dependencies.SqlGenerationHelper;
            _boolTypeMapping = _typeMappingSource.FindMapping(typeof(bool));
        }

        protected override bool TryGenerateWithoutWrappingSelect(SelectExpression selectExpression)
            => selectExpression.Tables is not [ValuesExpression]
               && base.TryGenerateWithoutWrappingSelect(selectExpression);

        private void GenerateList<T>(
            IReadOnlyList<T> items,
            Action<T> generationAction,
            Action<IRelationalCommandBuilder>? joinAction = null)
        {
            joinAction ??= (isb => isb.Append(", "));

            for (var i = 0; i < items.Count; i++)
            {
                if (i > 0)
                {
                    joinAction(Sql);
                }

                generationAction(items[i]);
            }
        }

        protected override Expression VisitJsonScalar(JsonScalarExpression jsonScalarExpression)
        {
            var path = jsonScalarExpression.Path;
            if (path.Count == 0)
            {
                Visit(jsonScalarExpression.Json);
                return jsonScalarExpression;
            }

            throw new NotSupportedException(
                "JSON path queries are not supported; only the root JSON column can be selected.");
        }

        protected override Expression VisitOrdering(OrderingExpression orderingExpression)
        {
            // Jet uses the value -1 as True, so ordering by a boolean expression will first list the True values
            // before the False values, which is the opposite of what .NET and other DBMS do, which are using 1 as True.

            if (orderingExpression.Expression.TypeMapping is BoolTypeMapping
                && orderingExpression.Expression.TypeMapping.GetType() == _boolTypeMapping?.GetType())
            {
                orderingExpression = new OrderingExpression(
                    new SqlUnaryExpression(
                        ExpressionType.Not,
                        orderingExpression.Expression,
                        orderingExpression.Expression.Type,
                        orderingExpression.Expression.TypeMapping),
                    orderingExpression.IsAscending);
            }

            if (orderingExpression.Expression is SqlConstantExpression or SqlParameterExpression)
            {
                Sql.Append("1");
            }
            else
            {
                Visit(orderingExpression.Expression);
            }

            if (!orderingExpression.IsAscending)
            {
                Sql.Append(" DESC");
            }

            return orderingExpression;
        }

        protected override Expression VisitSqlBinary(SqlBinaryExpression sqlBinaryExpression)
        {
            Check.NotNull(sqlBinaryExpression, nameof(sqlBinaryExpression));

            // String concatenation propagates NULL for EF, but Access's '&' coerces a NULL operand to a
            // zero-length string instead (see GetOperator for why '+', which does propagate, is not usable).
            // Restore the propagation around the concat rather than in it, and only for operands that can
            // actually be NULL - a concat of non-nullable operands generates exactly as it did before.
            if (sqlBinaryExpression.OperatorType == ExpressionType.Add
                && sqlBinaryExpression.Type == typeof(string)
                && (MayBeNull(sqlBinaryExpression.Left) || MayBeNull(sqlBinaryExpression.Right)))
            {
                // Guard shape follows EF's own: IS NOT NULL checks ANDed together, concat in the THEN. The two
                // operands are frequently the same expression (x + x), so check each distinct one once.
                var checks = new List<SqlExpression>(2);
                foreach (var operand in new[] { sqlBinaryExpression.Left, sqlBinaryExpression.Right })
                {
                    if (MayBeNull(operand) && !checks.Any(c => c.Equals(operand)))
                    {
                        checks.Add(operand);
                    }
                }

                Sql.Append("IIF(");

                for (var i = 0; i < checks.Count; i++)
                {
                    if (i > 0)
                    {
                        Sql.Append(" AND ");
                    }

                    Visit(checks[i]);
                    Sql.Append(" IS NOT NULL");
                }

                Sql.Append(", ");

                base.VisitSqlBinary(sqlBinaryExpression);

                Sql.Append(", NULL)");
                return sqlBinaryExpression;
            }

            var res = base.VisitSqlBinary(sqlBinaryExpression);
            return res;
        }

        /// <summary>
        ///     Whether an operand of a string concatenation needs a NULL guard: only a bare nullable column does.
        ///     Anything composed - a COALESCE, a CASE, a function, a parameter - is left alone, because EF has
        ///     already expressed whatever null handling it wants there. Guarding those as well produced
        ///     IIF(IIF(x IS NULL, '', x) IS NOT NULL, ...), a null check on an expression that cannot be null,
        ///     with the operand duplicated three times.
        /// </summary>
        private static bool MayBeNull(SqlExpression expression)
            => expression is ColumnExpression { IsNullable: true };

        /// <summary>The nullable columns an expression reads, so constants and parameters contribute nothing.</summary>
        private static IEnumerable<ColumnExpression> NullableColumns(SqlExpression expression)
        {
            switch (expression)
            {
                case ColumnExpression { IsNullable: true } column:
                    yield return column;
                    break;

                case SqlUnaryExpression unary:
                    foreach (var column in NullableColumns(unary.Operand))
                    {
                        yield return column;
                    }

                    break;

                case SqlBinaryExpression binary:
                    foreach (var column in NullableColumns(binary.Left).Concat(NullableColumns(binary.Right)))
                    {
                        yield return column;
                    }

                    break;

                case SqlFunctionExpression { Arguments: { } functionArguments }:
                    foreach (var column in functionArguments.SelectMany(NullableColumns))
                    {
                        yield return column;
                    }

                    break;
            }
        }

        protected override Expression VisitSqlConstant(SqlConstantExpression sqlConstantExpression)
        {
            if (sqlConstantExpression.TypeMapping is BoolTypeMapping
                && sqlConstantExpression.TypeMapping.GetType() != _boolTypeMapping?.GetType())
            {
                Sql.Append((bool)sqlConstantExpression.Value! ? "TRUE" : "FALSE");
                return sqlConstantExpression;
            }
            return base.VisitSqlConstant(sqlConstantExpression);
        }

        protected override Expression VisitSqlUnary(SqlUnaryExpression sqlUnaryExpression)
        {
            switch (sqlUnaryExpression.OperatorType)
            {
                case ExpressionType.Convert:
                    return VisitJetConvertExpression(sqlUnaryExpression);
                case ExpressionType.Not when sqlUnaryExpression.Type != typeof(bool):
                    {
                        Sql.Append(" (BNOT");

                        var requiresBrackets = RequiresParentheses(sqlUnaryExpression, sqlUnaryExpression.Operand);
                        if (requiresBrackets)
                        {
                            Sql.Append("(");
                        }

                        Visit(sqlUnaryExpression.Operand);
                        if (requiresBrackets)
                        {
                            Sql.Append(")");
                        }

                        Sql.Append(")");

                        return sqlUnaryExpression;
                    }
                default:
                    return base.VisitSqlUnary(sqlUnaryExpression);
            }
        }


        private Expression VisitJetConvertExpression(SqlUnaryExpression convertExpression)
        {
            var typeMapping = convertExpression.TypeMapping ?? throw new InvalidOperationException(
                RelationalStrings.UnsupportedType(convertExpression.Type.ShortDisplayName()));

            // We are explicitly converting to the target type (convertExpression.Type) and not the CLR type of the
            // associated type mapping. This allows for conversions on the database side (e.g. CDBL()) but handling
            // of the returned value using a different (unaligned) type mapping (e.g. date/time related ones).
            if (_convertMappings.TryGetValue(convertExpression.Type.Name, out var function))
            {
                SqlExpression checksqlexp = convertExpression.Operand;
                SqlExpression? notnullsqlexp = null;

                SqlFunctionExpression WrapConvert(SqlExpression inner) =>
                    new SqlFunctionExpression(function, [inner], false, [false], typeMapping.ClrType, null);

                if (convertExpression.TypeMapping is ByteArrayTypeMapping)
                {
                    notnullsqlexp = checksqlexp;
                }
                else
                {
                    // A bool operand arrives already flipped to 0/1 by JetSqlExpressionFactory.Convert, so
                    // CBYTE/CINT/CLNG receive .NET's values rather than VARIANT_BOOL's 0/-1.
                    notnullsqlexp = WrapConvert(convertExpression.Operand);
                }

                SqlConstantExpression nullcons = new(null, typeof(string), RelationalTypeMapping.NullMapping);
                SqlUnaryExpression isnullexp = new(ExpressionType.Equal, checksqlexp, typeof(bool), null);
                List<CaseWhenClause> whenclause =
                [
                    new CaseWhenClause(isnullexp, nullcons)
                ];
                CaseExpression caseexp = new(whenclause, notnullsqlexp);

                switch (checksqlexp)
                {
                    case ColumnExpression { IsNullable: true }:
                        Visit(caseexp);
                        break;
                    case ColumnExpression:
                        Visit(notnullsqlexp);
                        break;
                    case SqlFunctionExpression { IsNullable: true, ArgumentsPropagateNullability: not null } functionExpression
                        when functionExpression.ArgumentsPropagateNullability.Any(d => d):
                        Visit(caseexp);
                        break;
                    case SqlFunctionExpression:
                        Visit(notnullsqlexp);
                        break;
                    case SqlBinaryExpression binaryExpression:
                        {
                            static bool IsNullable(SqlExpression? e) =>
                                e is ColumnExpression { IsNullable: true }
                                or SqlFunctionExpression { IsNullable: true };

                            if (IsNullable(binaryExpression.Left) || IsNullable(binaryExpression.Right))
                                Visit(caseexp);
                            else
                                Visit(notnullsqlexp);

                            break;
                        }
                    case SqlUnaryExpression:
                    case SqlConstantExpression { Value: not null }:
                        Visit(notnullsqlexp);
                        break;
                    default:
                        Visit(caseexp);
                        break;
                }

                return convertExpression;
            }

            if (typeMapping.ClrType.Name == nameof(String))
            {
                Sql.Append("(");
                Visit(convertExpression.Operand);
                Sql.Append(@" & '')");
                return convertExpression;
            }

            //Just pass the operand in the default case
            //If we have a type mapping on the operand, then it seems to work fine
            //Jet appears to be fairly flexible when types aren't specifically mentioned
            //Keep an eye on this for any further problems - doesn't show anything in the tests right now
            Visit(convertExpression.Operand);
            return convertExpression;
        }

        protected override string GetOperator(SqlBinaryExpression binaryExpression)
            => binaryExpression.OperatorType switch
            {
                ExpressionType.Add when binaryExpression.Type == typeof(string) => " & ",
                ExpressionType.And => " BAND ",
                ExpressionType.Modulo => " MOD ",
                ExpressionType.Or => " BOR ",
                ExpressionType.Not => " BNOT ",
                ExpressionType.ExclusiveOr => " BXOR ",
                ExpressionType.Divide when binaryExpression.Type == typeof(int) => " \\ ",
                _ => base.GetOperator(binaryExpression),
            };

        /// <summary>
        /// <summary>Generates the TOP part of the SELECT statement,</summary>
        /// <param name="selectExpression"> The select expression. </param>
        protected override void GenerateTop(SelectExpression selectExpression)
        {
            Check.NotNull(selectExpression, nameof(selectExpression));

            if (selectExpression is { Limit: not null, Offset: null })
            {
                Sql.Append("TOP ");
                Visit(selectExpression.Limit);
                Sql.Append(" ");
            }
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override void GenerateLimitOffset(SelectExpression selectExpression)
        {
            if (selectExpression.Offset != null)
            {
                Sql.AppendLine()
                    .Append("OFFSET ");

                Visit(selectExpression.Offset);

                Sql.Append(" ROWS");

                if (selectExpression.Limit != null)
                {
                    Sql.Append(" FETCH NEXT ");

                    Visit(selectExpression.Limit);

                    Sql.Append(" ROWS ONLY");
                }
            }
        }
        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override Expression VisitSqlFunction(SqlFunctionExpression sqlFunctionExpression)
        {
            if (sqlFunctionExpression.Name.StartsWith("@@", StringComparison.Ordinal))
            {
                Sql.Append(sqlFunctionExpression.Name);
                return sqlFunctionExpression;
            }

            // The guard has to be applied here rather than in the query tree: EF removes a CASE that merely
            // replicates SQL's native null propagation (dotnet/efcore#34127), which is what this looks like to
            // every dialect where these functions do propagate. IIF short-circuits, so the call is not evaluated.
            if (_nullHostileArguments.TryGetValue(sqlFunctionExpression.Name, out var guardedPositions)
                && sqlFunctionExpression.Arguments is { Count: > 0 } arguments)
            {
                // Nullability coming from the value argument itself needs no guard: ACE returns NULL for the
                // whole call when the value is NULL, before it coerces the numeric arguments. Only a NULL
                // arriving from somewhere else - MID(note, 1, LEN(otherTable.Name)) - reaches the coercion.
                var valueColumns = NullableColumns(arguments[0]).ToHashSet();

                var nullable = guardedPositions
                    .Where(position => position < arguments.Count)
                    .Where(position => NullableColumns(arguments[position]).Any(column => !valueColumns.Contains(column)))
                    .Select(position => arguments[position])
                    .ToList();

                if (nullable.Count > 0)
                {
                    Sql.Append("IIF(");
                    for (var i = 0; i < nullable.Count; i++)
                    {
                        if (i > 0)
                        {
                            Sql.Append(" OR ");
                        }

                        Visit(nullable[i]);
                        Sql.Append(" IS NULL");
                    }

                    Sql.Append(", NULL, ");
                    base.VisitSqlFunction(sqlFunctionExpression);
                    Sql.Append(")");
                    return sqlFunctionExpression;
                }
            }

            if (sqlFunctionExpression.Name.Equals("POW", StringComparison.OrdinalIgnoreCase) && sqlFunctionExpression.Arguments != null)
            {
                Visit(sqlFunctionExpression.Arguments[0]);
                Sql.Append("^");
                Visit(sqlFunctionExpression.Arguments[1]);
                return sqlFunctionExpression;
            }

            if (sqlFunctionExpression.Name.Equals("REPLACE", StringComparison.OrdinalIgnoreCase) &&
                sqlFunctionExpression.Arguments is { Count: 3 })
            {
                // Access VBA's Replace() throws "Type mismatch" when ANY argument is NULL rather than
                // propagating NULL as relational semantics require. Access IIF is also non-short-circuit
                // (evaluates both branches), so a simple IIF wrapper doesn't prevent the crash.
                // Solution: outer IIF returns NULL when any nullable arg IS NULL, while inner IIFs
                // substitute safe non-NULL placeholders so REPLACE never actually receives NULL.
                static SqlExpression? GetNullableTarget(SqlExpression arg) => arg switch
                {
                    ColumnExpression { IsNullable: true } col => col,
                    SqlUnaryExpression { OperatorType: ExpressionType.Convert, Operand: ColumnExpression { IsNullable: true } inner } => inner,
                    SqlUnaryExpression { OperatorType: ExpressionType.Convert, Operand: SqlFunctionExpression { IsNullable: true } inner } => inner,
                    _ => null
                };

                var arg0Check = GetNullableTarget(sqlFunctionExpression.Arguments[0]);
                var arg1Check = GetNullableTarget(sqlFunctionExpression.Arguments[1]);
                var arg2Check = GetNullableTarget(sqlFunctionExpression.Arguments[2]);

                if (arg0Check != null || arg1Check != null || arg2Check != null)
                {
                    Sql.Append("IIF(");
                    var nullChecks = new SqlExpression?[] { arg0Check, arg1Check, arg2Check }
                        .Where(c => c != null).ToList();
                    for (int i = 0; i < nullChecks.Count; i++)
                    {
                        if (i > 0) Sql.Append(" OR ");
                        Visit(nullChecks[i]!);
                        Sql.Append(" IS NULL");
                    }
                    Sql.Append(", NULL, REPLACE(");

                    // Arg 0 (expression): '' prevents Type mismatch if NULL slips past outer IIF
                    if (arg0Check != null)
                    {
                        Sql.Append("IIF("); Visit(arg0Check); Sql.Append(" IS NULL, '', ");
                        Visit(sqlFunctionExpression.Arguments[0]); Sql.Append(")");
                    }
                    else Visit(sqlFunctionExpression.Arguments[0]);

                    Sql.Append(", ");

                    // Arg 1 (find): CHR(1) is a safe non-empty placeholder unlikely to appear in data
                    if (arg1Check != null)
                    {
                        Sql.Append("IIF("); Visit(arg1Check); Sql.Append(" IS NULL, CHR(1), ");
                        Visit(sqlFunctionExpression.Arguments[1]); Sql.Append(")");
                    }
                    else Visit(sqlFunctionExpression.Arguments[1]);

                    Sql.Append(", ");

                    // Arg 2 (replacewith): CHR(1) placeholder; result is discarded by outer IIF anyway
                    if (arg2Check != null)
                    {
                        Sql.Append("IIF("); Visit(arg2Check); Sql.Append(" IS NULL, CHR(1), ");
                        Visit(sqlFunctionExpression.Arguments[2]); Sql.Append(")");
                    }
                    else Visit(sqlFunctionExpression.Arguments[2]);

                    Sql.Append("))");
                    return sqlFunctionExpression;
                }
            }

            if (sqlFunctionExpression.Name.Equals("MID", StringComparison.OrdinalIgnoreCase) &&
                sqlFunctionExpression.Arguments is { Count: > 2 })
            {
                if (sqlFunctionExpression.Arguments[2] is ColumnExpression { IsNullable: true })
                {
                    Sql.Append("IIF(");
                    Visit(sqlFunctionExpression.Arguments[2]);
                    Sql.Append(" IS NULL, NULL, ");
                    base.VisitSqlFunction(sqlFunctionExpression);
                    Sql.Append(")");
                    return sqlFunctionExpression;
                }
                if (sqlFunctionExpression.Arguments[2] is SqlUnaryExpression { OperatorType: ExpressionType.Convert, Operand: ColumnExpression { IsNullable: true } or SqlFunctionExpression { IsNullable: true } } unaryExpression)
                {
                    Sql.Append("IIF(");
                    Visit(unaryExpression.Operand);
                    Sql.Append(" IS NULL, NULL, ");
                    base.VisitSqlFunction(sqlFunctionExpression);
                    Sql.Append(")");
                    return sqlFunctionExpression;
                }
            }
            var result = base.VisitSqlFunction(sqlFunctionExpression);
            return result;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        protected override Expression VisitDelete(DeleteExpression deleteExpression)
        {
            var selectExpression = deleteExpression.SelectExpression;

            if (selectExpression.Offset == null
                && selectExpression.Having == null
                && selectExpression.Orderings.Count == 0
                && selectExpression.GroupBy.Count == 0
                && selectExpression.Projection.Count == 0)
            {
                Sql.Append("DELETE ");

                if (selectExpression.Tables.Count > 1)
                {
                    Sql.Append($"{Dependencies.SqlGenerationHelper.DelimitIdentifier(deleteExpression.Table.Alias)}.*");
                    Sql.AppendLine();
                }

                Sql.Append("FROM ");
                GenerateList(selectExpression.Tables, e => Visit(e), sql => sql.AppendLine());

                if (selectExpression.Predicate != null)
                {
                    Sql.AppendLine().Append("WHERE ");

                    Visit(selectExpression.Predicate);
                }

                GenerateLimitOffset(selectExpression);

                return deleteExpression;
            }

            throw new InvalidOperationException(
                RelationalStrings.ExecuteOperationWithUnsupportedOperatorInSqlGeneration(nameof(EntityFrameworkQueryableExtensions.ExecuteDelete)));
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        protected override Expression VisitUpdate(UpdateExpression updateExpression)
        {
            var selectExpression = updateExpression.SelectExpression;

            if (selectExpression.Offset == null
                && selectExpression.Having == null
                && selectExpression.Orderings.Count == 0
                && selectExpression.GroupBy.Count == 0
                && selectExpression.Projection.Count == 0
                && selectExpression.Limit == null)
            {
                Sql.Append("UPDATE ");

                GenerateList(selectExpression.Tables, e => Visit(e), sql => sql.AppendLine());

                Sql.AppendLine().Append("SET ");
                Visit(updateExpression.ColumnValueSetters[0].Column);
                Sql.Append(" = ");
                Visit(updateExpression.ColumnValueSetters[0].Value);

                using (Sql.Indent())
                {
                    foreach (var columnValueSetter in updateExpression.ColumnValueSetters.Skip(1))
                    {
                        Sql.AppendLine(",");
                        Visit(columnValueSetter.Column);
                        Sql.Append(" = ");
                        Visit(columnValueSetter.Value);
                    }
                }

                if (selectExpression.Predicate != null)
                {
                    Sql.AppendLine().Append("WHERE ");
                    Visit(selectExpression.Predicate);
                }

                return updateExpression;
            }

            throw new InvalidOperationException(
                RelationalStrings.ExecuteOperationWithUnsupportedOperatorInSqlGeneration(nameof(EntityFrameworkQueryableExtensions.ExecuteUpdate)));
        }

        /// <inheritdoc />
        protected override void CheckComposableSqlTrimmed(ReadOnlySpan<char> sql)
        {
            base.CheckComposableSqlTrimmed(sql);

            if (sql.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(RelationalStrings.FromSqlNonComposable);
            }
        }
    }
}
