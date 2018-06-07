using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EntityFrameworkCore.Jet;
using Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EntityFramework.Jet.FunctionalTests
{
    public class JetDatabaseCreationTest
    {
        [Fact]
        public async Task Exists_returns_false_when_database_doesnt_exist()
        {
            await Exists_returns_false_when_database_doesnt_exist_test(async: false);
        }

        [Fact]
        public async Task ExistsAsync_returns_false_when_database_doesnt_exist()
        {
            await Exists_returns_false_when_database_doesnt_exist_test(async: true);
        }

        private static async Task Exists_returns_false_when_database_doesnt_exist_test(bool async)
        {
            using (var testDatabase = await JetTestStore.CreateScratchAsync(createDatabase: false))
            {
                using (var context = new BloggingContext(testDatabase))
                {
                    var creator = context.GetService<IRelationalDatabaseCreator>();

                    Assert.False(async ? await creator.ExistsAsync() : creator.Exists());

                    Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
                }
            }
        }

        [Fact]
        public async Task Exists_returns_true_when_database_exists()
        {
            await Exists_returns_true_when_database_exists_test(async: false);
        }

        [Fact]
        public async Task ExistsAsync_returns_true_when_database_exists()
        {
            await Exists_returns_true_when_database_exists_test(async: true);
        }

        private static async Task Exists_returns_true_when_database_exists_test(bool async)
        {
            using (var testDatabase = await JetTestStore.CreateScratchAsync(createDatabase: true))
            {
                using (var context = new BloggingContext(testDatabase))
                {
                    var creator = context.GetService<IRelationalDatabaseCreator>();

                    Assert.True(async ? await creator.ExistsAsync() : creator.Exists());

                    Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
                }
            }
        }

        [Fact]
        public async Task EnsureDeleted_will_delete_database()
        {
            await EnsureDeleted_will_delete_database_test(async: false, openConnection: false);
        }

        [Fact]
        public async Task EnsureDeletedAsync_will_delete_database()
        {
            await EnsureDeleted_will_delete_database_test(async: true, openConnection: false);
        }

        [Fact]
        public async Task EnsureDeleted_will_not_delete_database_with_opened_connections()
        {
            await EnsureDeleted_will_delete_database_test(async: false, openConnection: true);
        }

        [Fact]
        public async Task EnsureDeletedAsync_will_not_delete_database_with_opened_connections()
        {
            await EnsureDeleted_will_delete_database_test(async: true, openConnection: true);
        }

        private static async Task EnsureDeleted_will_delete_database_test(bool async, bool openConnection)
        {
            using (var testDatabase = await JetTestStore.CreateScratchAsync(createDatabase: true))
            {
                if (!openConnection)
                {
                    testDatabase.Close();
                }

                using (var context = new BloggingContext(testDatabase))
                {
                    var creator = context.GetService<IRelationalDatabaseCreator>();

                    Assert.True(async ? await creator.ExistsAsync() : creator.Exists());

                    if (openConnection)
                    {

                        Assert.Throws<IOException>(() => context.Database.EnsureDeleted());
                    }
                    else
                    {
                        if (async)
                        {
                            Assert.True(await context.Database.EnsureDeletedAsync());
                        }
                        else
                        {
                            Assert.True(context.Database.EnsureDeleted());
                        }

                        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);

                        Assert.False(async ? await creator.ExistsAsync() : creator.Exists());

                        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
                    }
                }
            }
        }

        [Fact]
        public async Task EnsuredDeleted_noop_when_database_doesnt_exist()
        {
            await EnsuredDeleted_noop_when_database_doesnt_exist_test(async: false);
        }

        [Fact]
        public async Task EnsuredDeletedAsync_noop_when_database_doesnt_exist()
        {
            await EnsuredDeleted_noop_when_database_doesnt_exist_test(async: true);
        }

        private static async Task EnsuredDeleted_noop_when_database_doesnt_exist_test(bool async)
        {
            using (var testDatabase = await JetTestStore.CreateScratchAsync(createDatabase: false))
            {
                using (var context = new BloggingContext(testDatabase))
                {
                    var creator = context.GetService<IRelationalDatabaseCreator>();

                    Assert.False(async ? await creator.ExistsAsync() : creator.Exists());

                    if (async)
                    {
                        Assert.False(await creator.EnsureDeletedAsync());
                    }
                    else
                    {
                        Assert.False(creator.EnsureDeleted());
                    }

                    Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);

                    Assert.False(async ? await creator.ExistsAsync() : creator.Exists());

                    Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
                }
            }
        }

        [Fact]
        public async Task EnsureCreated_can_create_schema_in_existing_database()
        {
            await EnsureCreated_can_create_schema_in_existing_database_test(async: false);
        }

        [Fact]
        public async Task EnsureCreatedAsync_can_create_schema_in_existing_database()
        {
            await EnsureCreated_can_create_schema_in_existing_database_test(async: true);
        }

        private static async Task EnsureCreated_can_create_schema_in_existing_database_test(bool async)
        {
            using (var testDatabase = await JetTestStore.CreateScratchAsync())
            {
                await RunDatabaseCreationTest(testDatabase, async);
            }
        }

        [Fact]
        public async Task EnsureCreated_can_create_physical_database_and_schema()
        {
            await EnsureCreated_can_create_physical_database_and_schema_test(async: false);
        }

        [Fact]
        public async Task EnsureCreatedAsync_can_create_physical_database_and_schema()
        {
            await EnsureCreated_can_create_physical_database_and_schema_test(async: true);
        }

        private static async Task EnsureCreated_can_create_physical_database_and_schema_test(bool async)
        {
            using (var testDatabase = await JetTestStore.CreateScratchAsync(createDatabase: false))
            {
                await RunDatabaseCreationTest(testDatabase, async);
            }
        }

        private static async Task RunDatabaseCreationTest(JetTestStore testStore, bool async)
        {
            using (var context = new BloggingContext(testStore))
            {
                var creator = context.GetService<IRelationalDatabaseCreator>();

                if (async)
                {
                    Assert.True(await creator.EnsureCreatedAsync());
                }
                else
                {
                    Assert.True(creator.EnsureCreated());
                }

                Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);

                if (testStore.State != ConnectionState.Open)
                {
                    await testStore.OpenAsync();
                }

                var tables = testStore.Query<string>("SELECT NAME FROM (SHOW TABLES)");
                Assert.Equal(1, tables.Count());
                Assert.Equal("Blogs", tables.Single());

                var columns = (testStore.Query<string>(
                    "SELECT Table + '.' + Name + ' (' + TypeName + ')' FROM (SHOW TABLECOLUMNS) ORDER BY Table, Name")).ToArray();
                Assert.Equal(14, columns.Length);

            }
        }

        [Fact]
        public async Task EnsuredCreated_is_noop_when_database_exists_and_has_schema()
        {
            await EnsuredCreated_is_noop_when_database_exists_and_has_schema_test(async: false);
        }

        [Fact]
        public async Task EnsuredCreatedAsync_is_noop_when_database_exists_and_has_schema()
        {
            await EnsuredCreated_is_noop_when_database_exists_and_has_schema_test(async: true);
        }

        private static async Task EnsuredCreated_is_noop_when_database_exists_and_has_schema_test(bool async)
        {
            using (var testDatabase = await JetTestStore.CreateScratchAsync(createDatabase: false))
            {
                using (var context = new BloggingContext(testDatabase))
                {
                    context.Database.EnsureCreated();

                    if (async)
                    {
                        Assert.False(await context.Database.EnsureCreatedAsync());
                    }
                    else
                    {
                        Assert.False(context.Database.EnsureCreated());
                    }

                    Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
                }
            }
        }

        private static IServiceProvider CreateServiceProvider()
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection
                .AddEntityFrameworkJet();

            return serviceCollection.BuildServiceProvider();
        }

        private class BloggingContext : DbContext
        {
            private readonly JetTestStore _testStore;

            public BloggingContext(JetTestStore testStore)
            {
                _testStore = testStore;
            }

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            {
                optionsBuilder
                    .UseJet(_testStore.ConnectionString)
                    .UseInternalServiceProvider(CreateServiceProvider());
            }

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<Blog>(b =>
                {
                    b.HasKey(e => new { e.Key1, e.Key2 });
                    b.Property(e => e.AndRow).IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();
                });

            }

            public DbSet<Blog> Blogs { get; set; }
        }

        public class Blog
        {
            public string Key1 { get; set; }
            public byte[] Key2 { get; set; }
            public string Cheese { get; set; }
            public int ErMilan { get; set; }
            public bool George { get; set; }
            public Guid TheGu { get; set; }
            public DateTime NotFigTime { get; set; }
            public byte ToEat { get; set; }
            public double OrNothing { get; set; }
            public short Fuse { get; set; }
            public long WayRound { get; set; }
            public float On { get; set; }
            public byte[] AndChew { get; set; }
            public byte[] AndRow { get; set; }
        }
    }
}
