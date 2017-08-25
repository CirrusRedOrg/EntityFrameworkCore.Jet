using System;
using System.Data.Common;
using System.Data.Jet;
using System.Data.OleDb;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model76_FullCreate
{
    [TestClass]
    public class Model37_Model76_FullCreate : Test
    {
        protected override DbConnection GetConnection()
        {
            // ReSharper disable once CollectionNeverUpdated.Local

            OleDbConnectionStringBuilder oleDbConnectionStringBuilder = new OleDbConnectionStringBuilder();
            //oleDbConnectionStringBuilder.Provider = "Microsoft.Jet.OLEDB.4.0";
            //oleDbConnectionStringBuilder.DataSource = @".\Empty.mdb";
            oleDbConnectionStringBuilder.Provider = "Microsoft.ACE.OLEDB.12.0";
            oleDbConnectionStringBuilder.DataSource = Helpers.GetTestDirectory() + "\\BrandNewDatabase.accdb";
            return new JetConnection(oleDbConnectionStringBuilder.ToString());
        }
    }
}
