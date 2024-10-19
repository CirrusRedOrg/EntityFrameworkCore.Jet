// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Data;
using EntityFrameworkCore.Jet.Internal;
using EntityFrameworkCore.Jet.Migrations.Operations;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetDatabaseCreator(
        RelationalDatabaseCreatorDependencies dependencies,
        IJetRelationalConnection relationalConnection,
        IRawSqlCommandBuilder rawSqlCommandBuilder)
        : RelationalDatabaseCreator(dependencies)
    {
        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override void Create()
        {
            using var emptyConnection = relationalConnection.CreateEmptyConnection();
            Dependencies.MigrationCommandExecutor
                .ExecuteNonQuery(CreateCreateOperations(), emptyConnection);

            ClearPool();
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public override bool HasTables()
        {
            return Dependencies.ExecutionStrategy
                .Execute(
                    relationalConnection,
                    connection =>
                    {
                        using var dataReader = CreateHasTablesCommand()
                            .ExecuteReader(
                                new RelationalCommandParameterObject(
                                    connection,
                                    null,
                                    null,
                                    Dependencies.CurrentContext.Context,
                                    Dependencies.CommandLogger));
                        return dataReader.DbDataReader.HasRows;
                    });
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public override Task<bool> HasTablesAsync(CancellationToken cancellationToken = default)
            => Dependencies.ExecutionStrategy.ExecuteAsync(
                relationalConnection,
                async (connection, ct) =>
                {
                    await using var dataReader = await CreateHasTablesCommand()
                        .ExecuteReaderAsync(
                            new RelationalCommandParameterObject(
                                connection,
                                null,
                                null,
                                Dependencies.CurrentContext.Context,
                                Dependencies.CommandLogger),
                            cancellationToken: ct);
                    return dataReader.DbDataReader.HasRows;
                }, cancellationToken);

        private IRelationalCommand CreateHasTablesCommand()
            => rawSqlCommandBuilder.Build(@"SELECT * FROM `INFORMATION_SCHEMA.TABLES` WHERE TABLE_TYPE IN ('BASE TABLE', 'VIEW')");

        private IReadOnlyList<MigrationCommand> CreateCreateOperations()
        {
            // Alternative:
            // var dataSource = _relationalConnection.DbConnection.DataSource;

            var connection = (JetConnection) relationalConnection.DbConnection;
            var fileNameOrConnectionString = connection.ConnectionString;
            var connectionString = JetConnection.GetConnectionString(fileNameOrConnectionString, connection.DataAccessProviderFactory);

            var csb = (connection.JetFactory?.CreateConnectionStringBuilder()) ?? throw new InvalidOperationException("Failed to create connection string builder.");
            csb.ConnectionString = connectionString;
            
            var dataSource = csb.GetDataSource();
            var databasePassword = csb.GetDatabasePassword();

            return Dependencies.MigrationsSqlGenerator.Generate(
                [
                    new JetCreateDatabaseOperation
                    {
                        Name = dataSource!,
                        Password = databasePassword
                    }
                ]);
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override bool Exists()
            => JetConnection.DatabaseExists(relationalConnection.DbConnection.ConnectionString);

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override void Delete()
        {
            ClearAllPools();

            using var emptyConnection = relationalConnection.CreateEmptyConnection();
            Dependencies.MigrationCommandExecutor
                .ExecuteNonQuery(CreateDropCommands(), emptyConnection);
        }

        // ReSharper disable once UnusedMember.Local
        private IReadOnlyList<MigrationCommand> CreateDropCommands()
        {
            var databaseName = relationalConnection.DbConnection.DataSource;
            if (string.IsNullOrEmpty(databaseName))
            {
                throw new InvalidOperationException(JetStrings.NoInitialCatalog);
            }

            var operations = new MigrationOperation[]
            {
                new JetDropDatabaseOperation {Name = databaseName}
            };

            var masterCommands = Dependencies.MigrationsSqlGenerator.Generate(operations);
            return masterCommands;
        }

        // Clear connection pools in case there are active connections that are pooled
        private static void ClearAllPools()
            => JetConnection.ClearAllPools();

        // Clear connection pool for the database connection since after the 'create database' call, a previously
        // invalid connection may now be valid.
        private void ClearPool()
            => JetConnection.ClearPool((JetConnection) relationalConnection.DbConnection);
    }
}