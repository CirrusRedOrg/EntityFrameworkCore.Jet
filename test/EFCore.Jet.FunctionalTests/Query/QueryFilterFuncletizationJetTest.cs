// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query
{
    public class QueryFilterFuncletizationJetTest
        : QueryFilterFuncletizationTestBase<QueryFilterFuncletizationJetTest.QueryFilterFuncletizationJetFixture>
    {
        public QueryFilterFuncletizationJetTest(
            QueryFilterFuncletizationJetFixture fixture,
            ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            Fixture.TestSqlLoggerFactory.Clear();
            //Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
        }

        public override void DbContext_property_parameter_does_not_clash_with_closure_parameter_name()
        {
            base.DbContext_property_parameter_does_not_clash_with_closure_parameter_name();

            AssertSql(
                """
@ef_filter__Field='False'
@Field='False'

SELECT `f`.`Id`, `f`.`IsEnabled`
FROM `FieldFilter` AS `f`
WHERE `f`.`IsEnabled` = @ef_filter__Field AND `f`.`IsEnabled` = @Field
""");
        }

        public override void DbContext_field_is_parameterized()
        {
            base.DbContext_field_is_parameterized();

            AssertSql(
                """
@ef_filter__Field='False'

SELECT `f`.`Id`, `f`.`IsEnabled`
FROM `FieldFilter` AS `f`
WHERE `f`.`IsEnabled` = @ef_filter__Field
""",
                //
                """
@ef_filter__Field='True'

SELECT `f`.`Id`, `f`.`IsEnabled`
FROM `FieldFilter` AS `f`
WHERE `f`.`IsEnabled` = @ef_filter__Field
""");
        }

        public override void DbContext_property_is_parameterized()
        {
            base.DbContext_property_is_parameterized();

            AssertSql(
                """
@ef_filter__Property='False'

SELECT `p`.`Id`, `p`.`IsEnabled`
FROM `PropertyFilter` AS `p`
WHERE `p`.`IsEnabled` = @ef_filter__Property
""",
                //
                """
@ef_filter__Property='True'

SELECT `p`.`Id`, `p`.`IsEnabled`
FROM `PropertyFilter` AS `p`
WHERE `p`.`IsEnabled` = @ef_filter__Property
""");
        }

        public override void DbContext_method_call_is_parameterized()
        {
            base.DbContext_method_call_is_parameterized();

            AssertSql(
                """
@ef_filter__p='2'

SELECT `m`.`Id`, `m`.`Tenant`
FROM `MethodCallFilter` AS `m`
WHERE `m`.`Tenant` = @ef_filter__p
""");
        }

        public override void DbContext_list_is_parameterized()
        {
            base.DbContext_list_is_parameterized();

            AssertSql(
"""
SELECT `l`.`Id`, `l`.`Tenant`
FROM `ListFilter` AS `l`
WHERE 0 = 1
""",
                //
"""
SELECT `l`.`Id`, `l`.`Tenant`
FROM `ListFilter` AS `l`
WHERE 0 = 1
""",
                //
"""
SELECT `l`.`Id`, `l`.`Tenant`
FROM `ListFilter` AS `l`
WHERE `l`.`Tenant` = 1
""",
                //
"""
SELECT `l`.`Id`, `l`.`Tenant`
FROM `ListFilter` AS `l`
WHERE `l`.`Tenant` IN (2, 3)
""");
        }

        public override void DbContext_property_chain_is_parameterized()
        {
            base.DbContext_property_chain_is_parameterized();

            AssertSql(
                """
@ef_filter__Enabled='False'

SELECT `p`.`Id`, `p`.`IsEnabled`
FROM `PropertyChainFilter` AS `p`
WHERE `p`.`IsEnabled` = @ef_filter__Enabled
""",
                //
                """
@ef_filter__Enabled='True'

SELECT `p`.`Id`, `p`.`IsEnabled`
FROM `PropertyChainFilter` AS `p`
WHERE `p`.`IsEnabled` = @ef_filter__Enabled
""");
        }

        public override void DbContext_property_method_call_is_parameterized()
        {
            base.DbContext_property_method_call_is_parameterized();

            AssertSql(
                """
@ef_filter__p='2'

SELECT `p`.`Id`, `p`.`Tenant`
FROM `PropertyMethodCallFilter` AS `p`
WHERE `p`.`Tenant` = @ef_filter__p
""");
        }

        public override void DbContext_method_call_chain_is_parameterized()
        {
            base.DbContext_method_call_chain_is_parameterized();

            AssertSql(
                """
@ef_filter__p='2'

SELECT `m`.`Id`, `m`.`Tenant`
FROM `MethodCallChainFilter` AS `m`
WHERE `m`.`Tenant` = @ef_filter__p
""");
        }

        public override void DbContext_complex_expression_is_parameterized()
        {
            base.DbContext_complex_expression_is_parameterized();

            AssertSql(
                """
@ef_filter__Property='False'
@ef_filter__p0='True'

SELECT `c`.`Id`, `c`.`IsEnabled`
FROM `ComplexFilter` AS `c`
WHERE `c`.`IsEnabled` = @ef_filter__Property AND @ef_filter__p0 = TRUE
""",
                //
                """
@ef_filter__Property='True'
@ef_filter__p0='True'

SELECT `c`.`Id`, `c`.`IsEnabled`
FROM `ComplexFilter` AS `c`
WHERE `c`.`IsEnabled` = @ef_filter__Property AND @ef_filter__p0 = TRUE
""",
                //
                """
@ef_filter__Property='True'
@ef_filter__p0='False'

SELECT `c`.`Id`, `c`.`IsEnabled`
FROM `ComplexFilter` AS `c`
WHERE `c`.`IsEnabled` = @ef_filter__Property AND @ef_filter__p0 = TRUE
""");
        }

        public override void DbContext_property_based_filter_does_not_short_circuit()
        {
            base.DbContext_property_based_filter_does_not_short_circuit();

            AssertSql(
                """
@ef_filter__p0='False'
@ef_filter__IsModerated='True' (Nullable = true)

SELECT `s`.`Id`, `s`.`IsDeleted`, `s`.`IsModerated`
FROM `ShortCircuitFilter` AS `s`
WHERE `s`.`IsDeleted` = FALSE AND (@ef_filter__p0 = TRUE OR @ef_filter__IsModerated = `s`.`IsModerated`)
""",
                //
                """
@ef_filter__p0='False'
@ef_filter__IsModerated='False' (Nullable = true)

SELECT `s`.`Id`, `s`.`IsDeleted`, `s`.`IsModerated`
FROM `ShortCircuitFilter` AS `s`
WHERE `s`.`IsDeleted` = FALSE AND (@ef_filter__p0 = TRUE OR @ef_filter__IsModerated = `s`.`IsModerated`)
""",
                //
                """
@ef_filter__p0='True'

SELECT `s`.`Id`, `s`.`IsDeleted`, `s`.`IsModerated`
FROM `ShortCircuitFilter` AS `s`
WHERE `s`.`IsDeleted` = FALSE AND @ef_filter__p0 = TRUE
""");
        }

        public override void EntityTypeConfiguration_DbContext_field_is_parameterized()
        {
            base.EntityTypeConfiguration_DbContext_field_is_parameterized();

            AssertSql(
                """
@ef_filter__Field='False'

SELECT `e`.`Id`, `e`.`IsEnabled`
FROM `EntityTypeConfigurationFieldFilter` AS `e`
WHERE `e`.`IsEnabled` = @ef_filter__Field
""",
                //
                """
@ef_filter__Field='True'

SELECT `e`.`Id`, `e`.`IsEnabled`
FROM `EntityTypeConfigurationFieldFilter` AS `e`
WHERE `e`.`IsEnabled` = @ef_filter__Field
""");
        }

        public override void EntityTypeConfiguration_DbContext_property_is_parameterized()
        {
            base.EntityTypeConfiguration_DbContext_property_is_parameterized();

            AssertSql(
                """
@ef_filter__Property='False'

SELECT `e`.`Id`, `e`.`IsEnabled`
FROM `EntityTypeConfigurationPropertyFilter` AS `e`
WHERE `e`.`IsEnabled` = @ef_filter__Property
""",
                //
                """
@ef_filter__Property='True'

SELECT `e`.`Id`, `e`.`IsEnabled`
FROM `EntityTypeConfigurationPropertyFilter` AS `e`
WHERE `e`.`IsEnabled` = @ef_filter__Property
""");
        }

        public override void EntityTypeConfiguration_DbContext_method_call_is_parameterized()
        {
            base.EntityTypeConfiguration_DbContext_method_call_is_parameterized();

            AssertSql(
                """
@ef_filter__p='2'

SELECT `e`.`Id`, `e`.`Tenant`
FROM `EntityTypeConfigurationMethodCallFilter` AS `e`
WHERE `e`.`Tenant` = @ef_filter__p
""");
        }

        public override void EntityTypeConfiguration_DbContext_property_chain_is_parameterized()
        {
            base.EntityTypeConfiguration_DbContext_property_chain_is_parameterized();

            AssertSql(
                """
@ef_filter__Enabled='False'

SELECT `e`.`Id`, `e`.`IsEnabled`
FROM `EntityTypeConfigurationPropertyChainFilter` AS `e`
WHERE `e`.`IsEnabled` = @ef_filter__Enabled
""",
                //
                """
@ef_filter__Enabled='True'

SELECT `e`.`Id`, `e`.`IsEnabled`
FROM `EntityTypeConfigurationPropertyChainFilter` AS `e`
WHERE `e`.`IsEnabled` = @ef_filter__Enabled
""");
        }

        public override void Local_method_DbContext_field_is_parameterized()
        {
            base.Local_method_DbContext_field_is_parameterized();

            AssertSql(
                """
@ef_filter__Field='False'

SELECT `l`.`Id`, `l`.`IsEnabled`
FROM `LocalMethodFilter` AS `l`
WHERE `l`.`IsEnabled` = @ef_filter__Field
""",
                //
                """
@ef_filter__Field='True'

SELECT `l`.`Id`, `l`.`IsEnabled`
FROM `LocalMethodFilter` AS `l`
WHERE `l`.`IsEnabled` = @ef_filter__Field
""");
        }

        public override void Local_static_method_DbContext_property_is_parameterized()
        {
            base.Local_static_method_DbContext_property_is_parameterized();

            AssertSql(
                """
@ef_filter__Property='False'

SELECT `l`.`Id`, `l`.`IsEnabled`
FROM `LocalMethodParamsFilter` AS `l`
WHERE `l`.`IsEnabled` = @ef_filter__Property
""",
                //
                """
@ef_filter__Property='True'

SELECT `l`.`Id`, `l`.`IsEnabled`
FROM `LocalMethodParamsFilter` AS `l`
WHERE `l`.`IsEnabled` = @ef_filter__Property
""");
        }

        public override void Remote_method_DbContext_property_method_call_is_parameterized()
        {
            base.Remote_method_DbContext_property_method_call_is_parameterized();

            AssertSql(
                """
@ef_filter__p='2'

SELECT `r`.`Id`, `r`.`Tenant`
FROM `RemoteMethodParamsFilter` AS `r`
WHERE `r`.`Tenant` = @ef_filter__p
""");
        }

        public override void Extension_method_DbContext_field_is_parameterized()
        {
            base.Extension_method_DbContext_field_is_parameterized();

            AssertSql(
                """
@ef_filter__Field='False'

SELECT `e`.`Id`, `e`.`IsEnabled`
FROM `ExtensionBuilderFilter` AS `e`
WHERE `e`.`IsEnabled` = @ef_filter__Field
""",
                //
                """
@ef_filter__Field='True'

SELECT `e`.`Id`, `e`.`IsEnabled`
FROM `ExtensionBuilderFilter` AS `e`
WHERE `e`.`IsEnabled` = @ef_filter__Field
""");
        }

        public override void Extension_method_DbContext_property_chain_is_parameterized()
        {
            base.Extension_method_DbContext_property_chain_is_parameterized();

            AssertSql(
                """
@ef_filter__Enabled='False'

SELECT `e`.`Id`, `e`.`IsEnabled`
FROM `ExtensionContextFilter` AS `e`
WHERE `e`.`IsEnabled` = @ef_filter__Enabled
""",
                //
                """
@ef_filter__Enabled='True'

SELECT `e`.`Id`, `e`.`IsEnabled`
FROM `ExtensionContextFilter` AS `e`
WHERE `e`.`IsEnabled` = @ef_filter__Enabled
""");
        }

        public override void Using_DbSet_in_filter_works()
        {
            base.Using_DbSet_in_filter_works();

            AssertSql(
                """
@ef_filter__Property='False'

SELECT `p`.`Id`, `p`.`Filler`
FROM `PrincipalSetFilter` AS `p`
WHERE EXISTS (
    SELECT 1
    FROM `Dependents` AS `d`
    WHERE EXISTS (
        SELECT 1
        FROM `MultiContextFilter` AS `m`
        WHERE `m`.`IsEnabled` = @ef_filter__Property AND `m`.`BossId` = 1 AND `m`.`BossId` = `d`.`PrincipalSetFilterId`) AND `d`.`PrincipalSetFilterId` = `p`.`Id`)
""");
        }

        public override void Using_Context_set_method_in_filter_works()
        {
            base.Using_Context_set_method_in_filter_works();

            AssertSql(
                """
@ef_filter__Property='False'

SELECT `d`.`Id`, `d`.`PrincipalSetFilterId`
FROM `Dependents` AS `d`
WHERE EXISTS (
    SELECT 1
    FROM `MultiContextFilter` AS `m`
    WHERE `m`.`IsEnabled` = @ef_filter__Property AND `m`.`BossId` = 1 AND `m`.`BossId` = `d`.`PrincipalSetFilterId`)
""");
        }

        public override void Static_member_from_dbContext_is_inlined()
        {
            base.Static_member_from_dbContext_is_inlined();

            AssertSql(
                $"""
                    SELECT `d`.`Id`, `d`.`UserId`
                    FROM `DbContextStaticMemberFilter` AS `d`
                    WHERE `d`.`UserId` <> 1
                    """);
        }

        public override void Static_member_from_non_dbContext_is_inlined()
        {
            base.Static_member_from_non_dbContext_is_inlined();

            AssertSql(
                $"""
                    SELECT `s`.`Id`, `s`.`IsEnabled`
                    FROM `StaticMemberFilter` AS `s`
                    WHERE `s`.`IsEnabled` = TRUE
                    """);
        }

        public override void Local_variable_from_OnModelCreating_is_inlined()
        {
            base.Local_variable_from_OnModelCreating_is_inlined();

            AssertSql(
                $"""
                    SELECT `l`.`Id`, `l`.`IsEnabled`
                    FROM `LocalVariableFilter` AS `l`
                    WHERE `l`.`IsEnabled` = TRUE
                    """);
        }

        public override void Method_parameter_is_inlined()
        {
            base.Method_parameter_is_inlined();

            AssertSql(
                $"""
                    SELECT `p`.`Id`, `p`.`Tenant`
                    FROM `ParameterFilter` AS `p`
                    WHERE `p`.`Tenant` = 0
                    """);
        }

        public override void Using_multiple_context_in_filter_parametrize_only_current_context()
        {
            base.Using_multiple_context_in_filter_parametrize_only_current_context();

            AssertSql(
                """
@ef_filter__Property='False'

SELECT `m`.`Id`, `m`.`BossId`, `m`.`IsEnabled`
FROM `MultiContextFilter` AS `m`
WHERE `m`.`IsEnabled` = @ef_filter__Property AND `m`.`BossId` = 1
""",
                //
                """
@ef_filter__Property='True'

SELECT `m`.`Id`, `m`.`BossId`, `m`.`IsEnabled`
FROM `MultiContextFilter` AS `m`
WHERE `m`.`IsEnabled` = @ef_filter__Property AND `m`.`BossId` = 1
""");
        }

        private void AssertSql(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);

        public class QueryFilterFuncletizationJetFixture : QueryFilterFuncletizationRelationalFixture
        {
            protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;
        }
    }
}
