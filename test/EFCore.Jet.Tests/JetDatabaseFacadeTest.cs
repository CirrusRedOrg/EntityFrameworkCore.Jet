using System;
using EntityFrameworkCore.Jet.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using Xunit;

namespace EntityFrameworkCore.Jet.Tests
{
    public class JetDatabaseFacadeTest
    {
        [Fact]
        public void IsJet_when_using_OnConfguring()
        {
            using (var context = new SqlServerOnConfiguringContext())
            {
                Assert.True(context.Database.IsJet());
            }
        }

        [Fact]
        public void IsJet_in_OnModelCreating_when_using_OnConfguring()
        {
            using (var context = new SqlServerOnModelContext())
            {
                var _ = context.Model; // Trigger context initialization
                Assert.True(context.IsJetSet);
            }
        }

        [Fact]
        public void IsJet_in_constructor_when_using_OnConfguring()
        {
            using (var context = new SqlServerConstructorContext())
            {
                var _ = context.Model; // Trigger context initialization
                Assert.True(context.IsJetSet);
            }
        }

        [Fact]
        public void Cannot_use_IsJet_in_OnConfguring()
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
        public void IsJet_when_using_constructor()
        {
            using (var context = new ProviderContext(
                new DbContextOptionsBuilder().UseJet("Database=Maltesers").Options))
            {
                Assert.True(context.Database.IsJet());
            }
        }

        [Fact]
        public void IsJet_in_OnModelCreating_when_using_constructor()
        {
            using (var context = new ProviderOnModelContext(
                new DbContextOptionsBuilder().UseJet("Database=Maltesers").Options))
            {
                var _ = context.Model; // Trigger context initialization
                Assert.True(context.IsJetSet);
            }
        }

        [Fact]
        public void IsJet_in_constructor_when_using_constructor()
        {
            using (var context = new ProviderConstructorContext(
                new DbContextOptionsBuilder().UseJet("Database=Maltesers").Options))
            {
                var _ = context.Model; // Trigger context initialization
                Assert.True(context.IsJetSet);
            }
        }

        [Fact]
        public void Cannot_use_IsJet_in_OnConfguring_with_constructor()
        {
            using (var context = new ProviderUseInOnConfiguringContext(
                new DbContextOptionsBuilder().UseJet("Database=Maltesers").Options))
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
        //public void Not_IsJet_when_using_different_provider()
        //{
        //    using (var context = new ProviderContext(
        //        new DbContextOptionsBuilder().UseInMemoryDatabase("Maltesers").Options))
        //    {
        //        Assert.False(context.Database.IsJet());
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

            public bool? IsJetSet { get; protected set; }
        }

        private class SqlServerOnConfiguringContext : ProviderContext
        {
            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
                => optionsBuilder.UseJet("Database=Maltesers");
        }

        private class SqlServerOnModelContext : SqlServerOnConfiguringContext
        {
            protected override void OnModelCreating(ModelBuilder modelBuilder)
                => IsJetSet = Database.IsJet();
        }

        private class SqlServerConstructorContext : SqlServerOnConfiguringContext
        {
            public SqlServerConstructorContext()
                => IsJetSet = Database.IsJet();
        }

        private class SqlServerUseInOnConfiguringContext : SqlServerOnConfiguringContext
        {
            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            {
                base.OnConfiguring(optionsBuilder);

                IsJetSet = Database.IsJet();
            }
        }

        private class ProviderOnModelContext : ProviderContext
        {
            public ProviderOnModelContext(DbContextOptions options)
                : base(options)
            {
            }

            protected override void OnModelCreating(ModelBuilder modelBuilder)
                => IsJetSet = Database.IsJet();
        }

        private class ProviderConstructorContext : ProviderContext
        {
            public ProviderConstructorContext(DbContextOptions options)
                : base(options)
                => IsJetSet = Database.IsJet();
        }

        private class ProviderUseInOnConfiguringContext : ProviderContext
        {
            public ProviderUseInOnConfiguringContext(DbContextOptions options)
                : base(options)
            {
            }

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
                => IsJetSet = Database.IsJet();
        }
    }
}