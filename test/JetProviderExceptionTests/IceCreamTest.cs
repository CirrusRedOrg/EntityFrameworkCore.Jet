using System;
using EntityFrameworkCore.Jet.Data;
using System.Data.Odbc;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JetProviderExceptionTests
{
    public class IceCreamTest
    {
        public class IceCream
        {
            public int IceCreamId { get; set; }
            public string Name { get; set; }
            public string Brand { get; set; }
            public DateTime? BestServedBefore { get; set; }
        }

        public class Context : DbContext
        {
            public DbSet<IceCream> IceCreams { get; set; }

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            {
                optionsBuilder
                    // .UseJet("Provider=Microsoft.ACE.OLEDB.12.0;Data Source=JetProviderExceptionTests.accdb")
                    .UseJet(
                        JetConnection.GetConnectionString("JetProviderExceptionTests.accdb", OdbcFactory.Instance),
                        OdbcFactory.Instance)
                    .UseLoggerFactory(
                        LoggerFactory.Create(
                            b => b
                                .AddConsole()
                                .AddFilter(level => level >= LogLevel.Information)))
                    .EnableSensitiveDataLogging()
                    .EnableDetailedErrors();
            }

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                var today = DateTime.Today;

                modelBuilder.Entity<IceCream>()
                    .HasData(
                        new IceCream {IceCreamId = 1, Name = "Vanilla", Brand = "Ben & Jerry", BestServedBefore = today.AddDays(180)},
                        new IceCream {IceCreamId = 2, Name = "Chocolate", Brand = "Baskin-Robbins", BestServedBefore = today.AddDays(210)},
                        new IceCream {IceCreamId = 3, Name = "Strawberry", Brand = "Dairy Queen", BestServedBefore = today.AddDays(90)},
                        new IceCream {IceCreamId = 4, Name = "Matcha", Brand = "Kikyouya", BestServedBefore = today.AddDays(150)}
                    );
            }
        }

        public void Run()
        {
            try
            {
                using (var context = new Context())
                {
                    context.Database.EnsureDeleted();
                    context.Database.EnsureCreated();
                }

                for (var i = 0; i < 1024; i++)
                {
                    var today = DateTime.Today;
                    var from = today.AddDays(110);

                    using var context = new Context();

                    var query1 = context.IceCreams
                        .Where(c => c.Name == "Vanilla")
                        .Select(c => c.Brand)
                        .Union(
                            context.IceCreams
                                .Where(c => c.BestServedBefore > from)
                                .Select(c => c.Brand))
                        .ToList();

                    var boolean = false;

                    var query2 = context.IceCreams
                        .Select(c => new {f = boolean})
                        .ToList();

                    boolean = true;

                    var query3 = context.IceCreams
                        .Select(c => new {f = boolean})
                        .ToList();
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