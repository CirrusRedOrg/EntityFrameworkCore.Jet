// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Data.Common;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EntityFrameworkCore.Jet.Internal;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Migrations;
using EntityFrameworkCore.Jet.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EntityFrameworkCore.Jet.Migrations.Internal
{
    /// <summary>
    ///     <para>
    ///         This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///         the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///         any release. You should only use it directly in your code with extreme caution and knowing that
    ///         doing so can result in application failures when updating to a new Entity Framework Core release.
    ///     </para>
    ///     <para>
    ///         The service lifetime is <see cref="ServiceLifetime.Scoped" />. This means that each
    ///         <see cref="DbContext" /> instance will use its own instance of this service.
    ///         The implementation may depend on other services registered with any lifetime.
    ///         The implementation does not need to be thread-safe.
    ///     </para>
    /// </summary>
    public class JetHistoryRepository : HistoryRepository
    {
        private static readonly TimeSpan _retryDelay = TimeSpan.FromSeconds(1);
        public override LockReleaseBehavior LockReleaseBehavior => LockReleaseBehavior.Explicit;
        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public JetHistoryRepository([NotNull] HistoryRepositoryDependencies dependencies)
            : base(dependencies)
        {
        }

        /// <summary>
        ///     The name of the table that will serve as a database-wide lock for migrations.
        /// </summary>
        protected virtual string LockTableName { get; } = "__EFMigrationsLock";

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        protected override string ExistsSql => CreateExistsSql(TableName);

        private string CreateExistsSql(string tableName)
        {
            var stringTypeMapping = Dependencies.TypeMappingSource.GetMapping(typeof(string));

            return $"""
SELECT * FROM `INFORMATION_SCHEMA.TABLES` WHERE `TABLE_NAME` = {stringTypeMapping.GenerateSqlLiteral(tableName)};
""";
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        protected override bool InterpretExistsResult(object? value)
        {
            return value != DBNull.Value;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public override string GetInsertScript(HistoryRow row)
        {
            Check.NotNull(row, nameof(row));

            var stringTypeMapping = Dependencies.TypeMappingSource.GetMapping(typeof(string));

            return new StringBuilder().Append("INSERT INTO ")
                .Append(SqlGenerationHelper.DelimitIdentifier(TableName))
                .Append(" (")
                .Append(SqlGenerationHelper.DelimitIdentifier(MigrationIdColumnName))
                .Append(", ")
                .Append(SqlGenerationHelper.DelimitIdentifier(ProductVersionColumnName))
                .AppendLine(")")
                .Append("VALUES (")
                .Append(stringTypeMapping.GenerateSqlLiteral(row.MigrationId))
                .Append(", ")
                .Append(stringTypeMapping.GenerateSqlLiteral(row.ProductVersion))
                .AppendLine(");")
                .ToString();
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public override string GetDeleteScript(string migrationId)
        {
            Check.NotEmpty(migrationId, nameof(migrationId));

            var stringTypeMapping = Dependencies.TypeMappingSource.GetMapping(typeof(string));

            return new StringBuilder().Append("DELETE FROM ")
                .AppendLine(SqlGenerationHelper.DelimitIdentifier(TableName))
                .Append("WHERE ")
                .Append(SqlGenerationHelper.DelimitIdentifier(MigrationIdColumnName))
                .Append(" = ")
                .Append(stringTypeMapping.GenerateSqlLiteral(migrationId))
                .AppendLine(";")
                .ToString();
        }

        public override IMigrationsDatabaseLock AcquireDatabaseLock()
        {
            Dependencies.MigrationsLogger.AcquiringMigrationLock();

            if (!InterpretExistsResult(
                    Dependencies.RawSqlCommandBuilder.Build(CreateExistsSql(LockTableName))
                        .ExecuteScalar(CreateRelationalCommandParameters())))
            {
                try
                {
                    CreateLockTableCommand().ExecuteNonQuery(CreateRelationalCommandParameters());
                }
                catch (DbException e)
                {
                    if (!e.Message.Contains("already exists")) throw;
                }
            }

            var retryDelay = _retryDelay;
            while (true)
            {
                var dbLock = CreateMigrationDatabaseLock();
                int? insertCount = 0;
                //No CREATE TABLE IF EXISTS in Jet. We try a normal CREATE TABLE and catch the exception if it already exists
                try
                {
                    insertCount = (int?)CreateInsertLockCommand(DateTimeOffset.UtcNow)
                        .ExecuteScalar(CreateRelationalCommandParameters());
                }
                catch (DbException e)
                {
                    if (!e.Message.Contains("duplicate")) throw;
                }
                if ((int)insertCount! == 1)
                {
                    return dbLock;
                }

                Thread.Sleep(retryDelay);
                if (retryDelay < TimeSpan.FromMinutes(1))
                {
                    retryDelay = retryDelay.Add(retryDelay);
                }
            }
        }

        public override async Task<IMigrationsDatabaseLock> AcquireDatabaseLockAsync(
            CancellationToken cancellationToken = default)
        {
            Dependencies.MigrationsLogger.AcquiringMigrationLock();

            if (!InterpretExistsResult(
                    await Dependencies.RawSqlCommandBuilder.Build(CreateExistsSql(LockTableName))
                        .ExecuteScalarAsync(CreateRelationalCommandParameters(), cancellationToken).ConfigureAwait(false)))
            {
                await CreateLockTableCommand().ExecuteNonQueryAsync(CreateRelationalCommandParameters(), cancellationToken)
                    .ConfigureAwait(false);
            }

            var retryDelay = _retryDelay;
            while (true)
            {
                var dbLock = CreateMigrationDatabaseLock();
                var insertCount = await CreateInsertLockCommand(DateTimeOffset.UtcNow)
                    .ExecuteScalarAsync(CreateRelationalCommandParameters(), cancellationToken)
                    .ConfigureAwait(false);
                if ((int)insertCount! == 1)
                {
                    return dbLock;
                }

                await Task.Delay(_retryDelay, cancellationToken).ConfigureAwait(true);
                if (retryDelay < TimeSpan.FromMinutes(1))
                {
                    retryDelay = retryDelay.Add(retryDelay);
                }
            }
        }

        private IRelationalCommand CreateLockTableCommand()
            => Dependencies.RawSqlCommandBuilder.Build($"""
CREATE TABLE `{LockTableName}` (
    `Id` INTEGER NOT NULL CONSTRAINT `PK_{LockTableName}` PRIMARY KEY,
    `Timestamp` TEXT NOT NULL
);
""");

        private IRelationalCommand CreateInsertLockCommand(DateTimeOffset timestamp)
        {
            var timestampLiteral = Dependencies.TypeMappingSource.GetMapping(typeof(DateTimeOffset)).GenerateSqlLiteral(timestamp);

            return Dependencies.RawSqlCommandBuilder.Build($"""
INSERT INTO `{LockTableName}` (`Id`, `Timestamp`) VALUES(1, {timestampLiteral});
SELECT 1 FROM `{LockTableName}` WHERE `Id` = 1;
""");
        }

        private IRelationalCommand CreateDeleteLockCommand(int? id = null)
        {
            var sql = $"""
DELETE FROM `{LockTableName}`
""";
            if (id != null)
            {
                sql += $""" WHERE `Id` = {id}""";
            }
            sql += ";";
            return Dependencies.RawSqlCommandBuilder.Build(sql);
        }

        private JetMigrationDatabaseLock CreateMigrationDatabaseLock()
            => new(CreateDeleteLockCommand(), CreateRelationalCommandParameters(), this);

        private RelationalCommandParameterObject CreateRelationalCommandParameters()
            => new(
                Dependencies.Connection,
                null,
                null,
                Dependencies.CurrentContext.Context,
                Dependencies.CommandLogger, CommandSource.Migrations);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public override string GetCreateIfNotExistsScript()
        {
            var builder = new IndentedStringBuilder();

            var stringTypeMapping = Dependencies.TypeMappingSource.GetMapping(typeof(string));

            builder
                .Append("IF NOT EXISTS (SELECT * FROM `INFORMATION_SCHEMA.TABLES` WHERE `TABLE_NAME` = ")
                .Append(stringTypeMapping.GenerateSqlLiteral(TableName))
                .Append(") THEN ");
            using (builder.Indent())
            {
                builder.AppendLines(GetCreateScript());
            }
            builder.AppendLine(";");

            return builder.ToString();
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public override string GetBeginIfNotExistsScript(string migrationId)
        {
            throw new NotSupportedException(JetStrings.MigrationScriptGenerationNotSupported);
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public override string GetBeginIfExistsScript(string migrationId)
        {
            throw new NotSupportedException(JetStrings.MigrationScriptGenerationNotSupported);
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public override string GetEndIfScript()
        {
            throw new NotSupportedException(JetStrings.MigrationScriptGenerationNotSupported);
        }
    }
}