// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestModels.ManyToManyModel;

namespace EntityFrameworkCore.Jet.FunctionalTests;

public class TptManyToManyTrackingJetTest(TptManyToManyTrackingJetTest.TptManyToManyTrackingJetFixture fixture)
    : ManyToManyTrackingJetTestBase<TptManyToManyTrackingJetTest.TptManyToManyTrackingJetFixture>(fixture)
{
    public class TptManyToManyTrackingJetFixture : ManyToManyTrackingJetFixtureBase
    {
        protected override string StoreName
            => "TptManyToManyTrackingJetTest";

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);

            modelBuilder.Entity<EntityRoot>().ToTable("Roots");
            modelBuilder.Entity<EntityBranch>().ToTable("Branches");
            modelBuilder.Entity<EntityLeaf>().ToTable("Leaves");
            modelBuilder.Entity<EntityBranch2>().ToTable("Branch2s");
            modelBuilder.Entity<EntityLeaf2>().ToTable("Leaf2s");

            modelBuilder.Entity<UnidirectionalEntityRoot>().ToTable("UnidirectionalRoots");
            modelBuilder.Entity<UnidirectionalEntityBranch>().ToTable("UnidirectionalBranches");
            modelBuilder.Entity<UnidirectionalEntityLeaf>().ToTable("UnidirectionalLeaves");
        }
    }
}
