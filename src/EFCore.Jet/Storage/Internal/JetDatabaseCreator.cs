// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Data.OleDb;
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
        private readonly IJetRelationalConnection _relationalConnection;
        private readonly IRawSqlCommandBuilder _rawSqlCommandBuilder;

        public JetDatabaseCreator(
            [NotNull] RelationalDatabaseCreatorDependencies dependencies,
            [NotNull] IJetRelationalConnection relationalConnection,
            [NotNull] IRawSqlCommandBuilder rawSqlCommandBuilder)
            : base(dependencies)
        {
            _relationalConnection = relationalConnection;
            _rawSqlCommandBuilder = rawSqlCommandBuilder;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override void Create()
        {

            using (var emptyConnection = _relationalConnection.CreateEmptyConnection())
            {
                Dependencies.MigrationCommandExecutor
                    .ExecuteNonQuery(CreateCreateOperations(), emptyConnection);

                ClearPool();
            }

        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected override bool HasTables()
        {
            using (var dataReader = Dependencies.ExecutionStrategyFactory.Create()
                .Execute(_relationalConnection, connection => CreateShowUserTablesCommand().ExecuteReader(connection)))
                return dataReader.DbDataReader.HasRows;
        }

        private IRelationalCommand CreateShowUserTablesCommand()
        {
            return _rawSqlCommandBuilder
                           .Build("SHOW TABLES WHERE TYPE='USER'");
        }

        // ReSharper disable once UnusedMember.Local
        private IReadOnlyList<MigrationCommand> CreateCreateOperations()
        {
            var builder = new OleDbConnectionStringBuilder(_relationalConnection.DbConnection.ConnectionString);
            return Dependencies.MigrationsSqlGenerator.Generate(new[] { new JetCreateDatabaseOperation { Name = builder.DataSource} });
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override bool Exists()
        {
            return System.Data.Jet.JetConnection.DatabaseExists(_relationalConnection.DbConnection.ConnectionString);
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override void Delete()
        {
            ClearAllPools();

            using (var emptyConnection = _relationalConnection.CreateEmptyConnection())
            {
                Dependencies.MigrationCommandExecutor
                    .ExecuteNonQuery(CreateDropCommands(), emptyConnection);
            }
        }

        // ReSharper disable once UnusedMember.Local
        private IReadOnlyList<MigrationCommand> CreateDropCommands()
        {
            var databaseName = _relationalConnection.DbConnection.DataSource;
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
        private void ClearPool() => System.Data.Jet.JetConnection.ClearPool((System.Data.Jet.JetConnection)_relationalConnection.DbConnection);
    }
}
