// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace EntityFrameworkCore.Jet.FunctionalTests.BulkUpdates;

public class TPTFiltersInheritanceBulkUpdatesJetFixture : TPTInheritanceBulkUpdatesJetFixture
{
    protected override string StoreName
        => "TPTFiltersInheritanceBulkUpdatesTest";

    protected override bool EnableFilters
        => true;
}
