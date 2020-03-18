// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public class GraphUpdatesJetTestClientCascade : GraphUpdatesJetTestBase<
        GraphUpdatesJetTestClientCascade.GraphUpdatesWithClientCascadeJetFixture>
    {
        public GraphUpdatesJetTestClientCascade(GraphUpdatesWithClientCascadeJetFixture fixture)
            : base(fixture)
        {
        }

        public class GraphUpdatesWithClientCascadeJetFixture : GraphUpdatesJetFixtureBase
        {
            protected override string StoreName { get; } = "GraphClientCascadeUpdatesTest";
            public override bool NoStoreCascades => true;

            protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
            {
                base.OnModelCreating(modelBuilder, context);

                foreach (var foreignKey in modelBuilder.Model
                    .GetEntityTypes()
                    .SelectMany(e => e.GetDeclaredForeignKeys())
                    .Where(e => e.DeleteBehavior == DeleteBehavior.Cascade))
                {
                    foreignKey.DeleteBehavior = DeleteBehavior.ClientCascade;
                }
            }
        }
    }
}
