// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using EntityFrameworkCore.Jet.Metadata;
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
        protected override ResultSetMapping AppendSelectAffectedCountCommand(StringBuilder commandStringBuilder, string name, string schema, int commandPosition)
        {
            commandStringBuilder
                .Append("SELECT @@ROWCOUNT")
                .Append(SqlGenerationHelper.StatementTerminator).AppendLine()
                .AppendLine();

            return ResultSetMapping.LastInResultSet;
        }
    }
}
