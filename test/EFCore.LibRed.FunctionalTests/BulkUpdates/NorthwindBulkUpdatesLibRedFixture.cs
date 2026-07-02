// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using EntityFrameworkCore.LibRed.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.BulkUpdates;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.TestModels.Northwind;
using Microsoft.EntityFrameworkCore.TestUtilities;
using System;
using EntityFrameworkCore.LibRed.FunctionalTests.TestModels.Northwind;

namespace EntityFrameworkCore.LibRed.FunctionalTests.BulkUpdates;

public class NorthwindBulkUpdatesLibRedFixture<TModelCustomizer> : NorthwindBulkUpdatesRelationalFixture<TModelCustomizer>
    where TModelCustomizer : ITestModelCustomizer, new()
{
    protected override ITestStoreFactory TestStoreFactory
        => LibRedNorthwindTestStoreFactory.Instance;

    protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
    {
        base.OnModelCreating(modelBuilder, context);

        modelBuilder.Entity<MostExpensiveProduct>()
            .Property(p => p.UnitPrice)
            .HasColumnType("money");
    }

    protected override Type ContextType
        => typeof(NorthwindLibRedContext);
}
