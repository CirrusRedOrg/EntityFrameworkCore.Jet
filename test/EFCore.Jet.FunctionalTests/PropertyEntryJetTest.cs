using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFramework.Jet.FunctionalTests
{
    public class PropertyEntryJetTest : PropertyEntryTestBase<JetTestStore, F1JetFixture>
    {
        public PropertyEntryJetTest(F1JetFixture fixture)
            : base(fixture)
        {
        }

        public override void Property_entry_original_value_is_set()
        {
            base.Property_entry_original_value_is_set();

            Assert.Contains(
                @"SELECT TOP(1) [e].[Id], [e].[EngineSupplierId], [e].[Name], [e].[Id], [e].[StorageLocation_Latitude], [e].[StorageLocation_Longitude]
FROM [Engines] AS [e]",
                Sql);

            Assert.Contains(
                @"UPDATE [Engines] SET [Name] = @p0
WHERE [Id] = @p1 AND [EngineSupplierId] = @p2 AND [Name] = @p3",
                Sql);
        }

        private string Sql => Fixture.TestSqlLoggerFactory.Sql;
    }
}
