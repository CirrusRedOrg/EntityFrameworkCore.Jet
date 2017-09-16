using System;
using System.Collections.Generic;
using EntityFrameworkCore.Jet.Infrastructure.Internal;
using EntityFrameworkCore.Jet.Migrations;
using EntityFrameworkCore.Jet.Migrations.Internal;
using EntityFrameworkCore.Jet.Storage.Internal;
using EntityFrameworkCore.Jet.Tests.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Internal;
using Moq;
using Xunit;

namespace EntityFrameworkCore.Jet.Tests.Migrations
{
    public class JetHistoryRepositoryTest
    {
        private static string EOL => Environment.NewLine;

        [Fact]
        public void GetCreateScript_works()
        {
            var sql = CreateHistoryRepository().GetCreateScript();

            Assert.Equal(
                "CREATE TABLE [__EFMigrationsHistory] (" + EOL +
                "    [MigrationId] varchar(150) NOT NULL," + EOL +
                "    [ProductVersion] varchar(32) NOT NULL," + EOL +
                "    CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])" + EOL 
                + ");" + EOL,
                sql);
        }

        [Fact]
        public void GetCreateIfNotExistsScript_works()
        {
            var sql = CreateHistoryRepository().GetCreateIfNotExistsScript();
        }

        [Fact]
        public void GetDeleteScript_works()
        {
            var sql = CreateHistoryRepository().GetDeleteScript("Migration1");

            Assert.Equal(
                "DELETE FROM [__EFMigrationsHistory]" + EOL +
                "WHERE [MigrationId] = 'Migration1';" + EOL,
                sql);
        }

        [Fact]
        public void GetInsertScript_works()
        {
            var sql = CreateHistoryRepository().GetInsertScript(
                new HistoryRow("Migration1", "1.0.0"));

            Assert.Equal(
                "INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])" + EOL +
                "VALUES ('Migration1', '1.0.0');" + EOL,
                sql);
        }

        [Fact]
        public void GetBeginIfNotExistsScript_works()
        {
            CreateHistoryRepository().GetBeginIfNotExistsScript("Migration1");
        }

        [Fact]
        public void GetBeginIfExistsScript_works()
        {
            CreateHistoryRepository().GetBeginIfExistsScript("Migration1");
        }

        [Fact]
        public void GetEndIfScript_works()
        {
            CreateHistoryRepository().GetEndIfScript();
        }

        private static IHistoryRepository CreateHistoryRepository(string schema = null)
        {
            var sqlGenerator = new JetSqlGenerationHelper(new RelationalSqlGenerationHelperDependencies());
            var typeMapper = new JetTypeMapper(new RelationalTypeMapperDependencies());

            var commandBuilderFactory = new RelationalCommandBuilderFactory(
                new FakeDiagnosticsLogger<DbLoggerCategory.Database.Command>(),
                typeMapper);

            return new JetHistoryRepository(
                new HistoryRepositoryDependencies(
                    Mock.Of<IRelationalDatabaseCreator>(),
                    Mock.Of<IRawSqlCommandBuilder>(),
                    Mock.Of<IJetRelationalConnection>(),
                    new DbContextOptions<DbContext>(
                        new Dictionary<Type, IDbContextOptionsExtension>
                        {
                            {
                                typeof(JetOptionsExtension),
                                new JetOptionsExtension().WithMigrationsHistoryTableSchema(schema)
                            }
                        }),
                    new MigrationsModelDiffer(
                        new JetTypeMapper(new RelationalTypeMapperDependencies()),
                        new JetMigrationsAnnotationProvider(new MigrationsAnnotationProviderDependencies())),
                    new JetMigrationsSqlGenerator(
                        new MigrationsSqlGeneratorDependencies(
                            commandBuilderFactory,
                            new JetSqlGenerationHelper(new RelationalSqlGenerationHelperDependencies()),
                            typeMapper),
                        new JetMigrationsAnnotationProvider(new MigrationsAnnotationProviderDependencies())),
                    sqlGenerator));
        }

        private class Context : DbContext
        {
        }
    }
}
