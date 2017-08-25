using System;
using Microsoft.EntityFrameworkCore.Internal;
using Xunit;

namespace Microsoft.EntityFrameworkCore
{
    public class SqlCeDatabaseFacadeTest
    {
        [Fact]
        public void IsSqlCe_when_using_OnConfguring()
        {
            using (var context = new SqlServerOnConfiguringContext())
            {
                Assert.True(context.Database.IsSqlCe());
            }
        }

        [Fact]
        public void IsSqlCe_in_OnModelCreating_when_using_OnConfguring()
        {
            using (var context = new SqlServerOnModelContext())
            {
                var _ = context.Model; // Trigger context initialization
                Assert.True(context.IsSqlCeSet);
            }
        }

        [Fact]
        public void IsSqlCe_in_constructor_when_using_OnConfguring()
        {
            using (var context = new SqlServerConstructorContext())
            {
                var _ = context.Model; // Trigger context initialization
                Assert.True(context.IsSqlCeSet);
            }
        }

        [Fact]
        public void Cannot_use_IsSqlCe_in_OnConfguring()
        {
            using (var context = new SqlServerUseInOnConfiguringContext())
            {
                Assert.Equal(
                    CoreStrings.RecursiveOnConfiguring,
                    Assert.Throws<InvalidOperationException>(
                        () =>
                        {
                            var _ = context.Model; // Trigger context initialization
                        }).Message);
            }
        }

        [Fact]
        public void IsSqlCe_when_using_constructor()
        {
            using (var context = new ProviderContext(
                new DbContextOptionsBuilder().UseSqlCe("Database=Maltesers").Options))
            {
                Assert.True(context.Database.IsSqlCe());
            }
        }

        [Fact]
        public void IsSqlCe_in_OnModelCreating_when_using_constructor()
        {
            using (var context = new ProviderOnModelContext(
                new DbContextOptionsBuilder().UseSqlCe("Database=Maltesers").Options))
            {
                var _ = context.Model; // Trigger context initialization
                Assert.True(context.IsSqlCeSet);
            }
        }

        [Fact]
        public void IsSqlCe_in_constructor_when_using_constructor()
        {
            using (var context = new ProviderConstructorContext(
                new DbContextOptionsBuilder().UseSqlCe("Database=Maltesers").Options))
            {
                var _ = context.Model; // Trigger context initialization
                Assert.True(context.IsSqlCeSet);
            }
        }

        [Fact]
        public void Cannot_use_IsSqlCe_in_OnConfguring_with_constructor()
        {
            using (var context = new ProviderUseInOnConfiguringContext(
                new DbContextOptionsBuilder().UseSqlCe("Database=Maltesers").Options))
            {
                Assert.Equal(
                    CoreStrings.RecursiveOnConfiguring,
                    Assert.Throws<InvalidOperationException>(
                        () =>
                        {
                            var _ = context.Model; // Trigger context initialization
                        }).Message);
            }
        }

        //[Fact]
        //public void Not_IsSqlCe_when_using_different_provider()
        //{
        //    using (var context = new ProviderContext(
        //        new DbContextOptionsBuilder().UseInMemoryDatabase("Maltesers").Options))
        //    {
        //        Assert.False(context.Database.IsSqlCe());
        //    }
        //}

        private class ProviderContext : DbContext
        {
            protected ProviderContext()
            {
            }

            public ProviderContext(DbContextOptions options)
                : base(options)
            {
            }

            public bool? IsSqlCeSet { get; protected set; }
        }

        private class SqlServerOnConfiguringContext : ProviderContext
        {
            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
                => optionsBuilder.UseSqlCe("Database=Maltesers");
        }

        private class SqlServerOnModelContext : SqlServerOnConfiguringContext
        {
            protected override void OnModelCreating(ModelBuilder modelBuilder)
                => IsSqlCeSet = Database.IsSqlCe();
        }

        private class SqlServerConstructorContext : SqlServerOnConfiguringContext
        {
            public SqlServerConstructorContext()
                => IsSqlCeSet = Database.IsSqlCe();
        }

        private class SqlServerUseInOnConfiguringContext : SqlServerOnConfiguringContext
        {
            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            {
                base.OnConfiguring(optionsBuilder);

                IsSqlCeSet = Database.IsSqlCe();
            }
        }

        private class ProviderOnModelContext : ProviderContext
        {
            public ProviderOnModelContext(DbContextOptions options)
                : base(options)
            {
            }

            protected override void OnModelCreating(ModelBuilder modelBuilder)
                => IsSqlCeSet = Database.IsSqlCe();
        }

        private class ProviderConstructorContext : ProviderContext
        {
            public ProviderConstructorContext(DbContextOptions options)
                : base(options)
                => IsSqlCeSet = Database.IsSqlCe();
        }

        private class ProviderUseInOnConfiguringContext : ProviderContext
        {
            public ProviderUseInOnConfiguringContext(DbContextOptions options)
                : base(options)
            {
            }

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
                => IsSqlCeSet = Database.IsSqlCe();
        }
    }
}