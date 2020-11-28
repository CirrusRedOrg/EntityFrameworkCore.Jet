using System;
using EntityFrameworkCore.Jet.Data;

namespace JetProviderExceptionTests
{
    public class NorthwindTestJetCommand
    {
        public void Run()
        {
            try
            {
                for (var i = 0; i < 100; i++)
                {
                    using var connection = new JetConnection("Provider=Microsoft.ACE.OLEDB.12.0;Data Source=Northwind.accdb");
                    connection.Open();

                    for (var j = 0; j < 2000; j++)
                    {
                        Console.WriteLine($"{i:00}: {j:000}");

                        //
                        // Select_Union:
                        //

                        using (var command1 = connection.CreateCommand())
                        {
                            command1.CommandText = @"SELECT `c`.`Address`
FROM `Customers` AS `c`
WHERE `c`.`City` = 'Berlin'
UNION
SELECT `c0`.`Address`
FROM `Customers` AS `c0`
WHERE `c0`.`City` = 'London'";

                            using (var dataReader1 = command1.ExecuteReader())
                            {
                                while (dataReader1.Read())
                                {
                                }
                            }
                        }

                        //
                        // Select_bool_closure:
                        //

                        using (var command2 = connection.CreateCommand())
                        {
                            command2.CommandText = @"SELECT 1
FROM `Customers` AS `c`";

                            using (var dataReader2 = command2.ExecuteReader())
                            {
                                while (dataReader2.Read())
                                {
                                }
                            }
                        }
                    }
                }
            }
            catch (AccessViolationException e)
            {
                Console.WriteLine(e);
                Console.ReadKey(true);
            }
        }
    }
}