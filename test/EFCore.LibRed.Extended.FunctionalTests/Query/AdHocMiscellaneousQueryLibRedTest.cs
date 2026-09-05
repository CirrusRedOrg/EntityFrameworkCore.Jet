// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// ReSharper disable InconsistentNaming

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using EntityFrameworkCore.LibRed.Extended.FunctionalTests.TestUtilities;
using EntityFrameworkCore.LibRed.Infrastructure;
using EntityFrameworkCore.LibRed.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.EntityFrameworkCore.TestUtilities;
using NetTopologySuite.Geometries;
using Xunit;

namespace EntityFrameworkCore.LibRed.Extended.FunctionalTests.Query;

#nullable disable

public class AdHocMiscellaneousQueryLibRedTest(NonSharedFixture fixture) : AdHocMiscellaneousQueryRelationalTestBase(fixture)
{
    protected override ITestStoreFactory NonSharedTestStoreFactory
        => LibRedTestStoreFactory.Instance;

    protected override DbContextOptionsBuilder AddNonSharedOptions(DbContextOptionsBuilder builder)
        => base.AddNonSharedOptions(builder)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.OwnedEntityMappedToJsonCollectionWarning));

    protected override DbContextOptionsBuilder SetParameterizedCollectionMode(
        DbContextOptionsBuilder optionsBuilder,
        ParameterTranslationMode parameterizedCollectionMode)
    {
        new LibRedDbContextOptionsBuilder(optionsBuilder).UseParameterizedCollectionMode(parameterizedCollectionMode);

        return optionsBuilder;
    }

    protected override Task Seed2951(Context2951 context)
        => context.Database.ExecuteSqlRawAsync(
            """
CREATE TABLE ZeroKey (Id int);
INSERT INTO ZeroKey VALUES (NULL)
""");

    protected override async Task Seed30915(Context30915 context)
    {
        context.Statuses.AddRange(
            new Context30915.PickupStatus30915 { PickupStatusId = 1, Name = "Active" },
            new Context30915.PickupStatus30915 { PickupStatusId = 2, Name = "NoRequests" },
            new Context30915.PickupStatus30915 { PickupStatusId = 3, Name = "Busy" });

        context.Requests.AddRange(
            new Context30915.PickupRequest30915 { PickupStatusId = 1, Priority = 5 },
            new Context30915.PickupRequest30915 { PickupStatusId = 1, Priority = null },
            new Context30915.PickupRequest30915 { PickupStatusId = 3, Priority = 7 });

        await context.SaveChangesAsync();
    }

    #region 5456

    [Fact]
    public virtual async Task Include_group_join_is_per_query_context()
    {
        var contextFactory = await InitializeNonSharedTest<Context5456>(
            seed: c => c.SeedAsync(),
            createTestStore: () => LibRedTestStore.Create(NonSharedStoreName));

        Parallel.For(
            0, 10, i =>
            {
                using var ctx = contextFactory.CreateDbContext();
                var result = ctx.Posts.Where(x => x.Blog.Id > 1).Include(x => x.Blog).ToList();

                Assert.Equal(198, result.Count);
            });

        Parallel.For(
            0, 10, i =>
            {
                using var ctx = contextFactory.CreateDbContext();
                var result = ctx.Posts.Where(x => x.Blog.Id > 1).Include(x => x.Blog).Include(x => x.Comments).ToList();

                Assert.Equal(198, result.Count);
            });

        Parallel.For(
            0, 10, i =>
            {
                using var ctx = contextFactory.CreateDbContext();
                var result = ctx.Posts.Where(x => x.Blog.Id > 1).Include(x => x.Blog).ThenInclude(b => b.Author).ToList();

                Assert.Equal(198, result.Count);
            });
    }

    [Fact]
    public virtual async Task Include_group_join_is_per_query_context_async()
    {
        var contextFactory = await InitializeNonSharedTest<Context5456>(
            seed: c => c.SeedAsync(),
            createTestStore: () => LibRedTestStore.Create(NonSharedStoreName));

        await Parallel.ForAsync(
            0, 10, async (i, ct) =>
            {
                using var ctx = contextFactory.CreateDbContext();
                var result = await ctx.Posts.Where(x => x.Blog.Id > 1).Include(x => x.Blog).ToListAsync();

                Assert.Equal(198, result.Count);
            });

        await Parallel.ForAsync(
            0, 10, async (i, ct) =>
            {
                using var ctx = contextFactory.CreateDbContext();
                var result = await ctx.Posts.Where(x => x.Blog.Id > 1).Include(x => x.Blog).Include(x => x.Comments)
                    .ToListAsync();

                Assert.Equal(198, result.Count);
            });

        await Parallel.ForAsync(
            0, 10, async (i, ct) =>
            {
                using var ctx = contextFactory.CreateDbContext();
                var result = await ctx.Posts.Where(x => x.Blog.Id > 1).Include(x => x.Blog).ThenInclude(b => b.Author)
                    .ToListAsync();

                Assert.Equal(198, result.Count);
            });
    }

    private class Context5456(DbContextOptions options) : DbContext(options)
    {
        public DbSet<Blog> Blogs { get; set; }
        public DbSet<Post> Posts { get; set; }
        public DbSet<Comment> Comments { get; set; }
        public DbSet<Author> Authors { get; set; }

        public Task SeedAsync()
        {
            for (var i = 0; i < 100; i++)
            {
                Add(
                    new Blog { Posts = [new Post { Comments = [new Comment(), new Comment()] }, new Post()], Author = new Author() });
            }

            return SaveChangesAsync();
        }

        public class Blog
        {
            public int Id { get; set; }
            public List<Post> Posts { get; set; }
            public Author Author { get; set; }
        }

        public class Author
        {
            public int Id { get; set; }
            public List<Blog> Blogs { get; set; }
        }

        public class Post
        {
            public int Id { get; set; }
            public Blog Blog { get; set; }
            public List<Comment> Comments { get; set; }
        }

        public class Comment
        {
            public int Id { get; set; }
            public Post Blog { get; set; }
        }
    }

    #endregion

    #region 8864

    [Fact]
    public virtual async Task Select_nested_projection()
    {
        var contextFactory = await InitializeNonSharedTest<Context8864>(seed: c => c.SeedAsync());

        using (var context = contextFactory.CreateDbContext())
        {
            var customers = context.Customers
                .Select(c => new { Customer = c, CustomerAgain = Context8864.Get(context, c.Id) })
                .ToList();

            Assert.Equal(2, customers.Count);

            foreach (var customer in customers)
            {
                Assert.Same(customer.Customer, customer.CustomerAgain);
            }
        }

        AssertSql(
            """
SELECT `c`.`Id`, `c`.`Name`
FROM `Customers` AS `c`
""",
            //
            """
@id='1'

SELECT TOP 2 `c`.`Id`, `c`.`Name`
FROM `Customers` AS `c`
WHERE `c`.`Id` = @id
""",
            //
            """
@id='2'

SELECT TOP 2 `c`.`Id`, `c`.`Name`
FROM `Customers` AS `c`
WHERE `c`.`Id` = @id
""");
    }

    private class Context8864(DbContextOptions options) : DbContext(options)
    {
        public DbSet<Customer> Customers { get; set; }

        public Task SeedAsync()
        {
            AddRange(
                new Customer { Name = "Alan" },
                new Customer { Name = "Elon" });

            return SaveChangesAsync();
        }

        public static Customer Get(Context8864 context, int id)
            => context.Customers.Single(c => c.Id == id);

        public class Customer
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }
    }

    #endregion
    
    #region 12518

    [Fact]
    public virtual async Task Projecting_entity_with_value_converter_and_include_works()
    {
        var contextFactory = await InitializeNonSharedTest<Context12518>(seed: c => c.SeedAsync());
        using var context = contextFactory.CreateDbContext();
        var result = context.Parents.Include(p => p.Child).OrderBy(e => e.Id).FirstOrDefault();

        AssertSql(
            """
SELECT TOP 1 `p`.`Id`, `p`.`ChildId`, `c`.`Id`, `c`.`ParentId`, `c`.`ULongRowVersion`
FROM `Parents` AS `p`
LEFT JOIN `Children` AS `c` ON `p`.`ChildId` = `c`.`Id`
ORDER BY `p`.`Id`
""");
    }

    [Fact]
    public virtual async Task Projecting_column_with_value_converter_of_ulong_byte_array()
    {
        var contextFactory = await InitializeNonSharedTest<Context12518>(seed: c => c.SeedAsync());
        using var context = contextFactory.CreateDbContext();
        var result = context.Parents.OrderBy(e => e.Id).Select(p => (ulong?)p.Child.ULongRowVersion).FirstOrDefault();

        AssertSql(
            """
SELECT TOP 1 `c`.`ULongRowVersion`
FROM `Parents` AS `p`
LEFT JOIN `Children` AS `c` ON `p`.`ChildId` = `c`.`Id`
ORDER BY `p`.`Id`
""");
    }

    protected class Context12518(DbContextOptions options) : DbContext(options)
    {
        public virtual DbSet<Parent12518> Parents { get; set; }
        public virtual DbSet<Child12518> Children { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var child = modelBuilder.Entity<Child12518>();
            child.HasOne(_ => _.Parent)
                .WithOne(_ => _.Child)
                .HasForeignKey<Parent12518>(_ => _.ChildId);
            child.Property(x => x.ULongRowVersion)
                .HasConversion(new NumberToBytesConverter<ulong>())
                .IsRowVersion()
                .IsRequired()
                .HasColumnType("varbinary(8)");

            modelBuilder.Entity<Parent12518>();
        }

        public Task SeedAsync()
        {
            Parents.Add(new Parent12518());
            return SaveChangesAsync();
        }

        public class Parent12518
        {
            public Guid Id { get; set; } = Guid.NewGuid();
            public Guid? ChildId { get; set; }
            public Child12518 Child { get; set; }
        }

        public class Child12518
        {
            public Guid Id { get; set; } = Guid.NewGuid();
            public ulong ULongRowVersion { get; set; }
            public Guid ParentId { get; set; }
            public Parent12518 Parent { get; set; }
        }
    }

    #endregion

    #region 13118

    [Fact]
    public virtual async Task DateTime_Contains_with_smalldatetime_generates_correct_literal()
    {
        var contextFactory = await InitializeNonSharedTest<Context13118>(seed: c => c.SeedAsync());
        using var context = contextFactory.CreateDbContext();
        var testDateList = new List<DateTime> { new(2018, 10, 07) };
        var findRecordsWithDateInList = context.ReproEntity
            .Where(a => testDateList.Contains(a.MyTime))
            .ToList();

        Assert.Single(findRecordsWithDateInList);

        AssertSql(
            """
@testDateList1='2018-10-07T00:00:00.0000000' (DbType = DateTime)

SELECT `r`.`Id`, `r`.`MyTime`
FROM `ReproEntity` AS `r`
WHERE `r`.`MyTime` = @testDateList1
""");
    }

    private class Context13118(DbContextOptions options) : DbContext(options)
    {
        public virtual DbSet<ReproEntity13118> ReproEntity { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<ReproEntity13118>(e => e.Property("MyTime").HasColumnType("smalldatetime"));

        public Task SeedAsync()
        {
            AddRange(
                new ReproEntity13118 { MyTime = new DateTime(2018, 10, 07) },
                new ReproEntity13118 { MyTime = new DateTime(2018, 10, 08) });

            return SaveChangesAsync();
        }
    }

    private class ReproEntity13118
    {
        public Guid Id { get; set; }
        public DateTime MyTime { get; set; }
    }

    #endregion

    #region 14095

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Where_equals_DateTime_Now(bool async)
    {
        var contextFactory = await InitializeNonSharedTest<Context14095>(seed: c => c.SeedAsync());

        using var context = contextFactory.CreateDbContext();
        var query = context.Dates.Where(
            d => d.DateTime2_2 == DateTime.Now
                || d.DateTime2_7 == DateTime.Now
                || d.DateTime == DateTime.Now
                || d.SmallDateTime == DateTime.Now);

        var results = async
            ? await query.ToListAsync()
            : [.. query];

        Assert.Empty(results);

        AssertSql(
            """
SELECT `d`.`Id`, `d`.`DateTime`, `d`.`DateTime2`, `d`.`DateTime2_0`, `d`.`DateTime2_1`, `d`.`DateTime2_2`, `d`.`DateTime2_3`, `d`.`DateTime2_4`, `d`.`DateTime2_5`, `d`.`DateTime2_6`, `d`.`DateTime2_7`, `d`.`SmallDateTime`
FROM `Dates` AS `d`
WHERE `d`.`DateTime2_2` = NOW() OR `d`.`DateTime2_7` = NOW() OR `d`.`DateTime` = NOW() OR `d`.`SmallDateTime` = NOW()
""");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Where_not_equals_DateTime_Now(bool async)
    {
        var contextFactory = await InitializeNonSharedTest<Context14095>(seed: c => c.SeedAsync());

        using var context = contextFactory.CreateDbContext();
        var query = context.Dates.Where(d => d.DateTime2_2 != DateTime.Now
                                             && d.DateTime2_7 != DateTime.Now
                                             && d.DateTime != DateTime.Now
                                             && d.SmallDateTime != DateTime.Now);

        var results = async
            ? await query.ToListAsync()
            : query.ToList();

        Assert.Single(results);

        AssertSql(
            """
SELECT `d`.`Id`, `d`.`DateTime`, `d`.`DateTime2`, `d`.`DateTime2_0`, `d`.`DateTime2_1`, `d`.`DateTime2_2`, `d`.`DateTime2_3`, `d`.`DateTime2_4`, `d`.`DateTime2_5`, `d`.`DateTime2_6`, `d`.`DateTime2_7`, `d`.`SmallDateTime`
FROM `Dates` AS `d`
WHERE `d`.`DateTime2_2` <> NOW() AND `d`.`DateTime2_7` <> NOW() AND `d`.`DateTime` <> NOW() AND `d`.`SmallDateTime` <> NOW()
""");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Where_equals_new_DateTime(bool async)
    {
        var contextFactory = await InitializeNonSharedTest<Context14095>(seed: c => c.SeedAsync());

        using var context = contextFactory.CreateDbContext();
        var query = context.Dates.Where(
            d => d.SmallDateTime == new DateTime(1970, 9, 3, 12, 0, 0)
                && d.DateTime == new DateTime(1971, 9, 3, 12, 0, 10, 220)
                && d.DateTime2 == new DateTime(1972, 9, 3, 12, 0, 10, 333)
                && d.DateTime2_0 == new DateTime(1973, 9, 3, 12, 0, 10)
                && d.DateTime2_1 == new DateTime(1974, 9, 3, 12, 0, 10, 500)
                && d.DateTime2_2 == new DateTime(1975, 9, 3, 12, 0, 10, 660)
                && d.DateTime2_3 == new DateTime(1976, 9, 3, 12, 0, 10, 777)
                && d.DateTime2_4 == new DateTime(1977, 9, 3, 12, 0, 10, 888)
                && d.DateTime2_5 == new DateTime(1978, 9, 3, 12, 0, 10, 999)
                && d.DateTime2_6 == new DateTime(1979, 9, 3, 12, 0, 10, 111)
                && d.DateTime2_7 == new DateTime(1980, 9, 3, 12, 0, 10, 222));

        var results = async
            ? await query.ToListAsync()
            : [.. query];

        Assert.Single((IEnumerable)results);

        AssertSql(
            """
SELECT `d`.`Id`, `d`.`DateTime`, `d`.`DateTime2`, `d`.`DateTime2_0`, `d`.`DateTime2_1`, `d`.`DateTime2_2`, `d`.`DateTime2_3`, `d`.`DateTime2_4`, `d`.`DateTime2_5`, `d`.`DateTime2_6`, `d`.`DateTime2_7`, `d`.`SmallDateTime`
FROM `Dates` AS `d`
WHERE `d`.`SmallDateTime` = #1970-09-03 12:00:00# AND `d`.`DateTime` = #1971-09-03 12:00:10.220# AND `d`.`DateTime2` = #1972-09-03 12:00:10.333# AND `d`.`DateTime2_0` = #1973-09-03 12:00:10# AND `d`.`DateTime2_1` = #1974-09-03 12:00:10.500# AND `d`.`DateTime2_2` = #1975-09-03 12:00:10.660# AND `d`.`DateTime2_3` = #1976-09-03 12:00:10.777# AND `d`.`DateTime2_4` = #1977-09-03 12:00:10.888# AND `d`.`DateTime2_5` = #1978-09-03 12:00:10.999# AND `d`.`DateTime2_6` = #1979-09-03 12:00:10.111# AND `d`.`DateTime2_7` = #1980-09-03 12:00:10.222#
""");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Where_contains_DateTime_literals(bool async)
    {
        var dateTimes = new[]
        {
            new DateTime(1970, 9, 3, 12, 0, 0),
            new DateTime(1971, 9, 3, 12, 0, 10, 220),
            new DateTime(1972, 9, 3, 12, 0, 10, 333),
            new DateTime(1973, 9, 3, 12, 0, 10),
            new DateTime(1974, 9, 3, 12, 0, 10, 500),
            new DateTime(1975, 9, 3, 12, 0, 10, 660),
            new DateTime(1976, 9, 3, 12, 0, 10, 777),
            new DateTime(1977, 9, 3, 12, 0, 10, 888),
            new DateTime(1978, 9, 3, 12, 0, 10, 999),
            new DateTime(1979, 9, 3, 12, 0, 10, 111),
            new DateTime(1980, 9, 3, 12, 0, 10, 222)
        };

        var contextFactory = await InitializeNonSharedTest<Context14095>(seed: c => c.SeedAsync());

        using var context = contextFactory.CreateDbContext();
        var query = context.Dates.Where(
            d => dateTimes.Contains(d.SmallDateTime)
                && dateTimes.Contains(d.DateTime)
                && dateTimes.Contains(d.DateTime2)
                && dateTimes.Contains(d.DateTime2_0)
                && dateTimes.Contains(d.DateTime2_1)
                && dateTimes.Contains(d.DateTime2_2)
                && dateTimes.Contains(d.DateTime2_3)
                && dateTimes.Contains(d.DateTime2_4)
                && dateTimes.Contains(d.DateTime2_5)
                && dateTimes.Contains(d.DateTime2_6)
                && dateTimes.Contains(d.DateTime2_7));

        var results = async
            ? await query.ToListAsync()
            : [.. query];

        Assert.Single((IEnumerable)results);

        AssertSql(
            """
@dateTimes1='1970-09-03T12:00:00.0000000' (DbType = DateTime)
@dateTimes2='1971-09-03T12:00:10.2200000' (DbType = DateTime)
@dateTimes3='1972-09-03T12:00:10.3330000' (DbType = DateTime)
@dateTimes4='1973-09-03T12:00:10.0000000' (DbType = DateTime)
@dateTimes5='1974-09-03T12:00:10.5000000' (DbType = DateTime)
@dateTimes6='1975-09-03T12:00:10.6600000' (DbType = DateTime)
@dateTimes7='1976-09-03T12:00:10.7770000' (DbType = DateTime)
@dateTimes8='1977-09-03T12:00:10.8880000' (DbType = DateTime)
@dateTimes9='1978-09-03T12:00:10.9990000' (DbType = DateTime)
@dateTimes10='1979-09-03T12:00:10.1110000' (DbType = DateTime)
@dateTimes11='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes12='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes13='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes14='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes15='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes16='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes17='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes18='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes19='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes20='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes21='1970-09-03T12:00:00.0000000' (DbType = DateTime)
@dateTimes22='1971-09-03T12:00:10.2200000' (DbType = DateTime)
@dateTimes23='1972-09-03T12:00:10.3330000' (DbType = DateTime)
@dateTimes24='1973-09-03T12:00:10.0000000' (DbType = DateTime)
@dateTimes25='1974-09-03T12:00:10.5000000' (DbType = DateTime)
@dateTimes26='1975-09-03T12:00:10.6600000' (DbType = DateTime)
@dateTimes27='1976-09-03T12:00:10.7770000' (DbType = DateTime)
@dateTimes28='1977-09-03T12:00:10.8880000' (DbType = DateTime)
@dateTimes29='1978-09-03T12:00:10.9990000' (DbType = DateTime)
@dateTimes30='1979-09-03T12:00:10.1110000' (DbType = DateTime)
@dateTimes31='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes32='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes33='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes34='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes35='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes36='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes37='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes38='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes39='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes40='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes41='1970-09-03T12:00:00.0000000' (DbType = DateTime)
@dateTimes42='1971-09-03T12:00:10.2200000' (DbType = DateTime)
@dateTimes43='1972-09-03T12:00:10.3330000' (DbType = DateTime)
@dateTimes44='1973-09-03T12:00:10.0000000' (DbType = DateTime)
@dateTimes45='1974-09-03T12:00:10.5000000' (DbType = DateTime)
@dateTimes46='1975-09-03T12:00:10.6600000' (DbType = DateTime)
@dateTimes47='1976-09-03T12:00:10.7770000' (DbType = DateTime)
@dateTimes48='1977-09-03T12:00:10.8880000' (DbType = DateTime)
@dateTimes49='1978-09-03T12:00:10.9990000' (DbType = DateTime)
@dateTimes50='1979-09-03T12:00:10.1110000' (DbType = DateTime)
@dateTimes51='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes52='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes53='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes54='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes55='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes56='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes57='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes58='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes59='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes60='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes61='1970-09-03T12:00:00.0000000' (DbType = DateTime)
@dateTimes62='1971-09-03T12:00:10.2200000' (DbType = DateTime)
@dateTimes63='1972-09-03T12:00:10.3330000' (DbType = DateTime)
@dateTimes64='1973-09-03T12:00:10.0000000' (DbType = DateTime)
@dateTimes65='1974-09-03T12:00:10.5000000' (DbType = DateTime)
@dateTimes66='1975-09-03T12:00:10.6600000' (DbType = DateTime)
@dateTimes67='1976-09-03T12:00:10.7770000' (DbType = DateTime)
@dateTimes68='1977-09-03T12:00:10.8880000' (DbType = DateTime)
@dateTimes69='1978-09-03T12:00:10.9990000' (DbType = DateTime)
@dateTimes70='1979-09-03T12:00:10.1110000' (DbType = DateTime)
@dateTimes71='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes72='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes73='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes74='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes75='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes76='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes77='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes78='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes79='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes80='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes81='1970-09-03T12:00:00.0000000' (DbType = DateTime)
@dateTimes82='1971-09-03T12:00:10.2200000' (DbType = DateTime)
@dateTimes83='1972-09-03T12:00:10.3330000' (DbType = DateTime)
@dateTimes84='1973-09-03T12:00:10.0000000' (DbType = DateTime)
@dateTimes85='1974-09-03T12:00:10.5000000' (DbType = DateTime)
@dateTimes86='1975-09-03T12:00:10.6600000' (DbType = DateTime)
@dateTimes87='1976-09-03T12:00:10.7770000' (DbType = DateTime)
@dateTimes88='1977-09-03T12:00:10.8880000' (DbType = DateTime)
@dateTimes89='1978-09-03T12:00:10.9990000' (DbType = DateTime)
@dateTimes90='1979-09-03T12:00:10.1110000' (DbType = DateTime)
@dateTimes91='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes92='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes93='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes94='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes95='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes96='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes97='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes98='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes99='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes100='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes101='1970-09-03T12:00:00.0000000' (DbType = DateTime)
@dateTimes102='1971-09-03T12:00:10.2200000' (DbType = DateTime)
@dateTimes103='1972-09-03T12:00:10.3330000' (DbType = DateTime)
@dateTimes104='1973-09-03T12:00:10.0000000' (DbType = DateTime)
@dateTimes105='1974-09-03T12:00:10.5000000' (DbType = DateTime)
@dateTimes106='1975-09-03T12:00:10.6600000' (DbType = DateTime)
@dateTimes107='1976-09-03T12:00:10.7770000' (DbType = DateTime)
@dateTimes108='1977-09-03T12:00:10.8880000' (DbType = DateTime)
@dateTimes109='1978-09-03T12:00:10.9990000' (DbType = DateTime)
@dateTimes110='1979-09-03T12:00:10.1110000' (DbType = DateTime)
@dateTimes111='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes112='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes113='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes114='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes115='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes116='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes117='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes118='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes119='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes120='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes121='1970-09-03T12:00:00.0000000' (DbType = DateTime)
@dateTimes122='1971-09-03T12:00:10.2200000' (DbType = DateTime)
@dateTimes123='1972-09-03T12:00:10.3330000' (DbType = DateTime)
@dateTimes124='1973-09-03T12:00:10.0000000' (DbType = DateTime)
@dateTimes125='1974-09-03T12:00:10.5000000' (DbType = DateTime)
@dateTimes126='1975-09-03T12:00:10.6600000' (DbType = DateTime)
@dateTimes127='1976-09-03T12:00:10.7770000' (DbType = DateTime)
@dateTimes128='1977-09-03T12:00:10.8880000' (DbType = DateTime)
@dateTimes129='1978-09-03T12:00:10.9990000' (DbType = DateTime)
@dateTimes130='1979-09-03T12:00:10.1110000' (DbType = DateTime)
@dateTimes131='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes132='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes133='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes134='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes135='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes136='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes137='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes138='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes139='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes140='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes141='1970-09-03T12:00:00.0000000' (DbType = DateTime)
@dateTimes142='1971-09-03T12:00:10.2200000' (DbType = DateTime)
@dateTimes143='1972-09-03T12:00:10.3330000' (DbType = DateTime)
@dateTimes144='1973-09-03T12:00:10.0000000' (DbType = DateTime)
@dateTimes145='1974-09-03T12:00:10.5000000' (DbType = DateTime)
@dateTimes146='1975-09-03T12:00:10.6600000' (DbType = DateTime)
@dateTimes147='1976-09-03T12:00:10.7770000' (DbType = DateTime)
@dateTimes148='1977-09-03T12:00:10.8880000' (DbType = DateTime)
@dateTimes149='1978-09-03T12:00:10.9990000' (DbType = DateTime)
@dateTimes150='1979-09-03T12:00:10.1110000' (DbType = DateTime)
@dateTimes151='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes152='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes153='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes154='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes155='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes156='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes157='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes158='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes159='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes160='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes161='1970-09-03T12:00:00.0000000' (DbType = DateTime)
@dateTimes162='1971-09-03T12:00:10.2200000' (DbType = DateTime)
@dateTimes163='1972-09-03T12:00:10.3330000' (DbType = DateTime)
@dateTimes164='1973-09-03T12:00:10.0000000' (DbType = DateTime)
@dateTimes165='1974-09-03T12:00:10.5000000' (DbType = DateTime)
@dateTimes166='1975-09-03T12:00:10.6600000' (DbType = DateTime)
@dateTimes167='1976-09-03T12:00:10.7770000' (DbType = DateTime)
@dateTimes168='1977-09-03T12:00:10.8880000' (DbType = DateTime)
@dateTimes169='1978-09-03T12:00:10.9990000' (DbType = DateTime)
@dateTimes170='1979-09-03T12:00:10.1110000' (DbType = DateTime)
@dateTimes171='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes172='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes173='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes174='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes175='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes176='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes177='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes178='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes179='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes180='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes181='1970-09-03T12:00:00.0000000' (DbType = DateTime)
@dateTimes182='1971-09-03T12:00:10.2200000' (DbType = DateTime)
@dateTimes183='1972-09-03T12:00:10.3330000' (DbType = DateTime)
@dateTimes184='1973-09-03T12:00:10.0000000' (DbType = DateTime)
@dateTimes185='1974-09-03T12:00:10.5000000' (DbType = DateTime)
@dateTimes186='1975-09-03T12:00:10.6600000' (DbType = DateTime)
@dateTimes187='1976-09-03T12:00:10.7770000' (DbType = DateTime)
@dateTimes188='1977-09-03T12:00:10.8880000' (DbType = DateTime)
@dateTimes189='1978-09-03T12:00:10.9990000' (DbType = DateTime)
@dateTimes190='1979-09-03T12:00:10.1110000' (DbType = DateTime)
@dateTimes191='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes192='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes193='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes194='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes195='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes196='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes197='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes198='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes199='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes200='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes201='1970-09-03T12:00:00.0000000' (DbType = DateTime)
@dateTimes202='1971-09-03T12:00:10.2200000' (DbType = DateTime)
@dateTimes203='1972-09-03T12:00:10.3330000' (DbType = DateTime)
@dateTimes204='1973-09-03T12:00:10.0000000' (DbType = DateTime)
@dateTimes205='1974-09-03T12:00:10.5000000' (DbType = DateTime)
@dateTimes206='1975-09-03T12:00:10.6600000' (DbType = DateTime)
@dateTimes207='1976-09-03T12:00:10.7770000' (DbType = DateTime)
@dateTimes208='1977-09-03T12:00:10.8880000' (DbType = DateTime)
@dateTimes209='1978-09-03T12:00:10.9990000' (DbType = DateTime)
@dateTimes210='1979-09-03T12:00:10.1110000' (DbType = DateTime)
@dateTimes211='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes212='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes213='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes214='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes215='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes216='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes217='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes218='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes219='1980-09-03T12:00:10.2220000' (DbType = DateTime)
@dateTimes220='1980-09-03T12:00:10.2220000' (DbType = DateTime)

SELECT `d`.`Id`, `d`.`DateTime`, `d`.`DateTime2`, `d`.`DateTime2_0`, `d`.`DateTime2_1`, `d`.`DateTime2_2`, `d`.`DateTime2_3`, `d`.`DateTime2_4`, `d`.`DateTime2_5`, `d`.`DateTime2_6`, `d`.`DateTime2_7`, `d`.`SmallDateTime`
FROM `Dates` AS `d`
WHERE `d`.`SmallDateTime` IN (@dateTimes1, @dateTimes2, @dateTimes3, @dateTimes4, @dateTimes5, @dateTimes6, @dateTimes7, @dateTimes8, @dateTimes9, @dateTimes10, @dateTimes11, @dateTimes12, @dateTimes13, @dateTimes14, @dateTimes15, @dateTimes16, @dateTimes17, @dateTimes18, @dateTimes19, @dateTimes20) AND `d`.`DateTime` IN (@dateTimes21, @dateTimes22, @dateTimes23, @dateTimes24, @dateTimes25, @dateTimes26, @dateTimes27, @dateTimes28, @dateTimes29, @dateTimes30, @dateTimes31, @dateTimes32, @dateTimes33, @dateTimes34, @dateTimes35, @dateTimes36, @dateTimes37, @dateTimes38, @dateTimes39, @dateTimes40) AND `d`.`DateTime2` IN (@dateTimes41, @dateTimes42, @dateTimes43, @dateTimes44, @dateTimes45, @dateTimes46, @dateTimes47, @dateTimes48, @dateTimes49, @dateTimes50, @dateTimes51, @dateTimes52, @dateTimes53, @dateTimes54, @dateTimes55, @dateTimes56, @dateTimes57, @dateTimes58, @dateTimes59, @dateTimes60) AND `d`.`DateTime2_0` IN (@dateTimes61, @dateTimes62, @dateTimes63, @dateTimes64, @dateTimes65, @dateTimes66, @dateTimes67, @dateTimes68, @dateTimes69, @dateTimes70, @dateTimes71, @dateTimes72, @dateTimes73, @dateTimes74, @dateTimes75, @dateTimes76, @dateTimes77, @dateTimes78, @dateTimes79, @dateTimes80) AND `d`.`DateTime2_1` IN (@dateTimes81, @dateTimes82, @dateTimes83, @dateTimes84, @dateTimes85, @dateTimes86, @dateTimes87, @dateTimes88, @dateTimes89, @dateTimes90, @dateTimes91, @dateTimes92, @dateTimes93, @dateTimes94, @dateTimes95, @dateTimes96, @dateTimes97, @dateTimes98, @dateTimes99, @dateTimes100) AND `d`.`DateTime2_2` IN (@dateTimes101, @dateTimes102, @dateTimes103, @dateTimes104, @dateTimes105, @dateTimes106, @dateTimes107, @dateTimes108, @dateTimes109, @dateTimes110, @dateTimes111, @dateTimes112, @dateTimes113, @dateTimes114, @dateTimes115, @dateTimes116, @dateTimes117, @dateTimes118, @dateTimes119, @dateTimes120) AND `d`.`DateTime2_3` IN (@dateTimes121, @dateTimes122, @dateTimes123, @dateTimes124, @dateTimes125, @dateTimes126, @dateTimes127, @dateTimes128, @dateTimes129, @dateTimes130, @dateTimes131, @dateTimes132, @dateTimes133, @dateTimes134, @dateTimes135, @dateTimes136, @dateTimes137, @dateTimes138, @dateTimes139, @dateTimes140) AND `d`.`DateTime2_4` IN (@dateTimes141, @dateTimes142, @dateTimes143, @dateTimes144, @dateTimes145, @dateTimes146, @dateTimes147, @dateTimes148, @dateTimes149, @dateTimes150, @dateTimes151, @dateTimes152, @dateTimes153, @dateTimes154, @dateTimes155, @dateTimes156, @dateTimes157, @dateTimes158, @dateTimes159, @dateTimes160) AND `d`.`DateTime2_5` IN (@dateTimes161, @dateTimes162, @dateTimes163, @dateTimes164, @dateTimes165, @dateTimes166, @dateTimes167, @dateTimes168, @dateTimes169, @dateTimes170, @dateTimes171, @dateTimes172, @dateTimes173, @dateTimes174, @dateTimes175, @dateTimes176, @dateTimes177, @dateTimes178, @dateTimes179, @dateTimes180) AND `d`.`DateTime2_6` IN (@dateTimes181, @dateTimes182, @dateTimes183, @dateTimes184, @dateTimes185, @dateTimes186, @dateTimes187, @dateTimes188, @dateTimes189, @dateTimes190, @dateTimes191, @dateTimes192, @dateTimes193, @dateTimes194, @dateTimes195, @dateTimes196, @dateTimes197, @dateTimes198, @dateTimes199, @dateTimes200) AND `d`.`DateTime2_7` IN (@dateTimes201, @dateTimes202, @dateTimes203, @dateTimes204, @dateTimes205, @dateTimes206, @dateTimes207, @dateTimes208, @dateTimes209, @dateTimes210, @dateTimes211, @dateTimes212, @dateTimes213, @dateTimes214, @dateTimes215, @dateTimes216, @dateTimes217, @dateTimes218, @dateTimes219, @dateTimes220)
""");
    }

    protected class Context14095(DbContextOptions options) : DbContext(options)
    {
        public DbSet<DatesAndPrunes14095> Dates { get; set; }

        public Task SeedAsync()
        {
            Add(
                new DatesAndPrunes14095
                {
                    SmallDateTime = new DateTime(1970, 9, 3, 12, 0, 0),
                    DateTime = new DateTime(1971, 9, 3, 12, 0, 10, 220),
                    DateTime2 = new DateTime(1972, 9, 3, 12, 0, 10, 333),
                    DateTime2_0 = new DateTime(1973, 9, 3, 12, 0, 10),
                    DateTime2_1 = new DateTime(1974, 9, 3, 12, 0, 10, 500),
                    DateTime2_2 = new DateTime(1975, 9, 3, 12, 0, 10, 660),
                    DateTime2_3 = new DateTime(1976, 9, 3, 12, 0, 10, 777),
                    DateTime2_4 = new DateTime(1977, 9, 3, 12, 0, 10, 888),
                    DateTime2_5 = new DateTime(1978, 9, 3, 12, 0, 10, 999),
                    DateTime2_6 = new DateTime(1979, 9, 3, 12, 0, 10, 111),
                    DateTime2_7 = new DateTime(1980, 9, 3, 12, 0, 10, 222)
                });
            return SaveChangesAsync();
        }

        public class DatesAndPrunes14095
        {
            public int Id { get; set; }

            [Column(TypeName = "smalldatetime")]
            public DateTime SmallDateTime { get; set; }

            [Column(TypeName = "datetime")]
            public DateTime DateTime { get; set; }

            [Column(TypeName = "datetime2")]
            public DateTime DateTime2 { get; set; }

            [Column(TypeName = "datetime2(0)")]
            public DateTime DateTime2_0 { get; set; }

            [Column(TypeName = "datetime2(1)")]
            public DateTime DateTime2_1 { get; set; }

            [Column(TypeName = "datetime2(2)")]
            public DateTime DateTime2_2 { get; set; }

            [Column(TypeName = "datetime2(3)")]
            public DateTime DateTime2_3 { get; set; }

            [Column(TypeName = "datetime2(4)")]
            public DateTime DateTime2_4 { get; set; }

            [Column(TypeName = "datetime2(5)")]
            public DateTime DateTime2_5 { get; set; }

            [Column(TypeName = "datetime2(6)")]
            public DateTime DateTime2_6 { get; set; }

            [Column(TypeName = "datetime2(7)")]
            public DateTime DateTime2_7 { get; set; }
        }
    }

    #endregion

    #region 15518

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public virtual async Task Nested_queries_does_not_cause_concurrency_exception_sync(bool tracking)
    {
        var contextFactory = await InitializeNonSharedTest<Context15518>(seed: c => c.SeedAsync());

        using (var context = contextFactory.CreateDbContext())
        {
            var query = context.Repos.OrderBy(r => r.Id).Where(r => r.Id > 0);
            query = tracking ? query.AsTracking() : query.AsNoTracking();

            foreach (var a in query)
            {
                foreach (var b in query)
                {
                }
            }
        }

        using (var context = contextFactory.CreateDbContext())
        {
            var query = context.Repos.OrderBy(r => r.Id).Where(r => r.Id > 0);
            query = tracking ? query.AsTracking() : query.AsNoTracking();

            await foreach (var a in query.AsAsyncEnumerable())
            {
                await foreach (var b in query.AsAsyncEnumerable())
                {
                }
            }
        }

        AssertSql(
            """
SELECT `r`.`Id`, `r`.`Name`
FROM `Repos` AS `r`
WHERE `r`.`Id` > 0
ORDER BY `r`.`Id`
""",
            //
            """
SELECT `r`.`Id`, `r`.`Name`
FROM `Repos` AS `r`
WHERE `r`.`Id` > 0
ORDER BY `r`.`Id`
""",
            //
            """
SELECT `r`.`Id`, `r`.`Name`
FROM `Repos` AS `r`
WHERE `r`.`Id` > 0
ORDER BY `r`.`Id`
""",
            //
            """
SELECT `r`.`Id`, `r`.`Name`
FROM `Repos` AS `r`
WHERE `r`.`Id` > 0
ORDER BY `r`.`Id`
""",
            //
            """
SELECT `r`.`Id`, `r`.`Name`
FROM `Repos` AS `r`
WHERE `r`.`Id` > 0
ORDER BY `r`.`Id`
""",
            //
            """
SELECT `r`.`Id`, `r`.`Name`
FROM `Repos` AS `r`
WHERE `r`.`Id` > 0
ORDER BY `r`.`Id`
""");
    }

    private class Context15518(DbContextOptions options) : DbContext(options)
    {
        public DbSet<Repo> Repos { get; set; }

        public Task SeedAsync()
        {
            AddRange(
                new Repo { Name = "London" },
                new Repo { Name = "New York" });

            return SaveChangesAsync();
        }

        public class Repo
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }
    }

    #endregion

    #region 19206

    /*[Fact]
    public virtual async Task From_sql_expression_compares_correctly()
    {
        var contextFactory = await InitializeAsync<Context19206>(seed: c => c.SeedAsync());

        using (var context = contextFactory.CreateContext())
        {
            var query = from t1 in context.Tests.FromSqlInterpolated(
                            $"Select * from Tests Where Type = {Context19206.TestType19206.Unit}")
                        from t2 in context.Tests.FromSqlInterpolated(
                            $"Select * from Tests Where Type = {Context19206.TestType19206.Integration}")
                        select new { t1, t2 };

            var result = query.ToList();

            var item = Assert.Single((IEnumerable)result);
            Assert.Equal(Context19206.TestType19206.Unit, item.t1.Type);
            Assert.Equal(Context19206.TestType19206.Integration, item.t2.Type);

            AssertSql(
                """
p0='0'
p1='1'

SELECT [m].[Id], [m].[Type], [m0].[Id], [m0].[Type]
FROM (
    Select * from Tests Where Type = @p0
) AS [m]
CROSS JOIN (
    Select * from Tests Where Type = @p1
) AS [m0]
""");
        }
    }*/

    private class Context19206(DbContextOptions options) : DbContext(options)
    {
        public DbSet<Test> Tests { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
        }

        public Task SeedAsync()
        {
            Add(new Test { Type = TestType19206.Unit });
            Add(new Test { Type = TestType19206.Integration });
            return SaveChangesAsync();
        }

        public class Test
        {
            public int Id { get; set; }
            public TestType19206 Type { get; set; }
        }

        public enum TestType19206
        {
            Unit,
            Integration,
        }
    }

    #endregion

    #region 21666

    [Fact]
    public virtual async Task Thread_safety_in_relational_command_cache()
    {
        var contextFactory = await InitializeNonSharedTest<Context21666>(
            onConfiguring: options => ((IDbContextOptionsBuilderInfrastructure)options).AddOrUpdateExtension(
                options.Options.FindExtension<LibRedOptionsExtension>()
                    .WithConnection(null)
                    .WithConnectionString(LibRedTestStore.CreateConnectionString(NonSharedStoreName))));

        var ids = new[] { 1, 2, 3 };

        Parallel.For(
            0, 100,
            i =>
            {
                using var context = contextFactory.CreateDbContext();
                var query = context.Lists.Where(l => !l.IsDeleted && ids.Contains(l.Id)).ToList();
            });
    }

    private class Context21666(DbContextOptions options) : DbContext(options)
    {
        public DbSet<List> Lists { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
        }

        public class List
        {
            public int Id { get; set; }
            public bool IsDeleted { get; set; }
        }
    }

    #endregion

    #region 27427

    /*[Theory]
    [MemberData(nameof(IsAsyncData))]
    public virtual async Task Muliple_occurrences_of_FromSql_in_group_by_aggregate(bool async)
    {
        var contextFactory = await InitializeAsync<Context27427>();
        using var context = contextFactory.CreateContext();
        var query = context.DemoEntities
            .FromSqlRaw("SELECT * FROM DemoEntities WHERE Id = {0}", new SqlParameter { Value = 1 })
            .Select(e => e.Id);

        var query2 = context.DemoEntities
            .Where(e => query.Contains(e.Id))
            .GroupBy(e => e.Id)
            .Select(g => new { g.Key, Aggregate = g.Count() });

        if (async)
        {
            await query2.ToListAsync();
        }
        else
        {
            query2.ToList();
        }

        AssertSql(
            """
p0='1'

SELECT [d].[Id] AS [Key], COUNT(*) AS [Aggregate]
FROM [DemoEntities] AS [d]
WHERE [d].[Id] IN (
    SELECT [m].[Id]
    FROM (
        SELECT * FROM DemoEntities WHERE Id = @p0
    ) AS [m]
)
GROUP BY [d].[Id]
""");
    }*/

    protected class Context27427(DbContextOptions options) : DbContext(options)
    {
        public DbSet<DemoEntity> DemoEntities { get; set; }
    }

    protected class DemoEntity
    {
        public int Id { get; set; }
    }

    #endregion

    public override async Task First_FirstOrDefault_ix_async()
    {
        await base.First_FirstOrDefault_ix_async();
        //Dont test sql. parameter p0 is a bit flaky at times
        /*AssertSql(
            """
SELECT TOP 1 `p`.`Id`, `p`.`Name`
FROM `Products` AS `p`
ORDER BY `p`.`Id`
""",
            //
            """
@p0='1'

DELETE FROM `Products`
WHERE `Id` = @p0;
SELECT @@ROWCOUNT;
""",
            //
            """
@p0='Product 1' (Size = 255)

INSERT INTO `Products` (`Name`)
VALUES (@p0);
SELECT `Id`
FROM `Products`
WHERE @@ROWCOUNT = 1 AND `Id` = @@identity;
""",
            //
            """
SELECT TOP 1 `p`.`Id`, `p`.`Name`
FROM `Products` AS `p`
ORDER BY `p`.`Id`
""",
            //
            """
@p0='2'

DELETE FROM `Products`
WHERE `Id` = @p0;
SELECT @@ROWCOUNT;
""");*/
    }

    public override async Task Discriminator_type_is_handled_correctly()
    {
        await base.Discriminator_type_is_handled_correctly();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`Discriminator`, `p`.`Name`
FROM `Products` AS `p`
WHERE `p`.`Discriminator` = 1
""",
            //
            """
SELECT `p`.`Id`, `p`.`Discriminator`, `p`.`Name`
FROM `Products` AS `p`
WHERE `p`.`Discriminator` = 1
""");
    }

    public override async Task New_instances_in_projection_are_not_shared_across_results()
    {
        await base.New_instances_in_projection_are_not_shared_across_results();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`BlogId`, `p`.`Title`
FROM `Posts` AS `p`
""");
    }

    public override async Task Enum_has_flag_applies_explicit_cast_for_constant()
    {
        var contextFactory = await InitializeNonSharedTest<Context8538>(seed: c => c.SeedAsync());
        //Context8538.Permission is Int64 and Context8538.Permission.READ_WRITE is 36 bit and jet/ace cant do maths on bigint/decimal

        /*using (var context = contextFactory.CreateDbContext())
        {
            var query = context.Entities.Where(e => e.Permission.HasFlag(Context8538.Permission.READ_WRITE)).ToList();
            Assert.Single(query);
        }*/

        using (var context = contextFactory.CreateDbContext())
        {
            var query = context.Entities.Where(e => e.PermissionShort.HasFlag(Context8538.PermissionShort.READ_WRITE)).ToList();
            Assert.Single(query);
        }

        AssertSql(
            """
SELECT `e`.`Id`, `e`.`Permission`, `e`.`PermissionByte`, `e`.`PermissionShort`
FROM `Entities` AS `e`
WHERE (`e`.`PermissionShort` BAND CINT(4)) = CINT(4)
""");
    }

    public override async Task Enum_has_flag_does_not_apply_explicit_cast_for_non_constant()
    {
        var contextFactory = await InitializeNonSharedTest<Context8538>(seed: c => c.SeedAsync());

        /*using (var context = contextFactory.CreateDbContext())
        {
            var query = context.Entities.Where(e => e.Permission.HasFlag(e.Permission)).ToList();
            Assert.Equal(3, query.Count);
        }*/

        using (var context = contextFactory.CreateDbContext())
        {
            var query = context.Entities.Where(e => e.PermissionByte.HasFlag(e.PermissionByte)).ToList();
            Assert.Equal(3, query.Count);
        }

        AssertSql(
            """
            SELECT `e`.`Id`, `e`.`Permission`, `e`.`PermissionByte`, `e`.`PermissionShort`
            FROM `Entities` AS `e`
            WHERE (`e`.`PermissionByte` BAND `e`.`PermissionByte`) = `e`.`PermissionByte`
            """);
    }

    public override async Task Variable_from_closure_is_parametrized()
    {
        await base.Variable_from_closure_is_parametrized();

        AssertSql(
            """
@id='1'

SELECT `e`.`Id`, `e`.`Name`
FROM `Entities` AS `e`
WHERE `e`.`Id` = @id
""",
            //
            """
@id='2'

SELECT `e`.`Id`, `e`.`Name`
FROM `Entities` AS `e`
WHERE `e`.`Id` = @id
""",
            //
            """
@id='1'

SELECT `e`.`Id`, `e`.`Name`
FROM `Entities` AS `e`
WHERE `e`.`Id` = @id
""",
            //
            """
@id='2'

SELECT `e`.`Id`, `e`.`Name`
FROM `Entities` AS `e`
WHERE `e`.`Id` = @id
""",
            //
            """
@id='1'

SELECT `e`.`Id`, `e`.`Name`
FROM `Entities` AS `e`
WHERE `e`.`Id` IN (
    SELECT `e0`.`Id`
    FROM `Entities` AS `e0`
    WHERE `e0`.`Id` = @id
)
""",
            //
            """
@id='2'

SELECT `e`.`Id`, `e`.`Name`
FROM `Entities` AS `e`
WHERE `e`.`Id` IN (
    SELECT `e0`.`Id`
    FROM `Entities` AS `e0`
    WHERE `e0`.`Id` = @id
)
""");
    }

    public override async Task Relational_command_cache_creates_new_entry_when_parameter_nullability_changes()
    {
        await base.Relational_command_cache_creates_new_entry_when_parameter_nullability_changes();

        AssertSql(
            """
@name='A' (Size = 255)

SELECT `e`.`Id`, `e`.`Name`
FROM `Entities` AS `e`
WHERE `e`.`Name` = @name
""",
            //
            """
SELECT `e`.`Id`, `e`.`Name`
FROM `Entities` AS `e`
WHERE `e`.`Name` IS NULL
""");
    }

    public override async Task Query_cache_entries_are_evicted_as_necessary()
    {
        await base.Query_cache_entries_are_evicted_as_necessary();

        AssertSql();
    }

    public override async Task Explicitly_compiled_query_does_not_add_cache_entry()
    {
        await base.Explicitly_compiled_query_does_not_add_cache_entry();

        AssertSql(
            """
SELECT TOP 2 `e`.`Id`, `e`.`Name`
FROM `Entities` AS `e`
WHERE `e`.`Id` = 1
""");
    }

    public override async Task Conditional_expression_with_conditions_does_not_collapse_if_nullable_bool()
    {
        await base.Conditional_expression_with_conditions_does_not_collapse_if_nullable_bool();

        AssertSql(
            """
SELECT CASE
    WHEN `c0`.`Id` IS NOT NULL THEN NOT (`c0`.`Processed`)
END AS `Processing`
FROM `Carts` AS `c`
LEFT JOIN `Configuration` AS `c0` ON `c`.`ConfigurationId` = `c0`.`Id`
""");
    }

    public override async Task QueryBuffer_requirement_is_computed_when_querying_base_type_while_derived_type_has_shadow_prop()
    {
        await base.QueryBuffer_requirement_is_computed_when_querying_base_type_while_derived_type_has_shadow_prop();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`IsTwo`, `b`.`MoreStuffId`
FROM `Bases` AS `b`
""");
    }

    public override async Task Average_with_cast()
    {
        await base.Average_with_cast();

        AssertSql(
            """
SELECT `p`.`Id`, `p`.`DecimalColumn`, `p`.`DoubleColumn`, `p`.`FloatColumn`, `p`.`IntColumn`, `p`.`LongColumn`, `p`.`NullableDecimalColumn`, `p`.`NullableDoubleColumn`, `p`.`NullableFloatColumn`, `p`.`NullableIntColumn`, `p`.`NullableLongColumn`, `p`.`Price`
FROM `Prices` AS `p`
""",
            //
            """
SELECT AVG(`p`.`Price`)
FROM `Prices` AS `p`
""",
            //
            """
SELECT AVG(CDBL(`p`.`IntColumn`))
FROM `Prices` AS `p`
""",
            //
            """
SELECT AVG(CDBL(`p`.`NullableIntColumn`))
FROM `Prices` AS `p`
""",
            //
            """
SELECT AVG(CDBL(`p`.`LongColumn`))
FROM `Prices` AS `p`
""",
            //
            """
SELECT AVG(CDBL(`p`.`NullableLongColumn`))
FROM `Prices` AS `p`
""",
            //
            """
SELECT CSNG(AVG(`p`.`FloatColumn`))
FROM `Prices` AS `p`
""",
            //
            """
SELECT CSNG(AVG(`p`.`NullableFloatColumn`))
FROM `Prices` AS `p`
""",
            //
            """
SELECT AVG(`p`.`DoubleColumn`)
FROM `Prices` AS `p`
""",
            //
            """
SELECT AVG(`p`.`NullableDoubleColumn`)
FROM `Prices` AS `p`
""",
            //
            """
SELECT AVG(`p`.`DecimalColumn`)
FROM `Prices` AS `p`
""",
            //
            """
SELECT AVG(`p`.`NullableDecimalColumn`)
FROM `Prices` AS `p`
""");
    }

    public override async Task Parameterless_ctor_on_inner_DTO_gets_called_for_every_row()
    {
        await base.Parameterless_ctor_on_inner_DTO_gets_called_for_every_row();

        AssertSql(
            """
SELECT `e`.`Id`, `e`.`Name`
FROM `Entities` AS `e`
""");
    }

    public override async Task Union_and_insert_works_correctly_together()
    {
        await base.Union_and_insert_works_correctly_together();

        AssertSql(
            """
@id1='1'
@id2='2'

SELECT `t`.`Id`
FROM `Tables1` AS `t`
WHERE `t`.`Id` = @id1
UNION
SELECT `t0`.`Id`
FROM `Tables2` AS `t0`
WHERE `t0`.`Id` = @id2
""",
            //
            """
INSERT INTO `Tables1`
DEFAULT VALUES;
SELECT `Id`
FROM `Tables1`
WHERE @@ROWCOUNT = 1 AND `Id` = @@identity;
""",
            //
            """
INSERT INTO `Tables1`
DEFAULT VALUES;
SELECT `Id`
FROM `Tables1`
WHERE @@ROWCOUNT = 1 AND `Id` = @@identity;
""",
            //
            """
INSERT INTO `Tables2`
DEFAULT VALUES;
SELECT `Id`
FROM `Tables2`
WHERE @@ROWCOUNT = 1 AND `Id` = @@identity;
""",
            //
            """
INSERT INTO `Tables2`
DEFAULT VALUES;
SELECT `Id`
FROM `Tables2`
WHERE @@ROWCOUNT = 1 AND `Id` = @@identity;
""");
    }

    public override async Task Repeated_parameters_in_generated_query_sql()
    {
        await base.Repeated_parameters_in_generated_query_sql();

        AssertSql(
            """
@k='1'

SELECT TOP 1 `a`.`Id`, `a`.`Name`
FROM `Autos` AS `a`
WHERE `a`.`Id` = @k
""",
            //
            """
@p='2'

SELECT TOP 1 `a`.`Id`, `a`.`Name`
FROM `Autos` AS `a`
WHERE `a`.`Id` = @p
""",
            //
            """
@entity_equality_a_Id='1' (Nullable = true)
@entity_equality_b_Id='2' (Nullable = true)

SELECT `e`.`Id`, `e`.`AnotherAutoId`, `e`.`AutoId`
FROM `EqualAutos` AS `e`
LEFT JOIN `Autos` AS `a` ON `e`.`AutoId` = `a`.`Id`
LEFT JOIN `Autos` AS `a0` ON `e`.`AnotherAutoId` = `a0`.`Id`
WHERE (`a`.`Id` = @entity_equality_a_Id AND `a0`.`Id` = @entity_equality_b_Id) OR (`a`.`Id` = @entity_equality_b_Id AND `a0`.`Id` = @entity_equality_a_Id)
""");
    }

    public override async Task Operators_combine_nullability_of_entity_shapers()
    {
        await base.Operators_combine_nullability_of_entity_shapers();

        AssertSql(
            """
SELECT `a`.`Id`, `a`.`a`, `a`.`a1`, `a`.`forkey`, `b`.`Id` AS `Id0`, `b`.`b`, `b`.`b1`, `b`.`forkey` AS `forkey0`
FROM `As` AS `a`
LEFT JOIN `Bs` AS `b` ON `a`.`forkey` = `b`.`forkey`
UNION ALL
SELECT `a0`.`Id`, `a0`.`a`, `a0`.`a1`, `a0`.`forkey`, `b0`.`Id` AS `Id0`, `b0`.`b`, `b0`.`b1`, `b0`.`forkey` AS `forkey0`
FROM `Bs` AS `b0`
LEFT JOIN `As` AS `a0` ON `b0`.`forkey` = `a0`.`forkey`
WHERE `a0`.`Id` IS NULL
""",
            //
            """
SELECT `a`.`Id`, `a`.`a`, `a`.`a1`, `a`.`forkey`, `b`.`Id` AS `Id0`, `b`.`b`, `b`.`b1`, `b`.`forkey` AS `forkey0`
FROM `As` AS `a`
LEFT JOIN `Bs` AS `b` ON `a`.`forkey` = `b`.`forkey`
UNION
SELECT `a0`.`Id`, `a0`.`a`, `a0`.`a1`, `a0`.`forkey`, `b0`.`Id` AS `Id0`, `b0`.`b`, `b0`.`b1`, `b0`.`forkey` AS `forkey0`
FROM `Bs` AS `b0`
LEFT JOIN `As` AS `a0` ON `b0`.`forkey` = `a0`.`forkey`
WHERE `a0`.`Id` IS NULL
""",
            //
            """
SELECT `a`.`Id`, `a`.`a`, `a`.`a1`, `a`.`forkey`, `b`.`Id` AS `Id0`, `b`.`b`, `b`.`b1`, `b`.`forkey` AS `forkey0`
FROM `As` AS `a`
LEFT JOIN `Bs` AS `b` ON `a`.`forkey` = `b`.`forkey`
EXCEPT
SELECT `a0`.`Id`, `a0`.`a`, `a0`.`a1`, `a0`.`forkey`, `b0`.`Id` AS `Id0`, `b0`.`b`, `b0`.`b1`, `b0`.`forkey` AS `forkey0`
FROM `Bs` AS `b0`
LEFT JOIN `As` AS `a0` ON `b0`.`forkey` = `a0`.`forkey`
""",
            //
            """
SELECT `a`.`Id`, `a`.`a`, `a`.`a1`, `a`.`forkey`, `b`.`Id` AS `Id0`, `b`.`b`, `b`.`b1`, `b`.`forkey` AS `forkey0`
FROM `As` AS `a`
LEFT JOIN `Bs` AS `b` ON `a`.`forkey` = `b`.`forkey`
INTERSECT
SELECT `a0`.`Id`, `a0`.`a`, `a0`.`a1`, `a0`.`forkey`, `b0`.`Id` AS `Id0`, `b0`.`b`, `b0`.`b1`, `b0`.`forkey` AS `forkey0`
FROM `Bs` AS `b0`
LEFT JOIN `As` AS `a0` ON `b0`.`forkey` = `a0`.`forkey`
""");
    }

    public override async Task Shadow_property_with_inheritance()
    {
        await base.Shadow_property_with_inheritance();

        AssertSql(
            """
SELECT `c`.`Id`, `c`.`Discriminator`, `c`.`IsPrimary`, `c`.`UserName`, `c`.`EmployerId`, `c`.`ServiceOperatorId`
FROM `Contacts` AS `c`
""",
            //
            """
SELECT `c`.`Id`, `c`.`Discriminator`, `c`.`IsPrimary`, `c`.`UserName`, `c`.`ServiceOperatorId`, `s`.`Id`
FROM `Contacts` AS `c`
INNER JOIN `ServiceOperators` AS `s` ON `c`.`ServiceOperatorId` = `s`.`Id`
WHERE `c`.`Discriminator` = 'ServiceOperatorContact'
""",
            //
            """
SELECT `c`.`Id`, `c`.`Discriminator`, `c`.`IsPrimary`, `c`.`UserName`, `c`.`ServiceOperatorId`
FROM `Contacts` AS `c`
WHERE `c`.`Discriminator` = 'ServiceOperatorContact'
""");
    }

    public override async Task Inlined_dbcontext_is_not_leaking()
    {
        await base.Inlined_dbcontext_is_not_leaking();

        AssertSql(
            """
SELECT `b`.`Id`
FROM `Blogs` AS `b`
""");
    }

    public override async Task GroupJoin_Anonymous_projection_GroupBy_Aggregate_join_elimination()
    {
        await base.GroupJoin_Anonymous_projection_GroupBy_Aggregate_join_elimination();

        AssertSql(
            """
SELECT `t1`.`AnotherEntity11818_Name` AS `Key`, COUNT(*) + 5 AS `cnt`
FROM `Table` AS `t`
LEFT JOIN (
    SELECT `t0`.`Id`, `t0`.`Exists`, `t0`.`AnotherEntity11818_Name`
    FROM `Table` AS `t0`
    WHERE `t0`.`Exists` IS NOT NULL
) AS `t1` ON `t`.`Id` = CASE
    WHEN `t1`.`Exists` IS NOT NULL THEN `t1`.`Id`
END
GROUP BY `t1`.`AnotherEntity11818_Name`
""",
            //
            """
SELECT `t1`.`AnotherEntity11818_Name` AS `MyKey`, COUNT(*) + 5 AS `cnt`
FROM `Table` AS `t`
LEFT JOIN (
    SELECT `t0`.`Id`, `t0`.`Exists`, `t0`.`AnotherEntity11818_Name`
    FROM `Table` AS `t0`
    WHERE `t0`.`Exists` IS NOT NULL
) AS `t1` ON `t`.`Id` = CASE
    WHEN `t1`.`Exists` IS NOT NULL THEN `t1`.`Id`
END
LEFT JOIN (
    SELECT `t2`.`Id`, `t2`.`MaumarEntity11818_Exists`, `t2`.`MaumarEntity11818_Name`
    FROM `Table` AS `t2`
    WHERE `t2`.`MaumarEntity11818_Exists` IS NOT NULL
) AS `t3` ON `t`.`Id` = CASE
    WHEN `t3`.`MaumarEntity11818_Exists` IS NOT NULL THEN `t3`.`Id`
END
GROUP BY `t1`.`AnotherEntity11818_Name`, `t3`.`MaumarEntity11818_Name`
""",
            //
            """
SELECT TOP 1 `t1`.`AnotherEntity11818_Name` AS `MyKey`, `t3`.`MaumarEntity11818_Name` AS `cnt`
FROM `Table` AS `t`
LEFT JOIN (
    SELECT `t0`.`Id`, `t0`.`Exists`, `t0`.`AnotherEntity11818_Name`
    FROM `Table` AS `t0`
    WHERE `t0`.`Exists` IS NOT NULL
) AS `t1` ON `t`.`Id` = CASE
    WHEN `t1`.`Exists` IS NOT NULL THEN `t1`.`Id`
END
LEFT JOIN (
    SELECT `t2`.`Id`, `t2`.`MaumarEntity11818_Exists`, `t2`.`MaumarEntity11818_Name`
    FROM `Table` AS `t2`
    WHERE `t2`.`MaumarEntity11818_Exists` IS NOT NULL
) AS `t3` ON `t`.`Id` = CASE
    WHEN `t3`.`MaumarEntity11818_Exists` IS NOT NULL THEN `t3`.`Id`
END
GROUP BY `t1`.`AnotherEntity11818_Name`, `t3`.`MaumarEntity11818_Name`
""");
    }

    public override async Task LeftJoin_with_missing_key_values_on_both_sides(bool async)
    {
        await base.LeftJoin_with_missing_key_values_on_both_sides(async);

        AssertSql(
            """
SELECT `c`.`CustomerID`, `c`.`CustomerName`, CASE
    WHEN `p`.`PostcodeID` IS NULL THEN ''
    ELSE `p`.`TownName`
END AS `TownName`, CASE
    WHEN `p`.`PostcodeID` IS NULL THEN ''
    ELSE `p`.`PostcodeValue`
END AS `PostcodeValue`
FROM `Customers` AS `c`
LEFT JOIN `Postcodes` AS `p` ON `c`.`PostcodeID` = `p`.`PostcodeID`
""");
    }

    public override async Task Comparing_enum_casted_to_byte_with_int_parameter(bool async)
    {
        await base.Comparing_enum_casted_to_byte_with_int_parameter(async);

        AssertSql(
            """
@bitterTaste='1'

SELECT `i`.`IceCreamId`, `i`.`Name`, `i`.`Taste`
FROM `IceCreams` AS `i`
WHERE `i`.`Taste` = @bitterTaste
""");
    }

    public override async Task Comparing_enum_casted_to_byte_with_int_constant(bool async)
    {
        await base.Comparing_enum_casted_to_byte_with_int_constant(async);

        AssertSql(
            """
SELECT `i`.`IceCreamId`, `i`.`Name`, `i`.`Taste`
FROM `IceCreams` AS `i`
WHERE `i`.`Taste` = 1
""");
    }

    public override async Task Comparing_byte_column_to_enum_in_vb_creating_double_cast(bool async)
    {
        await base.Comparing_byte_column_to_enum_in_vb_creating_double_cast(async);

        AssertSql(
            """
SELECT `f`.`Id`, `f`.`Taste`
FROM `Foods` AS `f`
WHERE `f`.`Taste` = CBYTE(1)
""");
    }

    public override async Task Null_check_removal_in_ternary_maintain_appropriate_cast(bool async)
    {
        await base.Null_check_removal_in_ternary_maintain_appropriate_cast(async);

        AssertSql(
            """
SELECT `f`.`Taste` AS `Bar`
FROM `Foods` AS `f`
""");
    }

    public override async Task SaveChangesAsync_accepts_changes_with_ConfigureAwait_true()
    {
        await base.SaveChangesAsync_accepts_changes_with_ConfigureAwait_true();

        AssertSql(
            """
INSERT INTO `ObservableThings`
DEFAULT VALUES;
SELECT `Id`
FROM `ObservableThings`
WHERE @@ROWCOUNT = 1 AND `Id` = @@identity;
""");
    }

    public override async Task Bool_discriminator_column_works(bool async)
    {
        await base.Bool_discriminator_column_works(async);

        AssertSql(
            """
SELECT `a`.`Id`, `a`.`BlogId`, `b`.`Id`, `b`.`IsPhotoBlog`, `b`.`Title`, `b`.`NumberOfPhotos`
FROM `Authors` AS `a`
LEFT JOIN `Blog` AS `b` ON `a`.`BlogId` = `b`.`Id`
""");
    }

    public override async Task Multiple_different_entity_type_from_different_namespaces(bool async)
    {
        await base.Multiple_different_entity_type_from_different_namespaces(async);

        AssertSql(
            """
SELECT cast(null as int) AS MyValue
""");
    }

    public override async Task Unwrap_convert_node_over_projection_when_translating_contains_over_subquery(bool async)
    {
        await base.Unwrap_convert_node_over_projection_when_translating_contains_over_subquery(async);

        AssertSql(
            """
@currentUserId='1'

SELECT `u`.`Id` IN (
    SELECT `u0`.`Id`
    FROM `Memberships` AS `m`
    INNER JOIN `Users` AS `u0` ON `m`.`UserId` = `u0`.`Id`
    WHERE `m`.`GroupId` IN (
        SELECT `m0`.`GroupId`
        FROM `Memberships` AS `m0`
        WHERE `m0`.`UserId` = @currentUserId
    )
) AS `HasAccess`
FROM `Users` AS `u`
""");
    }

    public override async Task Unwrap_convert_node_over_projection_when_translating_contains_over_subquery_2(bool async)
    {
        await base.Unwrap_convert_node_over_projection_when_translating_contains_over_subquery_2(async);

        AssertSql(
            """
@currentUserId='1'

SELECT `u`.`Id` IN (
    SELECT `u0`.`Id`
    FROM `Memberships` AS `m`
    INNER JOIN `Groups` AS `g` ON `m`.`GroupId` = `g`.`Id`
    INNER JOIN `Users` AS `u0` ON `m`.`UserId` = `u0`.`Id`
    WHERE `g`.`Id` IN (
        SELECT `g0`.`Id`
        FROM `Memberships` AS `m0`
        INNER JOIN `Groups` AS `g0` ON `m0`.`GroupId` = `g0`.`Id`
        WHERE `m0`.`UserId` = @currentUserId
    )
) AS `HasAccess`
FROM `Users` AS `u`
""");
    }

    public override async Task Unwrap_convert_node_over_projection_when_translating_contains_over_subquery_3(bool async)
    {
        await base.Unwrap_convert_node_over_projection_when_translating_contains_over_subquery_3(async);

        AssertSql(
            """
@currentUserId='1'

SELECT EXISTS (
    SELECT 1
    FROM `Memberships` AS `m`
    INNER JOIN `Users` AS `u0` ON `m`.`UserId` = `u0`.`Id`
    WHERE `m`.`GroupId` IN (
        SELECT `m0`.`GroupId`
        FROM `Memberships` AS `m0`
        WHERE `m0`.`UserId` = @currentUserId
    ) AND `u0`.`Id` = `u`.`Id`) AS `HasAccess`
FROM `Users` AS `u`
""");
    }

    public override async Task GroupBy_aggregate_on_right_side_of_join(bool async)
    {
        await base.GroupBy_aggregate_on_right_side_of_join(async);

        AssertSql(
            """
@orderId='123456'

SELECT `o`.`Id`, `o`.`CancellationDate`, `o`.`OrderId`, `o`.`ShippingDate`
FROM `OrderItems` AS `o`
INNER JOIN (
    SELECT `o0`.`OrderId` AS `Key`
    FROM `OrderItems` AS `o0`
    WHERE `o0`.`OrderId` = @orderId
    GROUP BY `o0`.`OrderId`
) AS `o1` ON `o`.`OrderId` = `o1`.`Key`
WHERE `o`.`OrderId` = @orderId
ORDER BY `o`.`OrderId`
""");
    }

    public override async Task Enum_with_value_converter_matching_take_value(bool async)
    {
        await base.Enum_with_value_converter_matching_take_value(async);

        AssertSql(
            """
@orderItemType='MyType1' (Nullable = false) (Size = 255)
@p='1'

SELECT `o1`.`Id`, COALESCE((
    SELECT TOP 1 `o3`.`Price`
    FROM `OrderItems` AS `o3`
    WHERE `o1`.`Id` = `o3`.`OrderId` AND `o3`.`Type` = @orderItemType), 0.0) AS `SpecialSum`
FROM (
    SELECT TOP @p `o`.`Id`
    FROM `Orders` AS `o`
    WHERE EXISTS (
        SELECT 1
        FROM `OrderItems` AS `o0`
        WHERE `o`.`Id` = `o0`.`OrderId`)
    ORDER BY `o`.`Id`
) AS `o2`
INNER JOIN `Orders` AS `o1` ON `o2`.`Id` = `o1`.`Id`
ORDER BY `o2`.`Id`
""");
    }

    public override async Task GroupBy_Aggregate_over_navigations_repeated(bool async)
    {
        await base.GroupBy_Aggregate_over_navigations_repeated(async);

        AssertSql(
            """
SELECT MIN(`o`.`HourlyRate`) AS `HourlyRate`, MIN(`c`.`Id`) AS `CustomerId`, MIN(`c`.`Name`) AS `CustomerName`
FROM `TimeSheets` AS `t`
LEFT JOIN `Order` AS `o` ON `t`.`OrderId` = `o`.`Id`
INNER JOIN `Project` AS `p` ON `t`.`ProjectId` = `p`.`Id`
INNER JOIN `Customers` AS `c` ON `p`.`CustomerId` = `c`.`Id`
WHERE `t`.`OrderId` IS NOT NULL
GROUP BY `t`.`OrderId`
""");
    }

    public override async Task Aggregate_over_subquery_in_group_by_projection(bool async)
    {
        await base.Aggregate_over_subquery_in_group_by_projection(async);

        AssertSql(
            """
SELECT `o`.`CustomerId`, (
    SELECT MIN(`o0`.`HourlyRate`)
    FROM `Order` AS `o0`
    WHERE `o0`.`CustomerId` = `o`.`CustomerId`) AS `CustomerMinHourlyRate`, MIN(`o`.`HourlyRate`) AS `HourlyRate`, COUNT(*) AS `Count`
FROM `Order` AS `o`
WHERE `o`.`Number` <> 'A1' OR `o`.`Number` IS NULL
GROUP BY `o`.`CustomerId`, `o`.`Number`
""");
    }

    public override async Task Aggregate_over_subquery_in_group_by_projection_2(bool async)
    {
        await base.Aggregate_over_subquery_in_group_by_projection_2(async);

        AssertSql(
            """
SELECT `t`.`Value` AS `A`, (
    SELECT MAX(`t0`.`Id`)
    FROM `Tables` AS `t0`
    WHERE `t0`.`Value` = (MAX(`t`.`Id`) * 6) OR (`t0`.`Value` IS NULL AND MAX(`t`.`Id`) IS NULL)) AS `B`
FROM `Tables` AS `t`
GROUP BY `t`.`Value`
""");
    }

    public override async Task Group_by_aggregate_in_subquery_projection_after_group_by(bool async)
    {
        await base.Group_by_aggregate_in_subquery_projection_after_group_by(async);

        AssertSql(
            """
SELECT `t`.`Value` AS `A`, COALESCE(SUM(`t`.`Id`), 0) AS `B`, COALESCE((
    SELECT TOP 1 COALESCE(SUM(`t`.`Id`), 0) + COALESCE(SUM(`t0`.`Id`), 0)
    FROM `Tables` AS `t0`
    GROUP BY `t0`.`Value`
    ORDER BY 1), 0) AS `C`
FROM `Tables` AS `t`
GROUP BY `t`.`Value`
""");
    }

    public override async Task Subquery_first_member_compared_to_null(bool async)
    {
        await base.Subquery_first_member_compared_to_null(async);

        AssertSql(
    """
SELECT (
    SELECT TOP 1 `c1`.`SomeOtherNullableDateTime`
    FROM `Child` AS `c1`
    WHERE `p`.`Id` = `c1`.`ParentId` AND `c1`.`SomeNullableDateTime` IS NULL
    ORDER BY `c1`.`SomeInteger`)
FROM `Parents` AS `p`
WHERE EXISTS (
    SELECT 1
    FROM `Child` AS `c`
    WHERE `p`.`Id` = `c`.`ParentId` AND `c`.`SomeNullableDateTime` IS NULL) AND (
    SELECT TOP 1 `c0`.`SomeOtherNullableDateTime`
    FROM `Child` AS `c0`
    WHERE `p`.`Id` = `c0`.`ParentId` AND `c0`.`SomeNullableDateTime` IS NULL
    ORDER BY `c0`.`SomeInteger`) IS NOT NULL
""");
    }

    public override async Task SelectMany_where_Select(bool async)
    {
        await base.SelectMany_where_Select(async);

        AssertSql(
            """
SELECT `c1`.`SomeNullableDateTime`
FROM `Parents` AS `p`
INNER JOIN (
    SELECT `c0`.`ParentId`, `c0`.`SomeNullableDateTime`, `c0`.`SomeOtherNullableDateTime`
    FROM (
        SELECT `c`.`ParentId`, `c`.`SomeNullableDateTime`, `c`.`SomeOtherNullableDateTime`, ROW_NUMBER() OVER(PARTITION BY `c`.`ParentId` ORDER BY `c`.`SomeInteger`) AS `row`
        FROM `Child` AS `c`
        WHERE `c`.`SomeNullableDateTime` IS NULL
    ) AS `c0`
    WHERE `c0`.`row` <= 1
) AS `c1` ON `p`.`Id` = `c1`.`ParentId`
WHERE `c1`.`SomeOtherNullableDateTime` IS NOT NULL
""");
    }

    public override async Task Flattened_GroupJoin_on_interface_generic(bool async)
    {
        await base.Flattened_GroupJoin_on_interface_generic(async);

        AssertSql(
    """
SELECT `c`.`Id`, `c`.`ParentId`, `c`.`SomeInteger`, `c`.`SomeNullableDateTime`, `c`.`SomeOtherNullableDateTime`
FROM `Parents` AS `p`
LEFT JOIN `Child` AS `c` ON `p`.`Id` = `c`.`Id`
""");
    }

    public override async Task StoreType_for_UDF_used(bool async)
    {
        await base.StoreType_for_UDF_used(async);

        AssertSql(
            """
@date='2012-12-12T00:00:00.0000000' (DbType = DateTime)

SELECT `m`.`Id`, `m`.`SomeDate`
FROM `MyEntities` AS `m`
WHERE `m`.`SomeDate` = @date
""",
            //
            """
@date='2012-12-12T00:00:00.0000000' (DbType = DateTime)

SELECT `m`.`Id`, `m`.`SomeDate`
FROM `MyEntities` AS `m`
WHERE `dbo`.`ModifyDate`(`m`.`SomeDate`) = @date
""");
    }

    public override async Task Pushdown_does_not_add_grouping_key_to_projection_when_distinct_is_applied(bool async)
    {
        await base.Pushdown_does_not_add_grouping_key_to_projection_when_distinct_is_applied(async);

        AssertSql(
            """
@p='123456'

SELECT TOP @p `t`.`JSON`
FROM `TableDatas` AS `t`
INNER JOIN (
    SELECT DISTINCT `i`.`Parcel`
    FROM `IndexDatas` AS `i`
    WHERE `i`.`Parcel` = 'some condition'
    GROUP BY `i`.`Parcel`, `i`.`RowId`
    HAVING COUNT(*) = 1
) AS `i0` ON `t`.`ParcelNumber` = `i0`.`Parcel`
WHERE `t`.`TableId` = 123
ORDER BY `t`.`ParcelNumber`, `i0`.`Parcel`
""");
    }

    public override async Task Filter_on_nested_DTO_with_interface_gets_simplified_correctly(bool async)
    {
        await base.Filter_on_nested_DTO_with_interface_gets_simplified_correctly(async);

        AssertSql(
            """
SELECT `c`.`Id`, `c`.`CompanyId`, `c0`.`Id` IS NOT NULL, `c0`.`Id`, `c0`.`CompanyName`, `c0`.`CountryId`, `c1`.`Id`, `c1`.`CountryName`
FROM `Customers` AS `c`
LEFT JOIN `Companies` AS `c0` ON `c`.`CompanyId` = `c0`.`Id`
LEFT JOIN `Countries` AS `c1` ON `c0`.`CountryId` = `c1`.`Id`
WHERE CASE
    WHEN `c0`.`Id` IS NOT NULL THEN `c1`.`CountryName`
END = 'COUNTRY'
""");
    }

    public override async Task Check_inlined_constants_redacting(bool async, bool enableSensitiveDataLogging)
    {
        await base.Check_inlined_constants_redacting(async, enableSensitiveDataLogging);

        if (!enableSensitiveDataLogging)
        {
            AssertSql(
                """
SELECT `t`.`Id`, `t`.`Name`
FROM `TestEntities` AS `t`
WHERE `t`.`Id` IN (?, ?, ?)
""",
                //
                """
SELECT `t`.`Id`, `t`.`Name`
FROM `TestEntities` AS `t`
WHERE EXISTS (
    SELECT 1
    FROM (SELECT CLNG(?) AS `Value` UNION ALL VALUES (?), (?)) AS `i`
    WHERE `i`.`Value` = `t`.`Id`)
""",
                //
                """
SELECT `t`.`Id`, `t`.`Name`
FROM `TestEntities` AS `t`
WHERE ? = `t`.`Id`
""");
        }
        else
        {
            AssertSql(
                """
SELECT `t`.`Id`, `t`.`Name`
FROM `TestEntities` AS `t`
WHERE `t`.`Id` IN (1, 2, 3)
""",
                //
                """
SELECT `t`.`Id`, `t`.`Name`
FROM `TestEntities` AS `t`
WHERE EXISTS (
    SELECT 1
    FROM (SELECT CLNG(1) AS `Value` UNION ALL VALUES (2), (3)) AS `i`
    WHERE `i`.`Value` = `t`.`Id`)
""",
                //
                """
SELECT `t`.`Id`, `t`.`Name`
FROM `TestEntities` AS `t`
WHERE 1 = `t`.`Id`
""");
        }
    }

    public override async Task Coalesce_in_conditional_with_value_conversion(bool async)
    {
        await base.Coalesce_in_conditional_with_value_conversion(async);

        AssertSql(
            """
SELECT `d`.`Id`, CASE
    WHEN COALESCE(`d`.`Foo`, CINT(99)) = CINT(10) THEN 'A'
    ELSE 'B'
END AS `Foo`
FROM `Data` AS `d`
ORDER BY `d`.`Id`
""");
    }

    public override async Task Like_on_value_converted_string_column_does_not_produce_cast(bool async)
    {
        await base.Like_on_value_converted_string_column_does_not_produce_cast(async);

        AssertSql(
            """
SELECT `u`.`Id`, `u`.`Name`
FROM `Users` AS `u`
WHERE `u`.`Name` LIKE 'Name%'
""");
    }

    public override async Task Entity_equality_with_Contains_and_Parameter(bool async)
    {
        await base.Entity_equality_with_Contains_and_Parameter(async);

        AssertSql(
            """
@entity_equality_details_Id1='1'
@entity_equality_details_Id2='2'

SELECT `b`.`Id`, `b`.`DetailsId`, `b`.`Name`
FROM `Blogs` AS `b`
LEFT JOIN `BlogDetails` AS `b0` ON `b`.`DetailsId` = `b0`.`Id`
WHERE `b0`.`Id` IN (@entity_equality_details_Id1, @entity_equality_details_Id2)
""");
    }

    #region 30915

    public override async Task Anon_whole_object_GroupJoin_DefaultIfEmpty()
    {
        await base.Anon_whole_object_GroupJoin_DefaultIfEmpty();

        AssertSql(
            """
SELECT `s`.`PickupStatusId`, `r0`.`pickupStatusId`, `r0`.`Count`, `r0`.`marker`
FROM `Statuses` AS `s`
LEFT JOIN (
    SELECT `r`.`PickupStatusId` AS `pickupStatusId`, COUNT(*) AS `Count`, 1 AS `marker`
    FROM `Requests` AS `r`
    GROUP BY `r`.`PickupStatusId`
) AS `r0` ON `s`.`PickupStatusId` = `r0`.`pickupStatusId`
ORDER BY `s`.`PickupStatusId`, `r0`.`pickupStatusId`
""");
    }

    public override async Task Anon_whole_object_LeftJoin_operator()
    {
        await base.Anon_whole_object_LeftJoin_operator();

        AssertSql(
            """
SELECT `s`.`PickupStatusId`, `r0`.`pickupStatusId`, `r0`.`Count`, `r0`.`marker`
FROM `Statuses` AS `s`
LEFT JOIN (
    SELECT `r`.`PickupStatusId` AS `pickupStatusId`, COUNT(*) AS `Count`, 1 AS `marker`
    FROM `Requests` AS `r`
    GROUP BY `r`.`PickupStatusId`
) AS `r0` ON `s`.`PickupStatusId` = `r0`.`pickupStatusId`
ORDER BY `s`.`PickupStatusId`, `r0`.`pickupStatusId`
""");
    }

    public override async Task Anon_client_null_check_GroupJoin()
    {
        await base.Anon_client_null_check_GroupJoin();

        AssertSql(
            """
SELECT `s`.`PickupStatusId`, `r0`.`pickupStatusId`, `r0`.`Count`, `r0`.`marker`
FROM `Statuses` AS `s`
LEFT JOIN (
    SELECT `r`.`PickupStatusId` AS `pickupStatusId`, COUNT(*) AS `Count`, 1 AS `marker`
    FROM `Requests` AS `r`
    GROUP BY `r`.`PickupStatusId`
) AS `r0` ON `s`.`PickupStatusId` = `r0`.`pickupStatusId`
ORDER BY `s`.`PickupStatusId`, `r0`.`pickupStatusId`
""");
    }

    public override async Task Anon_client_null_check_LeftJoin_operator()
    {
        await base.Anon_client_null_check_LeftJoin_operator();

        AssertSql(
            """
SELECT `s`.`PickupStatusId`, `r0`.`pickupStatusId`, `r0`.`Count`, `r0`.`marker`
FROM `Statuses` AS `s`
LEFT JOIN (
    SELECT `r`.`PickupStatusId` AS `pickupStatusId`, COUNT(*) AS `Count`, 1 AS `marker`
    FROM `Requests` AS `r`
    GROUP BY `r`.`PickupStatusId`
) AS `r0` ON `s`.`PickupStatusId` = `r0`.`pickupStatusId`
ORDER BY `s`.`PickupStatusId`, `r0`.`pickupStatusId`
""");
    }

    public override async Task Anon_member_only_nullable_cast()
    {
        await base.Anon_member_only_nullable_cast();

        AssertSql(
            """
SELECT `s`.`PickupStatusId`, `r0`.`Count`
FROM `Statuses` AS `s`
LEFT JOIN (
    SELECT `r`.`PickupStatusId` AS `pickupStatusId`, COUNT(*) AS `Count`
    FROM `Requests` AS `r`
    GROUP BY `r`.`PickupStatusId`
) AS `r0` ON `s`.`PickupStatusId` = `r0`.`pickupStatusId`
ORDER BY `s`.`PickupStatusId`
""");
    }

    public override async Task Dto_memberinit_whole_object_LeftJoin()
    {
        await base.Dto_memberinit_whole_object_LeftJoin();

        AssertSql(
            """
SELECT `s`.`PickupStatusId`, `r0`.`PickupStatusId`, `r0`.`Count`, `r0`.`marker`
FROM `Statuses` AS `s`
LEFT JOIN (
    SELECT `r`.`PickupStatusId`, COUNT(*) AS `Count`, 1 AS `marker`
    FROM `Requests` AS `r`
    GROUP BY `r`.`PickupStatusId`
) AS `r0` ON `s`.`PickupStatusId` = `r0`.`PickupStatusId`
ORDER BY `s`.`PickupStatusId`, `r0`.`PickupStatusId`
""");
    }

    public override async Task Nested_anon_whole_object()
    {
        await base.Nested_anon_whole_object();

        AssertSql(
            """
SELECT `s`.`PickupStatusId`, `r0`.`pickupStatusId`, `r0`.`Count`, `r0`.`marker`
FROM `Statuses` AS `s`
LEFT JOIN (
    SELECT `r`.`PickupStatusId` AS `pickupStatusId`, COUNT(*) AS `Count`, 1 AS `marker`
    FROM `Requests` AS `r`
    GROUP BY `r`.`PickupStatusId`
) AS `r0` ON `s`.`PickupStatusId` = `r0`.`pickupStatusId`
ORDER BY `s`.`PickupStatusId`, `r0`.`pickupStatusId`
""");
    }

    public override async Task Distinct_after_join_member()
    {
        await base.Distinct_after_join_member();

        AssertSql(
            """
SELECT DISTINCT `s`.`PickupStatusId`, `r0`.`pickupStatusId`, `r0`.`Count`, `r0`.`marker`
FROM `Statuses` AS `s`
LEFT JOIN (
    SELECT `r`.`PickupStatusId` AS `pickupStatusId`, COUNT(*) AS `Count`, 1 AS `marker`
    FROM `Requests` AS `r`
    GROUP BY `r`.`PickupStatusId`
) AS `r0` ON `s`.`PickupStatusId` = `r0`.`pickupStatusId`
""");
    }

    public override async Task Take_after_join_whole_object()
    {
        await base.Take_after_join_whole_object();

        AssertSql(
            """
@p='10'

SELECT TOP @p `s`.`PickupStatusId`, `r0`.`pickupStatusId`, `r0`.`Count`, `r0`.`marker`
FROM `Statuses` AS `s`
LEFT JOIN (
    SELECT `r`.`PickupStatusId` AS `pickupStatusId`, COUNT(*) AS `Count`, 1 AS `marker`
    FROM `Requests` AS `r`
    GROUP BY `r`.`PickupStatusId`
) AS `r0` ON `s`.`PickupStatusId` = `r0`.`pickupStatusId`
ORDER BY `s`.`PickupStatusId`, `r0`.`pickupStatusId`
""");
    }

    public override async Task Projected_object_with_nullable_member()
    {
        await base.Projected_object_with_nullable_member();

        AssertSql(
            """
SELECT `s`.`PickupStatusId`, `r0`.`pickupStatusId`, `r0`.`MaxPriority`, `r0`.`marker`
FROM `Statuses` AS `s`
LEFT JOIN (
    SELECT `r`.`PickupStatusId` AS `pickupStatusId`, MAX(`r`.`Priority`) AS `MaxPriority`, 1 AS `marker`
    FROM `Requests` AS `r`
    GROUP BY `r`.`PickupStatusId`
) AS `r0` ON `s`.`PickupStatusId` = `r0`.`pickupStatusId`
ORDER BY `s`.`PickupStatusId`, `r0`.`pickupStatusId`
""");
    }

    public override async Task Projected_object_with_string_member()
    {
        await base.Projected_object_with_string_member();

        AssertSql(
            """
SELECT `s`.`PickupStatusId`, `r0`.`pickupStatusId`, `r0`.`Count`, `r0`.`Name`, `r0`.`marker`
FROM `Statuses` AS `s`
LEFT JOIN (
    SELECT `r`.`PickupStatusId` AS `pickupStatusId`, COUNT(*) AS `Count`, 'cat' AS `Name`, 1 AS `marker`
    FROM `Requests` AS `r`
    GROUP BY `r`.`PickupStatusId`
) AS `r0` ON `s`.`PickupStatusId` = `r0`.`pickupStatusId`
ORDER BY `s`.`PickupStatusId`, `r0`.`pickupStatusId`
""");
    }

    public override async Task Projected_object_all_nullable_members()
    {
        await base.Projected_object_all_nullable_members();

        AssertSql(
            """
SELECT `s`.`PickupStatusId`, `r0`.`pickupStatusId`, `r0`.`MaxPriority`, `r0`.`marker`
FROM `Statuses` AS `s`
LEFT JOIN (
    SELECT `r`.`PickupStatusId` AS `pickupStatusId`, MAX(`r`.`Priority`) AS `MaxPriority`, 1 AS `marker`
    FROM `Requests` AS `r`
    GROUP BY `r`.`PickupStatusId`
) AS `r0` ON `s`.`PickupStatusId` = `r0`.`pickupStatusId`
ORDER BY `s`.`PickupStatusId`, `r0`.`pickupStatusId`
""");
    }

    public override async Task Matched_row_with_null_aggregate_keeps_object_non_null()
    {
        await base.Matched_row_with_null_aggregate_keeps_object_non_null();

        AssertSql(
            """
SELECT `s`.`PickupStatusId`, `r0`.`pickupStatusId`, `r0`.`MaxPriority`, `r0`.`marker`
FROM `Statuses` AS `s`
LEFT JOIN (
    SELECT `r`.`PickupStatusId` AS `pickupStatusId`, MAX(`r`.`Priority`) AS `MaxPriority`, 1 AS `marker`
    FROM `Requests` AS `r`
    GROUP BY `r`.`PickupStatusId`
) AS `r0` ON `s`.`PickupStatusId` = `r0`.`pickupStatusId`
ORDER BY `s`.`PickupStatusId`, `r0`.`pickupStatusId`
""");
    }

    public override async Task Bare_whole_object_projection_is_null_on_no_match()
    {
        await base.Bare_whole_object_projection_is_null_on_no_match();

        AssertSql(
            """
SELECT `r0`.`pickupStatusId`, `r0`.`Count`, `r0`.`marker`
FROM `Statuses` AS `s`
LEFT JOIN (
    SELECT `r`.`PickupStatusId` AS `pickupStatusId`, COUNT(*) AS `Count`, 1 AS `marker`
    FROM `Requests` AS `r`
    GROUP BY `r`.`PickupStatusId`
) AS `r0` ON `s`.`PickupStatusId` = `r0`.`pickupStatusId`
ORDER BY `s`.`PickupStatusId`, `r0`.`pickupStatusId`
""");
    }

    public override async Task User_member_named_marker_does_not_collide_with_synthetic_marker()
    {
        await base.User_member_named_marker_does_not_collide_with_synthetic_marker();

        AssertSql(
            """
SELECT `s`.`PickupStatusId`, `r0`.`pickupStatusId`, `r0`.`marker`, `r0`.`marker0` AS `marker`
FROM `Statuses` AS `s`
LEFT JOIN (
    SELECT `r`.`PickupStatusId` AS `pickupStatusId`, COUNT(*) AS `marker`, 1 AS `marker0`
    FROM `Requests` AS `r`
    GROUP BY `r`.`PickupStatusId`
) AS `r0` ON `s`.`PickupStatusId` = `r0`.`pickupStatusId`
ORDER BY `s`.`PickupStatusId`, `r0`.`pickupStatusId`
""");
    }

    public override async Task Anon_whole_object_GroupJoin_DefaultIfEmpty_sync()
    {
        await base.Anon_whole_object_GroupJoin_DefaultIfEmpty_sync();

        AssertSql(
            """
SELECT `s`.`PickupStatusId`, `r0`.`pickupStatusId`, `r0`.`Count`, `r0`.`marker`
FROM `Statuses` AS `s`
LEFT JOIN (
    SELECT `r`.`PickupStatusId` AS `pickupStatusId`, COUNT(*) AS `Count`, 1 AS `marker`
    FROM `Requests` AS `r`
    GROUP BY `r`.`PickupStatusId`
) AS `r0` ON `s`.`PickupStatusId` = `r0`.`pickupStatusId`
ORDER BY `s`.`PickupStatusId`, `r0`.`pickupStatusId`
""");
    }

    public override async Task Projected_object_with_decimal_member()
    {
        await base.Projected_object_with_decimal_member();

        AssertSql(
            """
SELECT `s`.`PickupStatusId`, `r0`.`pickupStatusId`, `r0`.`Total`, `r0`.`marker`
FROM `Statuses` AS `s`
LEFT JOIN (
    SELECT `r`.`PickupStatusId` AS `pickupStatusId`, COALESCE(SUM(CDEC(`r`.`PickupStatusId`)), 0.0) AS `Total`, 1 AS `marker`
    FROM `Requests` AS `r`
    GROUP BY `r`.`PickupStatusId`
) AS `r0` ON `s`.`PickupStatusId` = `r0`.`pickupStatusId`
ORDER BY `s`.`PickupStatusId`, `r0`.`pickupStatusId`
""");
    }

    public override async Task Correlated_SelectMany_DefaultIfEmpty_whole_object()
    {
        await base.Correlated_SelectMany_DefaultIfEmpty_whole_object();

        AssertSql();
    }

    public override async Task Composed_user_marker_projection_into_subquery_self_heals()
    {
        await base.Composed_user_marker_projection_into_subquery_self_heals();

        AssertSql(
            """
SELECT `s0`.`PickupStatusId`, `s0`.`pickupStatusId0` AS `pickupStatusId`, `s0`.`marker`, `s0`.`marker0` AS `marker`
FROM (
    SELECT DISTINCT `s`.`PickupStatusId`, `r0`.`pickupStatusId` AS `pickupStatusId0`, `r0`.`marker`, `r0`.`marker0`
    FROM `Statuses` AS `s`
    LEFT JOIN (
        SELECT `r`.`PickupStatusId` AS `pickupStatusId`, COUNT(*) AS `marker`, 1 AS `marker0`
        FROM `Requests` AS `r`
        GROUP BY `r`.`PickupStatusId`
    ) AS `r0` ON `s`.`PickupStatusId` = `r0`.`pickupStatusId`
) AS `s0`
ORDER BY `s0`.`PickupStatusId`, `s0`.`pickupStatusId0`
""");
    }

    public override async Task Nested_transparent_identifier_of_entities_as_leftjoin_inner()
    {
        await base.Nested_transparent_identifier_of_entities_as_leftjoin_inner();

        AssertSql(
            """
SELECT `s`.`PickupStatusId`, `s1`.`Id`, `s1`.`PickupStatusId`, `s1`.`Priority`, `s1`.`PickupStatusId0`, `s1`.`Name`
FROM `Statuses` AS `s`
LEFT JOIN (
    SELECT `r`.`Id`, `r`.`PickupStatusId`, `r`.`Priority`, `s0`.`PickupStatusId` AS `PickupStatusId0`, `s0`.`Name`
    FROM `Requests` AS `r`
    INNER JOIN `Statuses` AS `s0` ON `r`.`PickupStatusId` = `s0`.`PickupStatusId`
) AS `s1` ON `s`.`PickupStatusId` = `s1`.`PickupStatusId0`
ORDER BY `s`.`PickupStatusId`
""");
    }

    public override async Task Distinct_with_unconsumed_marker_is_benign()
    {
        await base.Distinct_with_unconsumed_marker_is_benign();

        AssertSql(
            """
SELECT `s0`.`PickupStatusId`, `s0`.`pickupStatusId0`, `s0`.`Count`, `s0`.`marker`
FROM (
    SELECT DISTINCT `s`.`PickupStatusId`, `r0`.`pickupStatusId` AS `pickupStatusId0`, `r0`.`Count`, `r0`.`marker`
    FROM `Statuses` AS `s`
    LEFT JOIN (
        SELECT `r`.`PickupStatusId` AS `pickupStatusId`, COUNT(*) AS `Count`, 1 AS `marker`
        FROM `Requests` AS `r`
        GROUP BY `r`.`PickupStatusId`
    ) AS `r0` ON `s`.`PickupStatusId` = `r0`.`pickupStatusId`
) AS `s0`
ORDER BY `s0`.`PickupStatusId`, `s0`.`pickupStatusId0`
""");
    }

    public override async Task Member_only_access_nested_two_joins_deep()
    {
        await base.Member_only_access_nested_two_joins_deep();

        AssertSql(
            """
SELECT `s0`.`PickupStatusId`, `s0`.`Name`, `s1`.`marker` IS NULL, `s1`.`pickupStatusId0`, `s1`.`Count`
FROM (
    SELECT DISTINCT `s`.`PickupStatusId`, `r0`.`pickupStatusId` AS `pickupStatusId0`, `r0`.`Count`, `r0`.`marker`
    FROM `Statuses` AS `s`
    LEFT JOIN (
        SELECT `r`.`PickupStatusId` AS `pickupStatusId`, COUNT(*) AS `Count`, 1 AS `marker`
        FROM `Requests` AS `r`
        GROUP BY `r`.`PickupStatusId`
    ) AS `r0` ON `s`.`PickupStatusId` = `r0`.`pickupStatusId`
) AS `s1`
INNER JOIN `Statuses` AS `s0` ON `s1`.`PickupStatusId` = `s0`.`PickupStatusId`
ORDER BY `s0`.`PickupStatusId`, `s1`.`pickupStatusId0`
""");
    }

    public override async Task Dto_constructor_whole_object_LeftJoin()
    {
        await base.Dto_constructor_whole_object_LeftJoin();

        AssertSql();
    }

    public override async Task Struct_whole_object_LeftJoin()
    {
        await base.Struct_whole_object_LeftJoin();

        AssertSql(
            """
SELECT `s`.`PickupStatusId`, `r0`.`PickupStatusId`, `r0`.`Count`, `r0`.`marker`
FROM `Statuses` AS `s`
LEFT JOIN (
    SELECT `r`.`PickupStatusId`, COUNT(*) AS `Count`, 1 AS `marker`
    FROM `Requests` AS `r`
    GROUP BY `r`.`PickupStatusId`
) AS `r0` ON `s`.`PickupStatusId` = `r0`.`PickupStatusId`
ORDER BY `s`.`PickupStatusId`, `r0`.`PickupStatusId`
""");
    }

    public override async Task Struct_whole_object_GroupJoin_DefaultIfEmpty()
    {
        await base.Struct_whole_object_GroupJoin_DefaultIfEmpty();

        AssertSql(
            """
SELECT `s`.`PickupStatusId`, `r0`.`PickupStatusId`, `r0`.`Count`, `r0`.`marker`
FROM `Statuses` AS `s`
LEFT JOIN (
    SELECT `r`.`PickupStatusId`, COUNT(*) AS `Count`, 1 AS `marker`
    FROM `Requests` AS `r`
    GROUP BY `r`.`PickupStatusId`
) AS `r0` ON `s`.`PickupStatusId` = `r0`.`PickupStatusId`
ORDER BY `s`.`PickupStatusId`, `r0`.`PickupStatusId`
""");
    }

    public override async Task RecordStruct_whole_object_LeftJoin()
    {
        await base.RecordStruct_whole_object_LeftJoin();

        AssertSql();
    }

    public override async Task Nullable_struct_whole_object_from_nullable_side()
    {
        await base.Nullable_struct_whole_object_from_nullable_side();

        AssertSql(
            """
SELECT `s`.`PickupStatusId`, `r0`.`PickupStatusId`, `r0`.`Count`, `r0`.`marker`
FROM `Statuses` AS `s`
LEFT JOIN (
    SELECT `r`.`PickupStatusId`, COUNT(*) AS `Count`, 1 AS `marker`
    FROM `Requests` AS `r`
    GROUP BY `r`.`PickupStatusId`
) AS `r0` ON `s`.`PickupStatusId` = `r0`.`PickupStatusId`
ORDER BY `s`.`PickupStatusId`, `r0`.`PickupStatusId`
""");
    }

    public override async Task ValueTuple_whole_object_from_nullable_side()
    {
        await base.ValueTuple_whole_object_from_nullable_side();

        AssertSql(
            """
SELECT `s`.`PickupStatusId`, `r0`.`PickupStatusId`, `r0`.`c`, `r0`.`marker`
FROM `Statuses` AS `s`
LEFT JOIN (
    SELECT `r`.`PickupStatusId`, COUNT(*) AS `c`, 1 AS `marker`
    FROM `Requests` AS `r`
    GROUP BY `r`.`PickupStatusId`
) AS `r0` ON `s`.`PickupStatusId` = `r0`.`PickupStatusId`
ORDER BY `s`.`PickupStatusId`, `r0`.`PickupStatusId`
""");
    }

    public override async Task Second_join_after_then_whole_object()
    {
        await base.Second_join_after_then_whole_object();

        AssertSql(
            """
SELECT `s0`.`PickupStatusId`, `r0`.`pickupStatusId`, `r0`.`Count`, `r0`.`marker`
FROM `Statuses` AS `s`
LEFT JOIN (
    SELECT `r`.`PickupStatusId` AS `pickupStatusId`, COUNT(*) AS `Count`, 1 AS `marker`
    FROM `Requests` AS `r`
    GROUP BY `r`.`PickupStatusId`
) AS `r0` ON `s`.`PickupStatusId` = `r0`.`pickupStatusId`
INNER JOIN `Statuses` AS `s0` ON `s`.`PickupStatusId` = `s0`.`PickupStatusId`
ORDER BY `s0`.`PickupStatusId`, `r0`.`pickupStatusId`
""");
    }

    public override async Task Plain_inner_no_aggregate_LeftJoin_whole_object()
    {
        await base.Plain_inner_no_aggregate_LeftJoin_whole_object();

        AssertSql(
            """
SELECT `s`.`PickupStatusId`, `r`.`PickupStatusId`, 1 AS `Count`
FROM `Statuses` AS `s`
LEFT JOIN `Requests` AS `r` ON `s`.`PickupStatusId` = `r`.`PickupStatusId`
ORDER BY `s`.`PickupStatusId`, `r`.`Id`
""");
    }

    public override async Task Union_of_two_leftjoin_nonentity()
    {
        await base.Union_of_two_leftjoin_nonentity();

        AssertSql();
    }

    public override async Task OrderBy_member_of_nullable_projection()
    {
        await base.OrderBy_member_of_nullable_projection();

        AssertSql();
    }

    public override async Task Where_nonentity_projection_not_null_serverside()
    {
        await base.Where_nonentity_projection_not_null_serverside();

        AssertSql();
    }

    public override async Task Where_nonentity_projection_null_serverside()
    {
        await base.Where_nonentity_projection_null_serverside();

        AssertSql();
    }

    public override async Task Matched_struct_row_with_zero_aggregate_keeps_real_key()
    {
        await base.Matched_struct_row_with_zero_aggregate_keeps_real_key();

        AssertSql(
            """
SELECT `s`.`PickupStatusId`, `r0`.`PickupStatusId`, `r0`.`Count`, `r0`.`marker`
FROM `Statuses` AS `s`
LEFT JOIN (
    SELECT `r`.`PickupStatusId`, COUNT(CASE
        WHEN `r`.`Priority` > 100 THEN 1
    END) AS `Count`, 1 AS `marker`
    FROM `Requests` AS `r`
    GROUP BY `r`.`PickupStatusId`
) AS `r0` ON `s`.`PickupStatusId` = `r0`.`PickupStatusId`
ORDER BY `s`.`PickupStatusId`, `r0`.`PickupStatusId`
""");
    }

    public override async Task RightJoin_whole_object_outer_nullable()
    {
        await base.RightJoin_whole_object_outer_nullable();

        AssertSql(
            """
SELECT `s`.`PickupStatusId`, `r0`.`pickupStatusId`, `r0`.`Count`
FROM (
    SELECT `r`.`PickupStatusId` AS `pickupStatusId`, COUNT(*) AS `Count`
    FROM `Requests` AS `r`
    GROUP BY `r`.`PickupStatusId`
) AS `r0`
RIGHT JOIN `Statuses` AS `s` ON `r0`.`pickupStatusId` = `s`.`PickupStatusId`
ORDER BY `s`.`PickupStatusId`
""");
    }

    public override async Task GroupBy_after_join_then_whole_object()
    {
        await base.GroupBy_after_join_then_whole_object();

        AssertSql(
            """
SELECT `s1`.`PickupStatusId`, `s3`.`pickupStatusId`, `s3`.`Count`, `s3`.`marker`, `s3`.`c`
FROM (
    SELECT `s`.`PickupStatusId`
    FROM `Statuses` AS `s`
    LEFT JOIN (
        SELECT `r`.`PickupStatusId` AS `pickupStatusId`
        FROM `Requests` AS `r`
        GROUP BY `r`.`PickupStatusId`
    ) AS `r0` ON `s`.`PickupStatusId` = `r0`.`pickupStatusId`
    GROUP BY `s`.`PickupStatusId`
) AS `s1`
LEFT JOIN (
    SELECT `s2`.`pickupStatusId`, `s2`.`Count`, `s2`.`marker`, `s2`.`c`, `s2`.`PickupStatusId0`
    FROM (
        SELECT `r1`.`pickupStatusId`, `r1`.`Count`, `r1`.`marker`, 1 AS `c`, `s0`.`PickupStatusId` AS `PickupStatusId0`, ROW_NUMBER() OVER(PARTITION BY `s0`.`PickupStatusId` ORDER BY `s0`.`PickupStatusId`, `r1`.`pickupStatusId`) AS `row`
        FROM `Statuses` AS `s0`
        LEFT JOIN (
            SELECT `r2`.`PickupStatusId` AS `pickupStatusId`, COUNT(*) AS `Count`, 1 AS `marker`
            FROM `Requests` AS `r2`
            GROUP BY `r2`.`PickupStatusId`
        ) AS `r1` ON `s0`.`PickupStatusId` = `r1`.`pickupStatusId`
    ) AS `s2`
    WHERE `s2`.`row` <= 1
) AS `s3` ON `s1`.`PickupStatusId` = `s3`.`PickupStatusId0`
ORDER BY `s1`.`PickupStatusId`
""");
    }

    public override async Task GroupBy_after_join_then_whole_object_nested_in_wrapper()
    {
        await base.GroupBy_after_join_then_whole_object_nested_in_wrapper();

        // SQL is intentionally identical to the flat GroupBy_after_join_then_whole_object variant -- the wrapper is
        // client-side-only nesting, so it changes no SQL. This test exists to exercise the nested-node rekey path.
        AssertSql(
            """
SELECT `s1`.`PickupStatusId`, `s3`.`pickupStatusId`, `s3`.`Count`, `s3`.`marker`, `s3`.`c`
FROM (
    SELECT `s`.`PickupStatusId`
    FROM `Statuses` AS `s`
    LEFT JOIN (
        SELECT `r`.`PickupStatusId` AS `pickupStatusId`
        FROM `Requests` AS `r`
        GROUP BY `r`.`PickupStatusId`
    ) AS `r0` ON `s`.`PickupStatusId` = `r0`.`pickupStatusId`
    GROUP BY `s`.`PickupStatusId`
) AS `s1`
LEFT JOIN (
    SELECT `s2`.`pickupStatusId`, `s2`.`Count`, `s2`.`marker`, `s2`.`c`, `s2`.`PickupStatusId0`
    FROM (
        SELECT `r1`.`pickupStatusId`, `r1`.`Count`, `r1`.`marker`, 1 AS `c`, `s0`.`PickupStatusId` AS `PickupStatusId0`, ROW_NUMBER() OVER(PARTITION BY `s0`.`PickupStatusId` ORDER BY `s0`.`PickupStatusId`, `r1`.`pickupStatusId`) AS `row`
        FROM `Statuses` AS `s0`
        LEFT JOIN (
            SELECT `r2`.`PickupStatusId` AS `pickupStatusId`, COUNT(*) AS `Count`, 1 AS `marker`
            FROM `Requests` AS `r2`
            GROUP BY `r2`.`PickupStatusId`
        ) AS `r1` ON `s0`.`PickupStatusId` = `r1`.`pickupStatusId`
    ) AS `s2`
    WHERE `s2`.`row` <= 1
) AS `s3` ON `s1`.`PickupStatusId` = `s3`.`PickupStatusId0`
ORDER BY `s1`.`PickupStatusId`
""");
    }

    public override async Task GroupBy_after_join_then_whole_object_dto_memberinit()
    {
        await base.GroupBy_after_join_then_whole_object_dto_memberinit();

        AssertSql(
            """
SELECT `s1`.`PickupStatusId`, `s3`.`PickupStatusId`, `s3`.`Count`, `s3`.`marker`, `s3`.`c`
FROM (
    SELECT `s`.`PickupStatusId`
    FROM `Statuses` AS `s`
    LEFT JOIN (
        SELECT `r`.`PickupStatusId`
        FROM `Requests` AS `r`
        GROUP BY `r`.`PickupStatusId`
    ) AS `r0` ON `s`.`PickupStatusId` = `r0`.`PickupStatusId`
    GROUP BY `s`.`PickupStatusId`
) AS `s1`
LEFT JOIN (
    SELECT `s2`.`PickupStatusId`, `s2`.`Count`, `s2`.`marker`, `s2`.`c`, `s2`.`PickupStatusId0`
    FROM (
        SELECT `r1`.`PickupStatusId`, `r1`.`Count`, `r1`.`marker`, 1 AS `c`, `s0`.`PickupStatusId` AS `PickupStatusId0`, ROW_NUMBER() OVER(PARTITION BY `s0`.`PickupStatusId` ORDER BY `s0`.`PickupStatusId`, `r1`.`PickupStatusId`) AS `row`
        FROM `Statuses` AS `s0`
        LEFT JOIN (
            SELECT `r2`.`PickupStatusId`, COUNT(*) AS `Count`, 1 AS `marker`
            FROM `Requests` AS `r2`
            GROUP BY `r2`.`PickupStatusId`
        ) AS `r1` ON `s0`.`PickupStatusId` = `r1`.`PickupStatusId`
    ) AS `s2`
    WHERE `s2`.`row` <= 1
) AS `s3` ON `s1`.`PickupStatusId` = `s3`.`PickupStatusId0`
ORDER BY `s1`.`PickupStatusId`
""");
    }

    public override async Task GroupBy_after_join_then_whole_object_struct()
    {
        await base.GroupBy_after_join_then_whole_object_struct();

        AssertSql(
            """
SELECT `s1`.`PickupStatusId`, `s3`.`PickupStatusId`, `s3`.`Count`, `s3`.`marker`, `s3`.`c`
FROM (
    SELECT `s`.`PickupStatusId`
    FROM `Statuses` AS `s`
    LEFT JOIN (
        SELECT `r`.`PickupStatusId`
        FROM `Requests` AS `r`
        GROUP BY `r`.`PickupStatusId`
    ) AS `r0` ON `s`.`PickupStatusId` = `r0`.`PickupStatusId`
    GROUP BY `s`.`PickupStatusId`
) AS `s1`
LEFT JOIN (
    SELECT `s2`.`PickupStatusId`, `s2`.`Count`, `s2`.`marker`, `s2`.`c`, `s2`.`PickupStatusId0`
    FROM (
        SELECT `r1`.`PickupStatusId`, `r1`.`Count`, `r1`.`marker`, 1 AS `c`, `s0`.`PickupStatusId` AS `PickupStatusId0`, ROW_NUMBER() OVER(PARTITION BY `s0`.`PickupStatusId` ORDER BY `s0`.`PickupStatusId`, `r1`.`PickupStatusId`) AS `row`
        FROM `Statuses` AS `s0`
        LEFT JOIN (
            SELECT `r2`.`PickupStatusId`, COUNT(*) AS `Count`, 1 AS `marker`
            FROM `Requests` AS `r2`
            GROUP BY `r2`.`PickupStatusId`
        ) AS `r1` ON `s0`.`PickupStatusId` = `r1`.`PickupStatusId`
    ) AS `s2`
    WHERE `s2`.`row` <= 1
) AS `s3` ON `s1`.`PickupStatusId` = `s3`.`PickupStatusId0`
ORDER BY `s1`.`PickupStatusId`
""");
    }

    public override async Task Two_left_joined_nonentity_objects_second_marker_orphaned()
    {
        await base.Two_left_joined_nonentity_objects_second_marker_orphaned();

        AssertSql(
            """
SELECT `s`.`PickupStatusId`, `r0`.`pickupStatusId`, `r0`.`Count`, `r0`.`marker`, `r2`.`pickupStatusId`, `r2`.`Count`, `r2`.`marker`
FROM `Statuses` AS `s`
LEFT JOIN (
    SELECT `r`.`PickupStatusId` AS `pickupStatusId`, COUNT(*) AS `Count`, 1 AS `marker`
    FROM `Requests` AS `r`
    GROUP BY `r`.`PickupStatusId`
) AS `r0` ON `s`.`PickupStatusId` = `r0`.`pickupStatusId`
LEFT JOIN (
    SELECT `r1`.`PickupStatusId` AS `pickupStatusId`, COUNT(*) AS `Count`, 1 AS `marker`
    FROM `Requests` AS `r1`
    GROUP BY `r1`.`PickupStatusId`
) AS `r2` ON `s`.`PickupStatusId` = `r2`.`pickupStatusId`
ORDER BY `s`.`PickupStatusId`, `r2`.`pickupStatusId`
""");
    }

    public override async Task Three_sequential_joins_marker_survives_two_remaps()
    {
        await base.Three_sequential_joins_marker_survives_two_remaps();

        AssertSql(
            """
SELECT `s1`.`PickupStatusId`, `r0`.`pickupStatusId`, `r0`.`Count`, `r0`.`marker`
FROM `Statuses` AS `s`
LEFT JOIN (
    SELECT `r`.`PickupStatusId` AS `pickupStatusId`, COUNT(*) AS `Count`, 1 AS `marker`
    FROM `Requests` AS `r`
    GROUP BY `r`.`PickupStatusId`
) AS `r0` ON `s`.`PickupStatusId` = `r0`.`pickupStatusId`
INNER JOIN `Statuses` AS `s0` ON `s`.`PickupStatusId` = `s0`.`PickupStatusId`
INNER JOIN `Statuses` AS `s1` ON `s0`.`PickupStatusId` = `s1`.`PickupStatusId`
ORDER BY `s1`.`PickupStatusId`, `r0`.`pickupStatusId`
""");
    }

    public override async Task Marker_object_nested_in_outer_wrapper_across_second_join()
    {
        await base.Marker_object_nested_in_outer_wrapper_across_second_join();

        AssertSql(
            """
SELECT `s0`.`PickupStatusId`, `r0`.`pickupStatusId`, `r0`.`Count`, `r0`.`marker`
FROM `Statuses` AS `s`
LEFT JOIN (
    SELECT `r`.`PickupStatusId` AS `pickupStatusId`, COUNT(*) AS `Count`, 1 AS `marker`
    FROM `Requests` AS `r`
    GROUP BY `r`.`PickupStatusId`
) AS `r0` ON `s`.`PickupStatusId` = `r0`.`pickupStatusId`
INNER JOIN `Statuses` AS `s0` ON `s`.`PickupStatusId` = `s0`.`PickupStatusId`
ORDER BY `s0`.`PickupStatusId`, `r0`.`pickupStatusId`
""");
    }

    public override async Task Query_when_null_key_in_database_should_throw()
    {
        await base.Query_when_null_key_in_database_should_throw();

        AssertSql(
            """
SELECT `z`.`Id`
FROM `ZeroKey` AS `z`
""");
    }

    public override async Task Mapping_JsonElement_property_throws_a_meaningful_exception()
    {
        await base.Mapping_JsonElement_property_throws_a_meaningful_exception();

        AssertSql();
    }

    public override async Task Struct_composed_user_marker_projection_into_subquery_self_heals()
    {
        await base.Struct_composed_user_marker_projection_into_subquery_self_heals();

        AssertSql(
            """
SELECT `s0`.`PickupStatusId`, `s0`.`pickupStatusId0` AS `pickupStatusId`, `s0`.`marker`, `s0`.`marker0` AS `marker`
FROM (
    SELECT DISTINCT `s`.`PickupStatusId`, `r0`.`pickupStatusId` AS `pickupStatusId0`, `r0`.`marker`, `r0`.`marker0`
    FROM `Statuses` AS `s`
    LEFT JOIN (
        SELECT `r`.`PickupStatusId` AS `pickupStatusId`, COUNT(*) AS `marker`, 1 AS `marker0`
        FROM `Requests` AS `r`
        GROUP BY `r`.`PickupStatusId`
    ) AS `r0` ON `s`.`PickupStatusId` = `r0`.`pickupStatusId`
) AS `s0`
ORDER BY `s0`.`PickupStatusId`, `s0`.`pickupStatusId0`
""");
    }

    #endregion

    [Fact]
    public virtual void Check_all_tests_overridden()
        => TestHelpers.AssertAllMethodsOverridden(GetType());
}
