// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query.Associations.ComplexJson;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query.Associations.ComplexJson;

public class ComplexJsonJetFixture : ComplexJsonRelationalFixtureBase
{
    protected override ITestStoreFactory TestStoreFactory
        => JetTestStoreFactory.Instance;

    public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
    {
        var options = base.AddOptions(builder);
        return options;
    }
}
