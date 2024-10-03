using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Transactions;
using EntityFrameworkCore.Jet.Data.JetStoreSchemaDefinition;
using Microsoft.EntityFrameworkCore.Migrations.Internal;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Migrations.Internal
{
    public class JetMigrationCommandExecutor(IExecutionStrategy executionStrategy) : MigrationCommandExecutor(executionStrategy)
    {
        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public override int ExecuteNonQuery(
            IReadOnlyList<MigrationCommand> migrationCommands,
            IRelationalConnection connection,
            MigrationExecutionState executionState,
            bool commitTransaction,
            System.Data.IsolationLevel? isolationLevel = null)
        {
            var batches = CreateMigrationBatches(migrationCommands);
            foreach (var batch in batches)
            {
                base.ExecuteNonQuery(batch, connection, executionState, true, isolationLevel);
            }

            return -1;
        }

        public override async Task<int> ExecuteNonQueryAsync(
            IReadOnlyList<MigrationCommand> migrationCommands,
            IRelationalConnection connection,
            MigrationExecutionState executionState,
            bool commitTransaction,
            System.Data.IsolationLevel? isolationLevel = null,
            CancellationToken cancellationToken = default)
        {
            var batches = CreateMigrationBatches(migrationCommands);
            foreach (var batch in batches)
            {
                await base.ExecuteNonQueryAsync(batch, connection, executionState, true, isolationLevel, cancellationToken);
            }

            return -1;
        }

        List<IReadOnlyList<MigrationCommand>> CreateMigrationBatches(IReadOnlyList<MigrationCommand> migrationCommands)
        {
            //create new batch if JetSchemaOperationsHandling.IsDatabaseOperation is true otherwise had to current batch
            var migrationBatches = new List<IReadOnlyList<MigrationCommand>>();
            var currentBatch = new List<MigrationCommand>();
            foreach (var migrationCommand in migrationCommands)
            {
                if (JetSchemaOperationsHandling.IsDatabaseOperation(migrationCommand.CommandText))
                {
                    if (currentBatch.Any())
                    {
                        migrationBatches.Add(currentBatch);
                        currentBatch = new List<MigrationCommand>();
                    }
                    migrationBatches.Add(new List<MigrationCommand> { migrationCommand });
                }
                else
                {
                    currentBatch.Add(migrationCommand);
                }
            }
            if (currentBatch.Any())
            {
                migrationBatches.Add(currentBatch);
            }
            return migrationBatches;
        }
    }
}
