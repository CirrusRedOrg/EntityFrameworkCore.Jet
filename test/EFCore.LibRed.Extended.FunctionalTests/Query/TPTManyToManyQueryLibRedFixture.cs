// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using EntityFrameworkCore.LibRed.Extended.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.EntityFrameworkCore.BulkUpdates.Inheritance;
using Microsoft.EntityFrameworkCore.Query.Inheritance;

namespace EntityFrameworkCore.LibRed.Extended.FunctionalTests.Query;

public class TPTManyToManyQueryLibRedFixture : TPTManyToManyQueryRelationalFixture
{
    protected override ITestStoreFactory TestStoreFactory
        => LibRedTestStoreFactory.Instance;
}
