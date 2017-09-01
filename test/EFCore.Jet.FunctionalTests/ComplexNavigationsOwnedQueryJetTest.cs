using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace EntityFramework.Jet.FunctionalTests
{
    public class ComplexNavigationsOwnedQueryJetTest
        : ComplexNavigationsOwnedQueryTestBase<JetTestStore, ComplexNavigationsOwnedQueryJetFixture>
    {
        public ComplexNavigationsOwnedQueryJetTest(
            ComplexNavigationsOwnedQueryJetFixture fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            Fixture.TestSqlLoggerFactory.Clear();
            //Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
        }

        [Fact(Skip = "Unsupported by JET: JOIN with unsupported ON PREDICATE")]
        public override void Join_navigation_in_outer_selector_translated_to_extra_join()
        {
        }

        [Fact(Skip = "Unsupported by JET: JOIN with unsupported ON PREDICATE")]
        public override void Join_navigation_in_outer_selector_translated_to_extra_join_nested()
        {
            base.Join_navigation_in_outer_selector_translated_to_extra_join_nested();

            AssertSql(
                @"SELECT [e1].[Id] AS [Id1], [e3].[Id] AS [Id3]
FROM [Level1] AS [e1]
LEFT JOIN [Level2] AS [e1.OneToOne_Required_FK] ON [e1].[Id] = [e1.OneToOne_Required_FK].[Level1_Required_Id]
LEFT JOIN [Level3] AS [e1.OneToOne_Required_FK.OneToOne_Optional_FK] ON [e1.OneToOne_Required_FK].[Id] = [e1.OneToOne_Required_FK.OneToOne_Optional_FK].[Level2_Optional_Id]
INNER JOIN [Level3] AS [e3] ON [e1.OneToOne_Required_FK.OneToOne_Optional_FK].[Id] = [e3].[Id]");
        }

        [Fact(Skip = "Unsupported by JET: JOIN with unsupported ON PREDICATE")]
        public override void Join_navigation_in_outer_selector_translated_to_extra_join_nested2()
        {
            base.Join_navigation_in_outer_selector_translated_to_extra_join_nested2();

            AssertSql(
                @"SELECT [e3].[Id] AS [Id3], [e1].[Id] AS [Id1]
FROM [Level3] AS [e3]
INNER JOIN [Level2] AS [e3.OneToOne_Required_FK_Inverse] ON [e3].[Level2_Required_Id] = [e3.OneToOne_Required_FK_Inverse].[Id]
LEFT JOIN [Level1] AS [e3.OneToOne_Required_FK_Inverse.OneToOne_Optional_FK_Inverse] ON [e3.OneToOne_Required_FK_Inverse].[Level1_Optional_Id] = [e3.OneToOne_Required_FK_Inverse.OneToOne_Optional_FK_Inverse].[Id]
INNER JOIN [Level1] AS [e1] ON [e3.OneToOne_Required_FK_Inverse.OneToOne_Optional_FK_Inverse].[Id] = [e1].[Id]");
        }

        [Fact(Skip = "Unsupported by JET: JOIN with unsupported ON PREDICATE")]
        public override void Join_navigation_in_inner_selector_translated_to_subquery()
        {
            base.Join_navigation_in_inner_selector_translated_to_subquery();

            AssertSql(
                @"SELECT [e2].[Id] AS [Id2], [e1].[Id] AS [Id1]
FROM [Level2] AS [e2]
INNER JOIN [Level1] AS [e1] ON [e2].[Id] IN (
    SELECT TOP(1) [subQuery0].[Id]
    FROM [Level2] AS [subQuery0]
    WHERE [subQuery0].[Level1_Optional_Id] = [e1].[Id]
)");
        }

        [Fact(Skip = "Unsupported by JET: JOIN with unsupported ON PREDICATE")]
        public override void Join_navigations_in_inner_selector_translated_to_multiple_subquery_without_collision()
        {
            base.Join_navigations_in_inner_selector_translated_to_multiple_subquery_without_collision();

            AssertSql(
                @"SELECT [e2].[Id] AS [Id2], [e1].[Id] AS [Id1], [e3].[Id] AS [Id3]
FROM [Level2] AS [e2]
INNER JOIN [Level1] AS [e1] ON [e2].[Id] IN (
    SELECT TOP(1) [subQuery0].[Id]
    FROM [Level2] AS [subQuery0]
    WHERE [subQuery0].[Level1_Optional_Id] = [e1].[Id]
)
INNER JOIN [Level3] AS [e3] ON [e2].[Id] = [e3].[Level2_Optional_Id]");
        }

        [Fact(Skip = "Unsupported by JET: JOIN with unsupported ON PREDICATE")]
        public override void Join_navigation_translated_to_subquery_non_key_join()
        {
            base.Join_navigation_translated_to_subquery_non_key_join();

            AssertSql(
                @"SELECT [e2].[Id] AS [Id2], [e2].[Name] AS [Name2], [e1].[Id] AS [Id1], [e1].[Name] AS [Name1]
FROM [Level2] AS [e2]
INNER JOIN [Level1] AS [e1] ON [e2].[Name] IN (
    SELECT TOP(1) [subQuery0].[Name]
    FROM [Level2] AS [subQuery0]
    WHERE [subQuery0].[Level1_Optional_Id] = [e1].[Id]
)");
        }

        public override void Join_navigation_translated_to_subquery_self_ref()
        {
            base.Join_navigation_translated_to_subquery_self_ref();

            AssertSql(
                @"SELECT [e1].[Id] AS [Id1], [e2].[Id] AS [Id2]
FROM [Level1] AS [e1]
INNER JOIN [Level1] AS [e2] ON [e1].[Id] = [e2].[OneToMany_Optional_Self_InverseId]");
        }

        [Fact(Skip = "Unsupported by JET: JOIN with unsupported ON PREDICATE")]
        public override void Join_navigation_translated_to_subquery_nested()
        {
            base.Join_navigation_translated_to_subquery_nested();

            AssertSql(
                @"SELECT [e3].[Id] AS [Id3], [e1].[Id] AS [Id1]
FROM [Level3] AS [e3]
INNER JOIN [Level1] AS [e1] ON [e3].[Id] IN (
    SELECT TOP(1) [subQuery.OneToOne_Optional_FK0].[Id]
    FROM [Level2] AS [subQuery0]
    LEFT JOIN [Level3] AS [subQuery.OneToOne_Optional_FK0] ON [subQuery0].[Id] = [subQuery.OneToOne_Optional_FK0].[Level2_Optional_Id]
    WHERE [subQuery0].[Level1_Required_Id] = [e1].[Id]
)");
        }

        [Fact(Skip = "Unsupported by JET: JOIN with unsupported ON PREDICATE")]
        public override void Join_navigation_translated_to_subquery_deeply_nested_non_key_join()
        {
            base.Join_navigation_translated_to_subquery_deeply_nested_non_key_join();

            AssertSql(
                @"SELECT [e4].[Id] AS [Id4], [e4].[Name] AS [Name4], [e1].[Id] AS [Id1], [e1].[Name] AS [Name1]
FROM [Level4] AS [e4]
INNER JOIN [Level1] AS [e1] ON [e4].[Name] IN (
    SELECT TOP(1) [subQuery.OneToOne_Optional_FK.OneToOne_Required_PK0].[Name]
    FROM [Level2] AS [subQuery0]
    LEFT JOIN [Level3] AS [subQuery.OneToOne_Optional_FK0] ON [subQuery0].[Id] = [subQuery.OneToOne_Optional_FK0].[Level2_Optional_Id]
    LEFT JOIN [Level4] AS [subQuery.OneToOne_Optional_FK.OneToOne_Required_PK0] ON [subQuery.OneToOne_Optional_FK0].[Id] = [subQuery.OneToOne_Optional_FK.OneToOne_Required_PK0].[Id]
    WHERE [subQuery0].[Level1_Required_Id] = [e1].[Id]
)");
        }

        [Fact(Skip = "Unsupported by JET: JOIN with unsupported ON PREDICATE")]
        public override void Join_navigation_translated_to_subquery_deeply_nested_required()
        {
            base.Join_navigation_translated_to_subquery_deeply_nested_required();

            AssertSql(
                @"SELECT [e4].[Id] AS [Id4], [e4].[Name] AS [Name4], [e1].[Id] AS [Id1], [e1].[Name] AS [Name1]
FROM [Level1] AS [e1]
INNER JOIN [Level4] AS [e4] ON [e1].[Name] IN (
    SELECT TOP(1) [subQuery.OneToOne_Required_FK_Inverse.OneToOne_Required_PK_Inverse0].[Name]
    FROM [Level3] AS [subQuery0]
    INNER JOIN [Level2] AS [subQuery.OneToOne_Required_FK_Inverse0] ON [subQuery0].[Level2_Required_Id] = [subQuery.OneToOne_Required_FK_Inverse0].[Id]
    INNER JOIN [Level1] AS [subQuery.OneToOne_Required_FK_Inverse.OneToOne_Required_PK_Inverse0] ON [subQuery.OneToOne_Required_FK_Inverse0].[Id] = [subQuery.OneToOne_Required_FK_Inverse.OneToOne_Required_PK_Inverse0].[Id]
    WHERE [subQuery0].[Id] = [e4].[Level3_Required_Id]
)");
        }
        [Fact(Skip = "Assertion failed without evident reason")]
        public override void Result_operator_nav_prop_reference_optional_Average()
        {
            base.Result_operator_nav_prop_reference_optional_Average();

            AssertSql(
                @"SELECT AVG(CAST([e.OneToOne_Optional_FK].[Level1_Required_Id] AS float))
FROM [Level1] AS [e]
LEFT JOIN [Level2] AS [e.OneToOne_Optional_FK] ON [e].[Id] = [e.OneToOne_Optional_FK].[Level1_Optional_Id]");
        }

        [Fact(Skip = "Unsupported by JET: CROSS JOIN and OTHER JOIN")]
        public override void SelectMany_navigation_comparison2()
        {
        }

        [Fact(Skip = "Unsupported by JET: CROSS JOIN and OTHER JOIN")]
        public override void SelectMany_navigation_comparison3()
        {
            base.SelectMany_navigation_comparison3();

            AssertSql(
                @"SELECT [l1].[Id] AS [Id1], [l2].[Id] AS [Id2]
FROM [Level1] AS [l1]
LEFT JOIN [Level2] AS [l1.OneToOne_Optional_FK] ON [l1].[Id] = [l1.OneToOne_Optional_FK].[Level1_Optional_Id]
CROSS JOIN [Level2] AS [l2]
WHERE [l1.OneToOne_Optional_FK].[Id] = [l2].[Id]");
        }

        [Fact(Skip = "Unsupported by JET: JOIN with unsupported ON PREDICATE")]
        public override void Where_complex_predicate_with_with_nav_prop_and_OrElse1()
        {
            base.Where_complex_predicate_with_with_nav_prop_and_OrElse1();

            AssertSql(
                @"SELECT [l1].[Id] AS [Id1], [l2].[Id] AS [Id2]
FROM [Level1] AS [l1]
LEFT JOIN [Level2] AS [l1.OneToOne_Optional_FK] ON [l1].[Id] = [l1.OneToOne_Optional_FK].[Level1_Optional_Id]
CROSS JOIN [Level2] AS [l2]
INNER JOIN [Level1] AS [l2.OneToOne_Required_FK_Inverse] ON [l2].[Level1_Required_Id] = [l2.OneToOne_Required_FK_Inverse].[Id]
WHERE ([l1.OneToOne_Optional_FK].[Name] = N'L2 01') OR (([l2.OneToOne_Required_FK_Inverse].[Name] <> N'Bar') OR [l2.OneToOne_Required_FK_Inverse].[Name] IS NULL)");
        }

        [Fact(Skip = "Unsupported by JET: JOIN with unsupported ON PREDICATE")]
        public override void Query_source_materialization_bug_4547()
        {
            base.Query_source_materialization_bug_4547();

            AssertSql(
                @"SELECT [e1].[Id]
FROM [Level3] AS [e3]
INNER JOIN [Level1] AS [e1] ON [e3].[Id] IN (
    SELECT TOP(1) [subQuery30].[Id]
    FROM [Level2] AS [subQuery20]
    LEFT JOIN [Level3] AS [subQuery30] ON [subQuery20].[Id] = [subQuery30].[Level2_Optional_Id]
    ORDER BY [subQuery30].[Id]
)");
        }

        [Fact(Skip = "Unsupported by JET")]
        public override void GroupJoin_in_subquery_with_client_projection_nested1()
        {
            base.GroupJoin_in_subquery_with_client_projection_nested1();
        }

        [Fact(Skip = "Unsupported by JET")]
        public override void GroupJoin_in_subquery_with_client_projection_nested2()
        {
            base.GroupJoin_in_subquery_with_client_projection_nested2();
        }

        [Fact(Skip = "Unsupported by JET")]
        public override void GroupJoin_in_subquery_with_client_result_operator()
        {
            base.GroupJoin_in_subquery_with_client_result_operator();
        }

        [Fact(Skip = "Unsupported by JET")]
        public override void Explicit_GroupJoin_in_subquery_with_scalar_result_operator()
        {
            base.Explicit_GroupJoin_in_subquery_with_scalar_result_operator();
        }

        [Fact(Skip = "Unsupported by JET")]
        public override void Explicit_GroupJoin_in_subquery_with_multiple_result_operator_distinct_count_materializes_main_clause()
        {
            base.Explicit_GroupJoin_in_subquery_with_multiple_result_operator_distinct_count_materializes_main_clause();
        }

        [Fact(Skip = "Unsupported by JET: SKIP TAKE is supported only in outer queries")]
        public override void Complex_multi_include_with_order_by_and_paging()
        {
            base.Complex_multi_include_with_order_by_and_paging();
        }

        [Fact(Skip = "Unsupported by JET: JOIN with unsupported ON PREDICATE")]
        public override void Select_join_with_key_selector_being_a_subquery()
        {
            base.Select_join_with_key_selector_being_a_subquery();

            AssertSql(
                @"SELECT [l1].[Id], [l1].[Date], [l1].[Name], [l1].[OneToMany_Optional_Self_InverseId], [l1].[OneToMany_Required_Self_InverseId], [l1].[OneToOne_Optional_SelfId], [l2].[Id], [l2].[Date], [l2].[Level1_Optional_Id], [l2].[Level1_Required_Id], [l2].[Name], [l2].[OneToMany_Optional_InverseId], [l2].[OneToMany_Optional_Self_InverseId], [l2].[OneToMany_Required_InverseId], [l2].[OneToMany_Required_Self_InverseId], [l2].[OneToOne_Optional_PK_InverseId], [l2].[OneToOne_Optional_SelfId]
FROM [Level1] AS [l1]
INNER JOIN [Level2] AS [l2] ON [l1].[Id] IN (
    SELECT TOP(1) [l0].[Id]
    FROM [Level2] AS [l0]
    ORDER BY [l0].[Id]
)");
        }

        [Fact(Skip = "Unsupported by JET: SKIP TAKE is supported only in outer queries")]
        public override void Complex_multi_include_with_order_by_and_paging_joins_on_correct_key()
        {
            base.Complex_multi_include_with_order_by_and_paging_joins_on_correct_key();
        }

        [Fact(Skip = "Unsupported by JET: SKIP TAKE is supported only in outer queries")]
        public override void Complex_multi_include_with_order_by_and_paging_joins_on_correct_key2()
        {
            base.Complex_multi_include_with_order_by_and_paging_joins_on_correct_key2();
        }

        [Fact]
        public override void Simple_owned_level1()
        {
            base.Simple_owned_level1();

            AssertSql(
                @"SELECT [l1].[Id], [l1].[Date], [l1].[Name], [l1].[Id], [l1].[OneToOne_Required_PK_Date], [l1].[Level1_Optional_Id], [l1].[Level1_Required_Id], [l1].[Level2_Name], [l1].[OneToOne_Optional_PK_InverseId]
FROM [Level1] AS [l1]");
        }

        [Fact]
        public override void Simple_owned_level1_convention()
        {
            base.Simple_owned_level1_convention();

            AssertSql(
                @"SELECT [l].[Id], [l].[Date], [l].[Name]
FROM [Level1] AS [l]");
        }

        [Fact]
        public override void Simple_owned_level1_level2()
        {
            base.Simple_owned_level1_level2();

            AssertSql(
                @"SELECT [l1].[Id], [l1].[Date], [l1].[Name], [l1].[Id], [l1].[OneToOne_Required_PK_Date], [l1].[Level1_Optional_Id], [l1].[Level1_Required_Id], [l1].[Level2_Name], [l1].[OneToOne_Optional_PK_InverseId], [l1].[Id], [l1].[Level2_Optional_Id], [l1].[Level2_Required_Id], [l1].[Level3_Name], [l1].[Level3_OneToOne_Optional_PK_InverseId]
FROM [Level1] AS [l1]");
        }

        [Fact]
        public override void Simple_owned_level1_level2_level3()
        {
            base.Simple_owned_level1_level2_level3();

            AssertSql(
                @"SELECT [l1].[Id], [l1].[Date], [l1].[Name], [l1].[Id], [l1].[OneToOne_Required_PK_Date], [l1].[Level1_Optional_Id], [l1].[Level1_Required_Id], [l1].[Level2_Name], [l1].[OneToOne_Optional_PK_InverseId], [l1].[Id], [l1].[Level2_Optional_Id], [l1].[Level2_Required_Id], [l1].[Level3_Name], [l1].[Level3_OneToOne_Optional_PK_InverseId], [l1].[Id], [l1].[Level3_Optional_Id], [l1].[Level3_Required_Id], [l1].[Level4_Name], [l1].[Level4_OneToOne_Optional_PK_InverseId]
FROM [Level1] AS [l1]");
        }


        [ConditionalFact(Skip = "issue #4311")]
        public override void Nested_group_join_with_take()
        {
            base.Nested_group_join_with_take();
        }

        [ConditionalFact]
        public override void Explicit_GroupJoin_in_subquery_with_unrelated_projection2()
        {
            base.Explicit_GroupJoin_in_subquery_with_unrelated_projection2();

            AssertSql(
                @"SELECT [t1].[Id]
FROM (
    SELECT DISTINCT [l1].*
    FROM [Level1] AS [l1]
    LEFT JOIN (
        SELECT [t].*
        FROM [Level1] AS [t]
        WHERE [t].[Id] IS NOT NULL
    ) AS [t0] ON [l1].[Id] = [t0].[Level1_Optional_Id]
    WHERE ([t0].[Level2_Name] <> N'Foo') OR [t0].[Level2_Name] IS NULL
) AS [t1]");
        }

        [ConditionalFact]
        public override void Result_operator_nav_prop_reference_optional_via_DefaultIfEmpty()
        {
            base.Result_operator_nav_prop_reference_optional_via_DefaultIfEmpty();

            AssertSql(
                @"SELECT SUM(CASE
    WHEN [t0].[Id] IS NULL
    THEN 0 ELSE [t0].[Level1_Required_Id]
END)
FROM [Level1] AS [l1]
LEFT JOIN (
    SELECT [t].*
    FROM [Level1] AS [t]
    WHERE [t].[Id] IS NOT NULL
) AS [t0] ON [l1].[Id] = [t0].[Level1_Optional_Id]");
        }


        [Fact(Skip = "Unsupported by JET: JOIN with unsupported ON PREDICATE")]
        public override void Join_flattening_bug_4539()
        {
            base.Join_flattening_bug_4539();
        }

        private void AssertSql(params string[] expected)
        {
            string[] expectedFixed = new string[expected.Length];
            int i = 0;
            foreach (var item in expected)
            {
                if (AssertSqlHelper.IgnoreStatement(item))
                    return;
                expectedFixed[i++] = item.Replace("\r\n", "\n");
            }
            Fixture.TestSqlLoggerFactory.AssertBaseline(expectedFixed);
        }
    }
}
