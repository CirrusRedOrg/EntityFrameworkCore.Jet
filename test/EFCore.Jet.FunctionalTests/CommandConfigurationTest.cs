// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

// ReSharper disable UnusedAutoPropertyAccessor.Local
// ReSharper disable InconsistentNaming

#nullable disable

namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public class CommandConfigurationTest : IClassFixture<CommandConfigurationTest.CommandConfigurationFixture>
    {
        public CommandConfigurationTest(CommandConfigurationFixture fixture)
        {
            Fixture = fixture;
            Fixture.TestSqlLoggerFactory.Clear();
        }

        protected CommandConfigurationFixture Fixture { get; set; }

        [ConditionalFact]
        public void Constructed_select_query_CommandBuilder_throws_when_negative_CommandTimeout_is_used()
        {
            using (var context = CreateContext())
            {
                Assert.Throws<ArgumentException>(() => context.Database.SetCommandTimeout(-5));
            }
        }

        private ChipsContext CreateContext() => (ChipsContext)Fixture.CreateContext();

        protected void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction)
            => facade.UseTransaction(transaction.GetDbTransaction());

        public int CountSqlLinesContaining(string searchTerm, string sql)
            => CountLinesContaining(sql, searchTerm);

        public int CountLinesContaining(string source, string searchTerm)
        {
            var text = source.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

            var matchQuery = from word in text
                             where word.Contains(searchTerm)
                             select word;

            return matchQuery.Count();
        }

        private class ChipsContext(DbContextOptions options) : PoolableDbContext(options)
        {
            public DbSet<KettleChips> Chips { get; set; }
        }

        private class KettleChips
        {
            // ReSharper disable once UnusedMember.Local
            public int Id { get; set; }

            public string Name { get; set; }
            public DateTime BestBuyDate { get; set; }
        }

        public class CommandConfigurationFixture : SharedStoreFixtureBase<DbContext>
        {
            protected override string StoreName { get; } = "CommandConfiguration";
            protected override Type ContextType { get; } = typeof(ChipsContext);
            protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;
            public TestSqlLoggerFactory TestSqlLoggerFactory => (TestSqlLoggerFactory)ListLoggerFactory;
        }
    }
}
