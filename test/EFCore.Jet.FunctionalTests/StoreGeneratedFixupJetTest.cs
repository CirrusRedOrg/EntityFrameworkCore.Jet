// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

// ReSharper disable InconsistentNaming
namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public class StoreGeneratedFixupJetTest(StoreGeneratedFixupJetTest.StoreGeneratedFixupJetFixture fixture)
        : StoreGeneratedFixupRelationalTestBase<
            StoreGeneratedFixupJetTest.StoreGeneratedFixupJetFixture>(fixture)
    {
        [ConditionalFact]
        public Task Temp_values_are_replaced_on_save()
            => ExecuteWithStrategyInTransactionAsync(
                async context =>
                {
                    var entry = context.Add(new TestTemp());

                    Assert.True(entry.Property(e => e.Id).IsTemporary);
                    Assert.False(entry.Property(e => e.NotId).IsTemporary);

                    var tempValue = entry.Property(e => e.Id).CurrentValue;

                    await context.SaveChangesAsync();

                    Assert.False(entry.Property(e => e.Id).IsTemporary);
                    Assert.NotEqual(tempValue, entry.Property(e => e.Id).CurrentValue);
                });

        protected override void MarkIdsTemporary(DbContext context, object dependent, object principal)
        {
            var entry = context.Entry(dependent);
            entry.Property("Id1").IsTemporary = true;
            entry.Property("Id2").IsTemporary = true;

            foreach (var property in entry.Properties)
            {
                if (property.Metadata.IsForeignKey())
                {
                    property.IsTemporary = true;
                }
            }

            entry = context.Entry(principal);
            entry.Property("Id1").IsTemporary = true;
            entry.Property("Id2").IsTemporary = true;
        }

        protected override void MarkIdsTemporary(DbContext context, object game, object level, object item)
        {
            var entry = context.Entry(game);
            entry.Property("Id").IsTemporary = true;

            entry = context.Entry(item);
            entry.Property("Id").IsTemporary = true;
        }

        protected override bool EnforcesFKs => true;

        protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction)
            => facade.UseTransaction(transaction.GetDbTransaction());

        public class StoreGeneratedFixupJetFixture : StoreGeneratedFixupRelationalFixtureBase
        {
            protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;

            protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
            {
                base.OnModelCreating(modelBuilder, context);

                modelBuilder.Entity<Parent>(
                    b =>
                    {
                        b.Property(e => e.Id1).ValueGeneratedOnAdd();
                        b.Property(e => e.Id2).ValueGeneratedOnAdd().HasDefaultValueSql("GenGUID()");
                    });

                modelBuilder.Entity<Child>(
                    b =>
                    {
                        b.Property(e => e.Id1).ValueGeneratedOnAdd();
                        b.Property(e => e.Id2).ValueGeneratedOnAdd().HasDefaultValueSql("GenGUID()");
                    });

                modelBuilder.Entity<ParentPN>(
                    b =>
                    {
                        b.Property(e => e.Id1).ValueGeneratedOnAdd();
                        b.Property(e => e.Id2).ValueGeneratedOnAdd().HasDefaultValueSql("GenGUID()");
                    });

                modelBuilder.Entity<ChildPN>(
                    b =>
                    {
                        b.Property(e => e.Id1).ValueGeneratedOnAdd();
                        b.Property(e => e.Id2).ValueGeneratedOnAdd().HasDefaultValueSql("GenGUID()");
                    });

                modelBuilder.Entity<ParentDN>(
                    b =>
                    {
                        b.Property(e => e.Id1).ValueGeneratedOnAdd();
                        b.Property(e => e.Id2).ValueGeneratedOnAdd().HasDefaultValueSql("GenGUID()");
                    });

                modelBuilder.Entity<ChildDN>(
                    b =>
                    {
                        b.Property(e => e.Id1).ValueGeneratedOnAdd();
                        b.Property(e => e.Id2).ValueGeneratedOnAdd().HasDefaultValueSql("GenGUID()");
                    });

                modelBuilder.Entity<ParentNN>(
                    b =>
                    {
                        b.Property(e => e.Id1).ValueGeneratedOnAdd();
                        b.Property(e => e.Id2).ValueGeneratedOnAdd().HasDefaultValueSql("GenGUID()");
                    });

                modelBuilder.Entity<ChildNN>(
                    b =>
                    {
                        b.Property(e => e.Id1).ValueGeneratedOnAdd();
                        b.Property(e => e.Id2).ValueGeneratedOnAdd().HasDefaultValueSql("GenGUID()");
                    });

                modelBuilder.Entity<CategoryDN>(
                    b =>
                    {
                        b.Property(e => e.Id1).ValueGeneratedOnAdd();
                        b.Property(e => e.Id2).ValueGeneratedOnAdd().HasDefaultValueSql("GenGUID()");
                    });

                modelBuilder.Entity<ProductDN>(
                    b =>
                    {
                        b.Property(e => e.Id1).ValueGeneratedOnAdd();
                        b.Property(e => e.Id2).ValueGeneratedOnAdd().HasDefaultValueSql("GenGUID()");
                    });

                modelBuilder.Entity<CategoryPN>(
                    b =>
                    {
                        b.Property(e => e.Id1).ValueGeneratedOnAdd();
                        b.Property(e => e.Id2).ValueGeneratedOnAdd().HasDefaultValueSql("GenGUID()");
                    });

                modelBuilder.Entity<ProductPN>(
                    b =>
                    {
                        b.Property(e => e.Id1).ValueGeneratedOnAdd();
                        b.Property(e => e.Id2).ValueGeneratedOnAdd().HasDefaultValueSql("GenGUID()");
                    });

                modelBuilder.Entity<CategoryNN>(
                    b =>
                    {
                        b.Property(e => e.Id1).ValueGeneratedOnAdd();
                        b.Property(e => e.Id2).ValueGeneratedOnAdd().HasDefaultValueSql("GenGUID()");
                    });

                modelBuilder.Entity<ProductNN>(
                    b =>
                    {
                        b.Property(e => e.Id1).ValueGeneratedOnAdd();
                        b.Property(e => e.Id2).ValueGeneratedOnAdd().HasDefaultValueSql("GenGUID()");
                    });

                modelBuilder.Entity<Category>(
                    b =>
                    {
                        b.Property(e => e.Id1).ValueGeneratedOnAdd();
                        b.Property(e => e.Id2).ValueGeneratedOnAdd().HasDefaultValueSql("GenGUID()");
                    });

                modelBuilder.Entity<Product>(
                    b =>
                    {
                        b.Property(e => e.Id1).ValueGeneratedOnAdd();
                        b.Property(e => e.Id2).ValueGeneratedOnAdd().HasDefaultValueSql("GenGUID()");
                    });

                modelBuilder.Entity<Item>(b => b.Property(e => e.Id).ValueGeneratedOnAdd());

                modelBuilder.Entity<Game>(b => b.Property(e => e.Id).ValueGeneratedOnAdd().HasDefaultValueSql("GenGUID()"));
            }
        }
    }
}
