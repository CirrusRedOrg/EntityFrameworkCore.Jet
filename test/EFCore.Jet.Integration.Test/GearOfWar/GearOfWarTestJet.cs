using System;
using System.Data.Common;
using System.Data.Jet;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.GearOfWar
{
    [TestClass]
    public class GearOfWarTestJet : TestBase<GearsOfWarContext>
    {
        protected override DbConnection GetConnection()
        {
            return new JetConnection(JetConnection.GetConnectionString("GearOfWar.accdb"));
        }

        [TestMethod]
        public void GearOfWarTestJetSeedTest()
        {
            GearsOfWarContext.Seed(Context);
        }
    }
}
