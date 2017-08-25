using System;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using Xunit;

namespace EntityFramework.Jet.FunctionalTests
{
    public class MigrationsJetTest : MigrationsTestBase<MigrationsJetFixture>
    {
        public MigrationsJetTest(MigrationsJetFixture fixture)
            : base(fixture)
        {
        }

        public override void Can_generate_up_scripts()
        {
            base.Can_generate_up_scripts();

            Assert.Equal(
                @"CREATE TABLE [__EFMigrationsHistory] (
    [MigrationId] nvarchar(150) NOT NULL,
    [ProductVersion] nvarchar(32) NOT NULL,
    CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
)


GO

CREATE TABLE [Table1] (
    [Id] int NOT NULL,
    CONSTRAINT [PK_Table1] PRIMARY KEY ([Id])
)


GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES ('00000000000001_Migration1', '7.0.0-test')


GO

sp_rename N'Table1', N'Table2'
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES ('00000000000002_Migration2', '7.0.0-test')


GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES ('00000000000003_Migration3', '7.0.0-test')


GO

",
                Sql);
        }

        public override void Can_generate_idempotent_up_scripts()
        {
            Assert.Throws<NotSupportedException>(() => base.Can_generate_idempotent_up_scripts());
        }

        public override void Can_generate_down_scripts()
        {
            base.Can_generate_down_scripts();

            Assert.Equal(
                @"sp_rename N'Table2', N'Table1'
GO

DELETE FROM [__EFMigrationsHistory]
WHERE [MigrationId] = '00000000000002_Migration2'


GO

DROP TABLE [Table1]


GO

DELETE FROM [__EFMigrationsHistory]
WHERE [MigrationId] = '00000000000001_Migration1'


GO

",
                Sql);
        }

        public override void Can_generate_idempotent_down_scripts()
        {
            Assert.Throws<NotSupportedException>(() => base.Can_generate_idempotent_down_scripts());
        }

        public override void Can_get_active_provider()
        {
            base.Can_get_active_provider();

            Assert.Equal("EntityFrameworkCore.SqlServerCompact40", ActiveProvider);
        }

        protected override async Task AssertFirstMigrationAsync(DbConnection connection)
        {
            var sql = await GetDatabaseSchemaAsync(connection);
            Assert.Equal(
                @"
CreatedTable
    Id int NOT NULL
    ColumnWithDefaultToDrop int NULL DEFAULT 0
    ColumnWithDefaultToAlter int NULL DEFAULT 1
",
                sql);
        }

        protected override async Task AssertSecondMigrationAsync(DbConnection connection)
        {
            var sql = await GetDatabaseSchemaAsync(connection);
            Assert.Equal(
                @"
CreatedTable
    Id int NOT NULL
    ColumnWithDefaultToAlter int NULL
",
                sql);
        }

        private async Task<string> GetDatabaseSchemaAsync(DbConnection connection)
        {
            var builder = new IndentedStringBuilder();

            var command = connection.CreateCommand();
            command.CommandText = @"SELECT 
TABLE_NAME, 
COLUMN_NAME, 
DATA_TYPE, 
CASE WHEN IS_NULLABLE = 'YES'
	THEN CAST(1 as bit)
	ELSE CAST (0 AS bit)
END, 
COLUMN_DEFAULT
FROM INFORMATION_SCHEMA.COLUMNS;";

            using (var reader = await command.ExecuteReaderAsync())
            {
                var first = true;
                string lastTable = null;
                while (await reader.ReadAsync())
                {
                    var currentTable = reader.GetString(0);
                    if (currentTable != lastTable)
                    {
                        if (first)
                        {
                            first = false;
                        }
                        else
                        {
                            builder.DecrementIndent();
                        }

                        builder
                            .AppendLine()
                            .AppendLine(currentTable)
                            .IncrementIndent();

                        lastTable = currentTable;
                    }

                    builder
                        .Append(reader[1]) // Name
                        .Append(" ")
                        .Append(reader[2]) // Type
                        .Append(" ")
                        .Append(reader.GetBoolean(3) ? "NULL" : "NOT NULL");

                    if (!await reader.IsDBNullAsync(4))
                    {
                        builder
                            .Append(" DEFAULT ")
                            .Append(reader[4]);
                    }

                    builder.AppendLine();
                }
            }

            return builder.ToString();
        }
    }
}
