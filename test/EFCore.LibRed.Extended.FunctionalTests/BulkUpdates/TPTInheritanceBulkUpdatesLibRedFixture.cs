// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using EntityFrameworkCore.LibRed.Extended.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore.BulkUpdates;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.EntityFrameworkCore.BulkUpdates.Inheritance;

namespace EntityFrameworkCore.LibRed.Extended.FunctionalTests.BulkUpdates;

public class TPTInheritanceBulkUpdatesLibRedFixture : TPTInheritanceBulkUpdatesFixture
{
    protected override ITestStoreFactory TestStoreFactory
        => LibRedTestStoreFactory.Instance;
}
