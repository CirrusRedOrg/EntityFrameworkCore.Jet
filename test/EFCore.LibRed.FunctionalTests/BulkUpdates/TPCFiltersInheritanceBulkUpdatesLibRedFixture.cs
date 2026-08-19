// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


namespace EntityFrameworkCore.LibRed.FunctionalTests.BulkUpdates;

public class TPCFiltersInheritanceBulkUpdatesLibRedFixture : TPCInheritanceBulkUpdatesLibRedFixture
{
    protected override string StoreName
        => "TPCFiltersInheritanceBulkUpdatesTest";

    public override bool EnableFilters
        => true;
}
