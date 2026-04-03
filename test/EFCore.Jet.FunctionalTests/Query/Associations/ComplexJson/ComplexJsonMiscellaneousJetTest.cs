using Microsoft.EntityFrameworkCore.Query.Associations.ComplexJson;
using Microsoft.EntityFrameworkCore.TestUtilities;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query.Associations.ComplexJson;

public class ComplexJsonMiscellaneousJetTest(ComplexJsonJetFixture fixture, ITestOutputHelper testOutputHelper)
    : ComplexJsonMiscellaneousRelationalTestBase<ComplexJsonJetFixture>(fixture, testOutputHelper)
{
    #region Simple filters

    public override async Task Where_on_associate_scalar_property()
    {
        await base.Where_on_associate_scalar_property();

        AssertSql(
                """
SELECT [r].[Id], [r].[Name], [r].[AssociateCollection], [r].[OptionalAssociate], [r].[RequiredAssociate]
FROM [RootEntity] AS [r]
WHERE CAST(JSON_VALUE([r].[RequiredAssociate], '$.Int') AS int) = 8
""");
    }

    public override async Task Where_on_optional_associate_scalar_property()
    {
        await base.Where_on_optional_associate_scalar_property();

        AssertSql(
                """
SELECT [r].[Id], [r].[Name], [r].[AssociateCollection], [r].[OptionalAssociate], [r].[RequiredAssociate]
FROM [RootEntity] AS [r]
WHERE CAST(JSON_VALUE([r].[OptionalAssociate], '$.Int') AS int) = 8
""");
    }

    public override async Task Where_on_nested_associate_scalar_property()
    {
        await base.Where_on_nested_associate_scalar_property();

        AssertSql(
                """
SELECT [r].[Id], [r].[Name], [r].[AssociateCollection], [r].[OptionalAssociate], [r].[RequiredAssociate]
FROM [RootEntity] AS [r]
WHERE CAST(JSON_VALUE([r].[RequiredAssociate], '$.RequiredNestedAssociate.Int') AS int) = 8
""");
    }

    #endregion Simple filters

    #region Value types

    public override async Task Where_property_on_non_nullable_value_type()
    {
        await base.Where_property_on_non_nullable_value_type();

        AssertSql(
                """
SELECT [v].[Id], [v].[Name], [v].[AssociateCollection], [v].[OptionalAssociate], [v].[RequiredAssociate]
FROM [ValueRootEntity] AS [v]
WHERE CAST(JSON_VALUE([v].[RequiredAssociate], '$.Int') AS int) = 8
""");
    }

    public override async Task Where_property_on_nullable_value_type_Value()
    {
        await base.Where_property_on_nullable_value_type_Value();

        AssertSql(
                """
SELECT [v].[Id], [v].[Name], [v].[AssociateCollection], [v].[OptionalAssociate], [v].[RequiredAssociate]
FROM [ValueRootEntity] AS [v]
WHERE CAST(JSON_VALUE([v].[OptionalAssociate], '$.Int') AS int) = 8
""");
    }

    public override async Task Where_HasValue_on_nullable_value_type()
    {
        await base.Where_HasValue_on_nullable_value_type();

        AssertSql(
            """
SELECT `v`.`Id`, `v`.`Name`, `v`.`AssociateCollection`, `v`.`OptionalAssociate`, `v`.`RequiredAssociate`
FROM `ValueRootEntity` AS `v`
WHERE (`v`.`OptionalAssociate`) IS NOT NULL
""");
    }

    #endregion Value types

    public override async Task FromSql_on_root()
    {
        await base.FromSql_on_root();

        AssertSql(
            """
SELECT `m`.`Id`, `m`.`Name`, `m`.`AssociateCollection`, `m`.`OptionalAssociate`, `m`.`RequiredAssociate`
FROM (
    SELECT * FROM `RootEntity`
) AS `m`
""");
    }

    [ConditionalFact]
    public virtual void Check_all_tests_overridden()
        => TestHelpers.AssertAllMethodsOverridden(GetType());
}
