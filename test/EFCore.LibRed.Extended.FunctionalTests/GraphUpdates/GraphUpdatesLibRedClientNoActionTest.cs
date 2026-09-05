// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using System.Linq;

namespace EntityFrameworkCore.LibRed.Extended.FunctionalTests;

#nullable disable

public class GraphUpdatesLibRedClientNoActionTest(GraphUpdatesLibRedClientNoActionTest.LibRedFixture fixture)
    : GraphUpdatesLibRedTestBase<
        GraphUpdatesLibRedClientNoActionTest.LibRedFixture>(fixture)
{
    protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction)
        => facade.UseTransaction(transaction.GetDbTransaction());

    public class LibRedFixture : GraphUpdatesLibRedFixtureBase
    {
        public override bool ForceClientNoAction
            => true;

        protected override string StoreName
            => "GraphClientNoActionUpdatesTest";

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);

            foreach (var foreignKey in modelBuilder.Model
                         .GetEntityTypes()
                         .SelectMany(e => e.GetDeclaredForeignKeys())
                         .Where(e => !e.IsOwnership))
            {
                foreignKey.DeleteBehavior = DeleteBehavior.ClientNoAction;
            }
        }
    }
}
