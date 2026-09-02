using System.Text;
using EntityFrameworkCore.Jet.Infrastructure;
using EntityFrameworkCore.Jet.Internal;
using EntityFrameworkCore.Jet.Utilities;

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
    /// <remarks>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </remarks>
    public class JetHistoryRepository(HistoryRepositoryDependencies dependencies) : HistoryRepository(dependencies)
    {
        // Migration-lock retry policy. The lock guards a migration and is released explicitly the moment that
        // migration finishes, so contention resolves in milliseconds — these delays are sized for a local file,
        // not for a round trip to a remote server.
        //
        // The previous policy started at one second and doubled while under a minute, which had three separate
        // faults that compounded: the delay was never reset, so a thread that lost a single race retried only
        // once every 64 seconds for the rest of the run; there was no jitter, so every contender woke in the
        // same millisecond, collided, and all but one backed off together; and the cap was 60 seconds, four
        // orders of magnitude above the hold time. With N contenders in lockstep exactly one wins per round,
        // making a run take N x 64s — about sixteen minutes for fifteen threads, which is indistinguishable
        // from a deadlock and is why the parallel migration tests read as "hung" rather than "slow".
        private static readonly TimeSpan _retryDelay = TimeSpan.FromMilliseconds(50);
        private static readonly TimeSpan _maxRetryDelay = TimeSpan.FromSeconds(1);

        /// <summary>How long to keep trying for the migration lock before giving up.</summary>
        /// <remarks>
        ///     Without a deadline the retry loop cannot fail, only wait, so a lock that is never released blocks
        ///     the caller forever with nothing logged — the failure mode is a silent hang rather than an error
        ///     anyone can act on. Giving up turns it into a reportable fault, as SQL Server's
        ///     <c>sp_getapplock</c> timeout does.
        /// </remarks>
        private static readonly TimeSpan _lockTimeout = TimeSpan.FromMinutes(1);

        public override LockReleaseBehavior LockReleaseBehavior => LockReleaseBehavior.Explicit;

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public const string DefaultLockTableName = "__EFMigrationsLock";

        /// <summary>
        ///     The name of the table that will serve as a database-wide lock for migrations.
        /// </summary>
        protected virtual string LockTableName { get; } = DefaultLockTableName;

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
            // The exists query (`SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = …`) returns a row only
            // when the table exists. An empty result is "not found" — but ADO.NET's ExecuteScalar returns C# null
            // for an empty result set (per contract), while ACE's OLE DB path returns DBNull.Value. Treat BOTH as
            // not-found, else a null (e.g. from LibRed's spec-correct reader) is misread as "exists" and the lock/
            // history table is never created.
            return value is not null && value != DBNull.Value;
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
            var deadline = DateTime.UtcNow + _lockTimeout;
            while (true)
            {
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
                    // Built only once the lock is actually ours; the old loop constructed one per attempt and
                    // dropped it on every miss.
                    return CreateMigrationDatabaseLock();
                }

                if (DateTime.UtcNow >= deadline)
                {
                    throw new TimeoutException(LockTimeoutMessage());
                }

                Thread.Sleep(JitteredDelay(retryDelay));
                retryDelay = EscalateDelay(retryDelay);
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
                // Same guard as the synchronous overload, which this had been missing: the exists check above
                // is not atomic, so concurrent migrators can all decide the table is absent and all issue the
                // CREATE. Losing that race is the normal path, not a failure.
                try
                {
                    await CreateLockTableCommand()
                        .ExecuteNonQueryAsync(CreateRelationalCommandParameters(), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (DbException e)
                {
                    if (!e.Message.Contains("already exists")) throw;
                }
            }

            var retryDelay = _retryDelay;
            var deadline = DateTime.UtcNow + _lockTimeout;
            while (true)
            {
                int? insertCount = 0;
                try
                {
                    insertCount = (int?)await CreateInsertLockCommand(DateTimeOffset.UtcNow)
                        .ExecuteScalarAsync(CreateRelationalCommandParameters(), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (DbException e)
                {
                    // Likewise mirrored from the synchronous overload: the WHERE NOT EXISTS guard on the
                    // insert is not atomic either, so a duplicate key here means someone else took the lock.
                    if (!e.Message.Contains("duplicate")) throw;
                }
                if ((int)insertCount! == 1)
                {
                    return CreateMigrationDatabaseLock();
                }

                if (DateTime.UtcNow >= deadline)
                {
                    throw new TimeoutException(LockTimeoutMessage());
                }

                await Task.Delay(JitteredDelay(retryDelay), cancellationToken).ConfigureAwait(false);
                retryDelay = EscalateDelay(retryDelay);
            }
        }

        /// <summary>
        ///     Spreads the wait by +/-25% so contenders stop waking together. This matters as much as the cap:
        ///     with a fixed delay every loser retries in the same millisecond as every other loser, so the herd
        ///     stays synchronised and each round yields exactly one winner no matter how short the delay is.
        /// </summary>
        private static TimeSpan JitteredDelay(TimeSpan delay)
            => TimeSpan.FromTicks((long)(delay.Ticks * (0.75 + (Random.Shared.NextDouble() / 2.0))));

        /// <summary>Doubles the backoff up to <see cref="_maxRetryDelay" /> and holds there.</summary>
        private static TimeSpan EscalateDelay(TimeSpan delay)
            => delay >= _maxRetryDelay
                ? _maxRetryDelay
                : TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, _maxRetryDelay.Ticks));

        private string LockTimeoutMessage()
            => $"Timed out after {_lockTimeout.TotalSeconds:N0}s waiting for the migrations lock. Another "
                + $"migration may still be running, or a previous one may have left a row in "
                + $"'{LockTableName}' without releasing it; delete that row to clear the lock.";

        private IRelationalCommand CreateLockTableCommand()
            => Dependencies.RawSqlCommandBuilder.Build($"""
CREATE TABLE `{LockTableName}` (
    `Id` INTEGER NOT NULL CONSTRAINT `PK_{LockTableName}` PRIMARY KEY,
    `Timestamp` TEXT NOT NULL
);
""");

        /// <summary>
        ///     Takes the migration lock, reporting 1 when this connection wrote the row and 0 when someone else
        ///     already holds it — the contract SQLite gets from <c>INSERT OR IGNORE …; SELECT changes()</c>.
        ///     <para>
        ///         Jet/ACE has no <c>INSERT OR IGNORE</c> or <c>MERGE</c>, so the insert is made conditional with a
        ///         <c>WHERE NOT EXISTS</c> guard and reports what it actually wrote via <c>@@ROWCOUNT</c>. That keeps
        ///         losing the race on the ordinary result path instead of raising a duplicate-key error the caller has
        ///         to recognise by message text.
        ///     </para>
        ///     <para>
        ///         Jet requires a FROM, and the DUAL stand-in may be a real multi-row table (<c>MSysAccessStorage</c>,
        ///         <c>MSysRelationships</c>), so the source is wrapped as <c>(SELECT COUNT(*) FROM …)</c> — a one-row
        ///         derived table — exactly as <c>JetQuerySqlGenerator</c> does. Verified against ACE: the wrapped form
        ///         inserts one row then reports 0 on a second run, while an unwrapped multi-row source fails on the
        ///         primary key.
        ///     </para>
        ///     <para>
        ///         The guard is <b>not</b> atomic — unlike SQLite's <c>OR IGNORE</c>, two connections can both evaluate
        ///         it as true — so the caller keeps its duplicate-key catch as the backstop. The difference is that the
        ///         exception becomes the rare racing path rather than the normal contention path.
        ///     </para>
        /// </summary>
        private IRelationalCommand CreateInsertLockCommand(DateTimeOffset timestamp)
        {
            var timestampLiteral = Dependencies.TypeMappingSource.GetMapping(typeof(DateTimeOffset)).GenerateSqlLiteral(timestamp);
            var dualTableName = JetDualTable.Name;

            return Dependencies.RawSqlCommandBuilder.Build($"""
INSERT INTO `{LockTableName}` (`Id`, `Timestamp`)
SELECT 1, {timestampLiteral} FROM (SELECT COUNT(*) FROM `{dualTableName}`)
WHERE NOT EXISTS (SELECT * FROM `{LockTableName}` WHERE `Id` = 1);
SELECT @@ROWCOUNT;
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

        public override IReadOnlyList<HistoryRow> GetAppliedMigrations()
        {
            var rows = new List<HistoryRow>();
            //Note the exists check opens a new connection with adox/dao. If doing within a transaction it wont find the table until the transaction is committed. Just read anyway
            //No op if the table does not exist
            //if (Exists())
            {
                var command = Dependencies.RawSqlCommandBuilder.Build(GetAppliedMigrationsSql);

                using var reader = command.ExecuteReader(
                    new RelationalCommandParameterObject(
                        Dependencies.Connection,
                        null,
                        null,
                        Dependencies.CurrentContext.Context,
                        Dependencies.CommandLogger, CommandSource.Migrations));
                while (reader.Read())
                {
                    rows.Add(new HistoryRow(reader.DbDataReader.GetString(0), reader.DbDataReader.GetString(1)));
                }
            }

            return rows;
        }

        public override async Task<IReadOnlyList<HistoryRow>> GetAppliedMigrationsAsync(CancellationToken cancellationToken = new CancellationToken())
        {
            var rows = new List<HistoryRow>();
            //Note the exists check opens a new connection with adox/dao. If doing within a transaction it wont find the table until the transaction is committed. Just read anyway
            //No op if the table does not exist
            //if (await ExistsAsync(cancellationToken).ConfigureAwait(false))
            {
                var command = Dependencies.RawSqlCommandBuilder.Build(GetAppliedMigrationsSql);

                var reader = await command.ExecuteReaderAsync(
                    new RelationalCommandParameterObject(
                        Dependencies.Connection,
                        null,
                        null,
                        Dependencies.CurrentContext.Context,
                        Dependencies.CommandLogger, CommandSource.Migrations),
                    cancellationToken).ConfigureAwait(false);

                await using var _ = reader.ConfigureAwait(false);

                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    rows.Add(new HistoryRow(reader.DbDataReader.GetString(0), reader.DbDataReader.GetString(1)));
                }
            }

            return rows;
        }
    }
}