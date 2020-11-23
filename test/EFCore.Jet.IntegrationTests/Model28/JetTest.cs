using System;
using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model28
{
    [TestClass]
    public class Model28_SimpleTestJetTest : Test
    {
        protected override DbConnection GetConnection()
        {
            return IntegrationTests.Helpers.GetJetConnection();
        }
    }
}
