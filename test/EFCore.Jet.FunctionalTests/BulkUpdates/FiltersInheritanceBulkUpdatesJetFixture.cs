// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace EntityFrameworkCore.Jet.FunctionalTests.BulkUpdates;

public class FiltersInheritanceBulkUpdatesJetFixture : InheritanceBulkUpdatesJetFixture
{
    protected override string StoreName
        => "FiltersInheritanceBulkUpdatesTest";

    protected override bool EnableFilters
        => true;
}
