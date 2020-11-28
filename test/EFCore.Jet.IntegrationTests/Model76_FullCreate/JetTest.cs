using System;
using System.Data.Common;
using EntityFrameworkCore.Jet.Data;
using System.Data.OleDb;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model76_FullCreate
{
    [TestClass]
    public class Model76_FullCreate : Test
    {
        protected override DbConnection GetConnection()
        {
            return Helpers.CreateAndOpenJetDatabase();
        }
    }
}