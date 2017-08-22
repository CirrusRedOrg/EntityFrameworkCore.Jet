// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.Threading;
using System.Threading.Tasks;
using EntityFrameworkCore.Jet.Migrations.Operations;
using EntityFrameworkCore.Jet.Properties;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Storage.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetDatabaseCreator : RelationalDatabaseCreator
    {
        private readonly IJetConnection _connection;
        private readonly IRawSqlCommandBuilder _rawSqlCommandBuilder;

        public JetDatabaseCreator(
            [NotNull] RelationalDatabaseCreatorDependencies dependencies,
            [NotNull] IJetConnection connection,
            [NotNull] IRawSqlCommandBuilder rawSqlCommandBuilder)
            : base(dependencies)
        {
            _connection = connection;
            _rawSqlCommandBuilder = rawSqlCommandBuilder;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual TimeSpan RetryTimeout { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override void Create()
        {
            // Here we should create the file
            /*
            using (var masterConnection = _connection.CreateMasterConnection())
            {
                Dependencies.MigrationCommandExecutor
                    .ExecuteNonQuery(CreateCreateOperations(), masterConnection);

                ClearPool();
            }
            */

            Exists(retryOnNotExists: true);
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override async Task CreateAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            // Here we should create the file
            /*
            using (var masterConnection = _connection.CreateMasterConnection())
            {
                await Dependencies.MigrationCommandExecutor
                    .ExecuteNonQueryAsync(CreateCreateOperations(), masterConnection, cancellationToken);

                ClearPool();
            }
            */

            await ExistsAsync(retryOnNotExists: true, cancellationToken: cancellationToken);
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override bool HasTables()
        {
            using (var dataReader = Dependencies.ExecutionStrategyFactory.Create()
                .Execute(_connection, connection => CreateShowUserTablesCommand().ExecuteReader(connection)))
                return dataReader.DbDataReader.HasRows;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override Task<bool> HasTablesAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return new Task<bool>(HasTables);
        }

        private IRelationalCommand CreateShowUserTablesCommand()
        {
            return _rawSqlCommandBuilder
                           .Build("SHOW TABLES WHERE TYPE='USER'");
        }

        // ReSharper disable once UnusedMember.Local
        private IReadOnlyList<MigrationCommand> CreateCreateOperations()
        {
            var builder = new OleDbConnectionStringBuilder(_connection.DbConnection.ConnectionString);
            return Dependencies.MigrationsSqlGenerator.Generate(new[] { new JetCreateDatabaseOperation { FileName = builder.FileName} });
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override bool Exists()
            => Exists(retryOnNotExists: false);

        private bool Exists(bool retryOnNotExists)
            => Dependencies.ExecutionStrategyFactory.Create().Execute(
                DateTime.UtcNow + RetryTimeout, giveUp =>
                    {
                        while (true)
                        {
                            try
                            {
                                _connection.Open(errorsExpected: true);
                                _connection.Close();
                                return true;
                            }
                            catch (OleDbException e)
                            {
                                if (!retryOnNotExists
                                    && IsDoesNotExist(e))
                                {
                                    return false;
                                }

                                if (DateTime.UtcNow > giveUp
                                    || !RetryOnExistsFailure(e))
                                {
                                    throw;
                                }

                                Thread.Sleep(RetryDelay);
                            }
                        }
                    });

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override Task<bool> ExistsAsync(CancellationToken cancellationToken = default(CancellationToken))
            => ExistsAsync(retryOnNotExists: false, cancellationToken: cancellationToken);

        private Task<bool> ExistsAsync(bool retryOnNotExists, CancellationToken cancellationToken)
            => Dependencies.ExecutionStrategyFactory.Create().ExecuteAsync(
                DateTime.UtcNow + RetryTimeout, async (giveUp, ct) =>
                    {
                        while (true)
                        {
                            try
                            {
                                await _connection.OpenAsync(ct, errorsExpected: true);

                                _connection.Close();
                                return true;
                            }
                            catch (OleDbException e)
                            {
                                if (!retryOnNotExists
                                    && IsDoesNotExist(e))
                                {
                                    return false;
                                }

                                if (DateTime.UtcNow > giveUp
                                    || !RetryOnExistsFailure(e))
                                {
                                    throw;
                                }

                                await Task.Delay(RetryDelay, ct);
                            }
                        }
                    }, cancellationToken);

        // Login failed is thrown when database does not exist (See Issue #776)
        // Unable to attach database file is thrown when file does not exist (See Issue #2810)
        // Unable to open the physical file is thrown when file does not exist (See Issue #2810)
        private static bool IsDoesNotExist(OleDbException exception) =>
            // TODO: Check proper Jet Errors
            exception.ErrorCode == 4060 || exception.ErrorCode == 1832 || exception.ErrorCode == 5120;

        // See Issue #985
        private bool RetryOnExistsFailure(OleDbException exception)
        {
            // This is to handle the case where Open throws (Number 233):
            //   System.Data.Jet.SqlException: A connection was successfully established with the
            //   server, but then an error occurred during the login process. (provider: Named Pipes
            //   Provider, error: 0 - No process is on the other end of the pipe.)
            // It appears that this happens when the database has just been created but has not yet finished
            // opening or is auto-closing when using the AUTO_CLOSE option. The workaround is to flush the pool
            // for the connection and then retry the Open call.
            // Also handling (Number -2):
            //   System.Data.Jet.SqlException: Connection Timeout Expired.  The timeout period elapsed while
            //   attempting to consume the pre-login handshake acknowledgment.  This could be because the pre-login
            //   handshake failed or the server was unable to respond back in time.
            // And (Number 4060):
            //   System.Data.Jet.SqlException: Cannot open database "X" requested by the login. The
            //   login failed.
            // And (Number 1832)
            //   System.Data.Jet.SqlException: Unable to Attach database file as database xxxxxxx.
            // And (Number 5120)
            //   System.Data.Jet.SqlException: Unable to open the physical file xxxxxxx.
            // TODO: Check proper Jet Errors
            if (exception.ErrorCode == 233
                || exception.ErrorCode == -2
                || exception.ErrorCode == 4060
                || exception.ErrorCode == 1832
                || exception.ErrorCode == 5120)
            {
                ClearPool();
                return true;
            }
            return false;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override void Delete()
        {
            ClearAllPools();

            // Here we should delete the file
            /*
            using (var masterConnection = _connection.CreateMasterConnection())
            {
                Dependencies.MigrationCommandExecutor
                    .ExecuteNonQuery(CreateDropCommands(), masterConnection);
            }
            */
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override Task DeleteAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            ClearAllPools();

            // Here we should delete the file
            /*
            using (var masterConnection = _connection.CreateMasterConnection())
            {
                await Dependencies.MigrationCommandExecutor
                    .ExecuteNonQueryAsync(CreateDropCommands(), masterConnection, cancellationToken);
            }
            */
            return Task.CompletedTask;
        }

        // ReSharper disable once UnusedMember.Local
        private IReadOnlyList<MigrationCommand> CreateDropCommands()
        {
            var databaseName = _connection.DbConnection.Database;
            if (string.IsNullOrEmpty(databaseName))
            {
                throw new InvalidOperationException(JetStrings.NoInitialCatalog);
            }

            var operations = new MigrationOperation[]
            {
                new JetDropDatabaseOperation { Name = databaseName }
            };

            var masterCommands = Dependencies.MigrationsSqlGenerator.Generate(operations);
            return masterCommands;
        }

        // Clear connection pools in case there are active connections that are pooled
        private static void ClearAllPools() => System.Data.Jet.JetConnection.ClearAllPools();

        // Clear connection pool for the database connection since after the 'create database' call, a previously
        // invalid connection may now be valid.
        private void ClearPool() => System.Data.Jet.JetConnection.ClearPool((System.Data.Jet.JetConnection)_connection.DbConnection);
    }
}
