// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.LibRed.FunctionalTests;

#nullable disable

public class GraphUpdatesLibRedIdentityTest(GraphUpdatesLibRedIdentityTest.LibRedFixture fixture)
    : GraphUpdatesLibRedTestBase<GraphUpdatesLibRedIdentityTest.LibRedFixture>(fixture)
{
    protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction)
        => facade.UseTransaction(transaction.GetDbTransaction());

    public class LibRedFixture : GraphUpdatesLibRedFixtureBase
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
