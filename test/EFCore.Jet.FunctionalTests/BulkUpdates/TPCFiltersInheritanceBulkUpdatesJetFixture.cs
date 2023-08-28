// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


namespace EntityFrameworkCore.Jet.FunctionalTests.BulkUpdates;

public class TPCFiltersInheritanceBulkUpdatesJetFixture : TPCInheritanceBulkUpdatesJetFixture
{
    protected override string StoreName
        => "TPCFiltersInheritanceBulkUpdatesTest";

    protected override bool EnableFilters
        => true;
}
