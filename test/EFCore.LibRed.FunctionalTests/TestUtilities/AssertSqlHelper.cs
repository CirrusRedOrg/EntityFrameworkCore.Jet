
namespace EntityFrameworkCore.LibRed.FunctionalTests.TestUtilities
{
    public static class AssertSqlHelper
    {
        static AssertSqlHelper()
        {
        }

        public static string Parameter(string name)
            => name;

        public static string Declaration(string fullDeclaration)
            => fullDeclaration;
    }
}