using System;
using EntityFrameworkCore.Jet;
using Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EntityFramework.Jet.FunctionalTests
{
    public class JetMigrationsTest
    {
        [Fact]
        public void Empty_Migration_Creates_Database()
        {
            using (var testDatabase = JetTestStore.CreateScratch(createDatabase: false))
            {
                
                using (var context = CreateContext(testDatabase))
                {
                    context.Database.Migrate();

                    Assert.True(context.GetService<IRelationalDatabaseCreator>().Exists());
                }
            }
        }

        private static BloggingContext CreateContext(JetTestStore testStore)
        {
            var serviceProvider =
                new ServiceCollection()
                    .AddEntityFrameworkJet()
                    .BuildServiceProvider();

            var optionsBuilder = new DbContextOptionsBuilder();
            optionsBuilder
                .UseJet(testStore.ConnectionString)
                .UseInternalServiceProvider(serviceProvider);

            return new BloggingContext(serviceProvider, optionsBuilder.Options);
        }

        private class BloggingContext : DbContext
        {
            public BloggingContext(IServiceProvider serviceProvider, DbContextOptions options)
                : base(options)
            {
            }

            public DbSet<Blog> Blogs { get; set; }

            public class Blog
            {
                public int Id { get; set; }
                public string Name { get; set; }
            }
        }

        [DbContext(typeof(BloggingContext))]
        [Migration("00000000000000_Empty")]
        public class EmptyMigration : Migration
        {
            protected override void Up(MigrationBuilder migrationBuilder)
            {
            }
        }
    }
}
