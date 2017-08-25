using System;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Tests.Migrations
{
    public class SqlCeMigrationSqlGeneratorTest : MigrationSqlGeneratorTestBase
    {
        public override void AlterSequenceOperation_without_minValue_and_maxValue()
        {
            Assert.Throws<NotSupportedException>(() => base.AlterSequenceOperation_without_minValue_and_maxValue());
        }

        public override void AlterSequenceOperation_with_minValue_and_maxValue()
        {
            Assert.Throws<NotSupportedException>(() => base.AlterSequenceOperation_with_minValue_and_maxValue());
        }

        public override void CreateSequenceOperation_without_minValue_and_maxValue()
        {
            Assert.Throws<NotSupportedException>(() => base.CreateSequenceOperation_without_minValue_and_maxValue());
        }

        public override void CreateSequenceOperation_with_minValue_and_maxValue()
        {
            Assert.Throws<NotSupportedException>(() => base.CreateSequenceOperation_with_minValue_and_maxValue());
        }

        public override void CreateSequenceOperation_with_minValue_and_maxValue_not_long()
        {
            Assert.Throws<NotSupportedException>(() => base.CreateSequenceOperation_with_minValue_and_maxValue_not_long());
        }

        public override void DropSequenceOperation()
        {
            Assert.Throws<NotSupportedException>(() => base.DropSequenceOperation());
        }

        [Fact]
        public void CreateSchemaOperation_is_ignored()
        {
            Generate(new EnsureSchemaOperation());

            Assert.Empty(Sql);
        }

        [Fact]
        public void DropSchemaOperation_is_ignored()
        {
            Generate(new DropSchemaOperation());

            Assert.Empty(Sql);
        }

        public override void DropIndexOperation()
        {
            base.DropIndexOperation();

            Assert.Equal(
                "DROP INDEX [People].[IX_People_Name]",
                Sql);
        }

        [Fact]
        public virtual void RenameColumnOperation()
        {
            Assert.Throws<NotSupportedException>(() =>
                Generate(
                    new RenameColumnOperation
                    {
                        Table = "People",
                        Schema = "dbo",
                        Name = "Name",
                        NewName = "FullName"
                    }));
        }

        [Fact]
        public virtual void RenameTableOperation()
        {
            Generate(
                new RenameTableOperation
                {
                    Name = "People",
                    NewName = "Person"
                });

            Assert.Equal(
                "sp_rename N'People', N'Person'",
                Sql);
        }

        [Fact]
        public virtual void AddColumnOperation_identity()
        {
            Generate(
                new AddColumnOperation
                {
                    Table = "People",
                    Name = "Id",
                    ClrType = typeof(int),
                    ColumnType = "int",
                    DefaultValue = 0,
                    IsNullable = false,
                    [SqlCeAnnotationNames.ValueGeneration] =
                        SqlCeAnnotationNames.Identity
                });

            Assert.Equal(
                "ALTER TABLE [People] ADD [Id] int NOT NULL IDENTITY" + EOL + EOL,
                Sql);
        }

        [Fact]
        public virtual void AddPrimaryKeyOperation_nonclustered()
        {
            Generate(
                new AddPrimaryKeyOperation
                {
                    Table = "People",
                    Columns = new[] { "Id" }
                });

            Assert.Equal(
                "ALTER TABLE [People] ADD PRIMARY KEY ([Id])" + EOL + EOL,
                Sql);
        }


        [Fact]
        public virtual void SqlOperation_handles_backslash()
        {
            Generate(
                new SqlOperation
                {
                    Sql = @"-- Multiline \" + EOL +
                        "comment"
                });

            Assert.Equal(
                "-- Multiline comment" + EOL,
                Sql);
        }

        [Fact]
        public virtual void SqlOperation_ignores_sequential_gos()
        {
            Generate(
                new SqlOperation
                {
                    Sql = "-- Ready set" + EOL +
                        "GO" + EOL +
                        "GO"
                });

            Assert.Equal(
                "-- Ready set" + EOL,
                Sql);
        }

        [Fact]
        public virtual void SqlOperation_handles_go()
        {
            Generate(
                new SqlOperation
                {
                    Sql = "-- I" + EOL +
                        "go" + EOL +
                        "-- Too"
                });

            Assert.Equal(
                "-- I" + EOL +
                "GO" + EOL +
                EOL +
                "-- Too" + EOL,
                Sql);
        }

        [Fact]
        public virtual void SqlOperation_handles_go_with_count()
        {
            Generate(
                new SqlOperation
                {
                    Sql = "-- I" + EOL +
                        "GO 2"
                });

            Assert.Equal(
                "-- I" + EOL +
                "GO" + EOL +
                EOL +
                "-- I" + EOL,
                Sql);
        }

        [Fact]
        public virtual void SqlOperation_ignores_non_go()
        {
            Generate(
                new SqlOperation
                {
                    Sql = "-- I GO 2"
                });

            Assert.Equal(
                "-- I GO 2" + EOL,
                Sql);
        }

        public override void AlterColumnOperation()
        {
            base.AlterColumnOperation();

            Assert.StartsWith(
                "ALTER TABLE [People] ALTER COLUMN [LuckyNumber] DROP DEFAULT" + EOL,
                Sql);
        }

        [Fact]
        public virtual void CreateIndexOperation()
        {
            Generate(
                new CreateIndexOperation
                {
                    Name = "IX_People_Name",
                    Table = "People",
                    Columns = new[] { "Name" }
                });

            Assert.Equal(
                "CREATE INDEX [IX_People_Name] ON [People] ([Name])" + EOL + EOL,
                Sql);
        }

        public SqlCeMigrationSqlGeneratorTest()
            : base(SqlCeTestHelpers.Instance)
        {
        }
    }
}
