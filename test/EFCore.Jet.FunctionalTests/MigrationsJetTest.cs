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

        public override void Can_get_active_provider()
        {
            base.Can_get_active_provider();

            Assert.Equal("EntityFrameworkCore.Jet", ActiveProvider);
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
            command.CommandText = @"
SELECT 
    Table, 
    Name, 
    TypeName, 
    IsNullable, 
    Default
FROM 
    (SHOW TABLECOLUMNS)
ORDER BY
    Ordinal
;";

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

            return builder.ToString().Replace("\r\n", "\n");
        }
    }
}
