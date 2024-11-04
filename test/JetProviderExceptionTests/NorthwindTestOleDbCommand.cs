using System;
using System.Data.OleDb;

namespace JetProviderExceptionTests
{
    public class NorthwindTestOleDbCommand
    {
        public void Run()
        {
            try
            {
                using var connection = new OleDbConnection("Provider=Microsoft.ACE.OLEDB.16.0;Data Source=Northwind.accdb");
                connection.Open();

                for (var i = 0; i < 1000; i++)
                {
                    Console.WriteLine($"{i:000}");

                    //
                    // Select_Union:
                    //

                    using (var command1 = connection.CreateCommand())
                    {
                        command1.CommandText = """
                            SELECT `c`.`Address`
                            FROM `Customers` AS `c`
                            WHERE `c`.`City` = 'Berlin'
                            UNION
                            SELECT `c0`.`Address`
                            FROM `Customers` AS `c0`
                            WHERE `c0`.`City` = 'London'
                            """;

                        using (var dataReader1 = command1.ExecuteReader())
                        {
                            while (dataReader1.Read())
                            {
                            }
                        }
                    }

                    /*
                    using (var command15 = connection.CreateCommand())
                    {
                        command15.CommandText = @"SELECT [c].[Address]
FROM [Customers] AS [c]
WHERE [c].[City] = 'Madrid'";

                        using (var dataReader15 = command15.ExecuteReader())
                        {
                            while (dataReader15.Read())
                            {
                            }
                        }
                    }
                    */
                    
                    //
                    // Select_bool_closure:
                    //

                    using (var command2 = connection.CreateCommand())
                    {
                        command2.CommandText = """
                            SELECT 1
                            FROM `Customers` AS `c`
                            """;

                        using (var dataReader2 = command2.ExecuteReader())
                        {
                            while (dataReader2.Read())
                            {
                            }
                        }
                    }
                }
            }
            catch (AccessViolationException e)
            {
                Console.WriteLine(e);
            }
        }
    }
}