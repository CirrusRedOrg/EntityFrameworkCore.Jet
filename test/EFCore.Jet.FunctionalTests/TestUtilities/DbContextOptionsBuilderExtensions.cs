using EntityFrameworkCore.Jet.Infrastructure;

namespace EntityFramework.Jet.FunctionalTests.TestUtilities
{
    public static class DbContextOptionsBuilderExtensions
    {
        public static JetDbContextOptionsBuilder ApplyConfiguration(this JetDbContextOptionsBuilder optionsBuilder)
        {
            System.Data.Jet.JetConfiguration.ShowSqlStatements = true;
            return optionsBuilder;
        }
    }
}

