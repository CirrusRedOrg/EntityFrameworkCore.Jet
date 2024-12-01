// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.EntityFrameworkCore.Update;
using Xunit;
#nullable disable
namespace EntityFrameworkCore.Jet.FunctionalTests.Update;

public class NonSharedModelUpdatesJetTest : NonSharedModelUpdatesTestBase
{
    public override async Task Principal_and_dependent_roundtrips_with_cycle_breaking(bool async)
    {
        await base.Principal_and_dependent_roundtrips_with_cycle_breaking(async);

        AssertSql(
            $"""
@p0='AC South' (Size = 255)

INSERT INTO `AuthorsClub` (`Name`)
VALUES ({AssertSqlHelper.Parameter("@p0")});
SELECT `Id`
FROM `AuthorsClub`
WHERE @@ROWCOUNT = 1 AND `Id` = @@identity;
""",
            //
            $"""
@p1='1'
@p2='Alice' (Size = 255)

INSERT INTO `Author` (`AuthorsClubId`, `Name`)
VALUES ({AssertSqlHelper.Parameter("@p1")}, {AssertSqlHelper.Parameter("@p2")});
SELECT `Id`
FROM `Author`
WHERE @@ROWCOUNT = 1 AND `Id` = @@identity;
""",
            //
            $"""
@p3='1'
@p4=NULL (Size = 255)

INSERT INTO `Book` (`AuthorId`, `Title`)
VALUES ({AssertSqlHelper.Parameter("@p3")}, {AssertSqlHelper.Parameter("@p4")});
SELECT `Id`
FROM `Book`
WHERE @@ROWCOUNT = 1 AND `Id` = @@identity;
""",
            //
            """
SELECT TOP 2 `b`.`Id`, `b`.`AuthorId`, `b`.`Title`, `a`.`Id`, `a`.`AuthorsClubId`, `a`.`Name`
FROM `Book` AS `b`
INNER JOIN `Author` AS `a` ON `b`.`AuthorId` = `a`.`Id`
""",
            //
            $"""
@p0='AC North' (Size = 255)

INSERT INTO `AuthorsClub` (`Name`)
VALUES ({AssertSqlHelper.Parameter("@p0")});
SELECT `Id`
FROM `AuthorsClub`
WHERE @@ROWCOUNT = 1 AND `Id` = @@identity;
""",
            //
            $"""
@p1='2'
@p2='Author of the year 2023' (Size = 255)

INSERT INTO `Author` (`AuthorsClubId`, `Name`)
VALUES ({AssertSqlHelper.Parameter("@p1")}, {AssertSqlHelper.Parameter("@p2")});
SELECT `Id`
FROM `Author`
WHERE @@ROWCOUNT = 1 AND `Id` = @@identity;
""",
            //
            $"""
@p3='2'
@p4='1'

UPDATE `Book` SET `AuthorId` = {AssertSqlHelper.Parameter("@p3")}
WHERE `Id` = {AssertSqlHelper.Parameter("@p4")};
SELECT @@ROWCOUNT;
""",
            //
            $"""
@p0='1'

DELETE FROM `Author`
WHERE `Id` = {AssertSqlHelper.Parameter("@p0")};
SELECT @@ROWCOUNT;
""");
    }

    /*[ConditionalFact] // Issue #29502
    public virtual async Task Bulk_insert_result_set_mapping()
    {
        var contextFactory = await InitializeAsync<DbContext>(
            onModelCreating: mb =>
            {
                mb.Entity<User>().ToTable("Users");
                mb.Entity<DailyDigest>().ToTable("DailyDigests");
            },
            createTestStore: () => JetTestStore.GetOrCreateWithScriptPath(
                "Issue29502",
                Path.Combine("Update", "Issue29502.sql"),
                shared: false));

        await ExecuteWithStrategyInTransactionAsync(
            contextFactory,
            async context =>
            {
                var digests = await context.Set<User>()
                    .OrderBy(u => u.TimeCreatedUtc)
                    .Take(23)
                    .Select(u => new DailyDigest { User = u })
                    .ToListAsync();

                foreach (var digest in digests)
                {
                    context.Set<DailyDigest>().Add(digest);
                }

                await context.SaveChangesAsync();
            });
    }*/

    public class User
    {
        public string Id { get; set; } = null!;
        public DateTime TimeCreatedUtc { get; set; }
        public ICollection<DailyDigest> DailyDigests { get; set; } = null!;
    }

    public class DailyDigest
    {
        public int Id { get; set; }
        public User User { get; set; }
    }

    private void AssertSql(params string[] expected)
        => TestSqlLoggerFactory.AssertBaseline(expected);

    protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;
}
