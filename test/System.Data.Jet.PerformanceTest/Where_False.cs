using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace System.Data.Jet.PerformanceTest
{
    [TestClass]
    public class Where_False
    {
        [TestMethod]
        public void Where_False_Test()
        {

            const int cycles = 5000;

            using (new Timer($"Select {cycles} Where 1=2 get data reader"))
            {
                using (var connection = JetDatabaseFixture.GetConnection())
                {
                    connection.Open();
                    for (int i = 0; i < cycles; i++)
                    {
                        var reader = connection.CreateCommand("Select * from employees where 1=2").ExecuteReader();
                        reader.Dispose();
                    }
                }
            }

            using (new Timer($"Select {cycles} Where 1=2 get table structure"))
            {
                using (var connection = JetDatabaseFixture.GetConnection())
                {
                    connection.Open();
                    for (int i = 0; i < cycles; i++)
                    {
                        var reader = connection.CreateCommand("Select * from employees where 1=2").ExecuteReader(CommandBehavior.KeyInfo);
                        var schemaTable = reader.GetSchemaTable();
                        reader.Dispose();
                        // ReSharper disable once PossibleNullReferenceException
                        schemaTable.Dispose();
                    }
                }
            }

            using (new Timer($"Select {cycles} Top 1 get data reader"))
            {
                using (var connection = JetDatabaseFixture.GetConnection())
                {
                    connection.Open();
                    for (int i = 0; i < cycles; i++)
                    {
                        var reader = connection.CreateCommand("Select top 1 * from employees").ExecuteReader();
                        reader.Dispose();
                    }
                }
            }

            using (new Timer($"Select {cycles} Top 1 get table structure"))
            {
                using (var connection = JetDatabaseFixture.GetConnection())
                {
                    connection.Open();
                    for (int i = 0; i < cycles; i++)
                    {
                        var reader = connection.CreateCommand("Select top 1 * from employees").ExecuteReader(CommandBehavior.KeyInfo);
                        var schemaTable = reader.GetSchemaTable();
                        reader.Dispose();
                        // ReSharper disable once PossibleNullReferenceException
                        schemaTable.Dispose();
                    }
                }
            }

            using (new Timer($"Select {cycles} Top 1 Where 1=2 get data reader"))
            {
                using (var connection = JetDatabaseFixture.GetConnection())
                {
                    connection.Open();
                    for (int i = 0; i < cycles; i++)
                    {
                        var reader = connection.CreateCommand("Select top 1 * from employees where 1=2").ExecuteReader();
                        reader.Dispose();
                    }
                }
            }

            using (new Timer($"Select {cycles} Top 1 Where 1=2 get table structure"))
            {
                using (var connection = JetDatabaseFixture.GetConnection())
                {
                    connection.Open();
                    for (int i = 0; i < cycles; i++)
                    {
                        var reader = connection.CreateCommand("Select top 1 * from employees where 1=2").ExecuteReader(CommandBehavior.KeyInfo);
                        var schemaTable = reader.GetSchemaTable();
                        reader.Dispose();
                        // ReSharper disable once PossibleNullReferenceException
                        schemaTable.Dispose();
                    }
                }
            }

        }


        [TestMethod]
        public void Where_False_Test_DisposeConnection()
        {

            const int cycles = 60;


            using (new Timer($"Select {cycles} Where 1=2 get table structure (Dispose connection)"))
            {
                for (int i = 0; i < cycles; i++)
                {
                    using (var connection = JetDatabaseFixture.GetConnection())
                    {
                        connection.Open();
                        var reader = connection.CreateCommand("Select * from employees where 1=2").ExecuteReader(CommandBehavior.KeyInfo);
                        var schemaTable = reader.GetSchemaTable();
                        reader.Dispose();
                        // ReSharper disable once PossibleNullReferenceException
                        schemaTable.Dispose();
                    }
                }
            }

            using (new Timer($"Select {cycles} Top 1 get table structure (Dispose connection)"))
            {
                for (int i = 0; i < cycles; i++)
                {
                    using (var connection = JetDatabaseFixture.GetConnection())
                    {
                        connection.Open();
                        var reader = connection.CreateCommand("Select top 1 * from employees").ExecuteReader(CommandBehavior.KeyInfo);
                        var schemaTable = reader.GetSchemaTable();
                        reader.Dispose();
                        // ReSharper disable once PossibleNullReferenceException
                        schemaTable.Dispose();
                    }
                }
            }


        }


    }
}
