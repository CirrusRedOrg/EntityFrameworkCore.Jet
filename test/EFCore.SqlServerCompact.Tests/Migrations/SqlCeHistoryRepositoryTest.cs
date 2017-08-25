using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Internal;
using Moq;
using Xunit;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Microsoft.EntityFrameworkCore.Tests.Migrations
{
    public class SqlCeHistoryRepositoryTest
    {
        private static string EOL => Environment.NewLine;

        [Fact]
        public void GetCreateScript_works()
        {
            var sql = CreateHistoryRepository().GetCreateScript();

            Assert.Equal(
                "CREATE TABLE [__EFMigrationsHistory] (" + EOL +
                "    [MigrationId] nvarchar(150) NOT NULL," + EOL +
                "    [ProductVersion] nvarchar(32) NOT NULL," + EOL +
                "    CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])" + EOL 
                + ")" + EOL + EOL,
                sql);
        }

        [Fact]
        public void GetCreateIfNotExistsScript_works()
        {
            var sql = CreateHistoryRepository().GetCreateIfNotExistsScript();

            Assert.Equal(
                "CREATE TABLE [__EFMigrationsHistory] (" + EOL +
                "    [MigrationId] nvarchar(150) NOT NULL," + EOL +
                "    [ProductVersion] nvarchar(32) NOT NULL," + EOL +
                "    CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])" + EOL + 
                ")" + EOL + EOL,
                sql);
        }

        [Fact]
        public void GetDeleteScript_works()
        {
            var sql = CreateHistoryRepository().GetDeleteScript("Migration1");

            Assert.Equal(
                "DELETE FROM [__EFMigrationsHistory]" + EOL +
                "WHERE [MigrationId] = 'Migration1'" + EOL + EOL,
                sql);
        }

        [Fact]
        public void GetInsertScript_works()
        {
            var sql = CreateHistoryRepository().GetInsertScript(
                new HistoryRow("Migration1", "1.0.0"));

            Assert.Equal(
                "INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])" + EOL +
                "VALUES ('Migration1', '1.0.0')" + EOL + EOL,
                sql);
        }

        [Fact]
        public void GetBeginIfNotExistsScript_works()
        {
            Assert.Throws<NotSupportedException>(() => CreateHistoryRepository().GetBeginIfNotExistsScript("Migration1"));
        }

        [Fact]
        public void GetBeginIfExistsScript_works()
        {
            Assert.Throws<NotSupportedException>(() => CreateHistoryRepository().GetBeginIfExistsScript("Migration1"));
        }

        [Fact]
        public void GetEndIfScript_works()
        {
            Assert.Throws<NotSupportedException>(() => CreateHistoryRepository().GetEndIfScript());
        }

        private static IHistoryRepository CreateHistoryRepository(string schema = null)
        {
            var sqlGenerator = new SqlCeSqlGenerationHelper(new RelationalSqlGenerationHelperDependencies());
            var typeMapper = new SqlCeTypeMapper(new RelationalTypeMapperDependencies());

            var commandBuilderFactory = new RelationalCommandBuilderFactory(
                new FakeDiagnosticsLogger<DbLoggerCategory.Database.Command>(),
                typeMapper);

            return new SqlCeHistoryRepository(
                new HistoryRepositoryDependencies(
                    Mock.Of<IRelationalDatabaseCreator>(),
                    Mock.Of<IRawSqlCommandBuilder>(),
                    Mock.Of<ISqlCeDatabaseConnection>(),
                    new DbContextOptions<DbContext>(
                        new Dictionary<Type, IDbContextOptionsExtension>
                        {
                            {
                                typeof(SqlCeOptionsExtension),
                                new SqlCeOptionsExtension().WithMigrationsHistoryTableSchema(schema)
                            }
                        }),
                    new MigrationsModelDiffer(
                        new SqlCeTypeMapper(new RelationalTypeMapperDependencies()),
                        new SqlCeMigrationsAnnotationProvider(new MigrationsAnnotationProviderDependencies())),
                    new SqlCeMigrationsSqlGenerator(
                        new MigrationsSqlGeneratorDependencies(
                            commandBuilderFactory,
                            new SqlCeSqlGenerationHelper(new RelationalSqlGenerationHelperDependencies()),
                            typeMapper),
                        new SqlCeMigrationsAnnotationProvider(new MigrationsAnnotationProviderDependencies())),
                    sqlGenerator));
        }

        private class Context : DbContext
        {
        }
    }
}
