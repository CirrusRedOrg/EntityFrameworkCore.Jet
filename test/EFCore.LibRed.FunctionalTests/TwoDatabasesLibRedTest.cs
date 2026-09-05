// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using EntityFrameworkCore.LibRed.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.LibRed.FunctionalTests;
#nullable disable
public class TwoDatabasesLibRedTest(LibRedFixture fixture) : TwoDatabasesTestBase(fixture), IClassFixture<LibRedFixture>
{
    protected new LibRedFixture Fixture
        => (LibRedFixture)base.Fixture;

    protected override DbContextOptionsBuilder CreateTestOptions(
        DbContextOptionsBuilder optionsBuilder,
        bool withConnectionString = false,
        bool withNullConnectionString = false)
        => withConnectionString
            ? withNullConnectionString
                ? optionsBuilder.UseLibRed((string)null, b => b.UseSqlMode())
                : optionsBuilder.UseLibRed(DummyConnectionString, b => b.UseSqlMode())
            : optionsBuilder.UseLibRed(LibRedTestStore.CreateConnectionString("TwoDatabasesLibRedTest"), b => b.UseSqlMode());

    protected override TwoDatabasesWithDataContext CreateBackingContext(string databaseName)
        => new(Fixture.CreateOptions(LibRedTestStore.Create(databaseName)));

    protected override string DummyConnectionString
        => "Database=DoesNotExist";
}
