// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using EntityFrameworkCore.LibRed.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.LibRed.FunctionalTests.Query;

public class TPTInheritanceQueryLibRedFixture : TPTInheritanceQueryFixture
{
    protected override ITestStoreFactory TestStoreFactory
        => LibRedTestStoreFactory.Instance;

    protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
    {
        //modelBuilder.UseKeySequences();

        base.OnModelCreating(modelBuilder, context);
    }
}
