using System;
using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace System.Data.Jet.Test
{
    [TestClass]
    public class JetStoreSchemaDefinitionRetrieveTest
    {

        DbConnection _connection;

        [TestInitialize]
        public void Initialize()
        {
            _connection = Helpers.GetJetConnection();
            _connection.Open();


            string sql = @"IF NOT EXISTS (SELECT * FROM (SHOW TABLES) WHERE NAME = 'TestMoney') THEN
CREATE TABLE TestMoney ( MoneyCol money, DecimalCol decimal(19,0) );
";
            var command = _connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
            command.Dispose();
        }

        [TestCleanup]
        public void Cleanup()
        {
            string sql = @"
DROP TABLE TestMoney;
";
            var command = _connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
            command.Dispose();

            _connection.Dispose();
        }

        [TestMethod]
        public void Show()
        {
            Helpers.ShowDataReaderContent(_connection, "show tables");
            Helpers.ShowDataReaderContent(_connection, "show tablecolumns");
            Helpers.ShowDataReaderContent(_connection, "show indexes");
            Helpers.ShowDataReaderContent(_connection, "show indexcolumns");
            Helpers.ShowDataReaderContent(_connection, "show views");
            Helpers.ShowDataReaderContent(_connection, "show viewcolumns");
            Helpers.ShowDataReaderContent(_connection, "show constraints");
            Helpers.ShowDataReaderContent(_connection, "show checkconstraints");
            Helpers.ShowDataReaderContent(_connection, "show constraintcolumns");
            Helpers.ShowDataReaderContent(_connection, "show foreignKeyconstraints");
            Helpers.ShowDataReaderContent(_connection, "show foreignKeys");
            Helpers.ShowDataReaderContent(_connection, "show viewconstraints");
            Helpers.ShowDataReaderContent(_connection, "show viewconstraintcolumns");
            Helpers.ShowDataReaderContent(_connection, "show viewforeignkeys");
        }

        [TestMethod]
        public void ShowWithWhere()
        {
            Helpers.ShowDataReaderContent(_connection, "show indexes where Name like 'PK*'");
            Helpers.ShowDataReaderContent(_connection, "show indexcolumns where index like 'PK*'");
        }

        [TestMethod]
        public void ShowWithWhereOrder()
        {
            Helpers.ShowDataReaderContent(_connection, "show indexes where Name like 'PK*' order by Name");
            Helpers.ShowDataReaderContent(_connection, "show indexcolumns where index like 'PK*' order by Index, Ordinal");
        }

        [TestMethod]
        public void ShowWithOrder()
        {
            Helpers.ShowDataReaderContent(_connection, "show indexes order by Name");
            Helpers.ShowDataReaderContent(_connection, "show indexcolumns order by Index, Ordinal");
        }

        [TestMethod]
        public void SelectStatement()
        {
            Helpers.ShowDataReaderContent(_connection, "select * from show tables");
        }


    }
}
