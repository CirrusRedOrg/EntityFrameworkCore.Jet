using System;
using System.Data.Jet;
using System.Data.OleDb;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JetProviderExceptionTests
{
    public class NorthwindTestEFCore
    {
        public class Customer
        {
            public string CustomerID { get; set; }
            public string CompanyName { get; set; }
            public string ContactName { get; set; }
            public string ContactTitle { get; set; }
            public string Address { get; set; }
            public string City { get; set; }
            public string Region { get; set; }
            public string PostalCode { get; set; }
            public string Country { get; set; }
            public string Phone { get; set; }
            public string Fax { get; set; }
        }

        public class Context : DbContext
        {
            public DbSet<Customer> Customers { get; set; }

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            {
                optionsBuilder
                    .UseJet("Provider=Microsoft.ACE.OLEDB.12.0;Data Source=Northwind.accdb", OleDbFactory.Instance)
                    .UseLoggerFactory(
                        LoggerFactory.Create(
                            b => b
                                .AddConsole()
                                .AddFilter(level => level >= LogLevel.Information)))
                    .EnableSensitiveDataLogging()
                    .EnableDetailedErrors();
            }
        }

        public void Run()
        {
            try
            {
                for (var i = 0; i < 100; i++)
                {
                    using var context = new Context();

                    for (var j = 0; j < 12; j++)
                    {
                        //
                        // Select_Union:
                        //
                        
                        var query1 = context.Set<Customer>()
                            .Where(c => c.City == "Berlin")
                            .Select(c => c.Address)
                            .Union(
                                context.Set<Customer>()
                                    .Where(c => c.City == "London")
                                    .Select(c => c.Address))
                            .ToList();
                        
                        //
                        // Select_bool_closure:
                        //

                        var boolean = false;

                        var query2 = context.Set<Customer>()
                            .Select(c => new {f = boolean})
                            .ToList();

                        boolean = true;

                        var query3 = context.Set<Customer>()
                            .Select(c => new {f = boolean})
                            .ToList();
                    }
                }
            }
            catch (AccessViolationException e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
    }
}