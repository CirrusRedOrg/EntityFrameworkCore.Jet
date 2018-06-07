using System;
using System.Data.Common;
using EFCore.Jet.Integration.Test;
using EFCore.Jet.Integration.Test.GearOfWar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.GearOfWar
{
    //[TestClass]
    public class GearOfWarTestSqlCe : TestBase<GearsOfWarContext>
    {
        protected override DbConnection GetConnection()
        {
            return Helpers.GetSqlCeConnection();
        }

        [TestMethod]
        public void GearOfWarTestSqlCeSeedTest()
        {
            GearsOfWarContext.Seed(Context);
        }
    }
}
