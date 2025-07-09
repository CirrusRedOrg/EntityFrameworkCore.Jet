// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query.Relationships;
using Microsoft.EntityFrameworkCore.TestUtilities;
using System;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query.Relationships;

public class OwnedRelationshipsJetFixture : OwnedRelationshipsRelationalFixtureBase, ITestSqlLoggerFactory
{
    protected override ITestStoreFactory TestStoreFactory
        => JetTestStoreFactory.Instance;

    public TestSqlLoggerFactory TestSqlLoggerFactory
        => (TestSqlLoggerFactory)ListLoggerFactory;

    protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
    {
        base.OnModelCreating(modelBuilder, context);
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (entity.GetTableName() == "Root_RequiredReferenceTrunk_RequiredReferenceBranch_CollectionLeaf")
            {
                entity.SetTableName("Root_RequiredReferenceTrunk_RequiredReferenceBranch_CollB7BC1840");
            }
            if (entity.GetTableName() == "Root_RequiredReferenceTrunk_OptionalReferenceBranch_CollectionLeaf")
            {
                entity.SetTableName("Root_RequiredReferenceTrunk_OptionalReferenceBranch_CollB7BC1840");
            }
            if (entity.GetTableName() == "Root_OptionalReferenceTrunk_RequiredReferenceBranch_CollectionLeaf")
            {
                entity.SetTableName("Root_OptionalReferenceTrunk_RequiredReferenceBranch_CollB7BC1840");
            }
            if (entity.GetTableName() == "Root_OptionalReferenceTrunk_OptionalReferenceBranch_CollectionLeaf")
            {
                entity.SetTableName("Root_OptionalReferenceTrunk_OptionalReferenceBranch_CollB7BC1840");
            }

        }
    }
}
