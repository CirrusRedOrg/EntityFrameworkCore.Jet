using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;
using Xunit;

#pragma warning disable xUnit1003 // Theory methods must have test data

namespace EntityFramework.Jet.FunctionalTests
{
    public class GraphUpdatesWithIdentityJetTest : GraphUpdatesJetTestBase<GraphUpdatesWithIdentityJetTest.GraphUpdatesWithIdentityJetFixture>
    {
        public GraphUpdatesWithIdentityJetTest(GraphUpdatesWithIdentityJetFixture fixture)
            : base(fixture)
        {
        }

        public class GraphUpdatesWithIdentityJetFixture : GraphUpdatesJetFixtureBase
        {
            protected override string StoreName { get; } = "GraphIdentityUpdatesTest";

            protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
            {
                modelBuilder.ForJetUseIdentityColumns();

                base.OnModelCreating(modelBuilder, context);
            }
        }

        [Theory(Skip = "Unsupported by JET: JET behaviour requires that both columns in a composite foreign key are null")]
        public override void Reparent_to_different_one_to_many(ChangeMechanism changeMechanism, bool useExistingParent)
        {
            base.Reparent_to_different_one_to_many(changeMechanism, useExistingParent);
        }

        [Theory(Skip = "Unsupported by JET: JET behaviour requires that both columns in a composite foreign key are null")]
        public override DbUpdateException Optional_many_to_one_dependents_with_alternate_key_are_orphaned() { return null; }
        [Theory(Skip = "Unsupported by JET: JET behaviour requires that both columns in a composite foreign key are null")]
        public override DbUpdateException Optional_one_to_one_with_alternate_key_are_orphaned() { return null; }
        [Theory(Skip = "Unsupported by JET: JET behaviour requires that both columns in a composite foreign key are null")]
        public override void Save_optional_many_to_one_dependents_with_alternate_key(ChangeMechanism changeMechanism, bool useExistingEntities) { }
        [Theory(Skip = "Unsupported by JET: JET behaviour requires that both columns in a composite foreign key are null")]
        public override void Save_removed_optional_many_to_one_dependents_with_alternate_key(ChangeMechanism changeMechanism) { }


        [Theory(Skip = "Unsupported by JET: OleDB does not support parallel transactions")]
        public override DbUpdateException Save_required_one_to_one_changed_by_reference(ChangeMechanism changeMechanism) { return null; }

        [Fact(Skip = "Unsupported by JET: OleDB does not support parallel transactions")]
        public override DbUpdateException Required_many_to_one_dependents_are_cascade_deleted_starting_detached() { return null; }
        [Fact(Skip = "Unsupported by JET: OleDB does not support parallel transactions")]
        public override DbUpdateException Required_many_to_one_dependents_with_alternate_key_are_cascade_deleted_in_store() { return null; }
        [Fact(Skip = "Unsupported by JET: OleDB does not support parallel transactions")]
        public override DbUpdateException Required_many_to_one_dependents_with_alternate_key_are_cascade_deleted_starting_detached() { return null; }
        [Fact(Skip = "Unsupported by JET: OleDB does not support parallel transactions")]
        public override DbUpdateException Required_non_PK_one_to_one_are_cascade_deleted_in_store() { return null; }
        [Fact(Skip = "Unsupported by JET: OleDB does not support parallel transactions")]
        public override DbUpdateException Required_non_PK_one_to_one_are_cascade_deleted_starting_detached() { return null; }
        [Fact(Skip = "Unsupported by JET: OleDB does not support parallel transactions")]
        public override DbUpdateException Required_non_PK_one_to_one_with_alternate_key_are_cascade_deleted_in_store() { return null; }
        [Fact(Skip = "Unsupported by JET: OleDB does not support parallel transactions")]
        public override DbUpdateException Required_non_PK_one_to_one_with_alternate_key_are_cascade_deleted_starting_detached() { return null; }
        [Fact(Skip = "Unsupported by JET: OleDB does not support parallel transactions")]
        public override DbUpdateException Required_one_to_one_are_cascade_deleted_in_store() { return null; }
        [Fact(Skip = "Unsupported by JET: OleDB does not support parallel transactions")]
        public override DbUpdateException Required_one_to_one_are_cascade_deleted_starting_detached() { return null; }
        [Fact(Skip = "Unsupported by JET: OleDB does not support parallel transactions")]
        public override DbUpdateException Required_one_to_one_with_alternate_key_are_cascade_deleted_in_store() { return null; }
        [Fact(Skip = "Unsupported by JET: OleDB does not support parallel transactions")]
        public override DbUpdateException Required_one_to_one_with_alternate_key_are_cascade_deleted_starting_detached() { return null; }

        [Fact(Skip = "Unsupported by JET: OleDB does not support parallel transactions")]
        public override DbUpdateException Optional_many_to_one_dependents_are_orphaned_in_store() { return null; }
        [Fact(Skip = "Unsupported by JET: OleDB does not support parallel transactions")]
        public override DbUpdateException Optional_many_to_one_dependents_are_orphaned_starting_detached() { return null; }
        [Fact(Skip = "Unsupported by JET: OleDB does not support parallel transactions")]
        public override DbUpdateException Optional_many_to_one_dependents_with_alternate_key_are_orphaned_in_store() { return null; }
        [Fact(Skip = "Unsupported by JET: OleDB does not support parallel transactions")]
        public override DbUpdateException Optional_many_to_one_dependents_with_alternate_key_are_orphaned_starting_detached() { return null; }
        [Fact(Skip = "Unsupported by JET: OleDB does not support parallel transactions")]
        public override DbUpdateException Optional_one_to_one_are_orphaned_in_store() { return null; }
        [Fact(Skip = "Unsupported by JET: OleDB does not support parallel transactions")]
        public override DbUpdateException Optional_one_to_one_are_orphaned_starting_detached() { return null; }
        [Fact(Skip = "Unsupported by JET: OleDB does not support parallel transactions")]
        public override DbUpdateException Optional_one_to_one_with_alternate_key_are_orphaned_in_store() { return null; }
        [Fact(Skip = "Unsupported by JET: OleDB does not support parallel transactions")]
        public override DbUpdateException Optional_one_to_one_with_alternate_key_are_orphaned_starting_detached() { return null; }
    }
}
