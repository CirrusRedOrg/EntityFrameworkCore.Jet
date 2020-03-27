using System;
using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests
{
    [TestClass]
    public class DmlJetTest : DmlBaseTest
    {
        protected override DbConnection GetConnection()
            => Helpers.GetJetConnection();
    }
}
