using System.Data.Jet;

namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities
{
    public static class AssertSqlHelper
    {
        static AssertSqlHelper()
        {
            DataAccessProviderType = TestEnvironment.DataAccessProviderType;
        }

        public static DataAccessProviderType DataAccessProviderType { get; set; }

        public static string Parameter(string name)
            => Parameter(name, DataAccessProviderType);

        public static string Parameter(string name, DataAccessProviderType dataAccessProviderType)
            => dataAccessProviderType == DataAccessProviderType.Odbc
                ? "?"
                : name;
    }
}