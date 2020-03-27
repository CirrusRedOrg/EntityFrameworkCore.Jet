// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public class GraphUpdatesJetTestIdentity : GraphUpdatesJetTestBase<
        GraphUpdatesJetTestIdentity.GraphUpdatesWithIdentityJetFixture>
    {
        public GraphUpdatesJetTestIdentity(GraphUpdatesWithIdentityJetFixture fixture)
            : base(fixture)
        {
        }

        public class GraphUpdatesWithIdentityJetFixture : GraphUpdatesJetFixtureBase
        {
            protected override string StoreName { get; } = "GraphIdentityUpdatesTest";

            protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
            {
                modelBuilder.UseIdentityColumns();

                base.OnModelCreating(modelBuilder, context);
            }
        }
    }
}
