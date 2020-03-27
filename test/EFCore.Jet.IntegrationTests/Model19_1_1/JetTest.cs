using System;
using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model19_1_1
{
    [TestClass]
    public class Model19_1_1JetTest : Test
    {
        protected override DbConnection GetConnection()
        {
            return Helpers.GetJetConnection();
        }
    }
}
