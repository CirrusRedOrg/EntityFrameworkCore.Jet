using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model78_MigrationUpdate
{
    [TestClass]
    public class Model78_MigrationUpdate : Test
    {
        protected override DbConnection GetConnection()
        {
            return Helpers.CreateAndOpenJetDatabase();
        }
    }
}
