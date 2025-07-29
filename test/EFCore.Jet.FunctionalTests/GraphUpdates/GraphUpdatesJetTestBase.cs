// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.TestUtilities;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace EntityFrameworkCore.Jet.FunctionalTests;

#nullable disable

public abstract class GraphUpdatesJetTestBase<TFixture>(TFixture fixture) : GraphUpdatesTestBase<TFixture>(fixture)
    where TFixture : GraphUpdatesJetTestBase<TFixture>.GraphUpdatesJetFixtureBase, new()
{
    [ConditionalFact] // Issue #32638
    public virtual void Key_and_index_properties_use_appropriate_comparer()
    {
        var parent = new StringKeyAndIndexParent
        {
            Id = "Parent",
            AlternateId = "Parent",
            Index = "Index",
            UniqueIndex = "UniqueIndex"
        };

        var child = new StringKeyAndIndexChild { Id = "Child", ParentId = "parent" };

        using var context = CreateContext();
        context.AttachRange(parent, child);

        Assert.Same(child, parent.Child);
        Assert.Same(parent, child.Parent);

        parent.Id = "parent";
        parent.AlternateId = "parent";
        parent.Index = "index";
        parent.UniqueIndex = "uniqueIndex";
        child.Id = "child";
        child.ParentId = "Parent";

        context.ChangeTracker.DetectChanges();

        var parentEntry = context.Entry(parent);
        Assert.Equal(EntityState.Modified, parentEntry.State);
        Assert.False(parentEntry.Property(e => e.Id).IsModified);
        Assert.False(parentEntry.Property(e => e.AlternateId).IsModified);
        Assert.True(parentEntry.Property(e => e.Index).IsModified);
        Assert.True(parentEntry.Property(e => e.UniqueIndex).IsModified);

        var childEntry = context.Entry(child);

        if (childEntry.Metadata.IsOwned())
        {
            Assert.Equal(EntityState.Modified, childEntry.State);
            Assert.True(childEntry.Property(e => e.Id).IsModified); // Not a key for the owned type
            Assert.False(childEntry.Property(e => e.ParentId).IsModified);
        }
        else
        {
            Assert.Equal(EntityState.Unchanged, childEntry.State);
            Assert.False(childEntry.Property(e => e.Id).IsModified);
            Assert.False(childEntry.Property(e => e.ParentId).IsModified);
        }
    }

    protected class StringKeyAndIndexParent : NotifyingEntity
    {
        private string _id;
        private string _alternateId;
        private string _uniqueIndex;
        private string _index;
        private StringKeyAndIndexChild _child;

        public string Id
        {
            get => _id;
            set => SetWithNotify(value, ref _id);
        }

        public string AlternateId
        {
            get => _alternateId;
            set => SetWithNotify(value, ref _alternateId);
        }

        public string Index
        {
            get => _index;
            set => SetWithNotify(value, ref _index);
        }

        public string UniqueIndex
        {
            get => _uniqueIndex;
            set => SetWithNotify(value, ref _uniqueIndex);
        }

        public StringKeyAndIndexChild Child
        {
            get => _child;
            set => SetWithNotify(value, ref _child);
        }
    }

    protected class StringKeyAndIndexChild : NotifyingEntity
    {
        private string _id;
        private string _parentId;
        private int _foo;
        private StringKeyAndIndexParent _parent;

        public string Id
        {
            get => _id;
            set => SetWithNotify(value, ref _id);
        }

        public string ParentId
        {
            get => _parentId;
            set => SetWithNotify(value, ref _parentId);
        }

        public int Foo
        {
            get => _foo;
            set => SetWithNotify(value, ref _foo);
        }

        public StringKeyAndIndexParent Parent
        {
            get => _parent;
            set => SetWithNotify(value, ref _parent);
        }
    }

    protected override IQueryable<Root> ModifyQueryRoot(IQueryable<Root> query)
        => query.AsSplitQuery();

    protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction)
        => facade.UseTransaction(transaction.GetDbTransaction());

    public abstract class GraphUpdatesJetFixtureBase : GraphUpdatesFixtureBase
    {
        public TestSqlLoggerFactory TestSqlLoggerFactory
            => (TestSqlLoggerFactory)ListLoggerFactory;

        protected override ITestStoreFactory TestStoreFactory
            => JetTestStoreFactory.Instance;

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);

            modelBuilder.Entity<AccessState>(
                b =>
                {
                    b.Property(e => e.AccessStateId).ValueGeneratedNever();
                    b.HasData(new AccessState { AccessStateId = 1 });
                });

            modelBuilder.Entity<Cruiser>(
                b =>
                {
                    b.Property(e => e.IdUserState).HasDefaultValue(1);
                    b.HasOne(e => e.UserState).WithMany(e => e.Users).HasForeignKey(e => e.IdUserState);
                });

            modelBuilder.Entity<AccessStateWithSentinel>(
                b =>
                {
                    b.Property(e => e.AccessStateWithSentinelId).ValueGeneratedNever();
                    b.HasData(new AccessStateWithSentinel { AccessStateWithSentinelId = 1 });
                });

            modelBuilder.Entity<CruiserWithSentinel>(
                b =>
                {
                    b.Property(e => e.IdUserState).HasDefaultValue(1).HasSentinel(667);
                    b.HasOne(e => e.UserState).WithMany(e => e.Users).HasForeignKey(e => e.IdUserState);
                });

            modelBuilder.Entity<SomethingOfCategoryA>().Property<int>("CategoryId").HasDefaultValue(1);
            modelBuilder.Entity<SomethingOfCategoryB>().Property(e => e.CategoryId).HasDefaultValue(2);

            modelBuilder.Entity<StringKeyAndIndexParent>(
                b =>
                {
                    b.HasOne(e => e.Child)
                        .WithOne(e => e.Parent)
                        .HasForeignKey<StringKeyAndIndexChild>(e => e.ParentId)
                        .HasPrincipalKey<StringKeyAndIndexParent>(e => e.AlternateId);
                });

            modelBuilder.Entity<CompositeKeyWith<int>>(
                b =>
                {
                    b.Property(e => e.PrimaryGroup).HasDefaultValue(1).HasSentinel(1);
                });

            modelBuilder.Entity<CompositeKeyWith<bool>>(
                b =>
                {
                    b.Property(e => e.PrimaryGroup).HasDefaultValue(true);
                });

            modelBuilder.Entity<CompositeKeyWith<bool?>>(
                b =>
                {
                    b.Property(e => e.PrimaryGroup).HasDefaultValue(true);
                });

            modelBuilder.Entity<SharedFkRoot>().Property(x => x.Id).HasColumnType("int");
            modelBuilder.Entity<SharedFkDependant>().Property(x => x.Id).HasColumnType("int");
            modelBuilder.Entity<SharedFkParent>().Property(x => x.Id).HasColumnType("int");
        }

        protected override async Task SeedAsync(PoolableDbContext context)
        {
            await base.SeedAsync(context);

            await context.Database.ExecuteSqlAsync($"ALTER TABLE `OptionalComposite2` DROP CONSTRAINT `FK_OptionalComposite2_OptionalAk1_ParentId_ParentAlternateId`");
            await context.Database.ExecuteSqlAsync($"ALTER TABLE `OptionalOverlapping2` DROP CONSTRAINT `FK_OptionalOverlapping2_RequiredComposite1_ParentId_ParentAlter~`");
            await context.Database.ExecuteSqlAsync($"ALTER TABLE `OptionalSingleComposite2` DROP CONSTRAINT `FK_OptionalSingleComposite2_OptionalSingleAk1_BackId_ParentAlte~`");
            await context.Database.ExecuteSqlAsync($"ALTER TABLE `OwnedOptional2` DROP CONSTRAINT `FK_OwnedOptional2_OwnedOptional1_OwnedOptional1OwnerRootId_Owne~`");
            await context.Database.ExecuteSqlAsync($"ALTER TABLE `OwnedRequired2` DROP CONSTRAINT `FK_OwnedRequired2_OwnedRequired1_OwnedRequired1OwnerRootId_Owne~`");
            await context.Database.ExecuteSqlAsync($"ALTER TABLE `RequiredComposite2` DROP CONSTRAINT `FK_RequiredComposite2_RequiredAk1_ParentId_ParentAlternateId`");
            await context.Database.ExecuteSqlAsync($"ALTER TABLE `SharedFkParent` DROP CONSTRAINT `FK_SharedFkParent_SharedFkDependant_RootId_DependantId`");
        }
    }
}
