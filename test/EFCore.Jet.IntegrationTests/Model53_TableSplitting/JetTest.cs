using System;
using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model53_TableSplitting
{
    [TestClass]
    public class Model53_TableSplittingTest : Test
    {
        protected override DbConnection GetConnection()
        {
            return Helpers.GetJetConnection();
        }
    }
}
