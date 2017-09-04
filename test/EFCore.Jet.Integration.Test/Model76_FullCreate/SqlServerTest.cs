using System;
using System.Data.Common;
using System.Data.Jet;
using System.Data.OleDb;
using System.Data.SqlServerCe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model76_FullCreate
{
    [TestClass]
    public class Model37_Model76_FullCreateSqlServer : Test
    {
        protected override DbConnection GetConnection()
        {
            return Helpers.GetSqlServerConnection();
        }
    }
}
