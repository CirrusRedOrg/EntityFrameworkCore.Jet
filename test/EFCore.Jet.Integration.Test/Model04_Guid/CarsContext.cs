using System;
using System.Data.Common;
using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;

namespace EFCore.Jet.Integration.Test.Model04_Guid
{
    public class CarsContext : DbContext
    {
        public CarsContext(DbContextOptions options) : base(options)
        {
        }

        // For migration test
        public CarsContext()
        { }

        public DbSet<Car> Cars { get; set; }
        public DbSet<Person> Persons { get; set; }
    }
}
