// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.FunctionalTests;

#nullable disable

public class GraphUpdatesJetIdentityTest(GraphUpdatesJetIdentityTest.JetFixture fixture)
    : GraphUpdatesJetTestBase<GraphUpdatesJetIdentityTest.JetFixture>(fixture)
{
    protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction)
        => facade.UseTransaction(transaction.GetDbTransaction());

    public class JetFixture : GraphUpdatesJetFixtureBase
    {
        protected override string StoreName
            => "GraphIdentityUpdatesTest";

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            modelBuilder.UseJetIdentityColumns();

            base.OnModelCreating(modelBuilder, context);
        }
    }
}
