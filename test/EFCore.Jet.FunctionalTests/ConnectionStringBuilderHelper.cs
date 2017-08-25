using System;
using System.Data.OleDb;

namespace EntityFramework.Jet.FunctionalTests
{
    static class ConnectionStringBuilderHelper
    {
        public static string GetJetConnectionString()
        {
            // ReSharper disable once CollectionNeverUpdated.Local
            OleDbConnectionStringBuilder oleDbConnectionStringBuilder = new OleDbConnectionStringBuilder();
            //oleDbConnectionStringBuilder.Provider = "Microsoft.Jet.OLEDB.4.0";
            //oleDbConnectionStringBuilder.DataSource = @".\Empty.mdb";
            oleDbConnectionStringBuilder.Provider = "Microsoft.ACE.OLEDB.12.0";
            oleDbConnectionStringBuilder.DataSource = GetTestDirectory() + "\\Empty.accdb";
            return oleDbConnectionStringBuilder.ToString();
        }

        private static string GetTestDirectory()
        {
            return System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().GetName().CodeBase.Replace("file:///", ""));
        }


        public static string GetJetConnectionString(string fileNameWithoutExtension)
        {
            // ReSharper disable once CollectionNeverUpdated.Local
            OleDbConnectionStringBuilder oleDbConnectionStringBuilder = new OleDbConnectionStringBuilder();
            //oleDbConnectionStringBuilder.Provider = "Microsoft.Jet.OLEDB.4.0";
            //oleDbConnectionStringBuilder.DataSource = @".\Empty.mdb";
            oleDbConnectionStringBuilder.Provider = "Microsoft.ACE.OLEDB.12.0";
            oleDbConnectionStringBuilder.DataSource = GetTestDirectory() + "\\" + fileNameWithoutExtension + ".accdb";
            return oleDbConnectionStringBuilder.ToString();
        }
    }
}
