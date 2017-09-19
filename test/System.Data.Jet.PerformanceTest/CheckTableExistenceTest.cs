using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace System.Data.Jet.PerformanceTest
{
    //[TestClass]
    public class CheckTableExistenceTest
    {
        [TestInitialize]
        public void Initialize()
        {
            using (JetConnection connection = JetDatabaseFixture.GetConnection())
            {
                connection.Open();

                for (int i = 0; i < 300; i++)
                {

                    string sql = $@"
CREATE TABLE [Employees_{i}] (
	[EmployeeID] int IDENTITY (1, 1) NOT NULL ,
	[LastName] varchar (20) NOT NULL ,
	[FirstName] varchar (10) NOT NULL ,
	[Title] varchar (30) NULL ,
	[TitleOfCourtesy] varchar (25) NULL ,
	[BirthDate] datetime NULL ,
	[HireDate] datetime NULL ,
	[Address] varchar (60) NULL ,
	[City] varchar (15) NULL ,
	[Region] varchar (15) NULL ,
	[PostalCode] varchar (10) NULL ,
	[Country] varchar (15) NULL ,
	[HomePhone] varchar (24) NULL ,
	[Extension] varchar (4) NULL ,
	[Photo] image NULL ,
	[Notes] text NULL ,
	[ReportsTo] int NULL ,
	[PhotoPath] varchar (255) NULL ,
	CONSTRAINT [PK_Employees] PRIMARY KEY
	(
		[EmployeeID]
	)
);
CREATE  INDEX [LastName] ON [Employees_{i}]([LastName]);
CREATE  INDEX [PostalCode] ON [Employees_{i}]([PostalCode]);
";

                    connection.CreateCommand(sql).ExecuteNonQuery();
                }

            }
        }

        [TestCleanup]
        public void Cleanup()
        {
            using (JetConnection connection = JetDatabaseFixture.GetConnection())
            {
                connection.Open();

                for (int i = 0; i < 300; i++)
                {
                    string sql = $@"DROP TABLE [Employees_{i}]";
                    connection.CreateCommand(sql).ExecuteNonQuery();
                }

            }

        }


        [TestMethod]
        public void TryCatch()
        {
            int exists = 0;
            int notExists = 0;

            using(new Timer("TryCatch"))
            using (JetConnection connection = JetDatabaseFixture.GetConnection())
            {
                connection.Open();
                for (int i = 0; i < 1000; i++)
                {
                    if (TableExistsTryCatch(connection, $"Employees_{i % 500}"))
                        exists++;
                    else
                        notExists++;
                }
            }

            Assert.AreEqual(600, exists);
            Assert.AreEqual(400, notExists);

        }


        [TestMethod]
        public void ShowWhere()
        {
            int exists = 0;
            int notExists = 0;

            using (new Timer("ShowWhere"))
            using (JetConnection connection = JetDatabaseFixture.GetConnection())
            {
                connection.Open();
                for (int i = 0; i < 1000; i++)
                {
                    if (TableExistsShowWhere(connection, $"Employees_{i % 500}"))
                        exists++;
                    else
                        notExists++;
                }
            }

            Assert.AreEqual(600, exists);
            Assert.AreEqual(400, notExists);

        }



        public bool TableExistsTryCatch(JetConnection connection, string tableName)
        {
            bool tableExists;


            try
            {
                string sqlFormat = "select Top 1 * from [{0}]";
                connection.CreateCommand(String.Format(sqlFormat, tableName)).ExecuteNonQuery();
                tableExists = true;
            }
            catch
            {
                tableExists = false;
            }

            return tableExists;
        }

        public bool TableExistsShowWhere(JetConnection connection, string tableName)
        {
            string sqlFormat = "show tables where name = '{0}'";
            return connection.CreateCommand(String.Format(sqlFormat, tableName.Replace("'", "''"))).ExecuteScalar() != DBNull.Value;
        }


    }
}
