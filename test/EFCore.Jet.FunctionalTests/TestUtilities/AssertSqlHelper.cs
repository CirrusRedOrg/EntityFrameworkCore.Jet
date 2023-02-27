using System;
using EntityFrameworkCore.Jet.Data;

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

        public static string Declaration(string fullDeclaration)
            => Declaration(fullDeclaration, DataAccessProviderType);
        //TODO:confirm which way odbc and oledb do this
        public static string Declaration(string fullDeclaration, DataAccessProviderType dataAccessProviderType)
            => fullDeclaration;
    }
}