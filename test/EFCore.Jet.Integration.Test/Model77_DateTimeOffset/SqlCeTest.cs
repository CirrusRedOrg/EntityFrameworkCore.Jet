using System;
using System.Data.Common;
using System.Data.Jet;
using System.Data.OleDb;
using System.Data.SqlServerCe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model77_DateTimeOffset
{
    [TestClass]
    public class Model77_DateTimeOffsetSqlCe : Test
    {
        protected override DbConnection GetConnection()
        {
            SqlCeConnectionStringBuilder sqlCeConnectionStringBuilder = new SqlCeConnectionStringBuilder();
            sqlCeConnectionStringBuilder.DataSource = "BrandNewDatabase.sdf";
            return new SqlCeConnection(sqlCeConnectionStringBuilder.ToString());
        }
    }
}
