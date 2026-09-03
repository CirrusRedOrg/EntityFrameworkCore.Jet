// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EntityFrameworkCore.LibRed.Extended.FunctionalTests.TestUtilities;
using LibRed.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using System.Data;

// ReSharper disable InconsistentNaming
// ReSharper disable ClassNeverInstantiated.Local
// ReSharper disable UnusedAutoPropertyAccessor.Local

#nullable disable

namespace EntityFrameworkCore.LibRed.Extended.FunctionalTests
{
    public class ConnectionSpecificationTest
    {
        [Fact]
        public async Task Can_specify_no_connection_string_in_OnConfiguring()
        {
            var serviceProvider
                = new ServiceCollection()
                    .AddDbContext<NoneInOnConfiguringContext>()
                    .BuildServiceProvider(validateScopes: true);

            await using (await LibRedTestStore.GetNorthwindStoreAsync())
            {
                using var scope = serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<NoneInOnConfiguringContext>();

                context.Database.SetConnectionString(LibRedNorthwindTestStoreFactory.NorthwindConnectionString);

                Assert.True(await context.Customers.AnyAsync());
            }
        }

        [Fact]
        public async Task Can_specify_no_connection_string_in_OnConfiguring_with_default_service_provider()
        {
            await using (await LibRedTestStore.GetNorthwindStoreAsync())
            {
                await using var context = new NoneInOnConfiguringContext();

                context.Database.SetConnectionString(LibRedNorthwindTestStoreFactory.NorthwindConnectionString);

                Assert.True(await context.Customers.AnyAsync());
            }
        }

        [Fact]
        public async Task Throws_if_context_used_with_no_connection_or_connection_string()
        {
            await using (await LibRedTestStore.GetNorthwindStoreAsync())
            {
                await using var context = new NoneInOnConfiguringContext();

                await Assert.ThrowsAsync<InvalidOperationException>(() => context.Customers.AnyAsync());
            }
        }

        private class NoneInOnConfiguringContext : NorthwindContextBase
        {
            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
                => optionsBuilder
                    .EnableServiceProviderCaching(false)
                    .UseLibRed(b => b.ApplyConfiguration());
        }

        [Fact]
        public async Task Can_specify_connection_string_in_OnConfiguring()
        {
            var serviceProvider
                = new ServiceCollection()
                    .AddDbContext<StringInOnConfiguringContext>()
                    .BuildServiceProvider(validateScopes: true);

            await using (await LibRedTestStore.GetNorthwindStoreAsync())
            {
                using var scope = serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<StringInOnConfiguringContext>();
                Assert.True(await context.Customers.AnyAsync());
            }
        }

        [Fact]
        public async Task Can_specify_connection_string_in_OnConfiguring_with_default_service_provider()
        {
            await using (await LibRedTestStore.GetNorthwindStoreAsync())
            {
                await using var context = new StringInOnConfiguringContext();
                Assert.True(await context.Customers.AnyAsync());
            }
        }

        private class StringInOnConfiguringContext : NorthwindContextBase
        {
            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
                => optionsBuilder
                    .EnableServiceProviderCaching(false)
                    .UseLibRed(LibRedNorthwindTestStoreFactory.NorthwindConnectionString, b => b.ApplyConfiguration());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Can_specify_no_connection_in_OnConfiguring(bool contextOwnsConnection)
        {
            var serviceProvider
                = new ServiceCollection()
                    .AddScoped(p => new LibRedConnection(LibRedNorthwindTestStoreFactory.NorthwindConnectionString))
                    .AddDbContext<NoneInOnConfiguringContext>().BuildServiceProvider(validateScopes: true);

            LibRedConnection connection;

            await using (await LibRedTestStore.GetNorthwindStoreAsync())
            {
                using var scope = serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<NoneInOnConfiguringContext>();

                connection = new LibRedConnection(LibRedNorthwindTestStoreFactory.NorthwindConnectionString);
                context.Database.SetDbConnection(connection, contextOwnsConnection);

                Assert.True(await context.Customers.AnyAsync());
            }

            if (contextOwnsConnection)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => connection.OpenAsync()); // Disposed
            }
            else
            {
                await connection.OpenAsync();
                await connection.CloseAsync();
                await connection.DisposeAsync();
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Can_specify_no_connection_in_OnConfiguring_with_default_service_provider(bool contextOwnsConnection)
        {
            LibRedConnection connection;

            await using (await LibRedTestStore.GetNorthwindStoreAsync())
            {
                using var context = new NoneInOnConfiguringContext();

                connection = new LibRedConnection(LibRedNorthwindTestStoreFactory.NorthwindConnectionString);
                context.Database.SetDbConnection(connection, contextOwnsConnection);

                Assert.True(await context.Customers.AnyAsync());
            }

            if (contextOwnsConnection)
            {
                Assert.Throws<InvalidOperationException>(() => connection.Open()); // Disposed
            }
            else
            {
                connection.Open();
                connection.Close();
                connection.Dispose();
            }
        }

        [Fact]
        public async Task Can_specify_connection_in_OnConfiguring()
        {
            var serviceProvider
                = new ServiceCollection()
                    .AddScoped(p => new LibRedConnection(LibRedNorthwindTestStoreFactory.NorthwindConnectionString))
                    .AddDbContext<ConnectionInOnConfiguringContext>().BuildServiceProvider(validateScopes: true);

            await using (await LibRedTestStore.GetNorthwindStoreAsync())
            {
                using var scope = serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ConnectionInOnConfiguringContext>();
                Assert.True(await context.Customers.AnyAsync());
            }
        }

        [Fact]
        public async Task Can_specify_connection_in_OnConfiguring_with_default_service_provider()
        {
            await using (await LibRedTestStore.GetNorthwindStoreAsync())
            {
                using var connection = new LibRedConnection(LibRedNorthwindTestStoreFactory.NorthwindConnectionString);
                using var context = new ConnectionInOnConfiguringContext(connection);

                Assert.True(await context.Customers.AnyAsync());
            }
        }

        [Fact]
        public async Task Can_specify_owned_connection_in_OnConfiguring()
        {
            var serviceProvider
                = new ServiceCollection()
                    .AddSingleton(_ => new LibRedConnection(LibRedNorthwindTestStoreFactory.NorthwindConnectionString))
                    .AddDbContext<OwnedConnectionInOnConfiguringContext>().BuildServiceProvider(validateScopes: true);

            LibRedConnection connection;

            await using (await LibRedTestStore.GetNorthwindStoreAsync())
            {
                connection = serviceProvider.GetRequiredService<LibRedConnection>();

                using var scope = serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<OwnedConnectionInOnConfiguringContext>();
                Assert.True(await context.Customers.AnyAsync());
            }

            Assert.Throws<InvalidOperationException>(() => connection.Open()); // Disposed
        }

        [Fact]
        public async Task Can_specify_owned_connection_in_OnConfiguring_with_default_service_provider()
        {
            LibRedConnection connection;

            await using (await LibRedTestStore.GetNorthwindStoreAsync())
            {
                connection = new LibRedConnection(LibRedNorthwindTestStoreFactory.NorthwindConnectionString);
                using var context = new OwnedConnectionInOnConfiguringContext(connection);

                Assert.True(await context.Customers.AnyAsync());
            }

            Assert.Throws<InvalidOperationException>(() => connection.Open()); // Disposed
        }

        [Fact]
        public async Task Can_specify_then_change_connection()
        {
            var connection = new LibRedConnection(LibRedNorthwindTestStoreFactory.NorthwindConnectionString);

            var serviceProvider
                = new ServiceCollection()
                    .AddScoped(p => connection)
                    .AddDbContext<ConnectionInOnConfiguringContext>().BuildServiceProvider(validateScopes: true);

            await using (await LibRedTestStore.GetNorthwindStoreAsync())
            {
                using var scope = serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ConnectionInOnConfiguringContext>();

                Assert.Same(connection, context.Database.GetDbConnection());
                Assert.True(await context.Customers.AnyAsync());

                using var newConnection = new LibRedConnection(LibRedNorthwindTestStoreFactory.NorthwindConnectionString);
                context.Database.SetDbConnection(newConnection);

                Assert.Same(newConnection, context.Database.GetDbConnection());
                Assert.True(await context.Customers.AnyAsync());
            }
        }

        [Fact]
        public async Task Cannot_change_connection_when_open_and_owned()
        {
            var connection = new LibRedConnection(LibRedNorthwindTestStoreFactory.NorthwindConnectionString);

            var serviceProvider
                = new ServiceCollection()
                    .AddScoped(p => connection)
                    .AddDbContext<OwnedConnectionInOnConfiguringContext>().BuildServiceProvider(validateScopes: true);

            await using (await LibRedTestStore.GetNorthwindStoreAsync())
            {
                using var scope = serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<OwnedConnectionInOnConfiguringContext>();

                context.Database.OpenConnection();
                Assert.Same(connection, context.Database.GetDbConnection());
                Assert.True(await context.Customers.AnyAsync());

                using var newConnection = new LibRedConnection(LibRedNorthwindTestStoreFactory.NorthwindConnectionString);

                Assert.Equal(
                    RelationalStrings.CannotChangeWhenOpen,
                    Assert.Throws<InvalidOperationException>(() => context.Database.SetDbConnection(newConnection)).Message);
            }
        }

        [Fact]
        public async Task Can_change_connection_when_open_and_not_owned()
        {
            var connection = new LibRedConnection(LibRedNorthwindTestStoreFactory.NorthwindConnectionString);

            var serviceProvider
                = new ServiceCollection()
                    .AddScoped(p => connection)
                    .AddDbContext<ConnectionInOnConfiguringContext>().BuildServiceProvider(validateScopes: true);

            await using (await LibRedTestStore.GetNorthwindStoreAsync())
            {
                using var scope = serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ConnectionInOnConfiguringContext>();

                context.Database.OpenConnection();
                Assert.Same(connection, context.Database.GetDbConnection());
                Assert.True(await context.Customers.AnyAsync());

                using var newConnection = new LibRedConnection(LibRedNorthwindTestStoreFactory.NorthwindConnectionString);
                context.Database.SetDbConnection(newConnection);

                Assert.Same(newConnection, context.Database.GetDbConnection());
                Assert.True(await context.Customers.AnyAsync());
            }
        }

        private class ConnectionInOnConfiguringContext(LibRedConnection connection) : NorthwindContextBase
        {
            private readonly LibRedConnection _connection = connection;

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
                => optionsBuilder
                    .EnableServiceProviderCaching(false)
                    .UseLibRed(_connection, b => b.ApplyConfiguration());

            public override void Dispose()
            {
                _connection.Dispose();
                base.Dispose();
            }
        }

        private class OwnedConnectionInOnConfiguringContext(LibRedConnection connection) : NorthwindContextBase
        {
            private readonly LibRedConnection _connection = connection;

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
                => optionsBuilder
                    .EnableServiceProviderCaching(false)
                    .UseLibRed(_connection, contextOwnsConnection: true, b => b.ApplyConfiguration());
        }

        [Fact]
        public async Task Throws_if_no_connection_found_in_config_without_UseLibRed()
        {
            var serviceProvider
                = new ServiceCollection()
                    .AddDbContext<NoUseLibRedContext>().BuildServiceProvider(validateScopes: true);

            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<NoUseLibRedContext>();
            Assert.Equal(
                CoreStrings.NoProviderConfigured,
                (await Assert.ThrowsAsync<InvalidOperationException>(() => context.Customers.AnyAsync())).Message);
        }

        [Fact]
        public async Task Throws_if_no_config_without_UseLibRed()
        {
            var serviceProvider
                = new ServiceCollection()
                    .AddDbContext<NoUseLibRedContext>().BuildServiceProvider(validateScopes: true);

            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<NoUseLibRedContext>();
            Assert.Equal(
                CoreStrings.NoProviderConfigured,
                (await Assert.ThrowsAsync<InvalidOperationException>(() => context.Customers.AnyAsync())).Message);
        }

        private class NoUseLibRedContext : NorthwindContextBase
        {
            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
                => optionsBuilder.EnableServiceProviderCaching(false);
        }

        [Fact]
        public async Task Can_depend_on_DbContextOptions()
        {
            var serviceProvider
                = new ServiceCollection()
                    .AddScoped(p => new LibRedConnection(LibRedNorthwindTestStoreFactory.NorthwindConnectionString))
                    .AddDbContext<OptionsContext>()
                    .BuildServiceProvider(validateScopes: true);

            await using (await LibRedTestStore.GetNorthwindStoreAsync())
            {
                using var scope = serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<OptionsContext>();
                Assert.True(await context.Customers.AnyAsync());
            }
        }

        [Fact]
        public async Task Can_depend_on_DbContextOptions_with_default_service_provider()
        {
            await using (await LibRedTestStore.GetNorthwindStoreAsync())
            {
                using var connection = new LibRedConnection(LibRedNorthwindTestStoreFactory.NorthwindConnectionString);

                using var context = new OptionsContext(
                    new DbContextOptions<OptionsContext>(),
                    connection);

                Assert.True(await context.Customers.AnyAsync());
            }
        }

        private class OptionsContext(DbContextOptions<OptionsContext> options, LibRedConnection connection) : NorthwindContextBase(options)
        {
            private readonly LibRedConnection _connection = connection;
            private readonly DbContextOptions<OptionsContext> _options = options;

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            {
                Assert.Same(_options, optionsBuilder.Options);

                optionsBuilder
                    .EnableServiceProviderCaching(false)
                    .UseLibRed(_connection, b => b.ApplyConfiguration());

                Assert.NotSame(_options, optionsBuilder.Options);
            }

            public override void Dispose()
            {
                _connection.Dispose();
                base.Dispose();
            }
        }

        [Fact]
        public async Task Can_depend_on_non_generic_options_when_only_one_context()
        {
            var serviceProvider
                = new ServiceCollection()
                    .AddDbContext<NonGenericOptionsContext>()
                    .BuildServiceProvider(validateScopes: true);

            await using (await LibRedTestStore.GetNorthwindStoreAsync())
            {
                using var scope = serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<NonGenericOptionsContext>();
                Assert.True(await context.Customers.AnyAsync());
            }
        }

        [Fact]
        public async Task Can_depend_on_non_generic_options_when_only_one_context_with_default_service_provider()
        {
            await using (await LibRedTestStore.GetNorthwindStoreAsync())
            {
                using var context = new NonGenericOptionsContext(new DbContextOptions<DbContext>());
                Assert.True(await context.Customers.AnyAsync());
            }
        }

        private class NonGenericOptionsContext(DbContextOptions options) : NorthwindContextBase(options)
        {
            private readonly DbContextOptions _options = options;

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            {
                Assert.Same(_options, optionsBuilder.Options);

                optionsBuilder
                    .EnableServiceProviderCaching(false)
                    .UseLibRed(LibRedNorthwindTestStoreFactory.NorthwindConnectionString, b => b.ApplyConfiguration());

                Assert.NotSame(_options, optionsBuilder.Options);
            }
        }

        [Theory]
        [InlineData("MyConnectionString", "name=MyConnectionString")]
        [InlineData("ConnectionStrings:DefaultConnection", "name=ConnectionStrings:DefaultConnection")]
        [InlineData("ConnectionStrings:DefaultConnection", " NamE   =   ConnectionStrings:DefaultConnection  ")]
        public async Task Can_use_AddDbContext_and_get_connection_string_from_config(string key, string connectionString)
        {
            var configBuilder = new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string> { { key, LibRedNorthwindTestStoreFactory.NorthwindConnectionString } });

            var serviceProvider
                = new ServiceCollection()
                    .AddSingleton<IConfiguration>(configBuilder.Build())
                    .AddDbContext<UseConfigurationContext>(
                        b => b.UseLibRed(connectionString).EnableServiceProviderCaching(false))
                    .BuildServiceProvider(validateScopes: true);

            await using (await LibRedTestStore.GetNorthwindStoreAsync())
            {
                using var serviceScope = serviceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
                using var context = serviceScope.ServiceProvider.GetRequiredService<UseConfigurationContext>();
                Assert.True(await context.Customers.AnyAsync());
            }
        }

        private class UseConfigurationContext(DbContextOptions options) : NorthwindContextBase(options);

        private class NorthwindContextBase : DbContext
        {
            protected NorthwindContextBase()
            {
            }

            protected NorthwindContextBase(DbContextOptions options)
                : base(options)
            {
            }

            public DbSet<Customer> Customers { get; set; }

            protected override void OnModelCreating(ModelBuilder modelBuilder)
                => modelBuilder.Entity<Customer>(
                    b =>
                    {
                        b.HasKey(c => c.CustomerID);
                        b.ToTable("Customers");
                    });
        }

        private class Customer
        {
            public string CustomerID { get; set; }

            // ReSharper disable UnusedMember.Local
            public string CompanyName { get; set; }

            public string Fax { get; set; }
            // ReSharper restore UnusedMember.Local
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Can_use_an_existing_closed_connection_test(bool openConnection)
        {
            var serviceProvider = new ServiceCollection()
                .AddEntityFrameworkLibRed()
                .BuildServiceProvider(validateScopes: true);

            await using var store = await LibRedTestStore.GetNorthwindStoreAsync();
            store.CloseConnection();

            var openCount = 0;
            var closeCount = 0;
            var disposeCount = 0;

            using var connection = new LibRedConnection(store.ConnectionString);
            if (openConnection)
            {
                await connection.OpenAsync();
            }

            connection.StateChange += (_, a) =>
            {
                switch (a.CurrentState)
                {
                    case ConnectionState.Open:
                        openCount++;
                        break;
                    case ConnectionState.Closed:
                        closeCount++;
                        break;
                }
            };
            connection.Disposed += (_, __) => disposeCount++;

            using (var context = new NorthwindContext(serviceProvider, connection))
            {
                Assert.Equal(91, await context.Customers.CountAsync());
            }

            if (openConnection)
            {
                Assert.Equal(ConnectionState.Open, connection.State);
                Assert.Equal(0, openCount);
                Assert.Equal(0, closeCount);
            }
            else
            {
                Assert.Equal(ConnectionState.Closed, connection.State);
                Assert.Equal(1, openCount);
                Assert.Equal(1, closeCount);
            }

            Assert.Equal(0, disposeCount);
        }

        private class NorthwindContext(IServiceProvider serviceProvider, LibRedConnection connection) : DbContext
        {
            private readonly IServiceProvider _serviceProvider = serviceProvider;
            private readonly LibRedConnection _connection = connection;

            // ReSharper disable once UnusedAutoPropertyAccessor.Local
            public DbSet<Customer> Customers { get; set; }

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
                => optionsBuilder
                    .UseLibRed(_connection, b => b.ApplyConfiguration())
                    .UseInternalServiceProvider(_serviceProvider);

            protected override void OnModelCreating(ModelBuilder modelBuilder)
                => modelBuilder.Entity<Customer>(
                    b =>
                    {
                        b.HasKey(c => c.CustomerID);
                        b.ToTable("Customers");
                    });
        }
    }
}
