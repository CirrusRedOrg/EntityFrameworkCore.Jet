// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace EntityFrameworkCore.LibRed.Extended.FunctionalTests.BulkUpdates;

public class TPTFiltersInheritanceBulkUpdatesLibRedFixture : TPTInheritanceBulkUpdatesLibRedFixture
{
    protected override string StoreName
        => "TPTFiltersInheritanceBulkUpdatesTest";

    public override bool EnableFilters
        => true;
}
