using System;
using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model37_2Contexts
{
    [TestClass]
    public class Model37_2ContextsJetTest : Test
    {
        protected override DbConnection GetConnection()
        {
            return Helpers.CreateAndOpenJetDatabase();
        }
    }
}
