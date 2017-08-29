using System;
using System.Data.Common;
using EFCore.Jet.Integration.Test;
using EFCore.Jet.Integration.Test.GearOfWar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.SqlServer.Integration.Test.GearOfWar
{
    //[TestClass]
    public class GearOfWarTestSqlServer : TestBase<GearsOfWarContext>
    {
        protected override DbConnection GetConnection()
        {
            return Helpers.GetSqlServerConnection();
        }

        [TestMethod]
        public void GearOfWarTestSqlServerSeedTest()
        {
            GearsOfWarContext.Seed(Context);
        }
    }
}
