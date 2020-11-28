using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace System.Data.Jet.Test
{
    [TestClass]
    public class JetStoreSchemaDefinitionRetrieveTest
    {
        private const string StoreName = nameof(JetStoreSchemaDefinitionRetrieveTest) + ".accdb";

        private DbConnection _connection;

        [TestInitialize]
        public void Setup()
        {
            _connection = Helpers.CreateAndOpenDatabase(StoreName);

            const string sql = @"IF NOT EXISTS (SELECT * FROM (SHOW TABLES) WHERE NAME = 'TestMoney') THEN
CREATE TABLE TestMoney ( MoneyCol money, DecimalCol decimal(19,0) );
";

            using var command = _connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        [TestCleanup]
        public void TearDown()
        {
            _connection?.Close();
        }

        [TestMethod]
        public void Show()
        {
            Helpers.GetDataReaderContent(_connection, "show tables");
            Helpers.GetDataReaderContent(_connection, "show tablecolumns");
            Helpers.GetDataReaderContent(_connection, "show indexes");
            Helpers.GetDataReaderContent(_connection, "show indexcolumns");
            Helpers.GetDataReaderContent(_connection, "show views");
            Helpers.GetDataReaderContent(_connection, "show viewcolumns");
            Helpers.GetDataReaderContent(_connection, "show constraints");
            Helpers.GetDataReaderContent(_connection, "show checkconstraints");
            Helpers.GetDataReaderContent(_connection, "show constraintcolumns");
            Helpers.GetDataReaderContent(_connection, "show foreignKeyconstraints");
            Helpers.GetDataReaderContent(_connection, "show foreignKeys");
            Helpers.GetDataReaderContent(_connection, "show viewconstraints");
            Helpers.GetDataReaderContent(_connection, "show viewconstraintcolumns");
            Helpers.GetDataReaderContent(_connection, "show viewforeignkeys");
        }

        [TestMethod]
        public void ShowWithWhere()
        {
            Helpers.GetDataReaderContent(_connection, "show indexes where Name like 'PK*'");
            Helpers.GetDataReaderContent(_connection, "show indexcolumns where index like 'PK*'");
        }

        [TestMethod]
        public void ShowWithWhereOrder()
        {
            Helpers.GetDataReaderContent(_connection, "show indexes where Name like 'PK*' order by Name");
            Helpers.GetDataReaderContent(_connection, "show indexcolumns where index like 'PK*' order by Index, Ordinal");
        }

        [TestMethod]
        public void ShowWithOrder()
        {
            Helpers.GetDataReaderContent(_connection, "show indexes order by Name");
            Helpers.GetDataReaderContent(_connection, "show indexcolumns order by Index, Ordinal");
        }

        [TestMethod]
        public void SelectStatement()
        {
            Helpers.GetDataReaderContent(_connection, "select * from show tables");
        }
    }
}