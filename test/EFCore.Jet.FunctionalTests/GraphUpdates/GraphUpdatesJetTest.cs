// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public class GraphUpdatesJetTest
    {
        public class ClientCascade : GraphUpdatesJetTestBase<ClientCascade.JetFixture>
        {
            public ClientCascade(JetFixture fixture)
                : base(fixture)
            {
            }

            protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction)
                => facade.UseTransaction(transaction.GetDbTransaction());

            public class JetFixture : GraphUpdatesJetFixtureBase
            {
                public override bool NoStoreCascades => true;

                protected override string StoreName { get; } = "GraphClientCascadeUpdatesTest";

                protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
                {
                    base.OnModelCreating(modelBuilder, context);

                    foreach (var foreignKey in modelBuilder.Model
                        .GetEntityTypes()
                        .SelectMany(e => MutableEntityTypeExtensions.GetDeclaredForeignKeys(e))
                        .Where(e => e.DeleteBehavior == DeleteBehavior.Cascade))
                    {
                        foreignKey.DeleteBehavior = DeleteBehavior.ClientCascade;
                    }
                }
            }
        }

        public class ClientNoAction : GraphUpdatesJetTestBase<ClientNoAction.JetFixture>
        {
            public ClientNoAction(JetFixture fixture)
                : base(fixture)
            {
            }

            protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction)
                => facade.UseTransaction(transaction.GetDbTransaction());

            public class JetFixture : GraphUpdatesJetFixtureBase
            {
                public override bool ForceClientNoAction => true;

                protected override string StoreName { get; } = "GraphClientNoActionUpdatesTest";

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

        public class Identity : GraphUpdatesJetTestBase<Identity.JetFixture>
        {
            public Identity(JetFixture fixture)
                : base(fixture)
            {
            }

            protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction)
                => facade.UseTransaction(transaction.GetDbTransaction());

            public class JetFixture : GraphUpdatesJetFixtureBase
            {
                protected override string StoreName { get; } = "GraphIdentityUpdatesTest";

                protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
                {
                    modelBuilder.UseIdentityColumns();

                    base.OnModelCreating(modelBuilder, context);
                }
            }
        }
        
        public abstract class GraphUpdatesJetTestBase<TFixture> : GraphUpdatesTestBase<TFixture>
            where TFixture : GraphUpdatesJetTestBase<TFixture>.GraphUpdatesJetFixtureBase, new()
        {
            protected GraphUpdatesJetTestBase(TFixture fixture)
                : base(fixture)
            {
            }

            protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction)
                => facade.UseTransaction(transaction.GetDbTransaction());

            public abstract class GraphUpdatesJetFixtureBase : GraphUpdatesFixtureBase
            {
                public TestSqlLoggerFactory TestSqlLoggerFactory => (TestSqlLoggerFactory)ListLoggerFactory;
                protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;
            }
        }
    }
}
