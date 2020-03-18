// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public class GraphUpdatesJetTestClientNoAction : GraphUpdatesJetTestBase<
        GraphUpdatesJetTestClientNoAction.GraphUpdatesWithClientNoActionJetFixture>
    {
        public GraphUpdatesJetTestClientNoAction(GraphUpdatesWithClientNoActionJetFixture fixture)
            : base(fixture)
        {
        }

        public class GraphUpdatesWithClientNoActionJetFixture : GraphUpdatesJetFixtureBase
        {
            protected override string StoreName { get; } = "GraphClientNoActionUpdatesTest";
            public override bool ForceClientNoAction => true;

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
}
