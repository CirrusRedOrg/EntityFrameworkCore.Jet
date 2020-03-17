using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model77_DateTimeOffset
{
    [TestClass]
    public class Model77_DateTimeOffsetJet : Test
    {
        protected override DbConnection GetConnection()
        {
            return Helpers.CreateAndOpenJetDatabase();
        }
    }
}