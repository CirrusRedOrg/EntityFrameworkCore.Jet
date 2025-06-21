// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using System.Linq;

namespace EntityFrameworkCore.Jet.FunctionalTests;

#nullable disable

public class GraphUpdatesJetClientNoActionTest(GraphUpdatesJetClientNoActionTest.JetFixture fixture)
    : GraphUpdatesJetTestBase<
        GraphUpdatesJetClientNoActionTest.JetFixture>(fixture)
{
    protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction)
        => facade.UseTransaction(transaction.GetDbTransaction());

    public class JetFixture : GraphUpdatesJetFixtureBase
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
                         .SelectMany(e => e.GetDeclaredForeignKeys()))
            {
                foreignKey.DeleteBehavior = DeleteBehavior.ClientNoAction;
            }
        }
    }
}
