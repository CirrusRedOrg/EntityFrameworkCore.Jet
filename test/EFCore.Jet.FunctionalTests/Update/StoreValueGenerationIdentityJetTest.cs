// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using System;
using System.Threading.Tasks;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.TestModels.StoreValueGenerationModel;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.EntityFrameworkCore.Update;
using Xunit;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.FunctionalTests.Update;

#nullable enable

public class StoreValueGenerationIdentityJetTest : StoreValueGenerationTestBase<
    StoreValueGenerationIdentityJetTest.StoreValueGenerationIdentityJetFixture>
{
    public StoreValueGenerationIdentityJetTest(
        StoreValueGenerationIdentityJetFixture fixture,
        ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    protected override bool ShouldCreateImplicitTransaction(
        EntityState firstOperationType,
        EntityState? secondOperationType,
        GeneratedValues generatedValues,
        bool withSameEntityType)
    {
        if (generatedValues is GeneratedValues.Some or GeneratedValues.All
            && firstOperationType is EntityState.Added or EntityState.Modified)
        {
            //The GeneratedValues.Some table used to use a complex computed column (Data1 = Data2 + 1). This required getting the output when add or modify.
            //Since we have overridden it so that we specifiy both Data1 and Data2, the only output is the Id which is only with Add.
            //Modify does not require an output and will not have an implicit transaction as long as it is the only operation
            //If there is a second operation, then it will require an implicit transaction
            return firstOperationType is not EntityState.Modified || generatedValues is not GeneratedValues.Some || secondOperationType is not null;
        }

        return secondOperationType is not null;
    }

    protected override int ShouldExecuteInNumberOfCommands(
        EntityState firstOperationType,
        EntityState? secondOperationType,
        GeneratedValues generatedValues,
        bool withDatabaseGenerated)
        => secondOperationType is null ? 1 : 2;

    #region Single operation

    public override async Task Add_with_generated_values(bool async)
    {
        await base.Add_with_generated_values(async);

        AssertSql(
$"""
@p0='1001'
@p1='1000'

INSERT INTO `WithSomeDatabaseGenerated` (`Data1`, `Data2`)
VALUES ({AssertSqlHelper.Parameter("@p0")}, {AssertSqlHelper.Parameter("@p1")});
SELECT `Id`
FROM `WithSomeDatabaseGenerated`
WHERE @@ROWCOUNT = 1 AND `Id` = @@identity;
""");
    }

    public override async Task Add_with_no_generated_values(bool async)
    {
        await base.Add_with_no_generated_values(async);

        AssertSql(
$"""
@p0='100'
@p1='1000'
@p2='1000'

INSERT INTO `WithNoDatabaseGenerated` (`Id`, `Data1`, `Data2`)
VALUES ({AssertSqlHelper.Parameter("@p0")}, {AssertSqlHelper.Parameter("@p1")}, {AssertSqlHelper.Parameter("@p2")});
""");
    }

    public override async Task Add_with_all_generated_values(bool async)
    {
        await base.Add_with_all_generated_values(async);

        AssertSql(
"""
INSERT INTO `WithAllDatabaseGenerated`
DEFAULT VALUES;
SELECT `Id`, `Data1`, `Data2`
FROM `WithAllDatabaseGenerated`
WHERE @@ROWCOUNT = 1 AND `Id` = @@identity;
""");
    }

    public override async Task Modify_with_generated_values(bool async)
    {
        await base.Modify_with_generated_values(async);

        AssertSql(
$"""
@p0='1001'
@p1='1000'
@p2='1'

UPDATE `WithSomeDatabaseGenerated` SET `Data1` = {AssertSqlHelper.Parameter("@p0")}, `Data2` = {AssertSqlHelper.Parameter("@p1")}
WHERE `Id` = {AssertSqlHelper.Parameter("@p2")};
SELECT @@ROWCOUNT;
""");
    }

    public override async Task Modify_with_no_generated_values(bool async)
    {
        await base.Modify_with_no_generated_values(async);

        AssertSql(
$"""
@p0='1000'
@p1='1000'
@p2='1'

UPDATE `WithNoDatabaseGenerated` SET `Data1` = {AssertSqlHelper.Parameter("@p0")}, `Data2` = {AssertSqlHelper.Parameter("@p1")}
WHERE `Id` = {AssertSqlHelper.Parameter("@p2")};
SELECT @@ROWCOUNT;
""");
    }

    public override async Task Delete(bool async)
    {
        await base.Delete(async);

        AssertSql(
$"""
@p0='1'

DELETE FROM `WithSomeDatabaseGenerated`
WHERE `Id` = {AssertSqlHelper.Parameter("@p0")};
SELECT @@ROWCOUNT;
""");
    }

    #endregion Single operation

    #region Same two operations with same entity type

    public override async Task Add_Add_with_same_entity_type_and_generated_values(bool async)
    {
        await base.Add_Add_with_same_entity_type_and_generated_values(async);

        AssertSql(
            $"""
            @p0='1001'
            @p1='1000'

            INSERT INTO `WithSomeDatabaseGenerated` (`Data1`, `Data2`)
            VALUES ({AssertSqlHelper.Parameter("@p0")}, {AssertSqlHelper.Parameter("@p1")});
            SELECT `Id`
            FROM `WithSomeDatabaseGenerated`
            WHERE @@ROWCOUNT = 1 AND `Id` = @@identity;
            """,
            //
            $"""
            @p0='1002'
            @p1='1001'

            INSERT INTO `WithSomeDatabaseGenerated` (`Data1`, `Data2`)
            VALUES ({AssertSqlHelper.Parameter("@p0")}, {AssertSqlHelper.Parameter("@p1")});
            SELECT `Id`
            FROM `WithSomeDatabaseGenerated`
            WHERE @@ROWCOUNT = 1 AND `Id` = @@identity;
            """);
    }

    public override async Task Add_Add_with_same_entity_type_and_no_generated_values(bool async)
    {
        await base.Add_Add_with_same_entity_type_and_no_generated_values(async);

        AssertSql(
$"""
@p0='100'
@p1='1000'
@p2='1000'

INSERT INTO `WithNoDatabaseGenerated` (`Id`, `Data1`, `Data2`)
VALUES ({AssertSqlHelper.Parameter("@p0")}, {AssertSqlHelper.Parameter("@p1")}, {AssertSqlHelper.Parameter("@p2")});
""",
            //
$"""
@p0='101'
@p1='1001'
@p2='1001'

INSERT INTO `WithNoDatabaseGenerated` (`Id`, `Data1`, `Data2`)
VALUES ({AssertSqlHelper.Parameter("@p0")}, {AssertSqlHelper.Parameter("@p1")}, {AssertSqlHelper.Parameter("@p2")});
""");
    }

    public override async Task Add_Add_with_same_entity_type_and_all_generated_values(bool async)
    {
        await base.Add_Add_with_same_entity_type_and_all_generated_values(async);

        AssertSql(
"""
INSERT INTO `WithAllDatabaseGenerated`
DEFAULT VALUES;
SELECT `Id`, `Data1`, `Data2`
FROM `WithAllDatabaseGenerated`
WHERE @@ROWCOUNT = 1 AND `Id` = @@identity;
""",
            //
"""
INSERT INTO `WithAllDatabaseGenerated`
DEFAULT VALUES;
SELECT `Id`, `Data1`, `Data2`
FROM `WithAllDatabaseGenerated`
WHERE @@ROWCOUNT = 1 AND `Id` = @@identity;
""");
    }

    public override async Task Modify_Modify_with_same_entity_type_and_generated_values(bool async)
    {
        await base.Modify_Modify_with_same_entity_type_and_generated_values(async);

        AssertSql(
$"""
@p0='1001'
@p1='1000'
@p2='1'

UPDATE `WithSomeDatabaseGenerated` SET `Data1` = {AssertSqlHelper.Parameter("@p0")}, `Data2` = {AssertSqlHelper.Parameter("@p1")}
WHERE `Id` = {AssertSqlHelper.Parameter("@p2")};
SELECT @@ROWCOUNT;
""",
            //
$"""
@p0='1002'
@p1='1001'
@p2='2'

UPDATE `WithSomeDatabaseGenerated` SET `Data1` = {AssertSqlHelper.Parameter("@p0")}, `Data2` = {AssertSqlHelper.Parameter("@p1")}
WHERE `Id` = {AssertSqlHelper.Parameter("@p2")};
SELECT @@ROWCOUNT;
""");
    }

    public override async Task Modify_Modify_with_same_entity_type_and_no_generated_values(bool async)
    {
        await base.Modify_Modify_with_same_entity_type_and_no_generated_values(async);

        AssertSql(
$"""
@p0='1000'
@p1='1000'
@p2='1'

UPDATE `WithNoDatabaseGenerated` SET `Data1` = {AssertSqlHelper.Parameter("@p0")}, `Data2` = {AssertSqlHelper.Parameter("@p1")}
WHERE `Id` = {AssertSqlHelper.Parameter("@p2")};
SELECT @@ROWCOUNT;
""",
            //
$"""
@p0='1001'
@p1='1001'
@p2='2'

UPDATE `WithNoDatabaseGenerated` SET `Data1` = {AssertSqlHelper.Parameter("@p0")}, `Data2` = {AssertSqlHelper.Parameter("@p1")}
WHERE `Id` = {AssertSqlHelper.Parameter("@p2")};
SELECT @@ROWCOUNT;
""");
    }

    public override async Task Delete_Delete_with_same_entity_type(bool async)
    {
        await base.Delete_Delete_with_same_entity_type(async);

        AssertSql(
$"""
@p0='1'

DELETE FROM `WithSomeDatabaseGenerated`
WHERE `Id` = {AssertSqlHelper.Parameter("@p0")};
SELECT @@ROWCOUNT;
""",
            //
$"""
@p0='2'

DELETE FROM `WithSomeDatabaseGenerated`
WHERE `Id` = {AssertSqlHelper.Parameter("@p0")};
SELECT @@ROWCOUNT;
""");
    }

    #endregion Same two operations with same entity type

    #region Same two operations with different entity types

    public override async Task Add_Add_with_different_entity_types_and_generated_values(bool async)
    {
        await base.Add_Add_with_different_entity_types_and_generated_values(async);

        AssertSql(
            $"""
            @p0='1001'
            @p1='1000'

            INSERT INTO `WithSomeDatabaseGenerated` (`Data1`, `Data2`)
            VALUES ({AssertSqlHelper.Parameter("@p0")}, {AssertSqlHelper.Parameter("@p1")});
            SELECT `Id`
            FROM `WithSomeDatabaseGenerated`
            WHERE @@ROWCOUNT = 1 AND `Id` = @@identity;
            """,
            //
            $"""
            @p0='1002'
            @p1='1001'

            INSERT INTO `WithSomeDatabaseGenerated2` (`Data1`, `Data2`)
            VALUES ({AssertSqlHelper.Parameter("@p0")}, {AssertSqlHelper.Parameter("@p1")});
            SELECT `Id`
            FROM `WithSomeDatabaseGenerated2`
            WHERE @@ROWCOUNT = 1 AND `Id` = @@identity;
            """);
    }

    public override async Task Add_Add_with_different_entity_types_and_no_generated_values(bool async)
    {
        await base.Add_Add_with_different_entity_types_and_no_generated_values(async);

        AssertSql(
$"""
@p0='100'
@p1='1000'
@p2='1000'

INSERT INTO `WithNoDatabaseGenerated` (`Id`, `Data1`, `Data2`)
VALUES ({AssertSqlHelper.Parameter("@p0")}, {AssertSqlHelper.Parameter("@p1")}, {AssertSqlHelper.Parameter("@p2")});
""",
            //
$"""
@p0='101'
@p1='1001'
@p2='1001'

INSERT INTO `WithNoDatabaseGenerated2` (`Id`, `Data1`, `Data2`)
VALUES ({AssertSqlHelper.Parameter("@p0")}, {AssertSqlHelper.Parameter("@p1")}, {AssertSqlHelper.Parameter("@p2")});
""");
    }

    public override async Task Add_Add_with_different_entity_types_and_all_generated_values(bool async)
    {
        await base.Add_Add_with_different_entity_types_and_all_generated_values(async);

        AssertSql(
"""
INSERT INTO `WithAllDatabaseGenerated`
DEFAULT VALUES;
SELECT `Id`, `Data1`, `Data2`
FROM `WithAllDatabaseGenerated`
WHERE @@ROWCOUNT = 1 AND `Id` = @@identity;
""",
            //
"""
INSERT INTO `WithAllDatabaseGenerated2`
DEFAULT VALUES;
SELECT `Id`, `Data1`, `Data2`
FROM `WithAllDatabaseGenerated2`
WHERE @@ROWCOUNT = 1 AND `Id` = @@identity;
""");
    }

    public override async Task Modify_Modify_with_different_entity_types_and_generated_values(bool async)
    {
        await base.Modify_Modify_with_different_entity_types_and_generated_values(async);

        AssertSql(
$"""
@p0='1001'
@p1='1000'
@p2='1'

UPDATE `WithSomeDatabaseGenerated` SET `Data1` = {AssertSqlHelper.Parameter("@p0")}, `Data2` = {AssertSqlHelper.Parameter("@p1")}
WHERE `Id` = {AssertSqlHelper.Parameter("@p2")};
SELECT @@ROWCOUNT;
""",
            //
$"""
@p0='1002'
@p1='1001'
@p2='2'

UPDATE `WithSomeDatabaseGenerated2` SET `Data1` = {AssertSqlHelper.Parameter("@p0")}, `Data2` = {AssertSqlHelper.Parameter("@p1")}
WHERE `Id` = {AssertSqlHelper.Parameter("@p2")};
SELECT @@ROWCOUNT;
""");
    }

    public override async Task Modify_Modify_with_different_entity_types_and_no_generated_values(bool async)
    {
        await base.Modify_Modify_with_different_entity_types_and_no_generated_values(async);

        AssertSql(
$"""
@p0='1000'
@p1='1000'
@p2='1'

UPDATE `WithNoDatabaseGenerated` SET `Data1` = {AssertSqlHelper.Parameter("@p0")}, `Data2` = {AssertSqlHelper.Parameter("@p1")}
WHERE `Id` = {AssertSqlHelper.Parameter("@p2")};
SELECT @@ROWCOUNT;
""",
            //
$"""
@p0='1001'
@p1='1001'
@p2='2'

UPDATE `WithNoDatabaseGenerated2` SET `Data1` = {AssertSqlHelper.Parameter("@p0")}, `Data2` = {AssertSqlHelper.Parameter("@p1")}
WHERE `Id` = {AssertSqlHelper.Parameter("@p2")};
SELECT @@ROWCOUNT;
""");
    }

    public override async Task Delete_Delete_with_different_entity_types(bool async)
    {
        await base.Delete_Delete_with_different_entity_types(async);

        AssertSql(
$"""
@p0='1'

DELETE FROM `WithSomeDatabaseGenerated`
WHERE `Id` = {AssertSqlHelper.Parameter("@p0")};
SELECT @@ROWCOUNT;
""",
            //
$"""
@p0='2'

DELETE FROM `WithSomeDatabaseGenerated2`
WHERE `Id` = {AssertSqlHelper.Parameter("@p0")};
SELECT @@ROWCOUNT;
""");
    }

    #endregion Same two operations with different entity types

    #region Different two operations

    [ConditionalTheory]
    [MemberData(nameof(IsAsyncData))]
    public override async Task Delete_Add_with_same_entity_types(bool async)
    {
        await Test(EntityState.Deleted, EntityState.Added, GeneratedValues.Some, async, withSameEntityType: true);

        AssertSql(
$"""
@p0='1'

DELETE FROM `WithSomeDatabaseGenerated`
WHERE `Id` = {AssertSqlHelper.Parameter("@p0")};
SELECT @@ROWCOUNT;
""",
            //
$"""
@p0='1002'
@p1='1001'

INSERT INTO `WithSomeDatabaseGenerated` (`Data1`, `Data2`)
VALUES ({AssertSqlHelper.Parameter("@p0")}, {AssertSqlHelper.Parameter("@p1")});
SELECT `Id`
FROM `WithSomeDatabaseGenerated`
WHERE @@ROWCOUNT = 1 AND `Id` = @@identity;
""");
    }

    #endregion Different two operations

    protected override async Task Test(
        EntityState firstOperationType,
        EntityState? secondOperationType,
        GeneratedValues generatedValues,
        bool async,
        bool withSameEntityType = true)
    {
        await using var context = CreateContext();

        var firstDbSet = generatedValues switch
        {
            GeneratedValues.Some => context.WithSomeDatabaseGenerated,
            GeneratedValues.None => context.WithNoDatabaseGenerated,
            GeneratedValues.All => context.WithAllDatabaseGenerated,
            _ => throw new ArgumentOutOfRangeException(nameof(generatedValues))
        };

        var secondDbSet = secondOperationType is null
            ? null
            : (generatedValues, withSameEntityType) switch
            {
                (GeneratedValues.Some, true) => context.WithSomeDatabaseGenerated,
                (GeneratedValues.Some, false) => context.WithSomeDatabaseGenerated2,
                (GeneratedValues.None, true) => context.WithNoDatabaseGenerated,
                (GeneratedValues.None, false) => context.WithNoDatabaseGenerated2,
                (GeneratedValues.All, true) => context.WithAllDatabaseGenerated,
                (GeneratedValues.All, false) => context.WithAllDatabaseGenerated2,
                _ => throw new ArgumentOutOfRangeException(nameof(generatedValues))
            };

        StoreValueGenerationData first;
        StoreValueGenerationData? second;

        switch (firstOperationType)
        {
            case EntityState.Added:
                switch (generatedValues)
                {
                    case GeneratedValues.Some:
                        first = new StoreValueGenerationData { Data2 = 1000 };
                        first.Data1 = first.Data2 + 1;
                        firstDbSet.Add(first);
                        break;
                    case GeneratedValues.None:
                        first = new StoreValueGenerationData
                        {
                            Id = 100,
                            Data1 = 1000,
                            Data2 = 1000
                        };
                        firstDbSet.Add(first);
                        break;
                    case GeneratedValues.All:
                        first = new StoreValueGenerationData();
                        firstDbSet.Add(first);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(generatedValues));
                }

                break;

            case EntityState.Modified:
                switch (generatedValues)
                {
                    case GeneratedValues.Some:
                        first = firstDbSet.OrderBy(w => w.Id).First();
                        first.Data2 = 1000;
                        first.Data1 = first.Data2 + 1;
                        break;
                    case GeneratedValues.None:
                        first = firstDbSet.OrderBy(w => w.Id).First();
                        (first.Data1, first.Data2) = (1000, 1000);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(generatedValues));
                }

                break;

            case EntityState.Deleted:
                switch (generatedValues)
                {
                    case GeneratedValues.Some:
                        first = firstDbSet.OrderBy(w => w.Id).First();
                        context.Remove(first);
                        break;
                    case GeneratedValues.None:
                        first = firstDbSet.OrderBy(w => w.Id).First();
                        context.Remove(first);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(generatedValues));
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(firstOperationType));
        }

        switch (secondOperationType)
        {
            case EntityState.Added:
                switch (generatedValues)
                {
                    case GeneratedValues.Some:
                        second = new StoreValueGenerationData { Data2 = 1001 };
                        second.Data1 = second.Data2 + 1;
                        secondDbSet!.Add(second);
                        break;
                    case GeneratedValues.None:
                        second = new StoreValueGenerationData
                        {
                            Id = 101,
                            Data1 = 1001,
                            Data2 = 1001
                        };
                        secondDbSet!.Add(second);
                        break;
                    case GeneratedValues.All:
                        second = new StoreValueGenerationData();
                        secondDbSet!.Add(second);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(generatedValues));
                }

                break;

            case EntityState.Modified:
                switch (generatedValues)
                {
                    case GeneratedValues.Some:
                        second = secondDbSet!.OrderBy(w => w.Id).Skip(1).First();
                        second.Data2 = 1001;
                        second.Data1 = second.Data2 + 1;
                        break;
                    case GeneratedValues.None:
                        second = secondDbSet!.OrderBy(w => w.Id).Skip(1).First();
                        (second.Data1, second.Data2) = (1001, 1001);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(generatedValues));
                }

                break;

            case EntityState.Deleted:
                switch (generatedValues)
                {
                    case GeneratedValues.Some:
                        second = secondDbSet!.OrderBy(w => w.Id).Skip(1).First();
                        context.Remove(second);
                        break;
                    case GeneratedValues.None:
                        second = secondDbSet!.OrderBy(w => w.Id).Skip(1).First();
                        context.Remove(second);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(generatedValues));
                }

                break;

            case null:
                second = null;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(firstOperationType));
        }

        // Execute
        Fixture.ListLoggerFactory.Clear();

        if (async)
        {
            await context.SaveChangesAsync();
        }
        else
        {
            context.SaveChanges();
        }

        // Make sure a transaction was created (or not)
        if (ShouldCreateImplicitTransaction(firstOperationType, secondOperationType, generatedValues, withSameEntityType))
        {
            Assert.Contains(Fixture.ListLoggerFactory.Log, l => l.Id == RelationalEventId.TransactionStarted);
            Assert.Contains(Fixture.ListLoggerFactory.Log, l => l.Id == RelationalEventId.TransactionCommitted);
        }
        else
        {
            Assert.DoesNotContain(Fixture.ListLoggerFactory.Log, l => l.Id == RelationalEventId.TransactionStarted);
            Assert.DoesNotContain(Fixture.ListLoggerFactory.Log, l => l.Id == RelationalEventId.TransactionCommitted);
        }

        // Make sure the updates executed in the expected number of commands
        Assert.Equal(
            ShouldExecuteInNumberOfCommands(firstOperationType, secondOperationType, generatedValues, withSameEntityType),
            Fixture.ListLoggerFactory.Log.Count(l => l.Id == RelationalEventId.CommandExecuted));

        // To make sure generated values have been propagated, re-load the rows from the database and compare
        context.ChangeTracker.Clear();

        using (Fixture.TestSqlLoggerFactory.SuspendRecordingEvents())
        {
            if (firstOperationType != EntityState.Deleted)
            {
                Assert.Equal(await firstDbSet.FindAsync(first.Id), first);
            }

            if (second is not null && secondOperationType != EntityState.Deleted)
            {
                Assert.Equal(await secondDbSet!.FindAsync(second.Id), second);
            }
        }

        if (!ShouldCreateImplicitTransaction(firstOperationType, secondOperationType, generatedValues, withSameEntityType))
        {
            //Assert.Contains("SET IMPLICIT_TRANSACTIONS OFF", Fixture.TestSqlLoggerFactory.SqlStatements[0]);
        }
    }

    public class StoreValueGenerationIdentityJetFixture : StoreValueGenerationJetFixtureBase
    {
        protected override string StoreName
            => "StoreValueGenerationIdentityTest";

        protected override ITestStoreFactory TestStoreFactory
            => JetTestStoreFactory.Instance;
    }
}
