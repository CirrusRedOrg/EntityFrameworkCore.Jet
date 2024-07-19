// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Jet.FunctionalTests;
#nullable disable
public class TwoDatabasesJetTest(JetFixture fixture) : TwoDatabasesTestBase(fixture), IClassFixture<JetFixture>
{
    protected new JetFixture Fixture
        => (JetFixture)base.Fixture;

    protected override DbContextOptionsBuilder CreateTestOptions(
        DbContextOptionsBuilder optionsBuilder,
        bool withConnectionString = false,
        bool withNullConnectionString = false)
        => withConnectionString
            ? withNullConnectionString
                ? optionsBuilder.UseJet((string)null)
                : optionsBuilder.UseJet(DummyConnectionString)
            : optionsBuilder.UseJet(JetTestStore.CreateConnectionString("TwoDatabasesJetTest"), TestEnvironment.DataAccessProviderFactory);

    protected override TwoDatabasesWithDataContext CreateBackingContext(string databaseName)
        => new(Fixture.CreateOptions(JetTestStore.Create(databaseName)));

    protected override string DummyConnectionString
        => "Database=DoesNotExist";
}
