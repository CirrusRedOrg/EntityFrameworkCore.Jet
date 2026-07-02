// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using EntityFrameworkCore.LibRed.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query.Associations.ComplexJson;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.LibRed.FunctionalTests.Query.Associations.ComplexJson;

public class ComplexJsonLibRedFixture : ComplexJsonRelationalFixtureBase
{
    protected override ITestStoreFactory TestStoreFactory
        => LibRedTestStoreFactory.Instance;

    public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
    {
        var options = base.AddOptions(builder);
        return options;
    }
}
