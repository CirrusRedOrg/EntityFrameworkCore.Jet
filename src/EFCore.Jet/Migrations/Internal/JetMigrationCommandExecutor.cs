using EntityFrameworkCore.Jet.Data;
using EntityFrameworkCore.Jet.Data.JetStoreSchemaDefinition;
using Microsoft.EntityFrameworkCore.Migrations.Internal;

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
            int output = 0;

            foreach (var batch in batches)
            {
                output += base.ExecuteNonQuery(batch.Item1, connection, executionState, true, isolationLevel);
                if (batch.Item2) Thread.Sleep(4000);//Wait for adox/dao to complete
            }
            if (connection.CurrentTransaction != null)
            {
                connection.CurrentTransaction.Commit();
            }
            return output;
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
                await base.ExecuteNonQueryAsync(batch.Item1, connection, executionState, true, isolationLevel, cancellationToken);
                if (batch.Item2) Thread.Sleep(4000);
            }
            if (connection.CurrentTransaction != null)
            {
                await connection.CurrentTransaction.CommitAsync(cancellationToken);
            }
            return -1;
        }

        List<(IReadOnlyList<MigrationCommand>,bool)> CreateMigrationBatches(IReadOnlyList<MigrationCommand> migrationCommands)
        {
            //create new batch if JetSchemaOperationsHandling.IsDatabaseOperation is true otherwise had to current batch
            var migrationBatches = new List<(IReadOnlyList<MigrationCommand>, bool)>();
            var currentBatch = new List<MigrationCommand>();
            foreach (var migrationCommand in migrationCommands)
            {
                if (JetSchemaOperationsHandling.IsDatabaseOperation(migrationCommand.CommandText))
                {
                    if (currentBatch.Count != 0)
                    {
                        migrationBatches.Add((currentBatch,false));
                        currentBatch = [];
                    }
                    migrationBatches.Add(([migrationCommand],true));
                }
                else
                {
                    currentBatch.Add(migrationCommand);
                }
            }
            if (currentBatch.Count != 0)
            {
                migrationBatches.Add((currentBatch,false));
            }
            return migrationBatches;
        }
    }
}
