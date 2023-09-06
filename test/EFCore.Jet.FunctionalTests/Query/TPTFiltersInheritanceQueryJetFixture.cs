﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query;

public class TPTFiltersInheritanceQueryJetFixture : TPTInheritanceQueryJetFixture
{
    protected override bool EnableFilters
        => true;
}