// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using EntityFrameworkCore.Jet.Metadata;
using EntityFrameworkCore.Jet.Utilities;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.Jet.Update.Internal
{
    /// <summary>
    ///     <para>
    ///         This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///         the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///         any release. You should only use it directly in your code with extreme caution and knowing that
    ///         doing so can result in application failures when updating to a new Entity Framework Core release.
    ///     </para>
    ///     <para>
    ///         The service lifetime is <see cref="ServiceLifetime.Singleton" />. This means a single instance
    ///         is used by many <see cref="DbContext" /> instances. The implementation must be thread-safe.
    ///         This service cannot depend on services registered as <see cref="ServiceLifetime.Scoped" />.
    ///     </para>
    /// </summary>
    public class JetUpdateSqlGenerator : UpdateAndSelectSqlGenerator, IJetUpdateSqlGenerator
    {
        public JetUpdateSqlGenerator(
            [NotNull] UpdateSqlGeneratorDependencies dependencies)
            : base(dependencies)
        {
        }

        public override ResultSetMapping AppendInsertOperation(StringBuilder commandStringBuilder, IReadOnlyModificationCommand command,
            int commandPosition, out bool requiresTransaction)
        {
            //No database columns need to be read back
            if (command.ColumnModifications.All(o => !o.IsRead))
            {
                return AppendInsertReturningOperation(commandStringBuilder, command, commandPosition, out requiresTransaction);
            }
            return base.AppendInsertOperation(commandStringBuilder, command, commandPosition, out requiresTransaction);
        }

        public ResultSetMapping AppendBulkInsertOperation(StringBuilder commandStringBuilder, IReadOnlyList<IReadOnlyModificationCommand> modificationCommands, int commandPosition, out bool requiresTransaction)
        {
            var firstCommand = modificationCommands[0];
            var table = StoreObjectIdentifier.Table(firstCommand.TableName, modificationCommands[0].Schema);

            var readOperations = firstCommand.ColumnModifications.Where(o => o.IsRead).ToList();
            var writeOperations = firstCommand.ColumnModifications.Where(o => o.IsWrite).ToList();
            var keyOperations = firstCommand.ColumnModifications.Where(o => o.IsKey).ToList();

            var writableOperations = modificationCommands[0].ColumnModifications
                .Where(
                    o =>
                        o.Property?.GetValueGenerationStrategy(table) != JetValueGenerationStrategy.IdentityColumn
                        && o.Property?.GetComputedColumnSql() is null
                        && o.Property?.GetColumnType() is not "rowversion" and not "timestamp")
                .ToList();

            requiresTransaction = modificationCommands.Count > 1;
            foreach (var modification in modificationCommands)
            {
                AppendInsertOperation(commandStringBuilder, modification, commandPosition++, out var localRequiresTransaction);
                requiresTransaction = requiresTransaction || localRequiresTransaction;
            }

            return readOperations.Count == 0
                ? ResultSetMapping.NoResults
                : ResultSetMapping.LastInResultSet;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        protected override void AppendIdentityWhereCondition(StringBuilder commandStringBuilder, IColumnModification columnModification)
        {
            SqlGenerationHelper.DelimitIdentifier(commandStringBuilder, columnModification.ColumnName);
            commandStringBuilder.Append(" = ");

            commandStringBuilder.Append("@@identity");
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        protected override void AppendRowsAffectedWhereCondition(StringBuilder commandStringBuilder, int expectedRowsAffected)
        {
            commandStringBuilder
                .Append("@@ROWCOUNT = ")
                .Append(expectedRowsAffected.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>rom your code. This API may change or be removed in future releases.
        protected override ResultSetMapping AppendSelectAffectedCountCommand(StringBuilder commandStringBuilder, string name, string? schema, int commandPosition)
        {
            commandStringBuilder
                .Append("SELECT @@ROWCOUNT")
                .Append(SqlGenerationHelper.StatementTerminator).AppendLine()
                .AppendLine();

            return ResultSetMapping.LastInResultSet;
        }

        //If multiple columns were output, the SQL Server behavior is to produce a INSERT INTO ... OUTPUT statement
        //Jet does not support OUTPUT, so we need to use a SELECT statement instead
        //@@identity is available to get the identity value of the last inserted row.
        //Most tables will only have one identity column, so the AppendIdentityWhereColumn only gets called once
        //However if there is a complex identity so that you have more than one identity column, the rest of those get added
        //Given @@identity only gets the value of the first identity column, we must only use that and not any others

        protected override void AppendWhereAffectedClause(StringBuilder commandStringBuilder, IReadOnlyList<IColumnModification> operations)
        {
            commandStringBuilder
                .AppendLine()
                .Append("WHERE ");

            AppendRowsAffectedWhereCondition(commandStringBuilder, 1);
            bool isfirstkeycolumn = true;
            if (operations.Count > 0)
            {
                commandStringBuilder
                    .Append(" AND ")
                    .AppendJoin(
                        operations, (sb, v) =>
                        {
                            if (v is { IsKey: true, IsRead: false })
                            {
                                AppendWhereCondition(sb, v, v.UseOriginalValueParameter);
                                return true;
                            }

                            if (IsIdentityOperation(v) && isfirstkeycolumn)
                            {
                                AppendIdentityWhereCondition(sb, v);
                                isfirstkeycolumn = false;
                                return true;
                            }

                            return false;
                        }, " AND ");
            }
        }

        public override ResultSetMapping AppendStoredProcedureCall(
        StringBuilder commandStringBuilder,
        IReadOnlyModificationCommand command,
        int commandPosition,
        out bool requiresTransaction)
        {
            Check.DebugAssert(command.StoreStoredProcedure is not null, "command.StoredProcedure is not null");

            var storedProcedure = command.StoreStoredProcedure;

            var resultSetMapping = ResultSetMapping.NoResults;

            foreach (var resultColumn in storedProcedure.ResultColumns)
            {
                resultSetMapping = ResultSetMapping.LastInResultSet;

                if (resultColumn == command.RowsAffectedColumn)
                {
                    resultSetMapping |= ResultSetMapping.ResultSetWithRowsAffectedOnly;
                }
                else
                {
                    resultSetMapping = ResultSetMapping.LastInResultSet;
                    break;
                }
            }

            Check.DebugAssert(
                storedProcedure.Parameters.Any() || storedProcedure.ResultColumns.Any(),
                "Stored procedure call with neither parameters nor result columns");

            commandStringBuilder.Append("EXEC ");

            if (storedProcedure.ReturnValue is not null)
            {
                var returnValueModification = command.ColumnModifications.First(c => c.Column is IStoreStoredProcedureReturnValue);

                Check.DebugAssert(returnValueModification.UseCurrentValueParameter, "returnValueModification.UseCurrentValueParameter");
                Check.DebugAssert(!returnValueModification.UseOriginalValueParameter, "!returnValueModification.UseOriginalValueParameter");

                SqlGenerationHelper.GenerateParameterNamePlaceholder(commandStringBuilder, returnValueModification.ParameterName!);

                commandStringBuilder.Append(" = ");

                resultSetMapping |= ResultSetMapping.HasOutputParameters;
            }

            SqlGenerationHelper.DelimitIdentifier(commandStringBuilder, storedProcedure.Name, storedProcedure.Schema);

            if (storedProcedure.Parameters.Any())
            {
                commandStringBuilder.Append(' ');

                var first = true;

                // Only positional parameter style supported for now, see #28439

                // Note: the column modifications are already ordered according to the sproc parameter ordering
                // (see ModificationCommand.GenerateColumnModifications)
                for (var i = 0; i < command.ColumnModifications.Count; i++)
                {
                    var columnModification = command.ColumnModifications[i];

                    if (columnModification.Column is not IStoreStoredProcedureParameter parameter)
                    {
                        continue;
                    }

                    if (first)
                    {
                        first = false;
                    }
                    else
                    {
                        commandStringBuilder.Append(", ");
                    }

                    Check.DebugAssert(columnModification.UseParameter, "Column modification matched a parameter, but UseParameter is false");

                    commandStringBuilder.Append(columnModification.UseOriginalValueParameter
                        ? columnModification.OriginalParameterName!
                        : columnModification.ParameterName!);

                    // Note that in/out parameters also get suffixed with OUTPUT in SQL Server
                    if (parameter.Direction.HasFlag(ParameterDirection.Output))
                    {
                        commandStringBuilder.Append(" OUTPUT");
                        resultSetMapping |= ResultSetMapping.HasOutputParameters;
                    }
                }
            }

            commandStringBuilder.AppendLine(SqlGenerationHelper.StatementTerminator);

            requiresTransaction = true;

            return resultSetMapping;
        }
    }
}
