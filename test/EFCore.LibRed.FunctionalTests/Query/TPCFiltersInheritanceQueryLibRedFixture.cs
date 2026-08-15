// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.LibRed.FunctionalTests.Query;

public class TPCFiltersInheritanceQueryLibRedFixture : TPCInheritanceQueryLibRedFixture
{
    public override bool EnableFilters
        => true;

    public override bool UseGeneratedKeys
        => false;
}
