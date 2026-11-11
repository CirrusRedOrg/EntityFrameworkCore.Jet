using System.Threading.Tasks;
using EntityFrameworkCore.LibRed.Extended.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore.Query.Translations;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace EntityFrameworkCore.LibRed.Extended.FunctionalTests.Query.Translations;

public class JsonTranslationsLibRedTest : JsonTranslationsRelationalTestBase<
    JsonTranslationsLibRedTest.JsonTranslationsQueryLibRedFixture>
{
    public JsonTranslationsLibRedTest(JsonTranslationsQueryLibRedFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    public override async Task JsonPathExists_on_scalar_string_column()
    {
        await base.JsonPathExists_on_scalar_string_column();

        AssertSql(
            """
SELECT [j].[Id], [j].[JsonString], [j].[JsonComplexType], [j].[JsonOwnedType]
FROM [JsonEntities] AS [j]
WHERE JSON_PATH_EXISTS([j].[JsonString], N'$.OptionalInt') = 1
""");
    }

    public override async Task JsonPathExists_on_complex_property()
    {
        await base.JsonPathExists_on_complex_property();

        AssertSql(
            """
SELECT [j].[Id], [j].[JsonString], [j].[JsonComplexType], [j].[JsonOwnedType]
FROM [JsonEntities] AS [j]
WHERE JSON_PATH_EXISTS([j].[JsonComplexType], N'$.OptionalInt') = 1
""");
    }

    public override async Task JsonPathExists_on_owned_entity()
    {
        await base.JsonPathExists_on_owned_entity();

        AssertSql(
            """
SELECT [j].[Id], [j].[JsonString], [j].[JsonComplexType], [j].[JsonOwnedType]
FROM [JsonEntities] AS [j]
WHERE JSON_PATH_EXISTS([j].[JsonOwnedType], N'$.OptionalInt') = 1
""");
    }

    public class JsonTranslationsQueryLibRedFixture : JsonTranslationsQueryFixtureBase, ITestSqlLoggerFactory
    {
        protected override ITestStoreFactory TestStoreFactory
            => LibRedTestStoreFactory.Instance;

        protected override string RemoveJsonProperty(string column, string property)
            => $"JSON_MODIFY({column}, '$.{property}', NULL)";
    }

    [Fact]
    public virtual void Check_all_tests_overridden()
        => TestHelpers.AssertAllMethodsOverridden(GetType());

    private void AssertSql(params string[] expected)
        => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);
}
