using System;
using System.Data.Common;
using EntityFrameworkCore.Jet.Data;
using System.Data.OleDb;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model79_CantSaveDecimalValue
{
    [TestClass]
    public class Model79_CantSaveDecimalValue : Test
    {
        protected override DbConnection GetConnection()
        {
            return Helpers.GetJetConnection();
        }
    }
}
