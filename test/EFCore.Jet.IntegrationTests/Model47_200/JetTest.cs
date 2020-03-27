using System;
using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model47_200
{
    [TestClass]
    public class Model47_200JetTest : Test
    {
        protected override DbConnection GetConnection()
        {
            return Helpers.GetJetConnection();
        }
    }
}
