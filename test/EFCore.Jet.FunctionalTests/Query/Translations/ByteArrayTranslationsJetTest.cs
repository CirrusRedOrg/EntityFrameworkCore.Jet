// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.EntityFrameworkCore.Query.Translations;
using Microsoft.EntityFrameworkCore.TestUtilities;
using System;
using System.Threading.Tasks;
using Xunit;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query.Translations;

public class ByteArrayTranslationsJetTest : ByteArrayTranslationsTestBase<BasicTypesQueryJetFixture>
{
    public ByteArrayTranslationsJetTest(BasicTypesQueryJetFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    // Length, Index and First all need the EXACT byte length of a byte array, which Jet cannot give: LenB
    // reports the UTF-16 byte count and so rounds an odd length up to even. The provider refuses rather than
    // returning a wrong number, and points at EF.Functions.ByteArrayLength, whose remarks state the one case
    // it still gets wrong (data ending in 0x00, indistinguishable from the zero pad).
    //
    // The same scenarios were adapted this way in the GearsOfWar tests long ago; EF10 moved them into the
    // BasicTypesEntities suite, and these copies arrived carrying upstream's SQL Server baselines.
    public override async Task Length()
        => await Assert.ThrowsAsync<InvalidOperationException>(() => base.Length());

    public override async Task Index()
        => await Assert.ThrowsAsync<InvalidOperationException>(() => base.Index());

    public override async Task First()
        => await Assert.ThrowsAsync<InvalidOperationException>(() => base.First());

    public override async Task Contains_with_constant()
    {
        await base.Contains_with_constant();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE INSTR(1, STRCONV(`b`.`ByteArray`, 64), 0x01, 0) > 0
""");
    }

    public override async Task Contains_with_parameter()
    {
        await base.Contains_with_parameter();

        AssertSql(
            """
@someByte='1' (Size = 1)

SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE INSTR(1, STRCONV(`b`.`ByteArray`, 64), CHR(@someByte), 0) > 0
""");
    }

    public override async Task Contains_with_column()
    {
        await base.Contains_with_column();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE INSTR(1, STRCONV(`b`.`ByteArray`, 64), CHR(`b`.`Byte`), 0) > 0
""");
    }

    public override async Task Any()
    {
        await base.Any();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE LENB(`b`.`ByteArray`) > 0
""");
    }

    public override async Task SequenceEqual()
    {
        await base.SequenceEqual();

        AssertSql(
            """
@byteArrayParam='0xDEADBEEF' (Size = 510)

SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`ByteArray` = @byteArrayParam
""");
    }

    [Fact]
    public virtual void Check_all_tests_overridden()
        => TestHelpers.AssertAllMethodsOverridden(GetType());

    private void AssertSql(params string[] expected)
        => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);
}
