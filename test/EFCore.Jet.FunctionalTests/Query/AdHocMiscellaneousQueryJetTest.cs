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
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using EntityFrameworkCore.Jet.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.EntityFrameworkCore.TestUtilities;
using NetTopologySuite.Geometries;
using Xunit;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query;

#nullable disable

public class AdHocMiscellaneousQueryJetTest : AdHocMiscellaneousQueryRelationalTestBase
{
    protected override ITestStoreFactory TestStoreFactory
        => JetTestStoreFactory.Instance;

    protected override Task Seed2951(Context2951 context)
        => context.Database.ExecuteSqlRawAsync(
            """
CREATE TABLE ZeroKey (Id int);
INSERT ZeroKey VALUES (NULL)
""");

    #region 5456

    [ConditionalFact]
    public virtual async Task Include_group_join_is_per_query_context()
    {
        var contextFactory = await InitializeAsync<Context5456>(
            seed: c => c.SeedAsync(),
            createTestStore: async () => await JetTestStore.CreateInitializedAsync(StoreName, multipleActiveResultSets: true));

        Parallel.For(
            0, 10, i =>
            {
                using var ctx = contextFactory.CreateContext();
                var result = ctx.Posts.Where(x => x.Blog.Id > 1).Include(x => x.Blog).ToList();

                Assert.Equal(198, result.Count);
            });

        Parallel.For(
            0, 10, i =>
            {
                using var ctx = contextFactory.CreateContext();
                var result = ctx.Posts.Where(x => x.Blog.Id > 1).Include(x => x.Blog).Include(x => x.Comments).ToList();

                Assert.Equal(198, result.Count);
            });

        Parallel.For(
            0, 10, i =>
            {
                using var ctx = contextFactory.CreateContext();
                var result = ctx.Posts.Where(x => x.Blog.Id > 1).Include(x => x.Blog).ThenInclude(b => b.Author).ToList();

                Assert.Equal(198, result.Count);
            });
    }

    [ConditionalFact]
    public virtual async Task Include_group_join_is_per_query_context_async()
    {
        var contextFactory = await InitializeAsync<Context5456>(
            seed: c => c.SeedAsync(),
            createTestStore: async () => await JetTestStore.CreateInitializedAsync(StoreName, multipleActiveResultSets: true));

        await Parallel.ForAsync(
            0, 10, async (i, ct) =>
            {
                using var ctx = contextFactory.CreateContext();
                var result = await ctx.Posts.Where(x => x.Blog.Id > 1).Include(x => x.Blog).ToListAsync();

                Assert.Equal(198, result.Count);
            });

        await Parallel.ForAsync(
            0, 10, async (i, ct) =>
            {
                using var ctx = contextFactory.CreateContext();
                var result = await ctx.Posts.Where(x => x.Blog.Id > 1).Include(x => x.Blog).Include(x => x.Comments)
                    .ToListAsync();

                Assert.Equal(198, result.Count);
            });

        await Parallel.ForAsync(
            0, 10, async (i, ct) =>
            {
                using var ctx = contextFactory.CreateContext();
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

    [ConditionalFact]
    public virtual async Task Select_nested_projection()
    {
        var contextFactory = await InitializeAsync<Context8864>(seed: c => c.SeedAsync());

        using (var context = contextFactory.CreateContext())
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
@__id_0='1'

SELECT TOP 2 `c`.`Id`, `c`.`Name`
FROM `Customers` AS `c`
WHERE `c`.`Id` = @__id_0
""",
            //
            """
@__id_0='2'

SELECT TOP 2 `c`.`Id`, `c`.`Name`
FROM `Customers` AS `c`
WHERE `c`.`Id` = @__id_0
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

    #region 12482

    [ConditionalFact]
    public virtual async Task Batch_insert_with_sqlvariant_different_types()
    {
        var contextFactory = await InitializeAsync<Context12482>();

        using var context = contextFactory.CreateContext();
        context.AddRange(
            new Context12482.BaseEntity { Value = 10.0999 },
            new Context12482.BaseEntity { Value = -12345 },
            new Context12482.BaseEntity { Value = "String Value" },
            new Context12482.BaseEntity { Value = new DateTime(2020, 1, 1) });

        context.SaveChanges();

        AssertSql(
            """
@p0='10.0999' (Nullable = true) (DbType = Object)
@p1='-12345' (Nullable = true) (DbType = Object)
@p2='String Value' (Size = 12) (DbType = Object)
@p3='2020-01-01T00:00:00.0000000' (Nullable = true) (DbType = Object)

SET IMPLICIT_TRANSACTIONS OFF;
SET NOCOUNT ON;
MERGE [BaseEntities] USING (
VALUES (@p0, 0),
(@p1, 1),
(@p2, 2),
(@p3, 3)) AS i ([Value], _Position) ON 1=0
WHEN NOT MATCHED THEN
INSERT ([Value])
VALUES (i.[Value])
OUTPUT INSERTED.[Id], i._Position;
""");
    }

    private class Context12482(DbContextOptions options) : DbContext(options)
    {
        public virtual DbSet<BaseEntity> BaseEntities { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<BaseEntity>();

        public class BaseEntity
        {
            public int Id { get; set; }

            [Column(TypeName = "sql_variant")]
            public object Value { get; set; }
        }
    }

    #endregion

    #region 12518

    [ConditionalFact]
    public virtual async Task Projecting_entity_with_value_converter_and_include_works()
    {
        var contextFactory = await InitializeAsync<Context12518>(seed: c => c.SeedAsync());
        using var context = contextFactory.CreateContext();
        var result = context.Parents.Include(p => p.Child).OrderBy(e => e.Id).FirstOrDefault();

        AssertSql(
            """
SELECT TOP(1) [p].[Id], [p].[ChildId], [c].[Id], [c].[ParentId], [c].[ULongRowVersion]
FROM [Parents] AS [p]
LEFT JOIN [Children] AS [c] ON [p].[ChildId] = [c].[Id]
ORDER BY [p].[Id]
""");
    }

    [ConditionalFact]
    public virtual async Task Projecting_column_with_value_converter_of_ulong_byte_array()
    {
        var contextFactory = await InitializeAsync<Context12518>(seed: c => c.SeedAsync());
        using var context = contextFactory.CreateContext();
        var result = context.Parents.OrderBy(e => e.Id).Select(p => (ulong?)p.Child.ULongRowVersion).FirstOrDefault();

        AssertSql(
            """
SELECT TOP(1) [c].[ULongRowVersion]
FROM [Parents] AS [p]
LEFT JOIN [Children] AS [c] ON [p].[ChildId] = [c].[Id]
ORDER BY [p].[Id]
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
                .HasColumnType("RowVersion");

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

    [ConditionalFact]
    public virtual async Task DateTime_Contains_with_smalldatetime_generates_correct_literal()
    {
        var contextFactory = await InitializeAsync<Context13118>(seed: c => c.SeedAsync());
        using var context = contextFactory.CreateContext();
        var testDateList = new List<DateTime> { new(2018, 10, 07) };
        var findRecordsWithDateInList = context.ReproEntity
            .Where(a => testDateList.Contains(a.MyTime))
            .ToList();

        Assert.Single((IEnumerable)findRecordsWithDateInList);

        AssertSql(
            """
SELECT `r`.`Id`, `r`.`MyTime`
FROM `ReproEntity` AS `r`
WHERE `r`.`MyTime` = #2018-10-07#
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

    [ConditionalTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Where_equals_DateTime_Now(bool async)
    {
        var contextFactory = await InitializeAsync<Context14095>(seed: c => c.SeedAsync());

        using var context = contextFactory.CreateContext();
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
SELECT [d].[Id], [d].[DateTime], [d].[DateTime2], [d].[DateTime2_0], [d].[DateTime2_1], [d].[DateTime2_2], [d].[DateTime2_3], [d].[DateTime2_4], [d].[DateTime2_5], [d].[DateTime2_6], [d].[DateTime2_7], [d].[SmallDateTime]
FROM [Dates] AS [d]
WHERE [d].[DateTime2_2] = GETDATE() OR [d].[DateTime2_7] = GETDATE() OR [d].[DateTime] = GETDATE() OR [d].[SmallDateTime] = GETDATE()
""");
    }

    [ConditionalTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Where_not_equals_DateTime_Now(bool async)
    {
        var contextFactory = await InitializeAsync<Context14095>(seed: c => c.SeedAsync());

        using var context = contextFactory.CreateContext();
        var query = context.Dates.Where(
            d => d.DateTime2_2 != DateTime.Now
                && d.DateTime2_7 != DateTime.Now
                && d.DateTime != DateTime.Now
                && d.SmallDateTime != DateTime.Now);

        var results = async
            ? await query.ToListAsync()
            : [.. query];

        Assert.Single((IEnumerable)results);

        AssertSql(
            """
SELECT [d].[Id], [d].[DateTime], [d].[DateTime2], [d].[DateTime2_0], [d].[DateTime2_1], [d].[DateTime2_2], [d].[DateTime2_3], [d].[DateTime2_4], [d].[DateTime2_5], [d].[DateTime2_6], [d].[DateTime2_7], [d].[SmallDateTime]
FROM [Dates] AS [d]
WHERE [d].[DateTime2_2] <> GETDATE() AND [d].[DateTime2_7] <> GETDATE() AND [d].[DateTime] <> GETDATE() AND [d].[SmallDateTime] <> GETDATE()
""");
    }

    [ConditionalTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Where_equals_new_DateTime(bool async)
    {
        var contextFactory = await InitializeAsync<Context14095>(seed: c => c.SeedAsync());

        using var context = contextFactory.CreateContext();
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
SELECT [d].[Id], [d].[DateTime], [d].[DateTime2], [d].[DateTime2_0], [d].[DateTime2_1], [d].[DateTime2_2], [d].[DateTime2_3], [d].[DateTime2_4], [d].[DateTime2_5], [d].[DateTime2_6], [d].[DateTime2_7], [d].[SmallDateTime]
FROM [Dates] AS [d]
WHERE [d].[SmallDateTime] = '1970-09-03T12:00:00' AND [d].[DateTime] = '1971-09-03T12:00:10.220' AND [d].[DateTime2] = '1972-09-03T12:00:10.3330000' AND [d].[DateTime2_0] = '1973-09-03T12:00:10' AND [d].[DateTime2_1] = '1974-09-03T12:00:10.5' AND [d].[DateTime2_2] = '1975-09-03T12:00:10.66' AND [d].[DateTime2_3] = '1976-09-03T12:00:10.777' AND [d].[DateTime2_4] = '1977-09-03T12:00:10.8880' AND [d].[DateTime2_5] = '1978-09-03T12:00:10.99900' AND [d].[DateTime2_6] = '1979-09-03T12:00:10.111000' AND [d].[DateTime2_7] = '1980-09-03T12:00:10.2220000'
""");
    }

    [ConditionalTheory]
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

        var contextFactory = await InitializeAsync<Context14095>(seed: c => c.SeedAsync());

        using var context = contextFactory.CreateContext();
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
@__dateTimes_0='["1970-09-03T12:00:00","1971-09-03T12:00:10.22","1972-09-03T12:00:10.333","1973-09-03T12:00:10","1974-09-03T12:00:10.5","1975-09-03T12:00:10.66","1976-09-03T12:00:10.777","1977-09-03T12:00:10.888","1978-09-03T12:00:10.999","1979-09-03T12:00:10.111","1980-09-03T12:00:10.222"]' (Size = 4000)
@__dateTimes_0_1='["1970-09-03T12:00:00","1971-09-03T12:00:10.22","1972-09-03T12:00:10.333","1973-09-03T12:00:10","1974-09-03T12:00:10.5","1975-09-03T12:00:10.66","1976-09-03T12:00:10.777","1977-09-03T12:00:10.888","1978-09-03T12:00:10.999","1979-09-03T12:00:10.111","1980-09-03T12:00:10.222"]' (Size = 4000)
@__dateTimes_0_2='["1970-09-03T12:00:00","1971-09-03T12:00:10.22","1972-09-03T12:00:10.333","1973-09-03T12:00:10","1974-09-03T12:00:10.5","1975-09-03T12:00:10.66","1976-09-03T12:00:10.777","1977-09-03T12:00:10.888","1978-09-03T12:00:10.999","1979-09-03T12:00:10.111","1980-09-03T12:00:10.222"]' (Size = 4000)
@__dateTimes_0_3='["1970-09-03T12:00:00","1971-09-03T12:00:10.22","1972-09-03T12:00:10.333","1973-09-03T12:00:10","1974-09-03T12:00:10.5","1975-09-03T12:00:10.66","1976-09-03T12:00:10.777","1977-09-03T12:00:10.888","1978-09-03T12:00:10.999","1979-09-03T12:00:10.111","1980-09-03T12:00:10.222"]' (Size = 4000)
@__dateTimes_0_4='["1970-09-03T12:00:00","1971-09-03T12:00:10.22","1972-09-03T12:00:10.333","1973-09-03T12:00:10","1974-09-03T12:00:10.5","1975-09-03T12:00:10.66","1976-09-03T12:00:10.777","1977-09-03T12:00:10.888","1978-09-03T12:00:10.999","1979-09-03T12:00:10.111","1980-09-03T12:00:10.222"]' (Size = 4000)
@__dateTimes_0_5='["1970-09-03T12:00:00","1971-09-03T12:00:10.22","1972-09-03T12:00:10.333","1973-09-03T12:00:10","1974-09-03T12:00:10.5","1975-09-03T12:00:10.66","1976-09-03T12:00:10.777","1977-09-03T12:00:10.888","1978-09-03T12:00:10.999","1979-09-03T12:00:10.111","1980-09-03T12:00:10.222"]' (Size = 4000)
@__dateTimes_0_6='["1970-09-03T12:00:00","1971-09-03T12:00:10.22","1972-09-03T12:00:10.333","1973-09-03T12:00:10","1974-09-03T12:00:10.5","1975-09-03T12:00:10.66","1976-09-03T12:00:10.777","1977-09-03T12:00:10.888","1978-09-03T12:00:10.999","1979-09-03T12:00:10.111","1980-09-03T12:00:10.222"]' (Size = 4000)
@__dateTimes_0_7='["1970-09-03T12:00:00","1971-09-03T12:00:10.22","1972-09-03T12:00:10.333","1973-09-03T12:00:10","1974-09-03T12:00:10.5","1975-09-03T12:00:10.66","1976-09-03T12:00:10.777","1977-09-03T12:00:10.888","1978-09-03T12:00:10.999","1979-09-03T12:00:10.111","1980-09-03T12:00:10.222"]' (Size = 4000)
@__dateTimes_0_8='["1970-09-03T12:00:00","1971-09-03T12:00:10.22","1972-09-03T12:00:10.333","1973-09-03T12:00:10","1974-09-03T12:00:10.5","1975-09-03T12:00:10.66","1976-09-03T12:00:10.777","1977-09-03T12:00:10.888","1978-09-03T12:00:10.999","1979-09-03T12:00:10.111","1980-09-03T12:00:10.222"]' (Size = 4000)
@__dateTimes_0_9='["1970-09-03T12:00:00","1971-09-03T12:00:10.22","1972-09-03T12:00:10.333","1973-09-03T12:00:10","1974-09-03T12:00:10.5","1975-09-03T12:00:10.66","1976-09-03T12:00:10.777","1977-09-03T12:00:10.888","1978-09-03T12:00:10.999","1979-09-03T12:00:10.111","1980-09-03T12:00:10.222"]' (Size = 4000)
@__dateTimes_0_10='["1970-09-03T12:00:00","1971-09-03T12:00:10.22","1972-09-03T12:00:10.333","1973-09-03T12:00:10","1974-09-03T12:00:10.5","1975-09-03T12:00:10.66","1976-09-03T12:00:10.777","1977-09-03T12:00:10.888","1978-09-03T12:00:10.999","1979-09-03T12:00:10.111","1980-09-03T12:00:10.222"]' (Size = 4000)

SELECT [d].[Id], [d].[DateTime], [d].[DateTime2], [d].[DateTime2_0], [d].[DateTime2_1], [d].[DateTime2_2], [d].[DateTime2_3], [d].[DateTime2_4], [d].[DateTime2_5], [d].[DateTime2_6], [d].[DateTime2_7], [d].[SmallDateTime]
FROM [Dates] AS [d]
WHERE [d].[SmallDateTime] IN (
    SELECT [d0].[value]
    FROM OPENJSON(@__dateTimes_0) WITH ([value] smalldatetime '$') AS [d0]
) AND [d].[DateTime] IN (
    SELECT [d1].[value]
    FROM OPENJSON(@__dateTimes_0_1) WITH ([value] datetime '$') AS [d1]
) AND [d].[DateTime2] IN (
    SELECT [d2].[value]
    FROM OPENJSON(@__dateTimes_0_2) WITH ([value] datetime2 '$') AS [d2]
) AND [d].[DateTime2_0] IN (
    SELECT [d3].[value]
    FROM OPENJSON(@__dateTimes_0_3) WITH ([value] datetime2(0) '$') AS [d3]
) AND [d].[DateTime2_1] IN (
    SELECT [d4].[value]
    FROM OPENJSON(@__dateTimes_0_4) WITH ([value] datetime2(1) '$') AS [d4]
) AND [d].[DateTime2_2] IN (
    SELECT [d5].[value]
    FROM OPENJSON(@__dateTimes_0_5) WITH ([value] datetime2(2) '$') AS [d5]
) AND [d].[DateTime2_3] IN (
    SELECT [d6].[value]
    FROM OPENJSON(@__dateTimes_0_6) WITH ([value] datetime2(3) '$') AS [d6]
) AND [d].[DateTime2_4] IN (
    SELECT [d7].[value]
    FROM OPENJSON(@__dateTimes_0_7) WITH ([value] datetime2(4) '$') AS [d7]
) AND [d].[DateTime2_5] IN (
    SELECT [d8].[value]
    FROM OPENJSON(@__dateTimes_0_8) WITH ([value] datetime2(5) '$') AS [d8]
) AND [d].[DateTime2_6] IN (
    SELECT [d9].[value]
    FROM OPENJSON(@__dateTimes_0_9) WITH ([value] datetime2(6) '$') AS [d9]
) AND [d].[DateTime2_7] IN (
    SELECT [d10].[value]
    FROM OPENJSON(@__dateTimes_0_10) WITH ([value] datetime2(7) '$') AS [d10]
)
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

    [ConditionalTheory]
    [InlineData(false)]
    [InlineData(true)]
    public virtual async Task Nested_queries_does_not_cause_concurrency_exception_sync(bool tracking)
    {
        var contextFactory = await InitializeAsync<Context15518>(seed: c => c.SeedAsync());

        using (var context = contextFactory.CreateContext())
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

        using (var context = contextFactory.CreateContext())
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

    /*[ConditionalFact]
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

    [ConditionalFact]
    public virtual async Task Thread_safety_in_relational_command_cache()
    {
        var contextFactory = await InitializeAsync<Context21666>(
            onConfiguring: options => ((IDbContextOptionsBuilderInfrastructure)options).AddOrUpdateExtension(
                options.Options.FindExtension<JetOptionsExtension>()
                    .WithConnection(null)
                    .WithConnectionString(JetTestStore.CreateConnectionString(StoreName))));

        var ids = new[] { 1, 2, 3 };

        Parallel.For(
            0, 100,
            i =>
            {
                using var context = contextFactory.CreateContext();
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

    #region 24216

    [ConditionalFact]
    public virtual async Task Subquery_take_SelectMany_with_TVF()
    {
        var contextFactory = await InitializeAsync<Context24216>();
        using var context = contextFactory.CreateContext();

        context.Database.ExecuteSqlRaw(
            """
create function [dbo].[GetPersonStatusAsOf] (@personId bigint, @timestamp datetime2)
returns @personStatus table
(
    Id bigint not null,
    PersonId bigint not null,
    GenderId bigint not null,
    StatusMessage nvarchar(max)
)
as
begin
    insert into @personStatus
    select [m].[Id], [m].[PersonId], [m].[PersonId], null
    from [Message] as [m]
    where [m].[PersonId] = @personId and [m].[TimeStamp] = @timestamp
    return
end
""");

        ClearLog();

        var q = from m in context.Message
                orderby m.Id
                select m;

        var q2 =
            from m in q.Take(10)
            from asof in context.GetPersonStatusAsOf(m.PersonId, m.Timestamp)
            select new { Gender = (from g in context.Gender where g.Id == asof.GenderId select g.Description).Single() };

        q2.ToList();

        AssertSql(
            """
@__p_0='10'

SELECT (
    SELECT TOP(1) [g0].[Description]
    FROM [Gender] AS [g0]
    WHERE [g0].[Id] = [g].[GenderId]) AS [Gender]
FROM (
    SELECT TOP(@__p_0) [m].[Id], [m].[PersonId], [m].[Timestamp]
    FROM [Message] AS [m]
    ORDER BY [m].[Id]
) AS [m0]
CROSS APPLY [dbo].[GetPersonStatusAsOf]([m0].[PersonId], [m0].[Timestamp]) AS [g]
ORDER BY [m0].[Id]
""");
    }

    private class Gender24216
    {
        public long Id { get; set; }

        public string Description { get; set; }
    }

    private class Message24216
    {
        public long Id { get; set; }

        public long PersonId { get; set; }

        public DateTime Timestamp { get; set; }
    }

    private class PersonStatus24216
    {
        public long Id { get; set; }

        public long PersonId { get; set; }

        public long GenderId { get; set; }

        public string StatusMessage { get; set; }
    }

    private class Context24216(DbContextOptions options) : DbContext(options)
    {
        public DbSet<Gender24216> Gender { get; set; }

        public DbSet<Message24216> Message { get; set; }

        public IQueryable<PersonStatus24216> GetPersonStatusAsOf(long personId, DateTime asOf)
            => FromExpression(() => GetPersonStatusAsOf(personId, asOf));

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.HasDbFunction(
                typeof(Context24216).GetMethod(
                    nameof(GetPersonStatusAsOf),
                    [typeof(long), typeof(DateTime)]));
        }
    }

    #endregion

    #region 27427

    /*[ConditionalTheory]
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

        AssertSql(
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
""");
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
        await base.Enum_has_flag_applies_explicit_cast_for_constant();

        AssertSql(
            """
SELECT [e].[Id], [e].[Permission], [e].[PermissionByte], [e].[PermissionShort]
FROM [Entities] AS [e]
WHERE [e].[Permission] & CAST(17179869184 AS bigint) = CAST(17179869184 AS bigint)
""",
            //
            """
SELECT [e].[Id], [e].[Permission], [e].[PermissionByte], [e].[PermissionShort]
FROM [Entities] AS [e]
WHERE [e].[PermissionShort] & CAST(4 AS smallint) = CAST(4 AS smallint)
""");
    }

    public override async Task Enum_has_flag_does_not_apply_explicit_cast_for_non_constant()
    {
        await base.Enum_has_flag_does_not_apply_explicit_cast_for_non_constant();

        AssertSql(
            """
SELECT [e].[Id], [e].[Permission], [e].[PermissionByte], [e].[PermissionShort]
FROM [Entities] AS [e]
WHERE [e].[Permission] & [e].[Permission] = [e].[Permission]
""",
            //
            """
SELECT [e].[Id], [e].[Permission], [e].[PermissionByte], [e].[PermissionShort]
FROM [Entities] AS [e]
WHERE [e].[PermissionByte] & [e].[PermissionByte] = [e].[PermissionByte]
""");
    }

    public override async Task Variable_from_closure_is_parametrized()
    {
        await base.Variable_from_closure_is_parametrized();

        AssertSql(
            """
@__id_0='1'

SELECT `e`.`Id`, `e`.`Name`
FROM `Entities` AS `e`
WHERE `e`.`Id` = @__id_0
""",
            //
            """
@__id_0='2'

SELECT `e`.`Id`, `e`.`Name`
FROM `Entities` AS `e`
WHERE `e`.`Id` = @__id_0
""",
            //
            """
@__id_0='1'

SELECT `e`.`Id`, `e`.`Name`
FROM `Entities` AS `e`
WHERE `e`.`Id` = @__id_0
""",
            //
            """
@__id_0='2'

SELECT `e`.`Id`, `e`.`Name`
FROM `Entities` AS `e`
WHERE `e`.`Id` = @__id_0
""",
            //
            """
@__id_0='1'

SELECT `e`.`Id`, `e`.`Name`
FROM `Entities` AS `e`
WHERE `e`.`Id` IN (
    SELECT `e0`.`Id`
    FROM `Entities` AS `e0`
    WHERE `e0`.`Id` = @__id_0
)
""",
            //
            """
@__id_0='2'

SELECT `e`.`Id`, `e`.`Name`
FROM `Entities` AS `e`
WHERE `e`.`Id` IN (
    SELECT `e0`.`Id`
    FROM `Entities` AS `e0`
    WHERE `e0`.`Id` = @__id_0
)
""");
    }

    public override async Task Relational_command_cache_creates_new_entry_when_parameter_nullability_changes()
    {
        await base.Relational_command_cache_creates_new_entry_when_parameter_nullability_changes();

        AssertSql(
            """
@__name_0='A' (Size = 255)

SELECT `e`.`Id`, `e`.`Name`
FROM `Entities` AS `e`
WHERE `e`.`Name` = @__name_0
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
SELECT IIF(`c0`.`Id` IS NOT NULL, `c0`.`Processed` BXOR TRUE, NULL) AS `Processing`
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
SELECT AVG(IIF(`p`.`NullableIntColumn` IS NULL, NULL, CDBL(`p`.`NullableIntColumn`)))
FROM `Prices` AS `p`
""",
            //
            """
SELECT AVG(CDBL(`p`.`LongColumn`))
FROM `Prices` AS `p`
""",
            //
            """
SELECT AVG(IIF(`p`.`NullableLongColumn` IS NULL, NULL, CDBL(`p`.`NullableLongColumn`)))
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
@__id1_0='1'
@__id2_1='2'

SELECT `t`.`Id`
FROM `Tables1` AS `t`
WHERE `t`.`Id` = @__id1_0
UNION
SELECT `t0`.`Id`
FROM `Tables2` AS `t0`
WHERE `t0`.`Id` = @__id2_1
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
@__k_0='1'

SELECT TOP 1 `a`.`Id`, `a`.`Name`
FROM `Autos` AS `a`
WHERE `a`.`Id` = @__k_0
""",
            //
            """
@__p_0='2'

SELECT TOP 1 `a`.`Id`, `a`.`Name`
FROM `Autos` AS `a`
WHERE `a`.`Id` = @__p_0
""",
            //
            """
@__entity_equality_a_0_Id='1' (Nullable = true)
@__entity_equality_b_1_Id='2' (Nullable = true)
@__entity_equality_b_1_Id='2' (Nullable = true)
@__entity_equality_a_0_Id='1' (Nullable = true)

SELECT `e`.`Id`, `e`.`AnotherAutoId`, `e`.`AutoId`
FROM (`EqualAutos` AS `e`
LEFT JOIN `Autos` AS `a` ON `e`.`AutoId` = `a`.`Id`)
LEFT JOIN `Autos` AS `a0` ON `e`.`AnotherAutoId` = `a0`.`Id`
WHERE (`a`.`Id` = @__entity_equality_a_0_Id AND `a0`.`Id` = @__entity_equality_b_1_Id) OR (`a`.`Id` = @__entity_equality_b_1_Id AND `a0`.`Id` = @__entity_equality_a_0_Id)
""");
    }

    public override async Task Operators_combine_nullability_of_entity_shapers()
    {
        await base.Operators_combine_nullability_of_entity_shapers();

        AssertSql(
            """
SELECT [a].[Id], [a].[a], [a].[a1], [a].[forkey], [b].[Id] AS [Id0], [b].[b], [b].[b1], [b].[forkey] AS [forkey0]
FROM [As] AS [a]
LEFT JOIN [Bs] AS [b] ON [a].[forkey] = [b].[forkey]
UNION ALL
SELECT [a0].[Id], [a0].[a], [a0].[a1], [a0].[forkey], [b0].[Id] AS [Id0], [b0].[b], [b0].[b1], [b0].[forkey] AS [forkey0]
FROM [Bs] AS [b0]
LEFT JOIN [As] AS [a0] ON [b0].[forkey] = [a0].[forkey]
WHERE [a0].[Id] IS NULL
""",
            //
            """
SELECT [a].[Id], [a].[a], [a].[a1], [a].[forkey], [b].[Id] AS [Id0], [b].[b], [b].[b1], [b].[forkey] AS [forkey0]
FROM [As] AS [a]
LEFT JOIN [Bs] AS [b] ON [a].[forkey] = [b].[forkey]
UNION
SELECT [a0].[Id], [a0].[a], [a0].[a1], [a0].[forkey], [b0].[Id] AS [Id0], [b0].[b], [b0].[b1], [b0].[forkey] AS [forkey0]
FROM [Bs] AS [b0]
LEFT JOIN [As] AS [a0] ON [b0].[forkey] = [a0].[forkey]
WHERE [a0].[Id] IS NULL
""",
            //
            """
SELECT [a].[Id], [a].[a], [a].[a1], [a].[forkey], [b].[Id] AS [Id0], [b].[b], [b].[b1], [b].[forkey] AS [forkey0]
FROM [As] AS [a]
LEFT JOIN [Bs] AS [b] ON [a].[forkey] = [b].[forkey]
EXCEPT
SELECT [a0].[Id], [a0].[a], [a0].[a1], [a0].[forkey], [b0].[Id] AS [Id0], [b0].[b], [b0].[b1], [b0].[forkey] AS [forkey0]
FROM [Bs] AS [b0]
LEFT JOIN [As] AS [a0] ON [b0].[forkey] = [a0].[forkey]
""",
            //
            """
SELECT [a].[Id], [a].[a], [a].[a1], [a].[forkey], [b].[Id] AS [Id0], [b].[b], [b].[b1], [b].[forkey] AS [forkey0]
FROM [As] AS [a]
LEFT JOIN [Bs] AS [b] ON [a].[forkey] = [b].[forkey]
INTERSECT
SELECT [a0].[Id], [a0].[a], [a0].[a1], [a0].[forkey], [b0].[Id] AS [Id0], [b0].[b], [b0].[b1], [b0].[forkey] AS [forkey0]
FROM [Bs] AS [b0]
LEFT JOIN [As] AS [a0] ON [b0].[forkey] = [a0].[forkey]
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
) AS `t1` ON `t`.`Id` = IIF(`t1`.`Exists` IS NOT NULL, `t1`.`Id`, NULL)
GROUP BY `t1`.`AnotherEntity11818_Name`
""",
            //
            """
SELECT `t1`.`AnotherEntity11818_Name` AS `MyKey`, COUNT(*) + 5 AS `cnt`
FROM (`Table` AS `t`
LEFT JOIN (
    SELECT `t0`.`Id`, `t0`.`Exists`, `t0`.`AnotherEntity11818_Name`
    FROM `Table` AS `t0`
    WHERE `t0`.`Exists` IS NOT NULL
) AS `t1` ON `t`.`Id` = IIF(`t1`.`Exists` IS NOT NULL, `t1`.`Id`, NULL))
LEFT JOIN (
    SELECT `t2`.`Id`, `t2`.`MaumarEntity11818_Exists`, `t2`.`MaumarEntity11818_Name`
    FROM `Table` AS `t2`
    WHERE `t2`.`MaumarEntity11818_Exists` IS NOT NULL
) AS `t3` ON `t`.`Id` = IIF(`t3`.`MaumarEntity11818_Exists` IS NOT NULL, `t3`.`Id`, NULL)
GROUP BY `t1`.`AnotherEntity11818_Name`, `t3`.`MaumarEntity11818_Name`
""",
            //
            """
SELECT TOP 1 `t1`.`AnotherEntity11818_Name` AS `MyKey`, `t3`.`MaumarEntity11818_Name` AS `cnt`
FROM (`Table` AS `t`
LEFT JOIN (
    SELECT `t0`.`Id`, `t0`.`Exists`, `t0`.`AnotherEntity11818_Name`
    FROM `Table` AS `t0`
    WHERE `t0`.`Exists` IS NOT NULL
) AS `t1` ON `t`.`Id` = IIF(`t1`.`Exists` IS NOT NULL, `t1`.`Id`, NULL))
LEFT JOIN (
    SELECT `t2`.`Id`, `t2`.`MaumarEntity11818_Exists`, `t2`.`MaumarEntity11818_Name`
    FROM `Table` AS `t2`
    WHERE `t2`.`MaumarEntity11818_Exists` IS NOT NULL
) AS `t3` ON `t`.`Id` = IIF(`t3`.`MaumarEntity11818_Exists` IS NOT NULL, `t3`.`Id`, NULL)
GROUP BY `t1`.`AnotherEntity11818_Name`, `t3`.`MaumarEntity11818_Name`
""");
    }

    public override async Task Left_join_with_missing_key_values_on_both_sides(bool async)
    {
        await base.Left_join_with_missing_key_values_on_both_sides(async);

        AssertSql(
            """
SELECT `c`.`CustomerID`, `c`.`CustomerName`, IIF(`p`.`PostcodeID` IS NULL, '', `p`.`TownName`) AS `TownName`, IIF(`p`.`PostcodeID` IS NULL, '', `p`.`PostcodeValue`) AS `PostcodeValue`
FROM `Customers` AS `c`
LEFT JOIN `Postcodes` AS `p` ON `c`.`PostcodeID` = `p`.`PostcodeID`
""");
    }

    public override async Task Comparing_enum_casted_to_byte_with_int_parameter(bool async)
    {
        await base.Comparing_enum_casted_to_byte_with_int_parameter(async);

        AssertSql(
            """
@__bitterTaste_0='1'

SELECT `i`.`IceCreamId`, `i`.`Name`, `i`.`Taste`
FROM `IceCreams` AS `i`
WHERE `i`.`Taste` = @__bitterTaste_0
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
@__currentUserId_0='1'

SELECT IIF(`u`.`Id` IN (
        SELECT `u0`.`Id`
        FROM `Memberships` AS `m`
        INNER JOIN `Users` AS `u0` ON `m`.`UserId` = `u0`.`Id`
        WHERE `m`.`GroupId` IN (
            SELECT `m0`.`GroupId`
            FROM `Memberships` AS `m0`
            WHERE `m0`.`UserId` = @__currentUserId_0
        )
    ), TRUE, FALSE) AS `HasAccess`
FROM `Users` AS `u`
""");
    }

    public override async Task Unwrap_convert_node_over_projection_when_translating_contains_over_subquery_2(bool async)
    {
        await base.Unwrap_convert_node_over_projection_when_translating_contains_over_subquery_2(async);

        AssertSql(
            """
@__currentUserId_0='1'

SELECT IIF(`u`.`Id` IN (
        SELECT `u0`.`Id`
        FROM (`Memberships` AS `m`
        INNER JOIN `Groups` AS `g` ON `m`.`GroupId` = `g`.`Id`)
        INNER JOIN `Users` AS `u0` ON `m`.`UserId` = `u0`.`Id`
        WHERE `g`.`Id` IN (
            SELECT `g0`.`Id`
            FROM `Memberships` AS `m0`
            INNER JOIN `Groups` AS `g0` ON `m0`.`GroupId` = `g0`.`Id`
            WHERE `m0`.`UserId` = @__currentUserId_0
        )
    ), TRUE, FALSE) AS `HasAccess`
FROM `Users` AS `u`
""");
    }

    public override async Task Unwrap_convert_node_over_projection_when_translating_contains_over_subquery_3(bool async)
    {
        await base.Unwrap_convert_node_over_projection_when_translating_contains_over_subquery_3(async);

        AssertSql(
            """
@__currentUserId_0='1'

SELECT IIF(EXISTS (
        SELECT 1
        FROM `Memberships` AS `m`
        INNER JOIN `Users` AS `u0` ON `m`.`UserId` = `u0`.`Id`
        WHERE `m`.`GroupId` IN (
            SELECT `m0`.`GroupId`
            FROM `Memberships` AS `m0`
            WHERE `m0`.`UserId` = @__currentUserId_0
        ) AND `u0`.`Id` = `u`.`Id`), TRUE, FALSE) AS `HasAccess`
FROM `Users` AS `u`
""");
    }

    public override async Task GroupBy_aggregate_on_right_side_of_join(bool async)
    {
        await base.GroupBy_aggregate_on_right_side_of_join(async);

        AssertSql(
            """
@__orderId_0='123456'
@__orderId_0='123456'

SELECT `o`.`Id`, `o`.`CancellationDate`, `o`.`OrderId`, `o`.`ShippingDate`
FROM `OrderItems` AS `o`
INNER JOIN (
    SELECT `o0`.`OrderId` AS `Key`
    FROM `OrderItems` AS `o0`
    WHERE `o0`.`OrderId` = @__orderId_0
    GROUP BY `o0`.`OrderId`
) AS `o1` ON `o`.`OrderId` = `o1`.`Key`
WHERE `o`.`OrderId` = @__orderId_0
ORDER BY `o`.`OrderId`
""");
    }

    public override async Task Enum_with_value_converter_matching_take_value(bool async)
    {
        await base.Enum_with_value_converter_matching_take_value(async);

        AssertSql(
            """
@__orderItemType_1='MyType1' (Nullable = false) (Size = 255)
@__orderItemType_1='MyType1' (Nullable = false) (Size = 255)

SELECT `o1`.`Id`, IIF((
        SELECT TOP 1 `o3`.`Price`
        FROM `OrderItems` AS `o3`
        WHERE `o1`.`Id` = `o3`.`OrderId` AND `o3`.`Type` = @__orderItemType_1) IS NULL, 0.0, (
        SELECT TOP 1 `o3`.`Price`
        FROM `OrderItems` AS `o3`
        WHERE `o1`.`Id` = `o3`.`OrderId` AND `o3`.`Type` = @__orderItemType_1)) AS `SpecialSum`
FROM (
    SELECT TOP 1 `o`.`Id`
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
SELECT (
    SELECT MIN(`o`.`HourlyRate`)
    FROM `TimeSheets` AS `t0`
    LEFT JOIN `Order` AS `o` ON `t0`.`OrderId` = `o`.`Id`
    WHERE `t0`.`OrderId` IS NOT NULL AND `t`.`OrderId` = `t0`.`OrderId`) AS `HourlyRate`, (
    SELECT MIN(`c`.`Id`)
    FROM (`TimeSheets` AS `t1`
    INNER JOIN `Project` AS `p` ON `t1`.`ProjectId` = `p`.`Id`)
    LEFT JOIN `Customers` AS `c` ON `p`.`CustomerId` = `c`.`Id`
    WHERE (`t1`.`OrderId` IS NOT NULL AND `t`.`OrderId` = `t1`.`OrderId`) AND (`p`.`CustomerId` IS NOT NULL AND `c`.`Id` IS NOT NULL)) AS `CustomerId`, (
    SELECT MIN(`c0`.`Name`)
    FROM (`TimeSheets` AS `t2`
    INNER JOIN `Project` AS `p0` ON `t2`.`ProjectId` = `p0`.`Id`)
    LEFT JOIN `Customers` AS `c0` ON `p0`.`CustomerId` = `c0`.`Id`
    WHERE (`t2`.`OrderId` IS NOT NULL AND `t`.`OrderId` = `t2`.`OrderId`) AND (`p0`.`CustomerId` IS NOT NULL AND `c0`.`Id` IS NOT NULL)) AS `CustomerName`
FROM `TimeSheets` AS `t`
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
SELECT [t].[Value] AS [A], (
    SELECT MAX([t0].[Id])
    FROM [Tables] AS [t0]
    WHERE [t0].[Value] = MAX([t].[Id]) * 6 OR ([t0].[Value] IS NULL AND MAX([t].[Id]) IS NULL)) AS [B]
FROM [Tables] AS [t]
GROUP BY [t].[Value]
""");
    }

    public override async Task Group_by_aggregate_in_subquery_projection_after_group_by(bool async)
    {
        await base.Group_by_aggregate_in_subquery_projection_after_group_by(async);

        AssertSql(
            """
SELECT [t].[Value] AS [A], COALESCE(SUM([t].[Id]), 0) AS [B], COALESCE((
    SELECT TOP(1) COALESCE(SUM([t].[Id]), 0) + COALESCE(SUM([t0].[Id]), 0)
    FROM [Tables] AS [t0]
    GROUP BY [t0].[Value]
    ORDER BY (SELECT 1)), 0) AS [C]
FROM [Tables] AS [t]
GROUP BY [t].[Value]
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
SELECT [c1].[SomeNullableDateTime]
FROM [Parents] AS [p]
INNER JOIN (
    SELECT [c0].[ParentId], [c0].[SomeNullableDateTime], [c0].[SomeOtherNullableDateTime]
    FROM (
        SELECT [c].[ParentId], [c].[SomeNullableDateTime], [c].[SomeOtherNullableDateTime], ROW_NUMBER() OVER(PARTITION BY [c].[ParentId] ORDER BY [c].[SomeInteger]) AS [row]
        FROM [Child] AS [c]
        WHERE [c].[SomeNullableDateTime] IS NULL
    ) AS [c0]
    WHERE [c0].[row] <= 1
) AS [c1] ON [p].[Id] = [c1].[ParentId]
WHERE [c1].[SomeOtherNullableDateTime] IS NOT NULL
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
@__date_0='2012-12-12T00:00:00.0000000' (DbType = DateTime)

SELECT `m`.`Id`, `m`.`SomeDate`
FROM `MyEntities` AS `m`
WHERE `m`.`SomeDate` = CDATE(@__date_0)
""",
    //
    """
@__date_0='2012-12-12T00:00:00.0000000' (DbType = DateTime)

SELECT `m`.`Id`, `m`.`SomeDate`
FROM `MyEntities` AS `m`
WHERE `dbo`.`ModifyDate`(`m`.`SomeDate`) = CDATE(@__date_0)
""");
    }

    public override async Task Pushdown_does_not_add_grouping_key_to_projection_when_distinct_is_applied(bool async)
    {
        await base.Pushdown_does_not_add_grouping_key_to_projection_when_distinct_is_applied(async);

        AssertSql(
    """
SELECT TOP 123456 `t`.`JSON`
FROM `TableDatas` AS `t`
INNER JOIN (
    SELECT DISTINCT `i`.`Parcel`
    FROM `IndexDatas` AS `i`
    WHERE `i`.`Parcel` = 'some condition'
    GROUP BY `i`.`Parcel`, `i`.`RowId`
    HAVING COUNT(*) = 1
) AS `i0` ON `t`.`ParcelNumber` = `i0`.`Parcel`
WHERE `t`.`TableId` = 123
ORDER BY `t`.`ParcelNumber`
""");
    }

    public override async Task Filter_on_nested_DTO_with_interface_gets_simplified_correctly(bool async)
    {
        await base.Filter_on_nested_DTO_with_interface_gets_simplified_correctly(async);

        AssertSql(
            """
SELECT `c`.`Id`, `c`.`CompanyId`, IIF(`c0`.`Id` IS NOT NULL, TRUE, FALSE), `c0`.`Id`, `c0`.`CompanyName`, `c0`.`CountryId`, `c1`.`Id`, `c1`.`CountryName`
FROM (`Customers` AS `c`
LEFT JOIN `Companies` AS `c0` ON `c`.`CompanyId` = `c0`.`Id`)
LEFT JOIN `Countries` AS `c1` ON `c0`.`CountryId` = `c1`.`Id`
WHERE IIF(`c0`.`Id` IS NOT NULL, `c1`.`CountryName`, NULL) = 'COUNTRY'
""");
    }
}
